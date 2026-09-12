using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Demo
{
    /// <summary>
    /// 案例列表里的一行：一个在屏元素、一句"这条在验什么"，以及这一行自己的三个动作。
    /// </summary>
    /// <remarks>
    /// 采样器行、加载行、过冲行共用这一个形状 —— 用户看到的是一张统一的表，行的种类只体现在各自的令牌、
    /// 描述与动作里。三个动作是 <see cref="Action"/> 而不是事件处理函数：行本身不该知道定时器、载荷与元素之间的
    /// 关系，那些留在窗体那侧（与 WPF 版同一分工）。
    /// </remarks>
    /// <param name="Title">行首那个名字，加粗显示。</param>
    /// <param name="Description">这条在验什么。采样器那几条来自探针表，与把手、载荷同源。</param>
    /// <param name="Element">这一行真正在动的那个在屏元素的台子。</param>
    /// <param name="ElementWidth">台子的宽度，后面的文字从它右边开始。</param>
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
        Control Element,
        int ElementWidth,
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
    /// <b>每一行一样高</b>，所以每一条案例的行程都必须落在同一块台子里 —— 台子窄一点，元素就会在最该被看见的
    /// 那一瞬跑出边界，而"跑出去看不见"和"没在跑"在屏幕上分不开。给台子留多宽由各条案例自己的行程决定
    /// （见 <see cref="PlayHeight"/> 与各条案例的旅程）。
    /// </para>
    /// </remarks>
    internal sealed class SamplerBench
    {
        /// <summary>采样器那一格的尺寸。</summary>
        internal const int StageWidth = 96;
        internal const int StageHeight = 62;

        /// <summary>
        /// 加载行与过冲行共用的台子高度。
        /// </summary>
        /// <remarks>
        /// 比 60 高的方块加上起点边距还要多一截：台子必须**装得下**元素，WinForms 的子控件被父级裁掉，
        /// 静止态若在边界外，那一行看上去就是空的。
        /// </remarks>
        internal const int PlayHeight = 110;

        /// <summary>右侧控制区的固定宽度 —— 三列按钮在任何一行里都落在同一竖线上。</summary>
        internal const int ControlWidth = 186;

        /// <summary>列表内容区的宽度。竖滚动条占掉的那十几像素不挤进来，所以横向永远不会滚。</summary>
        internal const int RowWidth = 1180;

        /// <summary>元素台子的左边距：每一行的元素从同一条竖线开始。</summary>
        private const int ElementLeft = 8;

        /// <summary>行高：每一行都是它，台子与文字都在这条带子里垂直居中。</summary>
        internal const int RowHeight = PlayHeight + 8;

        /// <summary>台子与文字之间的空档。</summary>
        private const int CaptionGap = 10;

        private const int ControlHeight = 28;
        private const int ControlGap = 3;

        /// <summary>文字行高：标题一行，描述留得下两行。</summary>
        private const int CaptionHeight = 36;

        private static readonly Font TitleFont = new("微软雅黑", 8.25f, FontStyle.Bold);
        private static readonly Font BodyFont = new("微软雅黑", 8.25f);

        private readonly Dictionary<string, SamplerSubject> _subjects = new(StringComparer.Ordinal);

        /// <summary>某条采样器那一行的被写元素 —— 采样器写在它上面，载荷也从它读回。</summary>
        internal SamplerSubject SubjectFor(string sampler) => _subjects[sampler];

        /// <summary>
        /// 造一条采样器案例行：元素是一个在它自己那一格里被写的控件，令牌沿用 <c>over.sampler.&lt;类型名&gt;</c>。
        /// </summary>
        /// <remarks>
        /// "启动"的令牌刻意不跟另外两条走同一套命名：验收套件点的就是它，点它要写闭式解载荷并起那条真动画。
        /// </remarks>
        internal CaseRow SamplerRow(string sampler, Action start, Action stop, Action reset, Action bulkStart)
        {
            var stage = Stage(StageWidth, StageHeight, SamplerSubject.StageColor);
            var subject = new SamplerSubject
            {
                Location = new Point(0, 0),
                Size = new Size(StageWidth, StageHeight),
            };
            stage.Controls.Add(subject);
            _subjects[sampler] = subject;

            return new CaseRow(
                sampler,
                SamplerProbe.Description(sampler),
                stage,
                StageWidth,
                $"over.sampler.{sampler}",
                $"over.row.stop.{sampler}",
                $"over.row.reset.{sampler}",
                start,
                stop,
                reset,
                bulkStart);
        }

        /// <summary>
        /// 元素的台子：定尺、细边、垂直居中。
        /// </summary>
        /// <remarks>
        /// 台子的宽高就是这一条案例的场地 —— WinForms 的子控件被父级裁掉，台子给小了，元素跑出去就"没了"，
        /// 而那看上去和"没动"一模一样，正是这个演示要消除的错觉。
        /// </remarks>
        internal static Panel Stage(int width, int height, Color background) => new()
        {
            Location = new Point(ElementLeft, (RowHeight - height) / 2),
            Size = new Size(width, height),
            BackColor = background,
            BorderStyle = BorderStyle.FixedSingle,
        };

        /// <summary>
        /// 把一串案例行铺成一张表：一个可滚的容器，行按行高摆在绝对 Y 上。
        /// </summary>
        internal static Control Build(IReadOnlyList<CaseRow> rows)
        {
            var list = new Panel { Location = new Point(0, 0), Size = new Size(RowWidth + 16, 0) };

            for (var index = 0; index < rows.Count; index++)
            {
                var line = BuildRow(rows[index]);
                line.Location = new Point(0, RowHeight * index);
                list.Controls.Add(line);
            }

            // 内容区的高度决定滚动范围：行多长，滚多远。
            list.Size = new Size(RowWidth + 16, RowHeight * rows.Count);
            return list;
        }

        private static Control BuildRow(CaseRow row)
        {
            var line = new Panel
            {
                Location = new Point(0, 0),
                Size = new Size(RowWidth, RowHeight),
                BackColor = Color.White,
            };

            var captionTop = (RowHeight - CaptionHeight) / 2;
            var captionLeft = ElementLeft + row.ElementWidth + CaptionGap;
            var captionWidth = RowWidth - ElementLeft - ControlWidth - captionLeft;

            var title = new Label
            {
                Text = row.Title,
                Font = TitleFont,
                ForeColor = Color.FromArgb(0x33, 0x33, 0x33),
                AutoSize = false,
                Location = new Point(captionLeft, captionTop),
                Size = new Size(TextRenderer.MeasureText(row.Title, TitleFont).Width + 2, CaptionHeight),
            };

            var description = new Label
            {
                Text = row.Description,
                Font = BodyFont,
                ForeColor = Color.FromArgb(0x88, 0x88, 0x88),
                AutoSize = false,
                Location = new Point(title.Right + 4, captionTop),
                Size = new Size(Math.Max(0, captionWidth - title.Width - 4), CaptionHeight),
            };

            var controls = new Panel
            {
                Location = new Point(RowWidth - ElementLeft - ControlWidth, (RowHeight - ControlHeight) / 2),
                Size = new Size(ControlWidth, ControlHeight),
            };

            var buttonWidth = (ControlWidth - 2 * ControlGap) / 3;
            controls.Controls.Add(MakeControl("启动", row.StartToken, row.Start, 0, buttonWidth));
            controls.Controls.Add(MakeControl("关闭", row.StopToken, row.Stop, buttonWidth + ControlGap, buttonWidth));
            controls.Controls.Add(MakeControl("重置", row.ResetToken, row.Reset, 2 * (buttonWidth + ControlGap), buttonWidth));

            line.Controls.Add(row.Element);
            line.Controls.Add(title);
            line.Controls.Add(description);
            line.Controls.Add(controls);

            return line;
        }

        private static Button MakeControl(string text, string automationId, Action action, int left, int width)
        {
            var button = new Button
            {
                // Control.Name 就是套件认的 AutomationId（AutomationId 取的是 Owner.Name）。
                Name = automationId,
                Text = text,
                Font = BodyFont,
                Location = new Point(left, 0),
                Size = new Size(width, ControlHeight),
                BackColor = Color.WhiteSmoke,
            };

            button.Click += (sender, args) => action();
            return button;
        }
    }
}
