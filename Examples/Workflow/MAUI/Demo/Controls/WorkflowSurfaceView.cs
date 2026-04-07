using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Demo.ViewModels;
using Demo.Workflow;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;
using VeloxDev.Core.Interfaces.WorkflowSystem;

namespace Demo.Controls;

public sealed class WorkflowSurfaceView : ContentView
{
    public static readonly BindableProperty SessionProperty = BindableProperty.Create(
        nameof(Session),
        typeof(WorkflowDemoSession),
        typeof(WorkflowSurfaceView),
        propertyChanged: OnSessionChanged);

    private readonly Grid _root;
    private readonly GraphicsView _linksView;
    private readonly AbsoluteLayout _nodesLayer;
    private readonly WorkflowSurfaceDrawable _drawable = new();
    private readonly Dictionary<IWorkflowNodeViewModel, WorkflowNodeCardView> _cards = [];
    private WorkflowDemoSession? _session;
    private INotifyPropertyChanged? _controllerNotifier;
    private bool _linksInvalidateQueued;

    public WorkflowSurfaceView()
    {
        _linksView = new GraphicsView
        {
            Drawable = _drawable,
            InputTransparent = true
        };

        _nodesLayer = new AbsoluteLayout();

        _root = new Grid
        {
            BackgroundColor = Color.FromArgb("#0F172A"),
            Padding = 24,
            Children = { _linksView, _nodesLayer }
        };

        Content = _root;
    }

    public WorkflowDemoSession? Session
    {
        get => (WorkflowDemoSession?)GetValue(SessionProperty);
        set => SetValue(SessionProperty, value);
    }

    private static void OnSessionChanged(BindableObject bindable, object? oldValue, object? newValue)
    {
        var view = (WorkflowSurfaceView)bindable;
        view.AttachSession((WorkflowDemoSession?)oldValue, (WorkflowDemoSession?)newValue);
    }

    private void AttachSession(WorkflowDemoSession? oldSession, WorkflowDemoSession? newSession)
    {
        DetachSession(oldSession);
        _session = newSession;
        _drawable.Session = newSession;
        Rebuild();

        if (newSession is null)
        {
            return;
        }

        newSession.Tree.Nodes.CollectionChanged += OnNodesCollectionChanged;
        newSession.Tree.Links.CollectionChanged += OnLinksCollectionChanged;
        _controllerNotifier = newSession.Controller;
        _controllerNotifier.PropertyChanged += OnControllerPropertyChanged;

        foreach (var node in newSession.Tree.Nodes)
        {
            SubscribeNode(node);
        }
    }

    private void DetachSession(WorkflowDemoSession? session)
    {
        if (session is null)
        {
            return;
        }

        session.Tree.Nodes.CollectionChanged -= OnNodesCollectionChanged;
        session.Tree.Links.CollectionChanged -= OnLinksCollectionChanged;

        if (_controllerNotifier is not null)
        {
            _controllerNotifier.PropertyChanged -= OnControllerPropertyChanged;
            _controllerNotifier = null;
        }

        foreach (var node in session.Tree.Nodes)
        {
            UnsubscribeNode(node);
        }

        foreach (var card in _cards.Values)
        {
            card.Disconnect();
        }

        _cards.Clear();
        _nodesLayer.Children.Clear();
    }

    private void OnNodesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (var item in e.OldItems.OfType<IWorkflowNodeViewModel>())
            {
                UnsubscribeNode(item);
            }
        }

        if (e.NewItems is not null)
        {
            foreach (var item in e.NewItems.OfType<IWorkflowNodeViewModel>())
            {
                SubscribeNode(item);
            }
        }

        RunOnMainThread(Rebuild);
    }

    private void OnLinksCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => QueueLinksInvalidate();

    private void OnControllerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ControllerViewModel.IsActive))
        {
            RunOnMainThread(RefreshAllCards);
        }
    }

    private void SubscribeNode(IWorkflowNodeViewModel node)
    {
        if (node is INotifyPropertyChanged notify)
        {
            notify.PropertyChanged += OnNodePropertyChanged;
        }
    }

    private void UnsubscribeNode(IWorkflowNodeViewModel node)
    {
        if (node is INotifyPropertyChanged notify)
        {
            notify.PropertyChanged -= OnNodePropertyChanged;
        }
    }

    private void OnNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is IWorkflowNodeViewModel node && e.PropertyName is nameof(IWorkflowNodeViewModel.Anchor) or nameof(IWorkflowNodeViewModel.Size))
        {
            RunOnMainThread(() =>
            {
                UpdateNodeLayout(node);
                QueueLinksInvalidateCore();
            });
        }
    }

    private void RunOnMainThread(Action action)
    {
        if (MainThread.IsMainThread)
        {
            action();
            return;
        }

        MainThread.BeginInvokeOnMainThread(action);
    }

    private void QueueLinksInvalidate()
        => RunOnMainThread(QueueLinksInvalidateCore);

    private void QueueLinksInvalidateCore()
    {
        if (_linksInvalidateQueued)
        {
            return;
        }

        _linksInvalidateQueued = true;

        if (Dispatcher is not null)
        {
            Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(16), () =>
            {
                _linksInvalidateQueued = false;
                _linksView.Invalidate();
            });
            return;
        }

        _linksInvalidateQueued = false;
        _linksView.Invalidate();
    }

    private void RefreshAllCards()
    {
        foreach (var card in _cards.Values)
        {
            card.RefreshVisualState();
        }
    }

    private void Rebuild()
    {
        _nodesLayer.Children.Clear();
        foreach (var card in _cards.Values)
        {
            card.Disconnect();
        }

        _cards.Clear();

        if (_session is null)
        {
            WidthRequest = -1;
            HeightRequest = -1;
            QueueLinksInvalidateCore();
            return;
        }

        var visualNodes = _session.Tree.Nodes.ToList();
        if (visualNodes.Count == 0)
        {
            return;
        }

        var width = Math.Max(1280, visualNodes.Max(x => x.Anchor.Left + x.Size.Width) + 80);
        var height = Math.Max(760, visualNodes.Max(x => x.Anchor.Top + x.Size.Height) + 80);

        WidthRequest = width;
        HeightRequest = height;
        _root.WidthRequest = width;
        _root.HeightRequest = height;
        _linksView.WidthRequest = width;
        _linksView.HeightRequest = height;
        _nodesLayer.WidthRequest = width;
        _nodesLayer.HeightRequest = height;

        foreach (var node in visualNodes)
        {
            var card = new WorkflowNodeCardView(node, IsWorkflowActive);
            _cards[node] = card;
            _nodesLayer.Children.Add(card);
            UpdateNodeLayout(node);
        }

        QueueLinksInvalidateCore();
    }

    private void UpdateNodeLayout(IWorkflowNodeViewModel node)
    {
        if (!_cards.TryGetValue(node, out var card))
        {
            return;
        }

        AbsoluteLayout.SetLayoutBounds(card, new Rect(node.Anchor.Left, node.Anchor.Top, node.Size.Width, node.Size.Height));
    }

    private bool IsWorkflowActive()
        => _session?.Controller.IsActive == true;

    private sealed class WorkflowSurfaceDrawable : IDrawable
    {
        public WorkflowDemoSession? Session { get; set; }

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            canvas.SaveState();
            canvas.FillColor = Color.FromArgb("#0F172A");
            canvas.FillRectangle(dirtyRect);
            DrawGrid(canvas, dirtyRect);

            if (Session is null)
            {
                canvas.RestoreState();
                return;
            }

            foreach (var link in Session.Tree.Links.Where(x => x.IsVisible))
            {
                DrawLink(canvas, link);
            }

            canvas.RestoreState();
        }

        private static void DrawGrid(ICanvas canvas, RectF rect)
        {
            canvas.StrokeColor = Color.FromArgb("#1E293B");
            canvas.StrokeSize = 1;

            for (var x = 0; x < rect.Width; x += 40)
            {
                canvas.DrawLine(x, 0, x, rect.Height);
            }

            for (var y = 0; y < rect.Height; y += 40)
            {
                canvas.DrawLine(0, y, rect.Width, y);
            }
        }

        private static void DrawLink(ICanvas canvas, IWorkflowLinkViewModel link)
        {
            var startX = (float)link.Sender.Anchor.Left;
            var startY = (float)link.Sender.Anchor.Top;
            var endX = (float)link.Receiver.Anchor.Left;
            var endY = (float)link.Receiver.Anchor.Top;
            var controlOffset = Math.Max(80, Math.Abs(endX - startX) * 0.45f);

            var path = new PathF();
            path.MoveTo(startX, startY);
            path.CurveTo(startX + controlOffset, startY, endX - controlOffset, endY, endX, endY);

            canvas.StrokeColor = Color.FromArgb("#22D3EE");
            canvas.StrokeSize = 4;
            canvas.DrawPath(path);

            canvas.FillColor = Color.FromArgb("#67E8F9");
            canvas.FillCircle(startX, startY, 5);
            canvas.FillCircle(endX, endY, 5);
        }
    }

    private sealed class WorkflowNodeCardView : Border
    {
        private readonly IWorkflowNodeViewModel _node;
        private readonly Func<bool> _isWorkflowActive;
        private readonly Label _stateLabel;
        private double _lastPanX;
        private double _lastPanY;

        public WorkflowNodeCardView(IWorkflowNodeViewModel node, Func<bool> isWorkflowActive)
        {
            _node = node;
            _isWorkflowActive = isWorkflowActive;
            BindingContext = node;
            Padding = new Thickness(14);
            StrokeThickness = 1;
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(18) };
            Shadow = new Shadow
            {
                Brush = Brush.Black,
                Offset = new Point(0, 10),
                Radius = 16,
                Opacity = 0.28f
            };

            _stateLabel = new Label
            {
                FontSize = 12,
                FontAttributes = FontAttributes.Bold,
                HorizontalTextAlignment = TextAlignment.End,
                VerticalTextAlignment = TextAlignment.Center
            };

            Content = node is ControllerViewModel controller
                ? BuildControllerCard(controller)
                : BuildNodeCard((NodeViewModel)node);

            if (_node is INotifyPropertyChanged notify)
            {
                notify.PropertyChanged += OnNodePropertyChanged;
            }

            GestureRecognizers.Add(new PanGestureRecognizer());
            if (GestureRecognizers[0] is PanGestureRecognizer panGesture)
            {
                panGesture.PanUpdated += OnPanUpdated;
            }

            RefreshVisualState();
        }

        public void Disconnect()
        {
            if (_node is INotifyPropertyChanged notify)
            {
                notify.PropertyChanged -= OnNodePropertyChanged;
            }

            if (GestureRecognizers.FirstOrDefault() is PanGestureRecognizer panGesture)
            {
                panGesture.PanUpdated -= OnPanUpdated;
            }
        }

        private View BuildControllerCard(ControllerViewModel controller)
        {
            var title = new Label
            {
                Text = "Network Flow Controller",
                FontSize = 20,
                FontAttributes = FontAttributes.Bold,
                TextColor = Colors.White
            };

            _stateLabel.SetBinding(Label.TextProperty, nameof(ControllerViewModel.IsActive), stringFormat: "State: {0}");

            var seed = new Label { TextColor = Color.FromArgb("#D1D5DB"), LineBreakMode = LineBreakMode.WordWrap };
            seed.SetBinding(Label.TextProperty, nameof(ControllerViewModel.SeedPayload), stringFormat: "Seed: {0}");

            var mode = new Label { TextColor = Color.FromArgb("#D1D5DB") };
            mode.SetBinding(Label.TextProperty, nameof(ControllerViewModel.BroadcastMode), stringFormat: "Mode: {0}");

            Grid.SetColumn(_stateLabel, 1);

            return new VerticalStackLayout
            {
                Spacing = 10,
                Children =
                {
                    new Grid
                    {
                        ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)],
                        Children = { title, _stateLabel }
                    },
                    seed,
                    mode,
                    new Label
                    {
                        Text = "Controller 会触发整条请求链路，并把执行日志同步到下方。",
                        TextColor = Color.FromArgb("#CBD5E1"),
                        LineBreakMode = LineBreakMode.WordWrap
                    }
                }
            };
        }

        private View BuildNodeCard(NodeViewModel node)
        {
            var title = new Label
            {
                FontSize = 18,
                FontAttributes = FontAttributes.Bold,
                TextColor = Colors.White
            };
            title.SetBinding(Label.TextProperty, nameof(NodeViewModel.Title));

            var order = new Label
            {
                FontAttributes = FontAttributes.Bold,
                TextColor = Color.FromArgb("#FCD34D"),
                HorizontalTextAlignment = TextAlignment.End
            };
            order.SetBinding(Label.TextProperty, nameof(NodeViewModel.ExecutionOrderText));

            Grid.SetColumn(order, 1);

            _stateLabel.SetBinding(Label.TextProperty, nameof(NodeViewModel.LastStatus), stringFormat: "Status: {0}");

            var method = new Label { TextColor = Color.FromArgb("#BAE6FD"), FontAttributes = FontAttributes.Bold };
            method.SetBinding(Label.TextProperty, nameof(NodeViewModel.Method), stringFormat: "Method: {0}");

            var url = new Label { TextColor = Color.FromArgb("#E2E8F0"), LineBreakMode = LineBreakMode.TailTruncation, MaxLines = 2 };
            url.SetBinding(Label.TextProperty, nameof(NodeViewModel.Url), stringFormat: "URL: {0}");

            var duration = new Label { TextColor = Color.FromArgb("#CBD5E1") };
            duration.SetBinding(Label.TextProperty, nameof(NodeViewModel.LastDuration), stringFormat: "Duration: {0}");

            var trace = new Label { TextColor = Color.FromArgb("#CBD5E1"), LineBreakMode = LineBreakMode.TailTruncation, MaxLines = 2 };
            trace.SetBinding(Label.TextProperty, nameof(NodeViewModel.LastExecutionTrace), stringFormat: "Trace: {0}");

            var preview = new Label { TextColor = Color.FromArgb("#E2E8F0"), LineBreakMode = LineBreakMode.TailTruncation, MaxLines = 3 };
            preview.SetBinding(Label.TextProperty, nameof(NodeViewModel.LastResponsePreview), stringFormat: "Preview: {0}");

            return new VerticalStackLayout
            {
                Spacing = 8,
                Children =
                {
                    new Grid
                    {
                        ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)],
                        Children = { title, order }
                    },
                    _stateLabel,
                    method,
                    url,
                    duration,
                    trace,
                    preview
                }
            };
        }

        private void OnNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(NodeViewModel.IsRunning)
                or nameof(NodeViewModel.LastStatus)
                or nameof(NodeViewModel.LastError)
                or nameof(ControllerViewModel.IsActive))
            {
                MainThread.BeginInvokeOnMainThread(RefreshVisualState);
            }
        }

        public void RefreshVisualState()
        {
            if (_node is ControllerViewModel controller)
            {
                BackgroundColor = controller.IsActive ? Color.FromArgb("#0F766E") : Color.FromArgb("#1E293B");
                Stroke = new SolidColorBrush(controller.IsActive ? Color.FromArgb("#67E8F9") : Color.FromArgb("#475569"));
                _stateLabel.TextColor = controller.IsActive ? Color.FromArgb("#A7F3D0") : Color.FromArgb("#CBD5E1");
                return;
            }

            var node = (NodeViewModel)_node;
            var (background, stroke, foreground) = ResolveNodePalette(node, _isWorkflowActive());
            BackgroundColor = background;
            Stroke = new SolidColorBrush(stroke);
            _stateLabel.TextColor = foreground;
        }

        private void OnPanUpdated(object? sender, PanUpdatedEventArgs e)
        {
            switch (e.StatusType)
            {
                case GestureStatus.Started:
                    _lastPanX = 0;
                    _lastPanY = 0;
                    break;
                case GestureStatus.Running:
                    var deltaX = e.TotalX - _lastPanX;
                    var deltaY = e.TotalY - _lastPanY;
                    _lastPanX = e.TotalX;
                    _lastPanY = e.TotalY;
                    _node.MoveCommand.Execute(new VeloxDev.Core.WorkflowSystem.Offset(deltaX, deltaY));
                    break;
                case GestureStatus.Canceled:
                case GestureStatus.Completed:
                    _lastPanX = 0;
                    _lastPanY = 0;
                    break;
            }
        }

        private static (Color Background, Color Stroke, Color Foreground) ResolveNodePalette(NodeViewModel node, bool isWorkflowActive)
        {
            if (node.IsRunning)
            {
                return (Color.FromArgb("#1D4ED8"), Color.FromArgb("#93C5FD"), Color.FromArgb("#DBEAFE"));
            }

            if (!string.IsNullOrWhiteSpace(node.LastError))
            {
                return (Color.FromArgb("#7F1D1D"), Color.FromArgb("#FCA5A5"), Color.FromArgb("#FECACA"));
            }

            if (isWorkflowActive && (node.LastStatus.StartsWith("2") || node.LastStatus.StartsWith("3")))
            {
                return (Color.FromArgb("#14532D"), Color.FromArgb("#86EFAC"), Color.FromArgb("#DCFCE7"));
            }

            return (Color.FromArgb("#1E293B"), Color.FromArgb("#475569"), Color.FromArgb("#CBD5E1"));
        }
    }
}
