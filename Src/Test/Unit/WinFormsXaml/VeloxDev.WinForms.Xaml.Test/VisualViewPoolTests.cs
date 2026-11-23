using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Drawing;
using System.Threading.Tasks;
using VeloxDev.MVVM;
using VeloxDev.WinForms.Xaml;
using VeloxDev.WinForms.Xaml.Visuals;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.AttachedBehaviors;
using VC = VeloxDev.WinForms.Xaml.Visuals.Controls;
using DrawingSize = System.Drawing.Size;
using XamlVisibility = VeloxDev.WinForms.Xaml.Controls.Visibility;

namespace VeloxDev.WinForms.Xaml.Test;

/// <summary>
/// The workflow-free visual <see cref="ViewPool"/>: one visual per item through the Core pipeline, a data context
/// per view, the selector path, recycling and z-ordering between links and nodes.
/// </summary>
[STATestClass]
public sealed class VisualViewPoolTests
{
    [TestMethod]
    public void Surface_ViewPoolMaterializesVisibleItems()
    {
        var tree = new TreeDefaultComponent();
        tree.SetHelper(new TreeHelper(200d));
        var node = new NodeDefaultComponent
        {
            Anchor = new Anchor(100d, 80d),
            Size = new WorkflowSystem.Size(200d, 120d),
        };
        tree.CreateNodeCommand.Execute(node);

        using var host = new VisualHost { Size = new DrawingSize(800, 500) };
        var root = new VC.UserControl();
        var canvas = new VC.Canvas { Name = "PART_Canvas", IsHitTestVisible = true };
        root.Content = canvas;
        host.Root = root;

        var selector = new Selector();
        ViewPool.SetTemplateSelector(canvas, selector);

        WorkflowSurfaceBehavior.SetCanvasName(host, "PART_Canvas");
        WorkflowSurfaceBehavior.SetWorkflowTree(host, tree);
        WorkflowSurfaceBehavior.SetIsEnabled(host, true);

        using var bitmap = new Bitmap(1, 1);
        using var graphics = Graphics.FromImage(bitmap);
        host.RenderTo(graphics, new DrawingSize(800, 500));
        host.RenderTo(graphics, new DrawingSize(800, 500));

        Assert.IsTrue(tree.GetHelper().VisibleItems.Count > 0, "the viewport virtualization ran");
        Assert.IsTrue(canvas.Children.Count > 0, "the pool materialized a view");
        Assert.IsInstanceOfType<VC.Border>(canvas.Children[0]);
    }

    [TestMethod]
    public void ViewPool_CreatesOneViewPerItemWithDataContext()
    {
        var canvas = new VC.Canvas();
        var items = new ObservableCollection<IWorkflowComponent>
        {
            new NodeDefaultComponent(),
            new NodeDefaultComponent(),
            new NodeDefaultComponent(),
        };

        ViewPool.SetTemplateSelector(canvas, new Selector());
        ViewPool.SetItemsSource(canvas, items);
        ViewPool.Refresh(canvas);

        Assert.HasCount(3, canvas.Children, "one view per source item");
        Assert.AreEqual(3, ViewPool.GetActiveViewCount(canvas));
        Assert.AreEqual(0, ViewPool.GetPendingCount(canvas), "the headless host materializes synchronously");
        for (var index = 0; index < items.Count; index++)
        {
            Assert.AreSame(items[index], ViewAt(canvas, index).DataContext,
                "each view receives its item as the data context");
        }
    }

    [TestMethod]
    public void ViewPool_RecyclesThroughCoreAndRebindsTheReusedView()
    {
        var canvas = new VC.Canvas();
        var first = new NodeDefaultComponent();
        var second = new NodeDefaultComponent();
        var items = new ObservableCollection<IWorkflowComponent> { first, second };

        ViewPool.SetTemplateSelector(canvas, new Selector());
        ViewPool.SetItemsSource(canvas, items);
        ViewPool.Refresh(canvas);

        var firstView = ViewAt(canvas, 0);
        items.RemoveAt(0);

        // Core 的池化视图留在画布上：只解除绑定并隐藏，等待同类型条目复用。
        Assert.HasCount(2, canvas.Children, "a removed view stays in the host for recycling");
        Assert.AreEqual(XamlVisibility.Collapsed, firstView.Visibility);
        Assert.IsNull(firstView.DataContext, "the removed item is released");
        Assert.AreEqual(1, ViewPool.GetActiveViewCount(canvas));

        var third = new NodeDefaultComponent();
        items.Add(third);

        Assert.HasCount(2, canvas.Children, "the recycled view is reused, not duplicated");
        Assert.AreSame(firstView, ViewAt(canvas, 0), "Core handed back the same view for the same item type");
        Assert.AreSame(third, firstView.DataContext, "the reused view rebinds its new item");
        Assert.AreEqual(XamlVisibility.Visible, firstView.Visibility);
        Assert.AreSame(second, ViewAt(canvas, 1).DataContext, "the untouched view keeps its item");
        Assert.AreEqual(2, ViewPool.GetActiveViewCount(canvas));
    }

    [TestMethod]
    public void ViewPool_ReusedViewRebindsItsLocalDataContextBinding()
    {
        var canvas = new VC.Canvas();
        var items = new ObservableCollection<IWorkflowComponent>
        {
            new ReuseItem(new ReuseModel("first")),
            new ReuseItem(new ReuseModel("second")),
        };
        ViewPool.SetTemplateSelector(canvas, new ReuseSelector());
        ViewPool.SetItemsSource(canvas, items);
        ViewPool.Refresh(canvas);

        var firstView = (ReuseView)ViewAt(canvas, 0);
        Assert.AreEqual("first", firstView.Inner.Text);

        items.RemoveAt(0);
        var third = new ReuseItem(new ReuseModel("third"));
        items.Add(third);

        var reused = canvas.Children.OfType<ReuseView>().Single(view => ReferenceEquals(view.DataContext, third));
        Assert.AreSame(firstView, reused, "the pool reuses the view for the same item type");
        Assert.AreEqual("third", reused.Inner.Text,
            "the child's DataContext binding follows the reused view's new item");
    }

    [TestMethod]
    public void ViewPool_OrdersLinksBelowNodesByZIndexThenSourceOrder()
    {
        var canvas = new VC.Canvas();
        var firstNode = new NodeDefaultComponent { Anchor = new Anchor(10d, 20d) };
        var secondNode = new NodeDefaultComponent { Anchor = new Anchor(30d, 40d) };
        var link = new LinkDefaultComponent();
        var items = new ObservableCollection<IWorkflowComponent> { firstNode, link, secondNode };

        ViewPool.SetTemplateSelector(canvas, new Selector());
        ViewPool.SetItemsSource(canvas, items);
        ViewPool.Refresh(canvas);

        Assert.HasCount(3, canvas.Children);
        Assert.AreSame(link, ViewAt(canvas, 0).DataContext, "the link is sorted below the nodes");
        Assert.AreSame(firstNode, ViewAt(canvas, 1).DataContext, "source order is kept between equal z-indexes");
        Assert.AreSame(secondNode, ViewAt(canvas, 2).DataContext);
        Assert.AreEqual(-1, VC.Panel.GetZIndex(ViewAt(canvas, 0)));
        Assert.AreEqual(0, VC.Panel.GetZIndex(ViewAt(canvas, 1)));
    }

    [TestMethod]
    public void ViewPool_SelectorCreatesOneViewPerLink()
    {
        var firstSender = new SlotDefaultComponent { Anchor = new Anchor(60d, 70d) };
        var firstReceiver = new SlotDefaultComponent { Anchor = new Anchor(300d, 140d) };
        var firstLink = new LinkDefaultComponent { Sender = firstSender, Receiver = firstReceiver };

        var secondSender = new SlotDefaultComponent { Anchor = new Anchor(20d, 25d) };
        var secondReceiver = new SlotDefaultComponent { Anchor = new Anchor(180d, 95d) };
        var secondLink = new LinkDefaultComponent { Sender = secondSender, Receiver = secondReceiver };

        var canvas = new VC.Canvas();
        var items = new ObservableCollection<IWorkflowComponent> { firstLink, secondLink };

        ViewPool.SetTemplateSelector(canvas, new LinkSelector());
        ViewPool.SetItemsSource(canvas, items);
        ViewPool.Refresh(canvas);

        Assert.HasCount(2, canvas.Children, "one link view per link item");

        var firstView = (LinkItemView)ViewAt(canvas, 0);
        Assert.AreSame(firstLink, firstView.DataContext, "the link view's data context is the link");
        Assert.AreEqual(60d, firstView.StartLeft, 0.01d, "the endpoints bind from Sender/Receiver");
        Assert.AreEqual(70d, firstView.StartTop, 0.01d);
        Assert.AreEqual(300d, firstView.EndLeft, 0.01d);
        Assert.AreEqual(140d, firstView.EndTop, 0.01d);

        var secondView = (LinkItemView)ViewAt(canvas, 1);
        Assert.AreSame(secondLink, secondView.DataContext);
        Assert.AreEqual(20d, secondView.StartLeft, 0.01d);
        Assert.AreEqual(95d, secondView.EndTop, 0.01d);
    }

    [TestMethod]
    public void ViewPool_ResolvesElementNameBindingsAfterParenting()
    {
        var canvas = new VC.Canvas { Name = "PART_Canvas", Width = 300d, Height = 200d };
        var items = new ObservableCollection<IWorkflowComponent> { new NodeDefaultComponent() };

        ViewPool.SetTemplateSelector(canvas, new ElementNameSelector());
        ViewPool.SetItemsSource(canvas, items);
        ViewPool.Refresh(canvas);

        var view = ViewAt(canvas, 0);
        Assert.AreEqual(300d, (double)view.GetValue(VisualElement.WidthProperty)!, 0.01d,
            "an ElementName binding resolves once the pool parents the view");
        Assert.AreEqual(200d, (double)view.GetValue(VisualElement.HeightProperty)!, 0.01d);
    }

    private static VisualElement ViewAt(VC.Canvas canvas, int index)
        => (VisualElement)canvas.Children[index];

    private sealed class ReuseItem(ReuseModel child) : IWorkflowComponent
    {
        private static readonly IVeloxCommand Close = new VeloxCommand((_, _) => Task.CompletedTask);

        public ReuseModel Child { get; } = child;

        public IVeloxCommand CloseCommand => Close;

        public event PropertyChangingEventHandler? PropertyChanging;

        public event PropertyChangedEventHandler? PropertyChanged;

        public void InitializeWorkflow()
        {
        }

        public void OnPropertyChanging(string propertyName)
            => PropertyChanging?.Invoke(this, new PropertyChangingEventArgs(propertyName));

        public void OnPropertyChanged(string propertyName)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private sealed class ReuseModel(string name)
    {
        public string Name { get; } = name;
    }

    private sealed class ReuseSelector : VisualTemplateSelector
    {
        public override VisualElement? SelectTemplate(object? item)
            => item is ReuseItem ? new ReuseView() : null;
    }

    private sealed class ReuseView : VC.Grid
    {
        public ReuseView()
        {
            Inner = new VC.TextBlock();
            VisualBindingOperations.Bind(
                Inner,
                VisualElement.DataContextProperty,
                new Binding(nameof(ReuseItem.Child)));
            Inner.SetBinding(VC.TextBlock.TextProperty, new Binding(nameof(ReuseModel.Name)));
            Add(Inner);
        }

        public VC.TextBlock Inner { get; }
    }

    private sealed class ElementNameSelector : VisualTemplateSelector
    {
        public override VisualElement? SelectTemplate(object? item) => new ElementNameView();
    }

    private sealed class ElementNameView : VisualElement
    {
        public ElementNameView()
        {
            VisualBindingOperations.Bind(this, WidthProperty, new Binding("Width") { ElementName = "PART_Canvas" });
            VisualBindingOperations.Bind(this, HeightProperty, new Binding("Height") { ElementName = "PART_Canvas" });
        }
    }

    private sealed class Selector : VisualTemplateSelector
    {
        public override VisualElement? SelectTemplate(object? item)
        {
            var view = new VC.Border { Background = new SolidColorBrush(Color.Red) };
            if (item is INodeComponent)
            {
                VisualBindingOperations.Bind(view, VisualElement.WidthProperty, new Binding("Size.Width"));
                VisualBindingOperations.Bind(view, VisualElement.HeightProperty, new Binding("Size.Height"));
                VisualBindingOperations.Bind(view, VC.Canvas.LeftProperty, new Binding("Anchor.Horizontal"));
                VisualBindingOperations.Bind(view, VC.Canvas.TopProperty, new Binding("Anchor.Vertical"));
                VisualBindingOperations.Bind(view, VC.Panel.ZIndexProperty, new Binding("Anchor.Layer"));
            }
            else if (item is ILinkComponent)
            {
                VC.Panel.SetZIndex(view, -1);
            }

            return view;
        }
    }

    private sealed class LinkSelector : VisualTemplateSelector
    {
        public override VisualElement? SelectTemplate(object? item)
        {
            if (item is not ILinkComponent)
            {
                return null;
            }

            var view = new LinkItemView();
            VisualBindingOperations.Bind(view, LinkItemView.StartLeftProperty, new Binding("Sender.Anchor.Horizontal"));
            VisualBindingOperations.Bind(view, LinkItemView.StartTopProperty, new Binding("Sender.Anchor.Vertical"));
            VisualBindingOperations.Bind(view, LinkItemView.EndLeftProperty, new Binding("Receiver.Anchor.Horizontal"));
            VisualBindingOperations.Bind(view, LinkItemView.EndTopProperty, new Binding("Receiver.Anchor.Vertical"));
            return view;
        }
    }

    private sealed class LinkItemView : VisualElement
    {
        public static readonly DependencyProperty StartLeftProperty = DependencyProperty.Register(
            nameof(StartLeft), typeof(double), typeof(LinkItemView), new PropertyMetadata(0d));

        public static readonly DependencyProperty StartTopProperty = DependencyProperty.Register(
            nameof(StartTop), typeof(double), typeof(LinkItemView), new PropertyMetadata(0d));

        public static readonly DependencyProperty EndLeftProperty = DependencyProperty.Register(
            nameof(EndLeft), typeof(double), typeof(LinkItemView), new PropertyMetadata(0d));

        public static readonly DependencyProperty EndTopProperty = DependencyProperty.Register(
            nameof(EndTop), typeof(double), typeof(LinkItemView), new PropertyMetadata(0d));

        public double StartLeft
        {
            get => (double)GetValue(StartLeftProperty)!;
            set => SetValue(StartLeftProperty, value);
        }

        public double StartTop
        {
            get => (double)GetValue(StartTopProperty)!;
            set => SetValue(StartTopProperty, value);
        }

        public double EndLeft
        {
            get => (double)GetValue(EndLeftProperty)!;
            set => SetValue(EndLeftProperty, value);
        }

        public double EndTop
        {
            get => (double)GetValue(EndTopProperty)!;
            set => SetValue(EndTopProperty, value);
        }
    }
}
