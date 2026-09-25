using Jalium.UI.Media;

namespace Demo.Views.Workflow;

/// <summary>
/// 五张节点卡共用的那一份色板与字级 —— 这里是全套设计里唯一写色值的地方。
/// <para>
/// 与 Avalonia 的 <c>Views/Workflow/CardTheme.axaml</c> 同源。那边是一份 <c>&lt;Styles&gt;</c> 加类选择器
/// （<c>Border.card</c> / <c>TextBlock.card-title</c> / <c>Button.ghost</c>），WinUI 落成一组具名
/// <c>Style</c>，MAUI 落成带 <c>x:Key</c> 的 Style；Jalium 既没有标记语言也没有选择器，最接近的对应物
/// 就是一个静态类的常量 —— <see cref="NodeChrome"/> 拿它搭卡片，<see cref="PortGlyph"/> 与
/// <see cref="NodeEditorSurface"/> 拿它给端口和连线上色。
/// </para>
/// <para>
/// 为什么要抽出来而不是五张卡各写一遍：同一组色值要在标题、标签、正文、输入底、悬停底、禁用字、
/// 四支动作按钮的语义字色、类型色条、代码面之间反复出现，改一次要改五处，而且必然会漂
/// （这个仓库的其它地方已经吃过这个亏）。
/// </para>
/// <para>
/// 色板（照 Avalonia 的注释）：
/// 卡面 <c>#161B22</c> ／ 边框 <c>#2A313C</c>（1px，圆角 8）／ 分隔线 <c>#222A34</c>；
/// 标题 <c>#E6EAF2</c> 12.5 SemiBold ／ 标签 <c>#7C8798</c> 10 ／ 正文 <c>#D6DCE6</c> 11.5；
/// 输入底 <c>#0E1218</c> ／ 悬停底 <c>#1E2731</c> ／ 禁用字 <c>#5A6474</c>。
/// 类型色条：Controller <c>#38BDF8</c>、Timer/Python <c>#6EC6FF</c>、Enum <c>#D6A0FF</c>、兜底 <c>#94A3B8</c>。
/// </para>
/// <para>
/// 这里是 <see cref="Color"/> 与 <see cref="SolidColorBrush"/> 两种形态并存：前者给自绘（端口、连线）
/// 用，后者给从 Jalium 控件树里取属性的那些控件用。两者都由同一个常量派生，所以不存在两份色值。
/// </para>
/// </summary>
internal static class CardPalette
{
    // ── 卡面 ────────────────────────────────────────────────────────────────

    /// <summary>卡面：单层暗面，整张卡只有这一层底。</summary>
    public static readonly Color Surface = Rgb(0x16, 0x1B, 0x22);

    /// <summary>发丝边。1px，圆角 8。</summary>
    public static readonly Color Border = Rgb(0x2A, 0x31, 0x3C);

    /// <summary>悬停时的边：只抬亮，不换色 —— 动作语义已经写在各自的前景色里了。</summary>
    public static readonly Color BorderHover = Rgb(0x3A, 0x45, 0x53);

    /// <summary>禁用时的边：压暗而不是抹掉，形状仍是同一条发丝线。</summary>
    public static readonly Color BorderDisabled = Rgb(0x23, 0x2A, 0x34);

    /// <summary>分隔线：标题行下沿、动作区上沿。</summary>
    public static readonly Color Divider = Rgb(0x22, 0x2A, 0x34);

    /// <summary>标题字。</summary>
    public static readonly Color TitleText = Rgb(0xE6, 0xEA, 0xF2);

    /// <summary>小写标签字。</summary>
    public static readonly Color LabelText = Rgb(0x7C, 0x87, 0x98);

    /// <summary>正文字。</summary>
    public static readonly Color ValueText = Rgb(0xD6, 0xDC, 0xE6);

    /// <summary>输入底：比卡面更深一档，输入框因此读作「凹进去」。</summary>
    public static readonly Color FieldSurface = Rgb(0x0E, 0x12, 0x18);

    /// <summary>悬停底：按钮悬停时抬到这一档。</summary>
    public static readonly Color HoverSurface = Rgb(0x1E, 0x27, 0x31);

    /// <summary>禁用字。</summary>
    public static readonly Color DisabledText = Rgb(0x5A, 0x64, 0x74);

    // ── 类型色条与执行序号 ──────────────────────────────────────────────────

    public static readonly Color AccentController = Rgb(0x38, 0xBD, 0xF8);
    public static readonly Color AccentTimerPython = Rgb(0x6E, 0xC6, 0xFF);
    public static readonly Color AccentEnum = Rgb(0xD6, 0xA0, 0xFF);

    /// <summary>兜底色条：外来的节点类型没有「类型」可上色，给中性灰蓝。</summary>
    public static readonly Color AccentFallback = Rgb(0x94, 0xA3, 0xB8);

    /// <summary>标题行右侧那颗执行序号。</summary>
    public static readonly Color ExecOrderText = Rgb(0x6E, 0xE7, 0xA8);

    // ── 幽灵按钮的语义字色（同形，只有字色不同） ────────────────────────────

    public static readonly Color GhostCompileText = Rgb(0x7E, 0xC8, 0xFF);
    public static readonly Color GhostRunText = Rgb(0x6E, 0xE7, 0xA8);
    public static readonly Color GhostStopText = Rgb(0xFC, 0xA5, 0xA5);
    public static readonly Color GhostCloseText = Rgb(0x9A, 0xA6, 0xB5);

    // ── 代码面（Python 卡） ─────────────────────────────────────────────────

    /// <summary>脚本区自己的底色：比卡面更深，代码要读起来像代码。</summary>
    public static readonly Color CodeSurface = Rgb(0x0D, 0x11, 0x17);
    public static readonly Color CodeBorder = Rgb(0x22, 0x2A, 0x34);
    public static readonly Color CodeTitleText = Rgb(0x6E, 0xE7, 0xA8);
    public static readonly Color CodeMutedText = Rgb(0x5A, 0x64, 0x74);

    // ── 字级 ────────────────────────────────────────────────────────────────

    public const double TitleSize = 12.5;
    public const double LabelSize = 10;
    public const double SlotNameSize = 11;
    public const double ValueSize = 11.5;
    public const double PillSize = 10.5;
    public const double GhostSize = 11;
    public const double CodeSize = 12;

    /// <summary>代码面用的等宽字。Avalonia 写 Consolas，这里同款。</summary>
    public const string MonoFamily = "Consolas";

    // ── 形状 ────────────────────────────────────────────────────────────────

    /// <summary>标题行高度。卡片的每一行都从这里量起，端口行的起点也一样（见 NodePorts）。</summary>
    public const double HeaderHeight = 32;

    /// <summary>标题行左侧那条类型色条的宽度。</summary>
    public const double AccentWidth = 2;

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

    /// <summary>全透明底。Jalium 的控件默认底可能是主题给的，所以「没有底」要写出来而不是留 null。</summary>
    public static readonly SolidColorBrush Transparent = new(Color.FromArgb(0x00, 0x00, 0x00, 0x00));

    /// <summary>按语义色取一支画刷。动作按钮与端口名用得上。</summary>
    public static SolidColorBrush Of(Color color) => new(color);

    /// <summary>半透明的一支。Jalium 的 <see cref="SolidColorBrush"/> 没有带 alpha 的构造函数
    /// （只有 <c>#ctor(Color)</c>），所以透明度落在 <see cref="Brush.Opacity"/> 上 —— 自绘的
    /// 波纹与彗星每帧都要按相位算一个 alpha，走的就是这条。</summary>
    public static SolidColorBrush Alpha(Color color, double opacity) => new(color) { Opacity = opacity };

    private static Color Rgb(byte r, byte g, byte b) => Color.FromRgb(r, g, b);
}
