using Demo.ViewModels;
using Demo.Workflow;
using Microsoft.Maui.Controls.Shapes;
using System.Collections.Specialized;
using System.ComponentModel;
using VeloxDev.WorkflowSystem;

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

        var width = Math.Max(1280, visualNodes.Max(x => x.Anchor.Horizontal + x.Size.Width) + 80);
        var height = Math.Max(760, visualNodes.Max(x => x.Anchor.Vertical + x.Size.Height) + 80);

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
            var card = new WorkflowNodeCardView(this, node, IsWorkflowActive);
            _cards[node] = card;
            _nodesLayer.Children.Add(card);
            UpdateNodeLayout(node);
        }

        // Add slot hotspots to _nodesLayer directly (above cards) so they
        // are not clipped by card Borders and can receive touch/pointer input.
        foreach (var card in _cards.Values)
        {
            card.CreateExternalSlotHotspots();
            foreach (var hotspot in card.ExternalHotspots)
            {
                _nodesLayer.Children.Add(hotspot);
            }
        }

        SyncAllSlotAnchors();
        QueueLinksInvalidateCore();
    }

    private void UpdateNodeLayout(IWorkflowNodeViewModel node)
    {
        if (!_cards.TryGetValue(node, out var card))
        {
            return;
        }

        AbsoluteLayout.SetLayoutBounds(card, new Rect(node.Anchor.Horizontal, node.Anchor.Vertical, node.Size.Width, node.Size.Height));
        card.UpdateHotspotPositions();
        SyncNodeSlotAnchors(node);
    }

    private void BeginConnection(IWorkflowSlotViewModel slot)
    {
        if (_session is null)
        {
            return;
        }

        slot.SendConnectionCommand.Execute(null);
        QueueLinksInvalidateCore();
    }

    private void UpdateConnectionPointer(Anchor anchor)
    {
        if (_session is null || !_session.Tree.VirtualLink.IsVisible)
        {
            return;
        }

        _session.Tree.SetPointerCommand.Execute(anchor);
        QueueLinksInvalidateCore();
    }

    private void CompleteConnection(Anchor anchor, IWorkflowSlotViewModel sourceSlot)
    {
        if (_session is null)
        {
            return;
        }

        var receiver = HitTestSlot(anchor, sourceSlot);
        if (receiver is not null)
        {
            receiver.ReceiveConnectionCommand.Execute(null);
        }
        else
        {
            _session.Tree.ResetVirtualLinkCommand.Execute(null);
        }

        QueueLinksInvalidateCore();
    }

    private IWorkflowSlotViewModel? HitTestSlot(Anchor anchor, IWorkflowSlotViewModel? exclude = null)
    {
        if (_session is null)
        {
            return null;
        }

        const double radius = 18d;
        var radiusSquared = radius * radius;

        foreach (var slot in EnumerateSlots(_session))
        {
            if (ReferenceEquals(slot, exclude))
            {
                continue;
            }

            var dx = slot.Anchor.Horizontal - anchor.Horizontal;
            var dy = slot.Anchor.Vertical - anchor.Vertical;
            if ((dx * dx) + (dy * dy) <= radiusSquared)
            {
                return slot;
            }
        }

        return null;
    }

    private static IEnumerable<IWorkflowSlotViewModel> EnumerateSlots(WorkflowDemoSession session)
    {
        foreach (var node in session.Tree.Nodes)
        {
            if (node is NodeViewModel workflowNode)
            {
                if (workflowNode.InputSlot is not null)
                {
                    yield return workflowNode.InputSlot;
                }

                if (workflowNode.OutputSlot is not null)
                {
                    yield return workflowNode.OutputSlot;
                }

                continue;
            }

            if (node is ControllerViewModel controller && controller.OutputSlot is not null)
            {
                yield return controller.OutputSlot;
            }
        }
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

            if (Session.Tree.VirtualLink.IsVisible)
            {
                DrawLink(canvas, Session.Tree.VirtualLink, Color.FromArgb("#E2E8F0"));
            }

            foreach (var node in Session.Tree.Nodes)
            {
                DrawSlot(canvas, (node as NodeViewModel)?.InputSlot);
                DrawSlot(canvas, (node as NodeViewModel)?.OutputSlot);
                DrawSlot(canvas, (node as ControllerViewModel)?.OutputSlot);
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

        private static void DrawLink(ICanvas canvas, IWorkflowLinkViewModel link, Color? strokeColor = null)
        {
            var startX = (float)link.Sender.Anchor.Horizontal;
            var startY = (float)link.Sender.Anchor.Vertical;
            var endX = (float)link.Receiver.Anchor.Horizontal;
            var endY = (float)link.Receiver.Anchor.Vertical;
            var controlOffset = Math.Max(80, Math.Abs(endX - startX) * 0.45f);

            var path = new PathF();
            path.MoveTo(startX, startY);
            path.CurveTo(startX + controlOffset, startY, endX - controlOffset, endY, endX, endY);

            canvas.StrokeColor = strokeColor ?? Color.FromArgb("#22D3EE");
            canvas.StrokeSize = 4;
            canvas.DrawPath(path);

            canvas.FillColor = Color.FromArgb("#67E8F9");
            canvas.FillCircle(startX, startY, 5);
            canvas.FillCircle(endX, endY, 5);
        }

        private static void DrawSlot(ICanvas canvas, IWorkflowSlotViewModel? slot)
        {
            if (slot is null)
            {
                return;
            }

            canvas.FillColor = ResolveSlotColor(slot.State);
            canvas.FillCircle((float)slot.Anchor.Horizontal, (float)slot.Anchor.Vertical, 10);
            canvas.StrokeColor = Colors.White;
            canvas.StrokeSize = 1.5f;
            canvas.DrawCircle((float)slot.Anchor.Horizontal, (float)slot.Anchor.Vertical, 10);
        }

        private static Color ResolveSlotColor(SlotState state)
            => state switch
            {
                var value when value.HasFlag(SlotState.Sender) && value.HasFlag(SlotState.Receiver) => Colors.Violet,
                var value when value.HasFlag(SlotState.Sender) => Colors.Tomato,
                var value when value.HasFlag(SlotState.Receiver) => Colors.Lime,
                _ => Colors.White,
            };
    }

    private void SyncAllSlotAnchors()
    {
        if (_session is null)
        {
            return;
        }

        foreach (var node in _session.Tree.Nodes)
        {
            SyncNodeSlotAnchors(node);
        }
    }

    private static void SyncNodeSlotAnchors(IWorkflowNodeViewModel node)
    {
        if (node is NodeViewModel workflowNode)
        {
            SyncSlotAnchor(node, workflowNode.InputSlot, isInput: true);
            SyncSlotAnchor(node, workflowNode.OutputSlot, isInput: false);
            return;
        }

        if (node is ControllerViewModel controller)
        {
            SyncSlotAnchor(node, controller.OutputSlot, isInput: false);
        }
    }

    private static void SyncSlotAnchor(IWorkflowNodeViewModel node, IWorkflowSlotViewModel? slot, bool isInput)
    {
        if (slot is null)
        {
            return;
        }

        slot.Anchor = new Anchor(
            node.Anchor.Horizontal + (isInput ? 0 : node.Size.Width),
            node.Anchor.Vertical + (node.Size.Height / 2),
            slot.Anchor.Layer);
    }

    private sealed class WorkflowNodeCardView : Border
    {
        private readonly WorkflowSurfaceView _owner;
        private readonly IWorkflowNodeViewModel _node;
        private readonly Func<bool> _isWorkflowActive;
        private readonly Label _stateLabel;
        private double _lastPanX;
        private double _lastPanY;

        public WorkflowNodeCardView(WorkflowSurfaceView owner, IWorkflowNodeViewModel node, Func<bool> isWorkflowActive)
        {
            _owner = owner;
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

            Content = BuildInteractiveCard();

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

        private readonly List<View> _externalHotspots = [];

        public IReadOnlyList<View> ExternalHotspots => _externalHotspots;

        private View BuildInteractiveCard()
        {
            var root = new Grid();
            root.Children.Add(_node is ControllerViewModel controller
                ? BuildControllerCard(controller)
                : BuildNodeCard((NodeViewModel)_node));

            // Slot hotspots are added to the parent _nodesLayer directly
            // so they are NOT clipped by this Border and can receive gestures
            // even when they overflow outside the node bounds.
            return root;
        }

        public void CreateExternalSlotHotspots()
        {
            if (_node is NodeViewModel workflowNode)
            {
                if (workflowNode.InputSlot is not null)
                    _externalHotspots.Add(CreateSlotHotspot(workflowNode.InputSlot, true));
                if (workflowNode.OutputSlot is not null)
                    _externalHotspots.Add(CreateSlotHotspot(workflowNode.OutputSlot, false));
            }
            else if (_node is ControllerViewModel controllerNode && controllerNode.OutputSlot is not null)
            {
                _externalHotspots.Add(CreateSlotHotspot(controllerNode.OutputSlot, false));
            }
        }

        public void UpdateHotspotPositions()
        {
            int idx = 0;
            if (_node is NodeViewModel wn)
            {
                if (wn.InputSlot is not null && idx < _externalHotspots.Count)
                {
                    var h = _externalHotspots[idx++];
                    AbsoluteLayout.SetLayoutBounds(h, new Rect(
                        _node.Anchor.Horizontal - 14,
                        _node.Anchor.Vertical + (_node.Size.Height / 2) - 14,
                        28, 28));
                }
                if (wn.OutputSlot is not null && idx < _externalHotspots.Count)
                {
                    var h = _externalHotspots[idx++];
                    AbsoluteLayout.SetLayoutBounds(h, new Rect(
                        _node.Anchor.Horizontal + _node.Size.Width - 14,
                        _node.Anchor.Vertical + (_node.Size.Height / 2) - 14,
                        28, 28));
                }
            }
            else if (_node is ControllerViewModel ctrl && ctrl.OutputSlot is not null && idx < _externalHotspots.Count)
            {
                var h = _externalHotspots[idx++];
                AbsoluteLayout.SetLayoutBounds(h, new Rect(
                    _node.Anchor.Horizontal + _node.Size.Width - 14,
                    _node.Anchor.Vertical + (_node.Size.Height / 2) - 14,
                    28, 28));
            }
        }

        private View CreateSlotHotspot(IWorkflowSlotViewModel slot, bool isInput)
        {
            var hotspot = new Grid
            {
                WidthRequest = 28,
                HeightRequest = 28,
                HorizontalOptions = isInput ? LayoutOptions.Start : LayoutOptions.End,
                VerticalOptions = LayoutOptions.Center,
                Margin = isInput ? new Thickness(-14, 0, 0, 0) : new Thickness(0, 0, -14, 0),
                BackgroundColor = Colors.Transparent
            };

            var pan = new PanGestureRecognizer();
            pan.PanUpdated += (_, e) =>
            {
                var anchor = new Anchor(
                    slot.Anchor.Horizontal + e.TotalX,
                    slot.Anchor.Vertical + e.TotalY,
                    slot.Anchor.Layer);

                switch (e.StatusType)
                {
                    case GestureStatus.Started:
                        _owner.BeginConnection(slot);
                        _owner.UpdateConnectionPointer(anchor);
                        break;
                    case GestureStatus.Running:
                        _owner.UpdateConnectionPointer(anchor);
                        break;
                    case GestureStatus.Completed:
                    case GestureStatus.Canceled:
                        _owner.CompleteConnection(anchor, slot);
                        break;
                }
            };

            hotspot.GestureRecognizers.Add(pan);
            return hotspot;
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

            _externalHotspots.Clear();
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
                    new Label
                    {
                        Text = "Controller 会触发整条请求链路。后续节点是否继续广播，仅由节点自身决定。",
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

            var delay = new Label { TextColor = Color.FromArgb("#BAE6FD"), FontAttributes = FontAttributes.Bold };
            delay.SetBinding(Label.TextProperty, nameof(NodeViewModel.DelayMilliseconds), stringFormat: "Delay: {0} ms");

            var queue = new Label { TextColor = Color.FromArgb("#E2E8F0") };
            queue.SetBinding(Label.TextProperty, nameof(NodeViewModel.WaitCount), stringFormat: "Queue: {0}");

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
                    delay,
                    queue,
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

        private double _panStartX;
        private double _panStartY;

        private void OnPanUpdated(object? sender, PanUpdatedEventArgs e)
        {
            switch (e.StatusType)
            {
                case GestureStatus.Started:
                    _lastPanX = 0;
                    _lastPanY = 0;
                    _panStartX = _node.Anchor.Horizontal;
                    _panStartY = _node.Anchor.Vertical;
                    break;
                case GestureStatus.Running:
                    // Use lightweight Translation for visual feedback during drag
                    TranslationX = e.TotalX;
                    TranslationY = e.TotalY;
                    // Also move hotspots visually
                    foreach (var h in _externalHotspots)
                    {
                        h.TranslationX = e.TotalX;
                        h.TranslationY = e.TotalY;
                    }
                    _lastPanX = e.TotalX;
                    _lastPanY = e.TotalY;
                    break;
                case GestureStatus.Canceled:
                case GestureStatus.Completed:
                    // Reset translation and commit final position via MoveCommand
                    TranslationX = 0;
                    TranslationY = 0;
                    foreach (var h in _externalHotspots)
                    {
                        h.TranslationX = 0;
                        h.TranslationY = 0;
                    }
                    if (_lastPanX != 0 || _lastPanY != 0)
                    {
                        _node.MoveCommand.Execute(new Offset(_lastPanX, _lastPanY));
                    }
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
