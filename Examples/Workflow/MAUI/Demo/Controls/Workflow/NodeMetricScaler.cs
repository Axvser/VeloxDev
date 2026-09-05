using System.Collections.Generic;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;

namespace Demo.Controls;

/// <summary>
/// MAUI has no built-in Viewbox, so the FULL node views scale their interior METRICS in code
/// (the repo's established MAUI approach — see the Trimmed demo). Each kept node card collapses to
/// the node's Size; this scaler re-derives every interior metric from the authored design value:
/// fixed root rows, explicit font sizes and slot-glyph requests all become design × k where
/// k = collapsedWidth / DesignWidth (DesignWidth = the node type's [DefaultSize] width). Because the
/// node collapses uniformly (collapsed = design × k on both axes), the whole interior stays
/// uniformly proportional to the collapsed box — it cannot re-flow / overflow the node. Slots are
/// measured from their real Bounds after layout, so this metric-only scaling keeps link anchors correct.
/// </summary>
public sealed class NodeMetricScaler
{
    private readonly double _designWidth;
    private readonly List<RowDefinition> _fixedRows = new();
    private readonly List<double> _fixedRowDesign = new();
    private readonly Dictionary<object, double> _fontOriginal = new();
    private readonly Dictionary<object, double> _widthOriginal = new();
    private readonly Dictionary<object, double> _heightOriginal = new();
    private bool _rowsCaptured;

    public NodeMetricScaler(double designWidth)
    {
        _designWidth = designWidth;
    }

    public void ApplyScale(Grid designGrid, double width)
    {
        var k = width > 0 ? width / _designWidth : 1d;
        k = System.Math.Max(0.1, k);

        if (!_rowsCaptured)
        {
            CaptureRows(designGrid);
            _rowsCaptured = true;
        }

        for (var i = 0; i < _fixedRows.Count; i++)
        {
            _fixedRows[i].Height = new GridLength(System.Math.Max(2, _fixedRowDesign[i] * k));
        }

        Visit((Element)designGrid, k);
    }

    private void CaptureRows(Grid grid)
    {
        foreach (var row in grid.RowDefinitions)
        {
            if (row.Height.IsAbsolute && row.Height.Value > 0)
            {
                _fixedRows.Add(row);
                _fixedRowDesign.Add(row.Height.Value);
            }
        }
    }

    private void Visit(Element element, double k)
    {
        switch (element)
        {
            case Label label:
                label.FontSize = Scale(_fontOriginal, label, label.FontSize, k, 14);
                break;
            case Button button:
                button.FontSize = Scale(_fontOriginal, button, button.FontSize, k, 14);
                break;
            case Entry entry:
                entry.FontSize = Scale(_fontOriginal, entry, entry.FontSize, k, 14);
                break;
            case Editor editor:
                editor.FontSize = Scale(_fontOriginal, editor, editor.FontSize, k, 14);
                break;
            case Picker picker:
                picker.FontSize = Scale(_fontOriginal, picker, picker.FontSize, k, 14);
                break;
            case SearchBar search:
                search.FontSize = Scale(_fontOriginal, search, search.FontSize, k, 14);
                break;
            case SlotView slot:
            {
                if (slot.WidthRequest > 0)
                {
                    slot.WidthRequest = Scale(_widthOriginal, slot, slot.WidthRequest, k);
                }

                if (slot.HeightRequest > 0)
                {
                    slot.HeightRequest = Scale(_heightOriginal, slot, slot.HeightRequest, k);
                }

                break;
            }
        }

        foreach (var child in Children(element))
        {
            Visit(child, k);
        }
    }

    private static double Scale(Dictionary<object, double> originals, object element, double current, double k,
        double fallback = -1)
    {
        if (!originals.TryGetValue(element, out var design))
        {
            // current <= 0 means the control had no authored font size (MAUI reports -1): treat the
            // platform default (~14) as the authored design value so it scales uniformly too.
            design = current > 0 ? current : (fallback > 0 ? fallback : 14);
            originals[element] = design;
        }

        return System.Math.Max(2, design * k);
    }

    private static IEnumerable<Element> Children(Element element)
    {
        if (element is Layout layout)
        {
            foreach (var child in layout.Children)
            {
                if (child is Element e)
                {
                    yield return e;
                }
            }
        }

        // Covers ContentView, Border, ContentPresenter, ... — anything exposing a single Content.
        if (element is IContentView contentHost && contentHost.Content is Element hostChild)
        {
            yield return hostChild;
        }

        if (element is ScrollView scroll && scroll.Content is Element svChild)
        {
            yield return svChild;
        }
    }
}
