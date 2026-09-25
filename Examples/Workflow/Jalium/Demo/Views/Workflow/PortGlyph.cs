using Jalium.UI;
using Jalium.UI.Media;
using VeloxDev.WorkflowSystem;

namespace Demo.Views.Workflow;

/// <summary>
/// 端口上那一个字形的画法：一枚细环 + 一个小芯，外加一圈说方向的波纹。
/// <para>
/// 与 Avalonia 的 <c>Views/Workflow/SlotView.cs</c> 同源，差别只在「画在哪」：那边是一个控件每帧重画
/// 自己的 <c>Render</c>，这边是表面在 <c>OnPostRender</c> 里按 <see cref="NodePorts"/> 给的中心现画
/// （为什么不在卡里画，见 <see cref="NodeEditorSurface.DrawPorts"/> 的注释）。比例、周期与方向语言
/// 逐条照抄，所以两家的端口是同一个端口。
/// </para>
/// <para>
/// 方向由<b>运动的形状</b>承担，而不是靠一个颜色或一只转个不停的令牌：能发的时候波纹向外散，能收的时候
/// 向内聚，两个方向都有就同时来。这来自插槽声明的 <see cref="SlotChannel"/>，而不是它碰巧连了什么 ——
/// 所以一颗空着的口也能说清自己是干什么的。
/// </para>
/// <para>
/// 呼吸（被瞄准时芯会胀缩）是从同一个相位推出来的：<c>sin</c> 的相位就是 <c>phase</c>，所以它和波纹
/// 天然同步，也就不需要第二条动画（本平台一个控件上只能跑一条转换，两条会互相打断）。
/// </para>
/// <para>
/// 坐标是画布坐标（已经缩放过），所以调用方给的方框边长 <c>boxSize</c> 与中心 <c>center</c> 都必须是
/// 按当前缩放换算过的画布值。理由：表面不像卡片那样有一个 Viewbox 替它缩放。
/// </para>
/// </summary>
internal static class PortGlyph
{
    // 字形按端口的方框等比缩放，但以 32 设计单位为上限 —— 口在卡里只有二十几单位，再大就不成其为「点」了
    private const double GlyphReference = 32;
    private const double RingRadiusRatio = 0.205;
    private const double RingThicknessRatio = 0.045;
    private const double CoreRadiusRatio = 0.06;

    // 波纹的终点半径就是环本身（1.0×），外缘留 1 单位不碰边界。
    // 从前内圈取 1.25×，内聚波纹于是停在环外一截再凭空消失 —— 看着像没走到就被掐了
    private const double RippleInnerRatio = 1.0;
    private const double RippleThicknessRatio = 0.05;
    private const double RippleEdgeInset = 1;

    // 外散的出现段：用 12% 的路程升起来，不凭空冒出。内聚不走这条 —— 它从外缘的淡色开始，
    // 本来就不需要「出现」
    private const double RippleAttack = 0.12;

    // 内聚的两端透明度：外缘淡、环上实。它一路加深到底，不做收尾淡出
    private const double ReceiveOuterAlpha = 0.15;
    private const double ReceiveInnerAlpha = 0.90;

    // 一圈上只画一道波纹。两道虽然更「连续」，但同屏永远有两只环挂在固定的内外半径之间，
    // 读起来像一个静止的靶心 —— 眼睛抓不住单个环，方向也就读不出来
    private const int RippleCount = 1;

    /// <summary>
    /// 画一颗端口。<paramref name="boxSize"/> 是这个口的方框边长（画布单位，已按缩放换算），
    /// <paramref name="phase"/> 是表面那条唯一的时钟，<paramref name="hovered"/> 由表面自己记。
    /// </summary>
    public static void Draw(DrawingContext dc, Point center, double boxSize, SlotState state,
        SlotChannel channel, double phase, bool hovered)
    {
        double size = Math.Min(boxSize, GlyphReference);
        if (size <= 2)
        {
            return;
        }

        var color = ColorOf(state);
        bool canSend = CanSend(channel);
        bool canReceive = CanReceive(channel);
        bool aiming = IsAiming(state);
        bool connected = IsConnected(state);

        double ringRadius = size * RingRadiusRatio;

        // 呼吸从这个时钟推导：`sin` 的相位就是 phase，所以它和波纹天然同步
        double breath = aiming ? 0.5 + (0.5 * Math.Sin(phase * Math.PI * 2)) : 0;

        // 波纹先画：环压在波纹之上，波纹收在环上的那一端才读得出「并进环里」
        DrawRipples(dc, center, size, ringRadius, color, canSend, canReceive, aiming, connected, phase);

        // 环与芯：静息时压暗，被瞄准或已连上时全亮。呼吸推的是同一个亮度，所以它一定看得见。
        // 悬停再抬一档：端口小，没有反馈就不知道指针到底有没有搭上它
        double lit = (aiming || connected ? 0.75 : 0.45) + (0.25 * breath) + (hovered ? 0.2 : 0);
        dc.DrawEllipse(null,
            new Pen(CardPalette.Alpha(color, Math.Min(1, lit)), Math.Max(0.6, size * RingThicknessRatio)),
            center, ringRadius, ringRadius);

        double coreScale = (aiming ? 1.2 : 1.0) + (0.3 * breath);
        double core = size * CoreRadiusRatio * coreScale;
        dc.DrawEllipse(CardPalette.Alpha(color, Math.Min(1, lit + 0.2)), null, center, core, core);
    }

    /// <summary>
    /// 方向语言：能发就向外散，能收就向内聚，两个方向都声明了就同时来。
    /// </summary>
    /// <remarks>
    /// 外散越往外越淡 —— 能量在离开。内聚越往里越亮 —— 能量在落地。所以两条路即使停在一帧里也是相反的：
    /// 一条最外最淡，另一条最内最亮。
    /// </remarks>
    private static void DrawRipples(DrawingContext dc, Point center, double size, double ringRadius, Color color,
        bool canSend, bool canReceive, bool aiming, bool connected, double phase)
    {
        if (!canSend && !canReceive)
        {
            return;
        }

        double inner = ringRadius * RippleInnerRatio;
        double outer = (size / 2) - RippleEdgeInset;
        if (outer <= inner)
        {
            return;
        }

        double thickness = Math.Max(0.8, size * RippleThicknessRatio);
        double strength = aiming || connected ? 1.0 : 0.6;

        for (int k = 0; k < RippleCount; k++)
        {
            double p = (phase + (k / (double)RippleCount)) % 1.0;

            if (canSend)
            {
                // 外散：在环上冒出来（12% 的路程升到全亮），越往外越淡
                double appear = Math.Min(1, p / RippleAttack);
                double r = inner + ((outer - inner) * p);
                dc.DrawEllipse(null,
                    new Pen(CardPalette.Alpha(color, appear * (1 - p) * 0.7 * strength), thickness),
                    center, r, r);
            }

            if (canReceive)
            {
                // 内聚：外缘淡、一路加深，收在环上。不做收尾淡出是因为终点与环同半径（RippleInnerRatio = 1.0）：
                // 最后那一下是并进环里而不是凭空消失，所以从「最深」跳回「外缘的淡」读起来是新的一波从外面
                // 过来，而不是刚才那只环炸掉
                double arrive = ReceiveOuterAlpha + ((ReceiveInnerAlpha - ReceiveOuterAlpha) * p);
                double r = outer - ((outer - inner) * p);
                dc.DrawEllipse(null,
                    new Pen(CardPalette.Alpha(color, arrive * strength), thickness),
                    center, r, r);
            }
        }
    }

    /// <summary>Whether the slot may connect outward — a target direction is declared.</summary>
    private static bool CanSend(SlotChannel channel)
        => channel.HasFlag(SlotChannel.OneTarget) || channel.HasFlag(SlotChannel.MultipleTargets);

    /// <summary>Whether the slot may be connected inward — a source direction is declared.</summary>
    private static bool CanReceive(SlotChannel channel)
        => channel.HasFlag(SlotChannel.OneSource) || channel.HasFlag(SlotChannel.MultipleSources);

    private static bool IsAiming(SlotState state)
        => state.HasFlag(SlotState.PreviewSender) || state.HasFlag(SlotState.PreviewReceiver);

    private static bool IsConnected(SlotState state)
        => state.HasFlag(SlotState.Sender) || state.HasFlag(SlotState.Receiver);

    /// <summary>
    /// 端口颜色按 <see cref="SlotState"/> 取：默认白、发送端 Tomato、接收端 Lime、两头都通 Violet。
    /// 预览（正在拉线的那一头）不加色 —— 它的反馈在呼吸与波纹上，不在颜色上，Avalonia 那边亦然。
    /// </summary>
    private static Color ColorOf(SlotState state)
    {
        bool sender = state.HasFlag(SlotState.Sender);
        bool receiver = state.HasFlag(SlotState.Receiver);

        if (sender && receiver)
        {
            return Colors.Violet;
        }

        if (sender)
        {
            return Colors.Tomato;
        }

        return receiver ? Colors.Lime : Colors.White;
    }
}
