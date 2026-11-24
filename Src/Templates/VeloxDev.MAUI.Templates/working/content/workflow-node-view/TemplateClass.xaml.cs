// VeloxDev customization: Add node-specific visual state or events here; workflow behavior is configured in XAML.
namespace TemplateNamespace;

public partial class TemplateClass : ContentView
{
    private const double DesignWidth = 260;

    public TemplateClass()
    {
        InitializeComponent();
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        ApplyScale(width);
    }

    /// <summary>Scales every layout metric of the card by the zoom collapse factor (collapsed width / 260),
    /// so the 260x180 design re-flows uniformly into the collapsed box. Uses layout-only changes (no render
    /// transforms) so the slot-layout behavior's layout-position measurement keeps producing correct link
    /// anchors.</summary>
    private void ApplyScale(double width)
    {
        var k = width > 0 ? width / DesignWidth : 1d;
        k = System.Math.Max(0.02, k);

        if (DesignGrid is not null)
        {
            DesignGrid.RowDefinitions[0].Height = new GridLength(System.Math.Max(1, 36 * k));
        }

        if (PART_TitleLabel is Label title)
        {
            title.FontSize = System.Math.Max(3, 13 * k);
            title.Margin = new Thickness(System.Math.Max(1, 12 * k), 0);
        }

        if (PART_InputSlot is SlotView input)
        {
            input.WidthRequest = System.Math.Max(2, 18 * k);
            input.HeightRequest = System.Math.Max(2, 18 * k);
            input.Margin = new Thickness(System.Math.Max(0, 2 * k), 0, 0, 0);
        }

        if (PART_OutputSlots is VerticalStackLayout stack)
        {
            stack.Spacing = System.Math.Max(0, 6 * k);
            stack.Margin = new Thickness(System.Math.Max(0, 12 * k), System.Math.Max(0, 6 * k));

            foreach (var child in stack.Children)
            {
                if (child is not Grid row)
                {
                    continue;
                }

                foreach (var item in row.Children)
                {
                    if (item is SlotView slot)
                    {
                        slot.WidthRequest = System.Math.Max(2, 14 * k);
                        slot.HeightRequest = System.Math.Max(2, 14 * k);
                    }
                    else if (item is Label label)
                    {
                        label.FontSize = System.Math.Max(3, 11 * k);
                        label.Margin = new Thickness(0, 0, System.Math.Max(1, 10 * k), 0);
                    }
                }
            }
        }
    }
}
