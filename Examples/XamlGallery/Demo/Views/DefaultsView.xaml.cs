using VeloxDev.WinForms.Xaml;
using VeloxDev.WinForms.Xaml.Visuals.Controls;

namespace Demo.Views;

/// <summary>The default-controls card declared in visual XAML; only the data-bound parts are filled here.</summary>
public sealed partial class DefaultsView : StackPanel
{
    /// <summary>Creates the view and fills its list, tree, combo box and date examples.</summary>
    /// <param name="model">The shared sample data behind the tree view.</param>
    public DefaultsView(GalleryModel model)
    {
        // 这两个键必须在 InitializeComponent 之前进资源字典：XAML 里的 Style 在构造期按 StaticResource 取值。
        var theme = Theme.Current;
        Resources["PlateBackground"] = theme.Surface;
        Resources["PlateBorder"] = theme.ControlBorder;
        Resources["CaptionForeground"] = theme.MutedForeground;

        InitializeComponent();

        listBox.ItemsSource = new[] { "Alpha", "Bravo", "Charlie", "Delta" };
        comboBox.Items.Add("Balanced");
        comboBox.Items.Add("Throughput");
        comboBox.Items.Add("Latency");
        comboBox.SelectedIndex = 0;
        foreach (var node in model.TreeNodes)
        {
            tree.Items.Add(GalleryVisualCards.BuildTreeItem(node));
        }

        calendar.SelectedDate = DateTime.Today;
        datePicker.SelectedDate = DateTime.Today;
    }
}
