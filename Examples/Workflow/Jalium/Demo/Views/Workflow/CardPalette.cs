using Jalium.UI.Media;

namespace Demo.Views.Workflow;

// 五张节点卡共用的色板与字级 —— 全套设计里唯一写色值的地方。
// 与 Avalonia 的 Views/Workflow/CardTheme.axaml 同源；Jalium 既没有标记语言也没有选择器，最接近的对应物就是这个静态
// 类的常量。NodeChrome 拿它搭卡片，PortGlyph 与 NodeEditorSurface 拿它给端口和连线上色。
// Color 与 SolidColorBrush 两种形态并存：前者给自绘（端口、连线）用，后者给从 Jalium 控件树里取属性的控件用。
internal static class CardPalette
{
    // ── 卡面 ────────────────────────────────────────────────────────────────

    public static readonly Color Surface = Rgb(0x25, 0x25, 0x26);

    // 发丝边。1px，圆角 8。
    public static readonly Color Border = Rgb(0x3C, 0x3C, 0x3C);

    // 悬停只抬亮，不换色 —— 动作语义已经写在各自的前景色里了。
    public static readonly Color BorderHover = Rgb(0x56, 0x56, 0x56);

    // 压暗而不是抹掉，形状仍是同一条发丝线。
    public static readonly Color BorderDisabled = Rgb(0x2E, 0x2E, 0x2E);

    public static readonly Color Divider = Rgb(0x26, 0x26, 0x26);
    public static readonly Color TitleText = Rgb(0xE7, 0xE7, 0xE7);
    public static readonly Color LabelText = Rgb(0x6E, 0x76, 0x81);
    public static readonly Color ValueText = Rgb(0xA0, 0xA0, 0xA0);

    // 比卡面更深一档，输入框因此读作「凹进去」。
    public static readonly Color FieldSurface = Rgb(0x2A, 0x2A, 0x2A);

    public static readonly Color HoverSurface = Rgb(0x2A, 0x2D, 0x2E);
    public static readonly Color DisabledText = Rgb(0x58, 0x58, 0x58);

    // ── 类型色条与执行序号 ──────────────────────────────────────────────────

    public static readonly Color AccentController = Rgb(0x6E, 0x8B, 0xFF);
    public static readonly Color AccentTimerPython = Rgb(0x4F, 0xC1, 0xFF);
    public static readonly Color AccentEnum = Rgb(0xC9, 0x9B, 0xFF);

    // 外来的节点类型没有「类型」可上色，给中性灰蓝。
    public static readonly Color AccentFallback = Rgb(0x94, 0xA3, 0xB8);

    public static readonly Color ExecOrderText = Rgb(0x7E, 0xE7, 0x87);

    // ── 幽灵按钮的语义字色（同形，只有字色不同） ────────────────────────────

    public static readonly Color GhostCompileText = Rgb(0x4F, 0xC1, 0xFF);
    public static readonly Color GhostRunText = Rgb(0x7E, 0xE7, 0x87);
    public static readonly Color GhostStopText = Rgb(0xF8, 0x51, 0x49);
    public static readonly Color GhostCloseText = Rgb(0xA0, 0xA0, 0xA0);

    // ── 代码面（Python 卡） ─────────────────────────────────────────────────

    // 比卡面更深，代码要读起来像代码。
    public static readonly Color CodeSurface = Rgb(0x0D, 0x11, 0x17);
    public static readonly Color CodeBorder = Rgb(0x26, 0x26, 0x26);
    public static readonly Color CodeTitleText = Rgb(0x7E, 0xE7, 0x87);
    public static readonly Color CodeMutedText = Rgb(0x58, 0x58, 0x58);

    // ── 字级 ────────────────────────────────────────────────────────────────

    public const double TitleSize = 15;
    public const double LabelSize = 10.5;
    public const double SlotNameSize = 11;
    public const double ValueSize = 12;
    public const double PillSize = 10.5;
    public const double GhostSize = 11;
    public const double CodeSize = 12;

    // 代码面用的等宽字。Avalonia 写 Consolas，这里同款。
    public const string MonoFamily = "Consolas";

    // ── 形状 ────────────────────────────────────────────────────────────────

    // 标题行高度。卡片的每一行都从这里量起，端口行的起点也一样（见 NodePorts）。
    public const double HeaderHeight = 32;

    public const double AccentWidth = 3;
    public const double CardRadius = 8;
    public const double FieldRadius = 6;
    public const double PillRadius = 9;

    // ── 画刷（控件属性要 Brush 而不是 Color） ───────────────────────────────

    public static readonly SolidColorBrush SurfaceBrush = new(Surface);
    public static readonly SolidColorBrush BorderBrush = new(Border);
    public static readonly SolidColorBrush BorderHoverBrush = new(BorderHover);
    public static readonly SolidColorBrush BorderDisabledBrush = new(BorderDisabled);
    public static readonly SolidColorBrush DividerBrush = new(Divider);
    public static readonly SolidColorBrush TitleBrush = new(TitleText);
    public static readonly SolidColorBrush LabelBrush = new(LabelText);
    public static readonly SolidColorBrush ValueBrush = new(ValueText);
    public static readonly SolidColorBrush FieldBrush = new(FieldSurface);
    public static readonly SolidColorBrush HoverBrush = new(HoverSurface);
    public static readonly SolidColorBrush DisabledTextBrush = new(DisabledText);
    public static readonly SolidColorBrush ExecOrderBrush = new(ExecOrderText);
    public static readonly SolidColorBrush CodeSurfaceBrush = new(CodeSurface);

    // 全透明底。Jalium 的控件默认底可能是主题给的，所以「没有底」要写出来而不是留 null。
    public static readonly SolidColorBrush Transparent = new(Color.FromArgb(0x00, 0x00, 0x00, 0x00));

    // 按语义色取一支画刷。动作按钮与端口名用得上。
    public static SolidColorBrush Of(Color color) => new(color);

    // 半透明的一支。Jalium 的 SolidColorBrush 没有带 alpha 的构造函数（只有 #ctor(Color)），所以透明度落在
    // Brush.Opacity 上 —— 自绘的波纹与彗星每帧都要按相位算一个 alpha，走的就是这条。
    public static SolidColorBrush Alpha(Color color, double opacity) => new(color) { Opacity = opacity };

    private static Color Rgb(byte r, byte g, byte b) => Color.FromRgb(r, g, b);
}
