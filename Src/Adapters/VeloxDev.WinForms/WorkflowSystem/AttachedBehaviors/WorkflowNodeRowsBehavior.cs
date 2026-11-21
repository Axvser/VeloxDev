using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using VeloxDev.WinForms.Xaml;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// Builds a node view's bare input port and labeled output rows from the bound node's slots.
/// </summary>
/// <remarks>
/// <para>
/// A template or demo declares an ordinary control for the card and one child panel for the rows, names that panel
/// with <see cref="SetRowsHostName"/>, and supplies the slot view through <see cref="SetSlotViewFactory"/> or
/// <see cref="SetSlotViewType"/>. This helper rebuilds the rows whenever the node is rebound, applies the surface's
/// zoom collapse to the row metrics, and writes the slot into the view's <c>ViewModel</c> property (falling back to
/// <see cref="WorkflowSlotAttachment"/> for a legacy view without one). The card itself keeps its own paint.
/// </para>
/// <para>
/// The helper enables <see cref="WorkflowSlotLayoutBehavior"/> on the card for the rows host, because the slot
/// anchors link geometry depends on only exist once that behavior measures the views.
/// </para>
/// </remarks>
public sealed class WorkflowNodeRowsBehavior
{
    private const int DefaultRowHeight = 26;
    private const int DefaultSlotSize = 18;
    private const float DefaultLabelFontSize = 8.5f;

    private static readonly ConditionalWeakTable<Control, RowsOwner> States = new();

    /// <summary>Whether the rows lifecycle is active on the card.</summary>
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(WorkflowNodeRowsBehavior),
        new PropertyMetadata(false, OnIsEnabledChanged));

    /// <summary>Name of the child control that hosts the generated rows.</summary>
    public static readonly DependencyProperty RowsHostNameProperty = DependencyProperty.RegisterAttached(
        "RowsHostName", typeof(string), typeof(WorkflowNodeRowsBehavior),
        new PropertyMetadata("PART_DynamicOutputs", OnRowsHostNameChanged));

    /// <summary>Simple or full type name of the view created for each slot.</summary>
    public static readonly DependencyProperty SlotViewTypeProperty = DependencyProperty.RegisterAttached(
        "SlotViewType", typeof(string), typeof(WorkflowNodeRowsBehavior),
        new PropertyMetadata("SlotView", OnSlotViewTypeChanged));

    private static void OnIsEnabledChanged(Control target, DependencyPropertyChangedEventArgs e)
        => SetIsEnabled(target, e.NewValue is true);

    private static void OnRowsHostNameChanged(Control target, DependencyPropertyChangedEventArgs e)
        => SetRowsHostName(target, e.NewValue as string);

    private static void OnSlotViewTypeChanged(Control target, DependencyPropertyChangedEventArgs e)
        => SetSlotViewType(target, e.NewValue as string);

    /// <summary>Gets whether the rows lifecycle is active on the card.</summary>
    public static bool GetIsEnabled(Control element) => Owner(element).IsEnabled;

    /// <summary>Starts or stops rebuilding the card's slot rows.</summary>
    public static void SetIsEnabled(Control element, bool value)
    {
        var owner = Owner(element);
        if (owner.IsEnabled == value)
        {
            return;
        }

        owner.IsEnabled = value;
        if (value)
        {
            owner.Attach();
        }
        else
        {
            owner.Detach();
        }
    }

    /// <summary>Gets the name of the child control that hosts the rows.</summary>
    public static string? GetRowsHostName(Control element) => Owner(element).RowsHostName;

    /// <summary>Names the child control that hosts the rows; defaults to <c>PART_DynamicOutputs</c>.</summary>
    public static void SetRowsHostName(Control element, string? value)
    {
        var owner = Owner(element);
        owner.RowsHostName = value;
        // RowsHostName 可能晚于 IsEnabled 写入（XAML 属性顺序！）。Attach 时 EnsureSlotLayout
        // 已经把 SlotEnumeratorNames 固定成默认 PART_DynamicOutputs，这里必须跟着宿主名一起改，
        // 否则槽布局永远找不到真正的行宿主，slot anchor 停在 NaN，所有 link 被渲染门隐藏。
        owner.ApplySlotEnumeratorName();
        owner.RefreshIfAttached();
    }

    /// <summary>Gets the simple or full type name used to create a slot view.</summary>
    public static string? GetSlotViewType(Control element) => Owner(element).SlotViewType;

    /// <summary>Sets the simple or full type name used to create a slot view; defaults to <c>SlotView</c>.</summary>
    public static void SetSlotViewType(Control element, string? value)
    {
        Owner(element).SlotViewType = value;
        Owner(element).RefreshIfAttached();
    }

    /// <summary>Gets the factory that creates a slot view.</summary>
    public static Func<Control>? GetSlotViewFactory(Control element) => Owner(element).SlotViewFactory;

    /// <summary>Sets the factory that creates a slot view; takes precedence over the view type name.</summary>
    public static void SetSlotViewFactory(Control element, Func<Control>? value)
    {
        Owner(element).SlotViewFactory = value;
        Owner(element).RefreshIfAttached();
    }

    /// <summary>Gets the output row height at zoom scale 1.</summary>
    public static int GetRowHeight(Control element) => Owner(element).RowHeight;

    /// <summary>Sets the output row height at zoom scale 1.</summary>
    public static void SetRowHeight(Control element, int value)
    {
        Owner(element).RowHeight = Math.Max(1, value);
        Owner(element).RefreshIfAttached();
    }

    /// <summary>Gets the slot glyph size at zoom scale 1.</summary>
    public static int GetSlotSize(Control element) => Owner(element).SlotSize;

    /// <summary>Sets the slot glyph size at zoom scale 1.</summary>
    public static void SetSlotSize(Control element, int value)
    {
        Owner(element).SlotSize = Math.Max(1, value);
        Owner(element).RefreshIfAttached();
    }

    /// <summary>Gets the row label colour, or <see cref="Color.Empty"/> to follow the card's foreground.</summary>
    public static Color GetLabelForeground(Control element) => Owner(element).LabelForeground;

    /// <summary>Sets the row label colour.</summary>
    public static void SetLabelForeground(Control element, Color value)
    {
        Owner(element).LabelForeground = value;
        Owner(element).RefreshIfAttached();
    }

    /// <summary>Gets the row background, or <see cref="Color.Empty"/> to follow the card's background.</summary>
    public static Color GetLabelBackground(Control element) => Owner(element).LabelBackground;

    /// <summary>Sets the row background.</summary>
    public static void SetLabelBackground(Control element, Color value)
    {
        Owner(element).LabelBackground = value;
        Owner(element).RefreshIfAttached();
    }

    /// <summary>Rebuilds the rows from the currently bound node.</summary>
    public static void Refresh(Control element) => Owner(element).RefreshIfAttached();

    private static RowsOwner Owner(Control element)
    {
        if (element is null) throw new ArgumentNullException(nameof(element));
        return States.GetValue(element, static card => new RowsOwner(card));
    }

    private sealed class RowsOwner
    {
        private readonly Control card;
        private bool attached;
        private double collapse = 1d;
        private Control? rowsHost;
        private WorkflowNodeAttachment? attachment;
        private SlotRowsPanel? panel;

        public RowsOwner(Control card)
        {
            this.card = card;
            RowsHostName = "PART_DynamicOutputs";
            SlotViewType = "SlotView";
        }

        public bool IsEnabled { get; set; }

        public string? RowsHostName { get; set; }

        public string? SlotViewType { get; set; }

        public Func<Control>? SlotViewFactory { get; set; }

        public int RowHeight { get; set; } = DefaultRowHeight;

        public int SlotSize { get; set; } = DefaultSlotSize;

        public Color LabelForeground { get; set; } = Color.Empty;

        public Color LabelBackground { get; set; } = Color.Empty;

        public void Attach()
        {
            if (attached)
            {
                return;
            }

            attached = true;
            attachment = WorkflowNodeAttachment.For(card) ?? WorkflowNodeAttachment.Attach(card);
            attachment.Rebound += OnRebound;
            attachment.CollapseChanged += OnCollapseChanged;
            card.HandleCreated += OnCardStructureChanged;
            card.ControlAdded += OnCardControlAdded;
            card.Disposed += OnCardDisposed;

            EnsureSlotLayout();
            Rebuild();
        }

        public void Detach()
        {
            if (!attached)
            {
                return;
            }

            attached = false;
            card.HandleCreated -= OnCardStructureChanged;
            card.ControlAdded -= OnCardControlAdded;
            card.Disposed -= OnCardDisposed;

            if (attachment is not null)
            {
                attachment.Rebound -= OnRebound;
                attachment.CollapseChanged -= OnCollapseChanged;
                attachment = null;
            }

            panel?.Dispose();
            panel = null;
            rowsHost = null;
        }

        public void RefreshIfAttached()
        {
            if (attached)
            {
                Rebuild();
            }
        }

        private void OnCardDisposed(object? sender, EventArgs e) => Detach();

        private void OnCardStructureChanged(object? sender, EventArgs e)
        {
            if (rowsHost is null || panel is null)
            {
                Rebuild();
            }
        }

        private void OnCardControlAdded(object? sender, ControlEventArgs e)
        {
            if (rowsHost is null || ReferenceEquals(e.Control, rowsHost))
            {
                Rebuild();
            }
        }

        private void OnRebound(object? sender, EventArgs e) => Rebuild();

        private void OnCollapseChanged(object? sender, WorkflowNodeAttachment.CollapseChangedEventArgs e)
        {
            if (e.Collapse == collapse)
            {
                return;
            }

            collapse = e.Collapse;
            panel?.ApplyScale(collapse, RowHeight, SlotSize);
            WorkflowSlotLayoutBehavior.SyncNow(card);
        }

        // 卡片模板多半在构造 code-behind 里调 SetIsEnabled；这里再兜一次坐标宿，省得模板漏配。
        private void EnsureSlotLayout()
        {
            WorkflowSlotLayoutBehavior.SetIsEnabled(card, true);
            ApplySlotEnumeratorName();
        }

        public void ApplySlotEnumeratorName()
        {
            if (!string.IsNullOrWhiteSpace(RowsHostName))
            {
                WorkflowSlotLayoutBehavior.SetSlotEnumeratorNames(card, RowsHostName);
            }
        }

        private void Rebuild()
        {
            if (!attached || card.IsDisposed)
            {
                return;
            }

            var host = FindNamed(card, RowsHostName);
            if (host is null)
            {
                return;
            }

            if (!ReferenceEquals(rowsHost, host) || panel is null)
            {
                panel?.Dispose();
                rowsHost = host;
                panel = new SlotRowsPanel
                {
                    Dock = DockStyle.Fill,
                    BackColor = ResolveBackground(),
                };
                host.Controls.Add(panel);
            }

            panel.SuspendLayout();
            try
            {
                WorkflowNodeAttachment.DisposeChildren(panel);
                panel.SetInputView(null);

                // Slots 是可写属性，反序列化/选择器换型期间可能是 null，集合里也可能有空元素；
                // ResolveInputSlot 已经按这两点防守，这里必须同样处理。
                if (attachment?.Node is not { } node || node.Slots is not { } slots)
                {
                    return;
                }

                var foreground = ResolveForeground();
                var background = ResolveBackground();
                var inputSlot = attachment.ResolveInputSlot();
                var rows = new List<SlotRowPanel>();
                var outputIndex = 0;

                foreach (var slot in slots)
                {
                    if (slot is null)
                    {
                        continue;
                    }

                    if (inputSlot is not null && ReferenceEquals(slot, inputSlot))
                    {
                        continue;
                    }

                    var hasSource = (slot.Channel & (SlotChannel.OneSource | SlotChannel.MultipleSources)) != 0;
                    var hasTarget = (slot.Channel & (SlotChannel.OneTarget | SlotChannel.MultipleTargets)) != 0;

                    if (hasSource && !hasTarget)
                    {
                        // 纯输入口画成左侧的裸字形，不占输出行。
                        inputSlot ??= slot;
                        continue;
                    }

                    if (!hasTarget)
                    {
                        continue;
                    }

                    rows.Add(new SlotRowPanel(
                        attachment.ResolveSlotLabel(slot, outputIndex++),
                        CreateSlotView(slot),
                        foreground,
                        background,
                        RowHeight,
                        SlotSize,
                        DefaultLabelFontSize));
                }

                foreach (var row in rows)
                {
                    panel.Controls.Add(row);
                }

                if (inputSlot is not null)
                {
                    var inputView = CreateSlotView(inputSlot);
                    panel.Controls.Add(inputView);
                    panel.SetInputView(inputView);
                }
            }
            finally
            {
                panel.ResumeLayout(true);
                panel.ApplyScale(collapse, RowHeight, SlotSize);
                panel.Invalidate();
            }

            WorkflowSlotLayoutBehavior.Refresh(card);
        }

        private Control CreateSlotView(ISlotComponent slot)
        {
            var view = SlotViewFactory?.Invoke() ?? InstantiateSlotView() ?? new Panel();

            // 通用插槽视图暴露 ViewModel；旧的附加式视图才需要补一份 attachment。
            if (TrySetSlotViewModel(view, slot))
            {
                return view;
            }

            var slotAttachment = WorkflowSlotAttachment.For(view) ?? WorkflowSlotAttachment.Attach(view);
            slotAttachment.Slot = slot;
            return view;
        }

        private static bool TrySetSlotViewModel(Control view, ISlotComponent slot)
        {
            var property = view.GetType().GetProperty("ViewModel", BindingFlags.Instance | BindingFlags.Public);
            if (property is null || !property.CanWrite || !property.PropertyType.IsInstanceOfType(slot))
            {
                return false;
            }

            try
            {
                property.SetValue(view, slot);
                view.Tag = slot;
                return true;
            }
            catch
            {
                return false;
            }
        }

        [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
            "Trimming", "IL2072",
            Justification = "The slot view type is named by the host and ships with the application.")]
        private Control? InstantiateSlotView()
        {
            if (string.IsNullOrWhiteSpace(SlotViewType))
            {
                return null;
            }

            var name = SlotViewType!.Trim();
            var assembly = card.GetType().Assembly;
            var type = assembly.GetType(name, throwOnError: false);
            if (type is null && name.IndexOf('.') < 0 && !string.IsNullOrEmpty(card.GetType().Namespace))
            {
                type = assembly.GetType(card.GetType().Namespace + "." + name, throwOnError: false);
            }

            return type is null ? null : Activator.CreateInstance(type) as Control;
        }

        private Color ResolveForeground()
            => LabelForeground == Color.Empty ? card.ForeColor : LabelForeground;

        private Color ResolveBackground()
            => LabelBackground == Color.Empty ? card.BackColor : LabelBackground;

        private static Control? FindNamed(Control root, string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            foreach (var control in EnumerateSelfAndDescendants(root))
            {
                if (string.Equals(control.Name, name, StringComparison.Ordinal))
                {
                    return control;
                }
            }

            return null;
        }

        private static IEnumerable<Control> EnumerateSelfAndDescendants(Control root)
        {
            yield return root;
            foreach (var child in EnumerateDescendants(root))
            {
                yield return child;
            }
        }

        private static IEnumerable<Control> EnumerateDescendants(Control root)
        {
            foreach (Control child in root.Controls)
            {
                yield return child;
                foreach (var nested in EnumerateDescendants(child))
                {
                    yield return nested;
                }
            }
        }
    }

    // 行与裸输入口共用一个面板：输入口固定左侧居中，输出行在剩余高度里居中排。
    private sealed class SlotRowsPanel : Panel
    {
        private Control? inputView;
        private double collapse = 1d;
        private int baseSlotSize = DefaultSlotSize;

        public SlotRowsPanel()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw,
                true);
        }

        public void SetInputView(Control? view)
        {
            inputView = view;
            if (view is not null && view.Parent == this)
            {
                view.BringToFront();
            }
        }

        public void ApplyScale(double value, int rowHeight, int slotSize)
        {
            var next = value <= 0d ? 1d : value;
            var nextSlot = Math.Max(1, slotSize);

            // 不能因为本面板已经记住同一因子就早退：缩放之后新建的输入视图/行还没有应用过它。
            // 行自己的 ApplyScale 仍按 appliedScale 早退，重复调用不会重复建字体。
            collapse = next;
            baseSlotSize = nextSlot;

            SuspendLayout();
            foreach (Control child in Controls)
            {
                if (child is SlotRowPanel row)
                {
                    row.ApplyScale(collapse);
                }
            }

            ResumeLayout(true);
            PerformLayout();
            Invalidate();
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);

            // 每一处度量都从设计值乘同一个因子，而不是在上一次结果上叠乘：缩放来回走不会因取整漂移。
            var slotSize = Math.Max(2, (int)Math.Round(baseSlotSize * collapse));
            var inset = Math.Max(1, (int)Math.Round(4 * collapse));

            if (inputView is not null && inputView.Visible)
            {
                inputView.SetBounds(
                    inset,
                    (Height - slotSize) / 2,
                    slotSize,
                    slotSize);
            }

            var visible = new List<Control>();
            foreach (Control child in Controls)
            {
                if (!ReferenceEquals(child, inputView) && child.Visible)
                {
                    visible.Add(child);
                }
            }

            if (visible.Count == 0)
            {
                return;
            }

            var total = 0;
            foreach (var child in visible)
            {
                total += child.Height;
            }

            var y = Math.Max(0, (Height - total) / 2);
            if (inputView is { Visible: true })
            {
                y = Math.Max(y, inputView.Height);
            }

            foreach (var child in visible)
            {
                child.SetBounds(0, y, Width, child.Height);
                y += child.Height;
            }
        }
    }

    // 一行 = 右对齐的名字 + 右端的插槽视图。尺寸按折叠因子重算，低缩放时不裁切。
    private sealed class SlotRowPanel : Panel
    {
        // 设计值（缩放 1）：行的所有度量都由它们乘同一个因子得到。
        private const int LabelPaddingRight = 8;
        private const int SlotRightInset = 4;
        private const int SlotLabelGap = 10;
        private readonly Label label;
        private readonly Control slot;
        private readonly int baseRowHeight;
        private readonly int baseSlotSize;
        private readonly float baseFontSize;
        private readonly List<Font> retiredFonts = [];
        private double appliedScale = double.NaN;

        public SlotRowPanel(
            string name, Control slot, Color foreground, Color background,
            int rowHeight, int slotSize, float fontSize)
        {
            baseRowHeight = Math.Max(1, rowHeight);
            baseSlotSize = Math.Max(1, slotSize);
            baseFontSize = Math.Max(1f, fontSize);

            Height = baseRowHeight;
            Margin = Padding.Empty;
            BackColor = background;

            label = new Label
            {
                Text = name,
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleRight,
                ForeColor = foreground,
                BackColor = background,
                Margin = Padding.Empty,
                Padding = new Padding(0, 0, 8, 0),
                Font = new Font("Segoe UI", baseFontSize, FontStyle.Regular),
            };

            this.slot = slot;
            this.slot.Width = baseSlotSize;
            this.slot.Height = baseSlotSize;
            this.slot.Margin = Padding.Empty;

            Controls.Add(label);
            Controls.Add(this.slot);
        }

        public void ApplyScale(double collapse)
        {
            var scale = collapse <= 0d ? 1d : collapse;
            if (scale == appliedScale)
            {
                return;
            }

            appliedScale = scale;
            Height = Math.Max(1, (int)Math.Round(baseRowHeight * scale));
            var size = Math.Max(2, (int)Math.Round(baseSlotSize * scale));
            slot.Width = size;
            slot.Height = size;

            // 文字与它周围的留白走同一个因子，位置才跟着一起缩；字体换掉时释放上一份，拖动不再每次新建。
            label.Padding = new Padding(0, 0, Math.Max(0, (int)Math.Round(LabelPaddingRight * scale)), 0);
            var previous = label.Font;
            var next = new Font("Segoe UI", Math.Max(1f, baseFontSize * (float)scale), FontStyle.Regular);
            label.Font = next;

            // 不能在这里 Dispose 旧字体：Label 可能正在用它测量/绘制，销毁后 GDI+ 抛
            // "Parameter is not valid."，这次绘制会留下花屏。等整行销毁后再统一释放。
            if (!ReferenceEquals(previous, next) && previous is not null)
            {
                retiredFonts.Add(previous);
            }

            PerformLayout();
        }

        protected override void Dispose(bool disposing)
        {
            // 这一行的字体由本类创建；Label 销毁不会释放它。等 label 也退出绘制后再统一交还。
            var fonts = new List<Font>(retiredFonts);
            retiredFonts.Clear();
            if (label?.Font is { } current)
            {
                fonts.Add(current);
            }

            base.Dispose(disposing);

            if (disposing)
            {
                foreach (var font in fonts)
                {
                    font.Dispose();
                }
            }
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);

            // 构造期设置 Height 就会先跑一次布局，那时两个子控件还没赋值。
            if (label is null || slot is null)
            {
                return;
            }

            var scale = appliedScale <= 0d || double.IsNaN(appliedScale) ? 1d : appliedScale;
            var inset = Math.Max(0, (int)Math.Round(SlotRightInset * scale));
            var gap = Math.Max(0, (int)Math.Round(SlotLabelGap * scale));
            var slotWidth = slot.Width;
            slot.SetBounds(
                Width - slotWidth - inset,
                (Height - slot.Height) / 2,
                slotWidth,
                slot.Height);
            label.SetBounds(0, 0, Math.Max(0, Width - slotWidth - gap), Height);
        }
    }
}
