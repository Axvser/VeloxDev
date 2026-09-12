using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using Windows.Foundation;
using Windows.UI;

namespace Demo
{
    /// <summary>
    /// 案例列表里的一行：一个在屏元素、一句"这条在验什么"，以及这一行自己的三个动作。
    /// </summary>
    /// <remarks>
    /// 采样器行、加载行、过冲行共用这一个形状 —— 用户看到的是一张统一的表，行的种类只体现在各自的
    /// 令牌、描述与动作里。三个动作是 <see cref="Action"/> 而不是事件处理函数：行本身不该知道定时器、
    /// 载荷与元素之间的关系，那些留在窗口那侧（与 WPF / WinForms / Jalium / Avalonia 版同一分工）。
    /// </remarks>
    /// <param name="Title">行首那个名字，加粗显示。</param>
    /// <param name="Description">这条在验什么。采样器那几条来自探针表，与把手、载荷同源。</param>
    /// <param name="Element">这一行真正在动的那个在屏元素的台子。</param>
    /// <param name="ElementWidth">元素区的宽度。位移类那几条要放得下整段行程，其余的按元素本身给。</param>
    /// <param name="StartToken">"启动"按钮的自动化令牌。</param>
    /// <param name="StopToken">"关闭"按钮的自动化令牌。</param>
    /// <param name="ResetToken">"重置"按钮的自动化令牌。</param>
    /// <param name="Start">点"启动"。</param>
    /// <param name="Stop">点"关闭"。</param>
    /// <param name="Reset">点"重置"。</param>
    /// <param name="BulkStart">
    /// 顶栏"全部启动"用的动作，不填就等于 <paramref name="Start"/>。
    /// </param>
    /// <remarks>
    /// 两者之所以可能不同：行里那个"启动"是**单独**驱动这一行，它得管载荷、得盯着这一条跑完；而"全部启动"
    /// 是十几条一起发起，那时谁都不能去动别人。采样器行的"启动"里有一句"停掉上一次被观察的那一行"，
    /// 那是为了单点时不至于两条同时写载荷 —— 走到批量路径上就变成了"后一条把前一条掐掉"，只剩最后一条在跑。
    /// </remarks>
    internal sealed record CaseRow(
        string Title,
        string Description,
        FrameworkElement Element,
        double ElementWidth,
        string StartToken,
        string StopToken,
        string ResetToken,
        Action Start,
        Action Stop,
        Action Reset,
        Action? BulkStart = null);

    /// <summary>
    /// 案例列表：一条案例一行。左边是那条案例真正在动的在屏元素，中间是这条在验什么的文字，
    /// 右边固定宽度是这一行自己的 启动 / 关闭 / 重置。
    /// </summary>
    /// <remarks>
    /// 采样器那一类案例的元素是 <see cref="SamplerSubject"/> —— 采样器直接写在它上面，它自己按属性重绘，
    /// 所以"画出来"这件事不需要另一套映射代码。行的顺序就是传进来的顺序。
    /// <para>
    /// <b>每一行一样高</b>，所以每一条案例的行程都必须落在同一块台子里 —— 台子窄一点或矮一点，元素就会在最该
    /// 被看见的那一瞬跑出边界，而"跑出去看不见"和"没在跑"在屏幕上分不开。
    /// </para>
    /// <para>
    /// 给台子留多宽由各条案例自己的横向行程决定（见窗口那侧的 <c>*StageWidth</c>），<b>留多高则由最高的那条
    /// 行程决定</b>：旋转那一行转的是 80×60 的方块，任意角度下的外接矩形最大是它的对角线 —— 100 像素，
    /// 出现在转角约 53° 的那一刻（"平移"那一行只横向走，"投影"那一行在平面上翻，都不会更高）。所以台子高取
    /// 104，方块在中间只占 60，其余是留白。同一件事在别的平台上也各算过一次（Avalonia / Jalium 是 84，
    /// 因为那两条加载动画里含一个 1.3 倍的放大）。
    /// </para>
    /// <para>
    /// 采样器那一格只有 62 高（标尺 <see cref="SamplerSubject.DrawScale"/> 是按它定出来的，不能跟着长），
    /// 所以在等高的元素区里把它居中放一层。
    /// </para>
    /// <para>
    /// <b>WinUI 的 Panel 没有 <c>ClipToBounds</c>。</b>台子的裁切靠 <see cref="UIElement.Clip"/>：不裁的话
    /// 跑出台子的元素会滑到邻行的文字上，而"跑到别处"和"没动过"一样糟 —— 这个演示要消掉的正是这两种错觉。
    /// </para>
    /// </remarks>
    internal sealed class SamplerBench
    {
        /// <summary>案例行里元素区统一的高度：装得下最高的那条行程（旋转方块的对角线 100）。</summary>
        internal const double StageHeight = 104d;

        /// <summary>行高：每一行都是它，台子与文字都在这条带子里垂直居中。</summary>
        internal const double RowHeight = StageHeight + 8d;

        /// <summary>
        /// 元素在台子里的起点。
        /// </summary>
        /// <remarks>
        /// 横向留 12 而不是贴着左边缘：旋转中的方块在 53° 附近的外接矩形是 100×100，绕着元素中心摊开，
        /// 起点贴边的话那一瞬的左边角会被裁掉 2 像素。
        /// </remarks>
        internal const double ElementInset = 12d;

        /// <summary>60 高的方块在等高的台子里垂直居中。</summary>
        internal const double ElementTop = (StageHeight - 60d) / 2d;

        /// <summary>采样器那一格的尺寸。标尺（<see cref="SamplerSubject.DrawScale"/>）是按它定出来的，不能随手改。</summary>
        internal const double SamplerCellWidth = 96d;

        internal const double SamplerCellHeight = 62d;

        /// <summary>格子下面那行小字占的高度 —— 它就这一格在屏幕上的位置写成 UIA 量得出的一个框。</summary>
        private const double LabelHeight = 14d;

        /// <summary>右侧控制区的固定宽度 —— 三列按钮在任何一行里都落在同一竖线上。</summary>
        private const double ControlWidth = 186d;

        private const double ButtonHeight = 26d;

        private static readonly Color StageBackColor = Color.FromArgb(0xFF, 0x1E, 0x1E, 0x1E);
        private static readonly Color FrameColor = Color.FromArgb(0xFF, 0x40, 0x40, 0x40);
        private static readonly Color LineColor = Color.FromArgb(0xFF, 0x33, 0x33, 0x33);
        private static readonly Color CaptionColor = Color.FromArgb(0xFF, 0xAA, 0xAA, 0xAA);

        private readonly Dictionary<string, SamplerSubject> _subjects = new(StringComparer.Ordinal);

        /// <summary>某条采样器那一行的被写元素 —— 采样器写在它上面，载荷也从它读回。</summary>
        internal SamplerSubject SubjectFor(string sampler) => _subjects[sampler];

        /// <summary>
        /// 一条案例的元素区：定宽、等高、裁边、深色底。
        /// </summary>
        /// <remarks>
        /// 台子就是这一条案例的场地：宽度由那条案例真正走多远定，跑出去会被裁掉 —— 而"跑出去看不见"与
        /// "没在跑"在屏幕上一样，正是这个演示要消除的错觉。
        /// </remarks>
        internal static Canvas Stage(double width) => Stage(width, StageHeight);

        /// <summary>
        /// 一块指定大小的元素区。
        /// </summary>
        /// <remarks>
        /// 采样器那一格走这一条：它的高是标尺定死的 62，不随行里的元素区一起长。
        /// </remarks>
        internal static Canvas Stage(double width, double height) => new()
        {
            Width = width,
            Height = height,
            Background = new SolidColorBrush(StageBackColor),
            // WinUI 的 Panel 没有 ClipToBounds，裁切只能靠 UIElement.Clip。
            Clip = new RectangleGeometry { Rect = new Rect(0d, 0d, width, height) },
        };

        /// <summary>把一块矮的内容在等高的元素区里垂直居中。</summary>
        internal static Canvas Center(FrameworkElement content, double width)
        {
            var box = Stage(width);
            Canvas.SetTop(content, (StageHeight - content.Height) / 2d);
            box.Children.Add(content);
            return box;
        }

        /// <summary>
        /// 造一条采样器案例行：元素是一个在它自己那一格里被写的控件，令牌沿用 <c>over.sampler.&lt;类型名&gt;</c>。
        /// </summary>
        /// <remarks>
        /// "启动"的令牌刻意不跟另外两条走同一套命名：验收套件点的就是它，点它要写闭式解载荷并起那条真动画。
        /// <para>
        /// 这一格另外带着 <c>over.cell.&lt;类型名&gt;</c>：WinUI 是唯一有格子级令牌的平台，它标的是**这一行的
        /// 元素区在屏幕上的位置** —— 演示台本身没有可读的观测面，而"元素被排到窗口外"正是这类界面最容易
        /// 悄悄发生的故障。所以它落在那行小字上而不是台子本身：**面板在 WinUI 里根本没有自动化 peer**，
        /// 挂在 Canvas 上的令牌在 UIA 树里查不到，量不出任何东西。
        /// </para>
        /// </remarks>
        internal CaseRow SamplerRow(string sampler, Action start, Action stop, Action reset, Action bulkStart)
        {
            var cell = Stage(SamplerCellWidth, SamplerCellHeight);

            var subject = new SamplerSubject { Kind = sampler };
            cell.Children.Add(subject);
            _subjects[sampler] = subject;

            // 采样式台子的一格：格子上方是标尺画出来的东西，下面这行小字是它在屏幕上的锚点。
            var label = new TextBlock
            {
                Text = sampler,
                FontSize = 9,
                Margin = new Thickness(1, 2, 1, 0),
                Foreground = new SolidColorBrush(CaptionColor),
            };
            AutomationProperties.SetAutomationId(label, $"over.cell.{sampler}");

            var stack = new StackPanel { Width = SamplerCellWidth, Height = SamplerCellHeight + LabelHeight };
            stack.Children.Add(cell);
            stack.Children.Add(label);

            return new CaseRow(
                sampler,
                SamplerProbe.Description(sampler),
                Frame(Center(stack, SamplerCellWidth)),
                SamplerCellWidth,
                $"over.sampler.{sampler}",
                $"over.row.stop.{sampler}",
                $"over.row.reset.{sampler}",
                start,
                stop,
                reset,
                bulkStart);
        }

        /// <summary>
        /// 把一串案例行铺成一张表。
        /// </summary>
        internal static FrameworkElement Build(IReadOnlyList<CaseRow> rows)
        {
            var list = new StackPanel();

            foreach (var row in rows)
            {
                list.Children.Add(BuildRow(row));
            }

            return list;
        }

        /// <summary>元素自己画在深色底上、底色与窗口一样，所以每一行的元素都得有个框，否则"这一行的元素在哪"看不出来。</summary>
        internal static Border Frame(FrameworkElement element) => new()
        {
            BorderBrush = new SolidColorBrush(FrameColor),
            BorderThickness = new Thickness(1),
            Child = element,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        private static FrameworkElement BuildRow(CaseRow row)
        {
            // 名字加粗打头，后面跟这条在验什么 —— 一段文字里的两个 Run，所以描述跟着标题一起折行，
            // 而不是被挤成"标题一行、描述一行"的两截。
            var caption = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                FontSize = 11,
                Foreground = new SolidColorBrush(CaptionColor),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 8, 0),
            };
            caption.Inlines.Add(new Run { Text = row.Title, FontWeight = FontWeights.Bold });
            caption.Inlines.Add(new Run { Text = $"  {row.Description}" });

            var controls = new Grid { Width = ControlWidth, VerticalAlignment = VerticalAlignment.Center };
            for (var column = 0; column < 3; column++)
            {
                controls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            }

            controls.Children.Add(MakeControl("启动", row.StartToken, row.Start, 0));
            controls.Children.Add(MakeControl("关闭", row.StopToken, row.Stop, 1));
            controls.Children.Add(MakeControl("重置", row.ResetToken, row.Reset, 2));

            // 行高写死在这一层：台子（62）、文字（最多两行）与按钮都在它里面垂直居中，
            // 于是"每行一样高"由这一处保证，列表读起来才是一张表。
            var layout = new Grid { Height = RowHeight, Margin = new Thickness(2, 1, 2, 1) };
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var element = row.Element;
            element.VerticalAlignment = VerticalAlignment.Center;
            element.HorizontalAlignment = HorizontalAlignment.Left;

            Grid.SetColumn(element, 0);
            Grid.SetColumn(caption, 1);
            Grid.SetColumn(controls, 2);
            layout.Children.Add(element);
            layout.Children.Add(caption);
            layout.Children.Add(controls);

            return new Border
            {
                BorderBrush = new SolidColorBrush(LineColor),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Child = layout,
            };
        }

        private static Button MakeControl(string text, string automationId, Action action, int column)
        {
            var button = new Button
            {
                Content = text,
                Margin = new Thickness(2, 3, 2, 3),
                FontSize = 11,
                Height = ButtonHeight,
            };

            AutomationProperties.SetAutomationId(button, automationId);

            // 焦点落到行里的按钮上，就把这一行滚进视野 —— "套件把一行弄进视野"走的正是聚焦它
            // （见 AutomationLocator.BringIntoView）。
            //
            // WinUI 只在**键盘/手柄**改变焦点时才自动滚（UIElement.BringIntoViewRequested 的说明里写着这条），
            // 而 UIA 的 SetFocus 走的是程序性焦点，框架不会替它滚。于是滚出视野的行会一直停在视野外：
            // UIA 的 Invoke 照样点得着，人却看不见，验收侧量到的 BoundingRectangle 还是空的 —— 而"够得着"
            // 正是那块列表存在的意义。所以自己发一次请求，两个参数都是为了"别添乱"：
            //   - AnimationDesired = false：套件聚焦完就紧接着量它，滚动必须当场落地；
            //   - VerticalAlignmentRatio = NaN：只做够用的那一点 —— 否则点一个已经看得见的行里的按钮，
            //     那一行会平白跳到列表顶端。
            button.GotFocus += (sender, _) => ((Button)sender).StartBringIntoView(
                new BringIntoViewOptions { AnimationDesired = false, VerticalAlignmentRatio = double.NaN });

            button.Click += (_, _) => action();
            Grid.SetColumn(button, column);
            return button;
        }
    }
}
