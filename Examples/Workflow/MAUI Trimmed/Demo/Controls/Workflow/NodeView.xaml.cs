namespace Demo.Controls;

public partial class NodeView : ContentView
{
    private const double DesignWidth = 260;

    public NodeView()
    {
        InitializeComponent();
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        ApplyScale(width);
    }

    /// <summary>Scales the card's internal metrics by the zoom collapse factor (collapsed width / 260)
    /// so the header, fonts and slot glyphs re-flow into the collapsed box instead of clipping. Uses
    /// layout-only changes (no render transforms) so the slot-layout behavior's layout-position
    /// measurement keeps producing correct link anchors.</summary>
    private void ApplyScale(double width)
    {
        var k = width > 0 ? width / DesignWidth : 1d;
        k = System.Math.Max(0.1, k);

        if (DesignGrid is not null)
        {
            DesignGrid.RowDefinitions[0].Height = new GridLength(System.Math.Max(14, 36 * k));
        }

        if (PART_TitleLabel is Label title)
        {
            title.FontSize = System.Math.Max(5, 13 * k);
        }

        if (PART_InputSlot is SlotView input)
        {
            input.WidthRequest = System.Math.Max(8, 18 * k);
            input.HeightRequest = System.Math.Max(8, 18 * k);
        }

        if (PART_OutputSlots is VerticalStackLayout stack)
        {
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
                        slot.WidthRequest = System.Math.Max(6, 14 * k);
                        slot.HeightRequest = System.Math.Max(6, 14 * k);
                    }
                    else if (item is Label label)
                    {
                        label.FontSize = System.Math.Max(4, 11 * k);
                    }
                }
            }
        }
    }
}
