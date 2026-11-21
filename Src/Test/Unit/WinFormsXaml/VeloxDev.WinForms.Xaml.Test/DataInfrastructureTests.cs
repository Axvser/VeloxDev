using System.Collections.ObjectModel;
using System.ComponentModel;
using VeloxDev.WinForms.Xaml.Test.Fixtures;
using V = VeloxDev.WinForms.Xaml.Controls;
using X = VeloxDev.WinForms.Xaml;

namespace VeloxDev.WinForms.Xaml.Test;

/// <summary>
/// The second batch of WPF-parity data infrastructure: collection views, view sources, container generation and
/// item panel templates.
/// </summary>
[STATestClass]
public sealed class DataInfrastructureTests
{
    [TestMethod]
    public void CollectionView_SortsByOneOrMoreProperties()
    {
        var items = new[]
        {
            new Item("beta", 2, "z"),
            new Item("alpha", 3, "y"),
            new Item("gamma", 1, "x"),
        };

        using var view = new X.CollectionView(items);

        view.SortDescriptions.Add(new X.SortDescription("Name"));
        CollectionAssert.AreEqual(
            new[] { "alpha", "beta", "gamma" },
            view.Cast<Item>().Select(item => item.Name).ToArray());

        view.SortDescriptions.Clear();
        view.SortDescriptions.Add(new X.SortDescription("Rank", ListSortDirection.Descending));
        CollectionAssert.AreEqual(
            new[] { 3, 2, 1 },
            view.Cast<Item>().Select(item => item.Rank).ToArray());
    }

    [TestMethod]
    public void CollectionView_FiltersAndGroupsByPropertyPaths()
    {
        var items = new[]
        {
            new Item("alpha", 3, "first"),
            new Item("beta", 2, "second"),
            new Item("gamma", 1, "first"),
        };

        using var view = new X.CollectionView(items);

        view.Filter = candidate => ((Item)candidate).Rank > 1;
        Assert.AreEqual(2, view.Count);
        Assert.IsFalse(view.Contains(items[2]));

        view.Filter = null;
        view.GroupDescriptions.Add(new X.GroupDescription("Group"));

        Assert.AreEqual(3, view.Count);
        Assert.AreEqual(2, view.Groups.Count);
        Assert.AreEqual("first", view.Groups[0].Name);
        Assert.AreEqual(2, view.Groups[0].ItemCount);
        Assert.AreEqual("second", view.Groups[1].Name);
        Assert.AreEqual(1, view.Groups[1].ItemCount);
    }

    [TestMethod]
    public void CollectionView_MovesTheCurrentItemAndRaisesCurrentChanged()
    {
        using var view = new X.CollectionView(new[] { "a", "b", "c" });
        var changed = 0;
        view.CurrentChanged += (_, _) => changed++;

        Assert.AreEqual(-1, view.CurrentPosition);
        Assert.IsTrue(view.IsCurrentBeforeFirst);

        Assert.IsTrue(view.MoveCurrentToNext());
        Assert.AreEqual(0, view.CurrentPosition);
        Assert.AreEqual("a", view.CurrentItem);

        Assert.IsTrue(view.MoveCurrentTo("c"));
        Assert.AreEqual(2, view.CurrentPosition);

        Assert.IsFalse(view.MoveCurrentToNext());
        Assert.IsTrue(view.IsCurrentAfterLast);
        Assert.IsNull(view.CurrentItem);

        Assert.IsTrue(view.MoveCurrentToPrevious());
        Assert.AreEqual(2, view.CurrentPosition);

        Assert.IsTrue(view.MoveCurrentToFirst());
        Assert.IsTrue(view.MoveCurrentToLast());
        Assert.IsTrue(view.MoveCurrentToPosition(1));
        Assert.AreEqual("b", view.CurrentItem);
        Assert.IsTrue(changed > 0);

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => view.MoveCurrentToPosition(4));
    }

    [TestMethod]
    public void CollectionView_CurrentChangingCanCancelTheMove()
    {
        using var view = new X.CollectionView(new[] { "a", "b" });
        view.CurrentChanging += (_, e) => e.Cancel = true;

        Assert.IsFalse(view.MoveCurrentToFirst());
        Assert.AreEqual(-1, view.CurrentPosition);
    }

    [TestMethod]
    public void CollectionViewSource_RebuildsTheViewAndFollowsTheSource()
    {
        var source = new ObservableCollection<Item>
        {
            new Item("alpha", 3, "x"),
            new Item("beta", 2, "x"),
            new Item("gamma", 1, "y"),
        };

        var sourceView = new X.CollectionViewSource
        {
            Source = source,
            Filter = candidate => ((Item)candidate).Rank > 1,
        };

        Assert.IsNotNull(sourceView.View);
        Assert.AreEqual(2, sourceView.View!.Count);

        sourceView.SortDescriptions.Add(new X.SortDescription("Rank", ListSortDirection.Descending));
        Assert.AreEqual(3, ((Item)sourceView.View![0]!).Rank);

        source.Add(new Item("delta", 9, "x"));
        Assert.AreEqual(3, sourceView.View!.Count);
        Assert.AreEqual("delta", ((Item)sourceView.View![0]!).Name);

        sourceView.Source = null;
        Assert.IsNull(sourceView.View);
    }

    [TestMethod]
    public void CollectionViewSource_RebuildsWhenTheFilterChanges()
    {
        var sourceView = new X.CollectionViewSource
        {
            Source = new[] { 1, 2, 3, 4 },
        };

        Assert.AreEqual(4, sourceView.View!.Count);

        sourceView.Filter = candidate => (int)candidate % 2 == 0;
        Assert.AreEqual(2, sourceView.View!.Count);

        sourceView.Refresh();
        Assert.AreEqual(2, sourceView.View!.Count);
    }

    [TestMethod]
    public void ItemsControl_PopulatesTheItemContainerGenerator()
    {
        var alpha = new Item("alpha", 1, "x");
        var beta = new Item("beta", 2, "x");

        using var control = new V.ItemsControl { ItemsSource = new[] { alpha, beta } };
        control.PerformLayout();

        var generator = control.ItemContainerGenerator;
        Assert.AreEqual(2, generator.Count);
        Assert.AreSame(alpha, generator.Items[0]);

        var container = generator.ContainerFromItem(beta);
        Assert.IsNotNull(container);
        Assert.AreEqual(1, generator.IndexFromContainer(container!));
        Assert.AreSame(beta, generator.ItemFromContainer(container!));
        Assert.AreSame(container, generator.ContainerFromIndex(1));

        var statusChanges = 0;
        generator.StatusChanged += (_, _) => statusChanges++;
        generator.Reset();

        Assert.AreEqual(2, generator.Count);
        Assert.IsNull(generator.ContainerFromIndex(0));
        Assert.AreSame(alpha, generator.Items[0]);
        Assert.IsTrue(statusChanges > 0);

        control.ItemsSource = null;
        Assert.AreEqual(0, generator.Count);
        Assert.IsNull(generator.ContainerFromIndex(0));
    }

    [TestMethod]
    public void ItemsControl_UsesAnItemsPanelTemplateUntilAnExplicitPanelAppears()
    {
        var created = 0;
        var template = new X.ItemsPanelTemplate(() =>
        {
            created++;
            return new V.StackPanel();
        });

        using var control = new V.ItemsControl
        {
            ItemsSource = new[] { "a", "b" },
            ItemsPanelTemplate = template,
        };
        control.PerformLayout();

        Assert.AreEqual(1, created);
        var generatedPanel = control.Controls
            .Cast<System.Windows.Forms.Control>()
            .OfType<V.StackPanel>()
            .Single();
        Assert.AreEqual(2, generatedPanel.Controls.Count);

        var explicitPanel = new V.WrapPanel();
        control.ItemsPanel = explicitPanel;

        Assert.AreEqual(0, control.Controls.Cast<System.Windows.Forms.Control>().OfType<V.StackPanel>().Count());
        Assert.AreEqual(2, explicitPanel.Controls.Count);

        control.ItemsPanel = null;
        Assert.AreEqual(2, created);
    }

    [TestMethod]
    public void ItemsControl_ItemContainerStyleSelectorWinsPerItem()
    {
        var style = new X.Style { TargetType = typeof(ProbeControl) };
        style.Setters.Add(new Setter(ProbeControl.BorderWidthProperty, 42));

        using var control = new V.ItemsControl
        {
            ItemsSource = new[] { "a", "b" },
            ItemTemplate = new X.DataTemplate(() => new ProbeControl()),
            ItemContainerStyleSelector = new ProbeStyleSelector(style),
        };
        control.PerformLayout();

        var containers = control.Controls
            .Cast<System.Windows.Forms.Control>()
            .OfType<ProbeControl>()
            .ToArray();

        Assert.AreEqual(2, containers.Length);
        Assert.AreEqual(0, containers[0].BorderWidth);
        Assert.AreEqual(42, containers[1].BorderWidth);
    }

    [TestMethod]
    public void ContentControl_ContentTemplateSelectorWinsOverContentTemplate()
    {
        using var control = new V.ContentControl
        {
            Content = new object(),
            ContentTemplate = new X.DataTemplate(() => new V.TextBlock { Text = "fallback" }),
            ContentTemplateSelector = new FixedTemplateSelector(
                new X.DataTemplate(() => new V.TextBlock { Text = "selected" })),
        };
        control.PerformLayout();

        var visual = control.Controls
            .Cast<System.Windows.Forms.Control>()
            .OfType<V.TextBlock>()
            .Single();

        Assert.AreEqual("selected", visual.Text);
    }

    private sealed class Item
    {
        public Item(string name, int rank, string group)
        {
            Name = name;
            Rank = rank;
            Group = group;
        }

        public string Name { get; }

        public int Rank { get; }

        public string Group { get; }
    }

    private sealed class ProbeStyleSelector(X.Style style) : V.StyleSelector
    {
        public override X.Style? SelectStyle(object? item) => item is "b" ? style : null;
    }

    private sealed class FixedTemplateSelector(X.DataTemplate template) : X.TemplateSelector
    {
        public override X.DataTemplate? SelectTemplate(object? item) => template;
    }
}
