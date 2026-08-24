using System.ComponentModel;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Media;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>Reusable workflow surface control (code-first). Composes the surface border,
/// grid decorator (which wraps the ScrollViewer), the node Canvas and an optional minimap
/// overlay, registers the PART_* names the surface behavior resolves, and binds the node
/// Canvas's ViewPool to the tree's Helper.VisibleItems.</summary>
public class WorkflowTreeView : Grid
{
    public Border PART_SurfaceBorder { get; }
    public ScrollViewer PART_ScrollViewer { get; }
    public Canvas PART_Canvas { get; }
    public FrameworkElement PART_GridDecorator { get; private set; }
    public FrameworkElement? PART_MinimapOverlay { get; private set; }

    /// <summary>Factory for node/link views. Set before assigning <see cref="ViewModel"/>.</summary>
    public IWorkflowTemplateSelector? TemplateSelector { get; set; }

    /// <summary>The workflow tree. Setting it also sets DataContext (the surface behavior reads DataContext).</summary>
    public IWorkflowTreeViewModel? ViewModel
    {
        get => DataContext as IWorkflowTreeViewModel;
        set => DataContext = value;
    }

    /// <summary>Swaps in a styled grid decorator (must implement <see cref="IWorkflowGridDecorator"/>).</summary>
    public IWorkflowGridDecorator? GridDecorator
    {
        set
        {
            if (value is FrameworkElement fe)
            {
                SwapGridDecorator(fe);
            }
        }
    }

    /// <summary>Adds a styled minimap overlay (must implement <see cref="IWorkflowMinimapOverlay"/>).</summary>
    public IWorkflowMinimapOverlay? MinimapOverlay
    {
        set
        {
            if (value is FrameworkElement fe)
            {
                AddMinimap(fe);
            }
        }
    }

    private IWorkflowTreeViewModel? _tree;
    private INotifyPropertyChanged? _layoutNotify;

    public WorkflowTreeView()
    {
        NameScope.SetNameScope(this, new NameScope());

        PART_ScrollViewer = new ScrollViewer
        {
            Name = "PART_ScrollViewer",
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            // Transparent so the grid decorator's grid shows through.
            Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
        };
        PART_Canvas = new Canvas { Name = "PART_Canvas", Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)) };
        PART_ScrollViewer.Content = PART_Canvas;

        PART_GridDecorator = new WorkflowGridDecorator { Name = "PART_GridDecorator" };
        ((WorkflowGridDecorator)PART_GridDecorator).Child = PART_ScrollViewer;

        PART_SurfaceBorder = new Border
        {
            Name = "PART_SurfaceBorder",
            Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Child = PART_GridDecorator,
        };

        Children.Add(PART_SurfaceBorder);

        RegisterName("PART_SurfaceBorder", PART_SurfaceBorder);
        RegisterName("PART_GridDecorator", PART_GridDecorator);
        RegisterName("PART_ScrollViewer", PART_ScrollViewer);
        RegisterName("PART_Canvas", PART_Canvas);

        WorkflowSurfaceBehavior.SetIsEnabled(this, true);
        WorkflowSurfaceBehavior.SetScrollViewerName(this, "PART_ScrollViewer");
        WorkflowSurfaceBehavior.SetCanvasName(this, "PART_Canvas");
        WorkflowSurfaceBehavior.SetGridDecoratorName(this, "PART_GridDecorator");
        WorkflowSurfaceBehavior.SetPointerPressSourceName(this, "PART_Canvas");
        WorkflowSurfaceBehavior.SetMinimapOverlayName(this, "PART_MinimapOverlay");

        DataContextChanged += OnDataContextChanged;
    }

    private void SwapGridDecorator(FrameworkElement decorator)
    {
        if (PART_GridDecorator is Decorator old && ReferenceEquals(old.Child, PART_ScrollViewer))
        {
            old.Child = null;
        }

        if (decorator is Decorator d)
        {
            d.Child = PART_ScrollViewer;
        }

        PART_SurfaceBorder.Child = decorator;
        PART_GridDecorator = decorator;
        decorator.Name = "PART_GridDecorator";
        UnregisterName("PART_GridDecorator");
        RegisterName("PART_GridDecorator", decorator);
    }

    private void AddMinimap(FrameworkElement overlay)
    {
        overlay.Name = "PART_MinimapOverlay";
        overlay.Width = 200;
        overlay.Height = 140;
        overlay.HorizontalAlignment = HorizontalAlignment.Right;
        overlay.VerticalAlignment = VerticalAlignment.Top;
        overlay.Margin = new Thickness(0, 40, 16, 0);
        if (overlay is WorkflowMinimapOverlay builtin)
        {
            builtin.ScrollViewer = PART_ScrollViewer;
        }

        Children.Add(overlay);
        PART_MinimapOverlay = overlay;
        RegisterName("PART_MinimapOverlay", overlay);
    }

    private void OnDataContextChanged(object? sender, DependencyPropertyChangedEventArgs e)
    {
        UnsubscribeTree();
        _tree = DataContext as IWorkflowTreeViewModel;
        if (_tree is null)
        {
            return;
        }

        UpdateCanvasSize();
        if (TemplateSelector is not null)
        {
            ViewPool.SetTemplateSelector(PART_Canvas, TemplateSelector);
        }

        ViewPool.SetItemsSource(PART_Canvas, _tree.GetHelper().VisibleItems);
        if (_tree is INotifyPropertyChanged notify)
        {
            notify.PropertyChanged += OnTreePropertyChanged;
        }

        SubscribeLayout();
        WorkflowSurfaceBehavior.Refresh(this);
    }

    private void UnsubscribeTree()
    {
        if (_tree is INotifyPropertyChanged notify)
        {
            notify.PropertyChanged -= OnTreePropertyChanged;
        }

        UnsubscribeLayout();
    }

    /// <summary>Subscribes to the tree's CanvasLayout so canvas size / slot anchors stay in sync when
    /// the layout offset changes (pan / auto-grow), which does NOT change the tree's "Layout" reference.</summary>
    private void SubscribeLayout()
    {
        UnsubscribeLayout();
        if (_tree?.Layout is INotifyPropertyChanged notify)
        {
            _layoutNotify = notify;
            notify.PropertyChanged += OnLayoutPropertyChanged;
        }
    }

    private void UnsubscribeLayout()
    {
        if (_layoutNotify is not null)
        {
            _layoutNotify.PropertyChanged -= OnLayoutPropertyChanged;
            _layoutNotify = null;
        }
    }

    private void OnTreePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is "Layout")
        {
            SubscribeLayout();
            UpdateCanvasSize();
            WorkflowSurfaceBehavior.Refresh(this);
        }
    }

    private void OnLayoutPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is "ActualSize" or "ActualOffset")
        {
            UpdateCanvasSize();
            WorkflowSurfaceBehavior.Refresh(this);
        }
    }

    private void UpdateCanvasSize()
    {
        if (_tree is null)
        {
            return;
        }

        PART_Canvas.Width = _tree.Layout.ActualSize.Width;
        PART_Canvas.Height = _tree.Layout.ActualSize.Height;
        PART_Canvas.InvalidateMeasure();
    }
}
