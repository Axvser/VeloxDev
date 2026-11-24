using System.Drawing;
using System.Drawing.Drawing2D;
using Easel;
using VeloxDev.WorkflowSystem;
using DrawingPen = System.Drawing.Pen;

namespace Demo.Views.Workflow;

/// <summary>
/// The Full demo's port: the WPF Full demo's fine ring and core, drawn by the visual backend's own paint pass.
/// </summary>
/// <remarks>
/// <para>
/// The item-template / Trimmed <see cref="SlotView"/> keeps the large path glyph. This view is the Full demo's
/// equivalent of the WPF <c>SlotView</c>: a ring whose radius, thickness and core are the same fractions of the
/// control size (0.205 / 0.045 / 0.06 of a 32-unit reference), so a 32-unit input port and a 24-unit output port
/// read as small points rather than a filled square.
/// </para>
/// <para>
/// It is a <see cref="Easel.Visuals.VisualElement"/> rather than a native island: the whole
/// surface is painted in one pass, and the workflow engine finds this element through its <see cref="DataContext"/>
/// just like the XAML slot views. <see cref="SlotState"/> drives the ring's brightness; <see cref="Channel"/> is kept
/// for parity with the WPF view so a template can bind it even though the static ring does not animate.
/// </para>
/// </remarks>
public sealed class FullSlotView : Easel.Visuals.VisualElement
{
    // 与 WPF Full 的 SlotView 同一组比例：端口再大，字形也不超过 32 设计单位。
    private const double GlyphReference = 32d;
    private const double RingRadiusRatio = 0.205d;
    private const double RingThicknessRatio = 0.045d;
    private const double CoreRadiusRatio = 0.06d;

    /// <summary>The port's runtime connection state.</summary>
    public static readonly DependencyProperty SlotStateProperty = DependencyProperty.Register(
        nameof(SlotState), typeof(SlotState), typeof(FullSlotView),
        new PropertyMetadata(SlotState.StandBy) { AffectsRender = true });

    /// <summary>The port's declared connection capacity, kept for templates that bind it.</summary>
    public static readonly DependencyProperty ChannelProperty = DependencyProperty.Register(
        nameof(Channel), typeof(SlotChannel), typeof(FullSlotView),
        new PropertyMetadata(SlotChannel.MultipleBoth) { AffectsRender = true });

    /// <summary>Creates the port glyph.</summary>
    public FullSlotView()
    {
        // 命中开关就位即可，不必像 WPF 那样再铺一层透明子元素。
        // 端口跨在卡片边上，伸出父级 Bounds 的那一半会被父级的命中检查裁掉 —— 这是视觉后端的限制，WPF 没有。
        IsHitTestVisible = true;
    }

    /// <summary>The port's runtime connection state.</summary>
    public SlotState SlotState
    {
        get => (SlotState)GetValue(SlotStateProperty)!;
        set => SetValue(SlotStateProperty, value);
    }

    /// <summary>The port's declared connection capacity.</summary>
    public SlotChannel Channel
    {
        get => (SlotChannel)GetValue(ChannelProperty)!;
        set => SetValue(ChannelProperty, value);
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        // DataContext 不参与 AffectsRender；绑定晚解析时亮度会停在 StandBy，这里补一次重绘。
        if (ReferenceEquals(e.Property, DataContextProperty))
        {
            InvalidateVisual();
        }
    }

    /// <inheritdoc />
    protected override void OnRender(Graphics graphics)
    {
        var size = Math.Min(Math.Min(Bounds.Width, Bounds.Height), GlyphReference);
        if (size <= 2f)
        {
            return;
        }

        var state = EffectiveState;
        // 端口固定白色，只靠亮度区分静息与激活。
        var color = Color.White;
        var aiming = state.HasFlag(SlotState.PreviewSender) || state.HasFlag(SlotState.PreviewReceiver);
        var connected = state.HasFlag(SlotState.Sender) || state.HasFlag(SlotState.Receiver);

        // 与 WPF 相同的亮度：静息压暗、瞄准/已连全亮，悬停再亮一档（端口小，没有反馈就不知道指针搭上了）。
        var lit = (aiming || connected ? 0.75 : 0.45) + (IsPointerOver ? 0.20 : 0.0);

        var centerX = Bounds.Width / 2f;
        var centerY = Bounds.Height / 2f;
        var ringRadius = (float)(size * RingRadiusRatio);
        var thickness = (float)(size * RingThicknessRatio);
        var coreRadius = (float)(size * CoreRadiusRatio);

        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var ringPen = new DrawingPen(AtAlpha(color, lit), thickness);
        graphics.DrawEllipse(
            ringPen,
            centerX - ringRadius,
            centerY - ringRadius,
            ringRadius * 2f,
            ringRadius * 2f);

        using var coreBrush = new SolidBrush(AtAlpha(color, lit + 0.20));
        graphics.FillEllipse(
            coreBrush,
            centerX - coreRadius,
            centerY - coreRadius,
            coreRadius * 2f,
            coreRadius * 2f);
    }

    // 绑定可能先于 DataContext 落值；此时仍按 StandBy 画就错了，读一次模型上的真实状态。
    private SlotState EffectiveState
    {
        get
        {
            var state = SlotState;
            if (state == SlotState.StandBy && DataContext is ISlotComponent slot)
            {
                state = slot.State;
            }

            return state;
        }
    }

    private static Color AtAlpha(Color color, double alpha)
    {
        var value = (int)Math.Round(Math.Max(0.0, Math.Min(1.0, alpha)) * 255);
        return Color.FromArgb(value, color);
    }
}
