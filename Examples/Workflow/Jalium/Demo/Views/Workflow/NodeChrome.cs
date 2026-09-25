using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Media;

namespace Demo.Views.Workflow;

/// <summary>
/// 这套卡片外壳的构件工厂：卡面、标题行、分隔线、字级、输入框、状态胶囊、代码面、幽灵按钮。
/// <para>
/// 色值与字级全部取自 <see cref="CardPalette"/>，这里只负责形状与拼装 —— 五张卡因此共用同一条
/// 发丝边、同一个 32 高的标题行、同一支幽灵按钮，改一处就五张一起改。
/// </para>
/// <para>
/// 端口不在这里：本平台上端口由 <see cref="NodeEditorSurface"/> 画在卡片之上（理由见那边的注释），
/// 卡片只负责自己的内容。
/// </para>
/// </summary>
internal static class NodeChrome
{
    /// <summary>标题行高度。卡片的每一行都从这里量起，端口行的起点也一样（见 <see cref="NodePorts"/>）。</summary>
    public const double HeaderHeight = CardPalette.HeaderHeight;

    // ── 卡面 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// 整张卡：单层暗面 + 发丝边 + <paramref name="headerHeight"/> 高的标题行，下面是调用方的内容区。
    /// </summary>
    /// <param name="width">设计宽（必须与节点类型的 [DefaultSize] 一致）。</param>
    /// <param name="height">设计高（同上）。</param>
    /// <param name="accent">标题行左侧那条 2px 类型色条的颜色。</param>
    /// <param name="title">标题文字，左对齐。</param>
    /// <param name="execOrder">标题行右侧的执行序号；空串即不显示。</param>
    /// <param name="status">状态胶囊的文字；空串即没有胶囊（Controller 卡就没有）。</param>
    /// <param name="statusFg">胶囊文字色（Python 用中性 #9AA6B5，Enum 用类型色）。</param>
    /// <param name="statusBold">胶囊文字是否加粗（Enum 的路由结果加粗）。</param>
    /// <param name="content">内容区（标题行下面那一格），调用方往里放主体。</param>
    public static Border Card(double width, double height, Color accent, string title,
        string execOrder, string status, Color statusFg, bool statusBold,
        out Grid content, out TextBlock titleText, out TextBlock execText, out Border pill, out TextBlock pillText)
    {
        titleText = Text(title, CardPalette.TitleSize, CardPalette.TitleBrush, FontWeights.SemiBold);
        titleText.VerticalAlignment = VerticalAlignment.Center;
        titleText.Margin = new Thickness(12, 0, 6, 0);

        execText = Text(execOrder, CardPalette.ValueSize, CardPalette.ExecOrderBrush, FontWeights.Normal);
        execText.VerticalAlignment = VerticalAlignment.Center;
        execText.Margin = new Thickness(0, 0, 12, 0);
        execText.Visibility = Show(execOrder.Length > 0);

        pillText = Text(status, CardPalette.PillSize, new SolidColorBrush(statusFg),
            statusBold ? FontWeights.SemiBold : FontWeights.Normal);
        pillText.VerticalAlignment = VerticalAlignment.Center;
        pill = new Border
        {
            Background = CardPalette.HoverBrush,
            BorderBrush = CardPalette.BorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(CardPalette.PillRadius),
            Padding = new Thickness(7, 1),
            Margin = new Thickness(0, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Show(status.Length > 0),
            Child = pillText,
        };

        // 标题行：2px 色条 + 左对齐标题 + （执行序号 / 状态胶囊）。原来是居中白字压在自己的灰带上
        var columns = new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.FromPixels(CardPalette.AccentWidth) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var bar = new Border
        {
            Background = new SolidColorBrush(accent),
            CornerRadius = new CornerRadius(CardPalette.CardRadius, 0, 0, 0),
        };
        Grid.SetColumn(bar, 0);
        Grid.SetColumn(titleText, 1);
        Grid.SetColumn(execText, 2);
        Grid.SetColumn(pill, 3);
        columns.Children.Add(bar);
        columns.Children.Add(titleText);
        columns.Children.Add(execText);
        columns.Children.Add(pill);

        var header = new Border { Background = CardPalette.Transparent, Child = columns };

        var body = new Grid();
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.FromPixels(HeaderHeight) });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Star });
        Grid.SetRow(header, 0);
        body.Children.Add(header);

        // 分隔线排在标题行之后：它是压在同一格底沿的一条 1px 实心条，不给序就会被标题行盖住
        var divider = Divider(atBottom: true);
        Grid.SetRow(divider, 0);
        body.Children.Add(divider);

        content = new Grid();
        Grid.SetRow(content, 1);
        body.Children.Add(content);

        return new Border
        {
            Width = width,
            Height = height,
            CornerRadius = new CornerRadius(CardPalette.CardRadius),
            BorderThickness = new Thickness(1),
            BorderBrush = CardPalette.BorderBrush,
            Background = CardPalette.SurfaceBrush,
            // 设计画布自裁：主体内容（描述换行、任意多行输出、脚本区）裁到卡片的圆角矩形里，
            // 卡片被 Viewbox 整体缩放时也不会有东西画到边外。端口比这层高（表面上画），所以不受它影响。
            ClipToBounds = true,
            Child = body,
        };
    }

    /// <summary>
    /// 兜底卡：只有卡面、发丝边与一条贯穿全高的类型色条，衬一个「?」。
    /// <para>
    /// 故意留空：<c>IWorkflowNodeViewModel</c> 只有几何与插槽、没有显示名 —— 标题是具体 ViewModel 的属性，
    /// 所以这张卡没法诚实地说出它不认识的那个节点叫什么。这个 demo 里每种节点都有自己的视图，
    /// 它只会画到外来类型上。
    /// </para>
    /// </summary>
    public static Border BareCard(double width, double height, Color accent)
    {
        var columns = new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.FromPixels(CardPalette.AccentWidth) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });

        var bar = new Border
        {
            Background = new SolidColorBrush(accent),
            CornerRadius = new CornerRadius(CardPalette.CardRadius, 0, 0, 0),
        };
        var mark = Text("?", CardPalette.LabelSize, CardPalette.LabelBrush, FontWeights.Normal);
        mark.VerticalAlignment = VerticalAlignment.Center;
        mark.Margin = new Thickness(12, 0, 0, 0);
        Grid.SetColumn(bar, 0);
        Grid.SetColumn(mark, 1);
        columns.Children.Add(bar);
        columns.Children.Add(mark);

        return new Border
        {
            Width = width,
            Height = height,
            CornerRadius = new CornerRadius(CardPalette.CardRadius),
            BorderThickness = new Thickness(1),
            BorderBrush = CardPalette.BorderBrush,
            Background = CardPalette.SurfaceBrush,
            ClipToBounds = true,
            Child = columns,
        };
    }

    // ── 分隔线 ──────────────────────────────────────────────────────────────

    /// <summary>
    /// 1px 实心分隔线。写成一条有高度的实心条，而不是「只描底边」的 Border：后者要靠 Border 自己
    /// 量出 1px 高才画得出来，而一个没有子元素的 Border 量出什么高度是本平台的实现细节，不该赌。
    /// </summary>
    public static Border Divider(bool atBottom = true)
        => new()
        {
            Height = 1,
            Background = CardPalette.DividerBrush,
            VerticalAlignment = atBottom ? VerticalAlignment.Bottom : VerticalAlignment.Top,
        };

    // ── 字级 ────────────────────────────────────────────────────────────────

    public static TextBlock Text(string text, double size, Brush foreground, FontWeight weight)
        => new()
        {
            Text = text,
            FontSize = size,
            Foreground = foreground,
            FontWeight = weight,
        };

    /// <summary>小写标签：10号，字距比正文松，读起来像「字段名」而不是「一句话」。</summary>
    public static TextBlock Label(string text) => Text(text, CardPalette.LabelSize, CardPalette.LabelBrush, FontWeights.Normal);

    /// <summary>正文。</summary>
    public static TextBlock Value(string text) => Text(text, CardPalette.ValueSize, CardPalette.ValueBrush, FontWeights.Normal);

    // ── 输入 ────────────────────────────────────────────────────────────────

    /// <summary>输入框：凹一档的底 + 发丝边 + 圆角 6。</summary>
    /// <remarks>
    /// 没有占位提示：这套设计给 Controller 的种子框写了 PlaceholderText="payload"，但本 build 的
    /// Jalium 里 <c>TextBox.PlaceholderText</c> 只有包内的 XML 文档、程序集里没有这个成员
    /// （编译直接报 CS1061），而种子值本来就有一个默认值，提示也不会显示出来。
    /// </remarks>
    public static TextBox Field()
    {
        var box = new TextBox
        {
            FontSize = CardPalette.ValueSize,
            Padding = new Thickness(7, 4),
            CornerRadius = new CornerRadius(CardPalette.FieldRadius),
            Background = CardPalette.FieldBrush,
            BorderBrush = CardPalette.BorderBrush,
            BorderThickness = new Thickness(1),
            Foreground = CardPalette.ValueBrush,
            CaretBrush = new SolidColorBrush(CardPalette.AccentTimerPython),
        };

        return box;
    }

    /// <summary>下拉框。与输入框同一套形状。</summary>
    public static ComboBox Picker()
        => new()
        {
            FontSize = CardPalette.ValueSize,
            Padding = new Thickness(7, 3),
            CornerRadius = new CornerRadius(CardPalette.FieldRadius),
            Background = CardPalette.FieldBrush,
            BorderBrush = CardPalette.BorderBrush,
            BorderThickness = new Thickness(1),
            Foreground = CardPalette.ValueBrush,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

    // ── 幽灵按钮 ────────────────────────────────────────────────────────────

    /// <summary>
    /// 同形的「幽灵按钮」：透明底 + 发丝边 + 圆角 6，语义只体现在字色上，不靠高饱和色块。
    /// 悬停只抬底与边（<c>#1E2731</c> / <c>#3A4553</c>），不换字色 —— 语义已经写在字色里了。
    /// </summary>
    public static Button Ghost(string text, Color semantic)
    {
        var button = new Button
        {
            Content = text,
            Background = CardPalette.Transparent,
            BorderBrush = CardPalette.BorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(CardPalette.FieldRadius),
            Foreground = new SolidColorBrush(semantic),
            FontSize = CardPalette.GhostSize,
            Padding = new Thickness(10, 4),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };

        // 悬停与离开各抬一次底与边。禁用态不参与：它连指针事件都不收，
        // 但真值上还是过一道 IsEnabled，免得悬停中被禁用时留着一块高亮底
        button.MouseEnter += (_, _) =>
        {
            if (button.IsEnabled)
            {
                button.Background = CardPalette.HoverBrush;
                button.BorderBrush = CardPalette.BorderHoverBrush;
            }
        };
        button.MouseLeave += (_, _) => Reset(button, semantic, button.IsEnabled);

        return button;
    }

    /// <summary>
    /// 把幽灵按钮摆到启用或禁用态。禁用只压暗边与字，形状不变 —— 平台默认模板会把按钮填成一块实灰，
    /// 那在一排幽灵按钮里格外突兀（Avalonia 那边为同一件事写了一条 :disabled 样式）。
    /// </summary>
    public static void SetGhostEnabled(Button button, Color semantic, bool enabled)
    {
        button.IsEnabled = enabled;
        Reset(button, semantic, enabled);
    }

    private static void Reset(Button button, Color semantic, bool enabled)
    {
        button.Background = CardPalette.Transparent;
        button.BorderBrush = enabled ? CardPalette.BorderBrush : CardPalette.BorderDisabledBrush;
        button.Foreground = enabled ? new SolidColorBrush(semantic) : CardPalette.DisabledTextBrush;
    }

    // ── 代码面（Python 卡） ─────────────────────────────────────────────────

    /// <summary>
    /// 脚本区：比卡面更深的一块底 + 一圈发丝边 + 圆角 6，顶上一条自己的小标题带
    /// （<c>script.py</c> 与 <c>Python 3</c>）。代码要读起来像代码，所以它不跟卡面共用底色。
    /// </summary>
    public static Border CodeBox(out TextBox editor)
    {
        editor = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = new FontFamily(CardPalette.MonoFamily),
            FontSize = CardPalette.CodeSize,
            Foreground = CardPalette.ValueBrush,
            Background = CardPalette.CodeSurfaceBrush,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0, 0, CardPalette.FieldRadius, CardPalette.FieldRadius),
            Padding = new Thickness(6, 4),
            CaretBrush = new SolidColorBrush(CardPalette.AccentTimerPython),
        };

        var name = Text("script.py", 11, new SolidColorBrush(CardPalette.CodeTitleText), FontWeights.SemiBold);
        name.FontFamily = new FontFamily(CardPalette.MonoFamily);
        var version = Text("Python 3", CardPalette.LabelSize, new SolidColorBrush(CardPalette.CodeMutedText), FontWeights.Normal);
        version.VerticalAlignment = VerticalAlignment.Center;

        var caption = new Grid();
        caption.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
        caption.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(name, 0);
        Grid.SetColumn(version, 1);
        caption.Children.Add(name);
        caption.Children.Add(version);

        var strip = new Border
        {
            Background = CardPalette.SurfaceBrush,
            CornerRadius = new CornerRadius(CardPalette.FieldRadius, CardPalette.FieldRadius, 0, 0),
            Padding = new Thickness(8, 4),
            Child = caption,
        };

        var rows = new Grid();
        rows.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        rows.RowDefinitions.Add(new RowDefinition { Height = GridLength.Star });
        Grid.SetRow(strip, 0);
        Grid.SetRow(editor, 1);
        rows.Children.Add(strip);
        rows.Children.Add(editor);
        var stripDivider = Divider(atBottom: true);
        Grid.SetRow(stripDivider, 0);
        rows.Children.Add(stripDivider);

        return new Border
        {
            Background = CardPalette.CodeSurfaceBrush,
            BorderBrush = new SolidColorBrush(CardPalette.CodeBorder),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(CardPalette.FieldRadius),
            Child = rows,
        };
    }

    private static Visibility Show(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;
}
