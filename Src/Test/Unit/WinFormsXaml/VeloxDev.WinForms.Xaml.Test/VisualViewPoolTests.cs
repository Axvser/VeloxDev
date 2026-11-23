using System.Collections.ObjectModel;
using System.Drawing;
using VeloxDev.WinForms.Xaml;
using VeloxDev.WinForms.Xaml.Visuals;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.AttachedBehaviors;
using VC = VeloxDev.WinForms.Xaml.Visuals.Controls;
using DrawingSize = System.Drawing.Size;

namespace VeloxDev.WinForms.Xaml.Test;

/// <summary>
/// The workflow-free visual <see cref="ViewPool"/>: one visual per item, a data context per view, the template and
/// selector paths, and z-ordering between links and nodes.
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
        var items = new ObservableCollection<object> { new object(), new object(), new object() };

        ViewPool.SetTemplateSelector(canvas, new Selector());
        ViewPool.SetItemsSource(canvas, items);
        ViewPool.Refresh(canvas);

        Assert.HasCount(3, canvas.Children, "one view per source item");
        for (var index = 0; index < items.Count; index++)
        {
            Assert.AreSame(items[index], ViewAt(canvas, index).DataContext,
                "each view receives its item as the data context");
        }
    }

    [TestMethod]
    public void ViewPool_ReusedViewRebindsItsLocalDataContextBinding()
    {
        var canvas = new VC.Canvas();
        var items = new ObservableCollection<object> { new ReuseItem(new ReuseModel("first")), new ReuseItem(new ReuseModel("second")) };
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
    public void ViewPool_RemovesViewsWhenItemsLeaveTheSource()
    {
        var canvas = new VC.Canvas();
        var items = new ObservableCollection<object> { new object(), new object() };

        ViewPool.SetTemplateSelector(canvas, new Selector());
        ViewPool.SetItemsSource(canvas, items);
        ViewPool.Refresh(canvas);

        var first = ViewAt(canvas, 0);
        items.RemoveAt(0);

        Assert.HasCount(1, canvas.Children, "the removed item's view is gone");
        Assert.IsNull(first.Parent, "the removed view is detached from the pool");
        Assert.AreSame(items[0], ViewAt(canvas, 0).DataContext, "the remaining view keeps its item");

        items.Add(new object());
        Assert.HasCount(2, canvas.Children, "an added item materializes another view");
    }

    [TestMethod]
    public void ViewPool_UsesTheItemTemplateWhenNoSelectorIsSet()
    {
        var canvas = new VC.Canvas();
        var items = new ObservableCollection<object> { new object(), new object() };
        var created = 0;

        ViewPool.SetItemTemplate(canvas, new DataTemplate(() =>
        {
            created++;
            return new VC.Border { Width = 10f, Height = 10f };
        }));
        ViewPool.SetItemsSource(canvas, items);
        ViewPool.Refresh(canvas);

        Assert.AreEqual(2, created, "the item template created one view per item");
        Assert.HasCount(2, canvas.Children);
        Assert.IsInstanceOfType<VC.Border>(canvas.Children[0]);
        Assert.AreSame(items[0], ViewAt(canvas, 0).DataContext);
    }

    [TestMethod]
    public void ViewPool_OrdersLinksBelowNodesByZIndexThenSourceOrder()
    {
        var canvas = new VC.Canvas();
        var firstNode = new NodeDefaultComponent { Anchor = new Anchor(10d, 20d) };
        var secondNode = new NodeDefaultComponent { Anchor = new Anchor(30d, 40d) };
        var link = new LinkDefaultComponent();
        var items = new ObservableCollection<object> { firstNode, link, secondNode };

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
        var items = new ObservableCollection<object> { firstLink, secondLink };

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

    private sealed class ReuseItem(ReuseModel child)
    {
        public ReuseModel Child { get; } = child;
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

    private static VisualElement ViewAt(VC.Canvas canvas, int index)
        => (VisualElement)canvas.Children[index];

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
