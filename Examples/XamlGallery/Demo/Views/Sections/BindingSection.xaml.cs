using System.Drawing;
using VeloxDev.WinForms.Xaml;
using VeloxDev.WinForms.Xaml.Controls;

namespace Demo.Views.Sections;

/// <summary>The binding, resource and validation examples.</summary>
public sealed partial class BindingSection : VeloxDev.WinForms.Xaml.Controls.UserControl
{
    private static readonly Color[] LiveColors =
    [
        Color.FromArgb(0xFF, 0x5B, 0x8C, 0xFF),
        Color.FromArgb(0xFF, 0x9B, 0x6B, 0xFF),
        Color.FromArgb(0xFF, 0x3D, 0xD6, 0x8C),
        Color.FromArgb(0xFF, 0xF2, 0xB4, 0x4B),
    ];

    private int liveIndex;

    /// <summary>Creates the section and attaches the code-only validation rule.</summary>
    public BindingSection()
    {
        InitializeComponent();

        // 规则集合没有标记语法；这一条走 SetBinding，和 XAML 里的绑定共用同一套表达式管线。
        var binding = new Binding(nameof(GalleryModel.OptionalName))
        {
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
        };
        binding.ValidationRules.Add(new MaxLengthRule { Maximum = 10 });
        ruleEditor.SetBinding(TextBox.TextProperty, binding);
    }

    private void OnClearNameClick(object? sender, EventArgs e)
    {
        if (DataContext is GalleryModel model)
        {
            model.OptionalName = string.Empty;
        }
    }

    private void OnFillNameClick(object? sender, EventArgs e)
    {
        if (DataContext is GalleryModel model)
        {
            model.OptionalName = "Grace Hopper";
        }
    }

    private void OnNextAccentClick(object? sender, EventArgs e)
    {
        liveIndex = (liveIndex + 1) % LiveColors.Length;
        Resources["LiveBrush"] = new SolidColorBrush(LiveColors[liveIndex]);
    }
}
