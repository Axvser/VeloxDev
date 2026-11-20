using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using VeloxDev.WinForms.Xaml;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// Keeps a markup-declared title label bound to a node card's title and scaled with the surface's zoom collapse.
/// </summary>
/// <remarks>
/// <para>
/// Set <see cref="IsEnabledProperty"/> on the view's root and name the title label and its host strip. The behavior
/// supplies the default title reader (<c>Name</c>, then <c>Title</c>) when the attachment has none, so a view that
/// declares no <see cref="WorkflowNodeAttachment.NodeTitle"/> still shows a title.
/// </para>
/// <para>
/// The design metrics are read from the declared controls when the parts resolve: the strip's height, the label's
/// horizontal padding and the label's font size are the scale-1 values, and every collapse multiplies them by the
/// same factor. The label's background and foreground follow the card's, because a label does not composite over
/// its parent on WinForms.
/// </para>
/// </remarks>
public sealed class WorkflowNodeTitleBehavior
{
    private static readonly ConditionalWeakTable<Control, TitleState> States = new();

    /// <summary>Whether the title binding and zoom scaling are active on the view.</summary>
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(WorkflowNodeTitleBehavior),
        new PropertyMetadata(false, OnIsEnabledChanged));

    /// <summary>Name of the <see cref="System.Windows.Forms.Label"/> that shows the title.</summary>
    public static readonly DependencyProperty LabelNameProperty = DependencyProperty.RegisterAttached(
        "LabelName", typeof(string), typeof(WorkflowNodeTitleBehavior),
        new PropertyMetadata("PART_Title", OnConfigurationChanged));

    /// <summary>Name of the control that hosts the title label and whose height scales with the collapse.</summary>
    public static readonly DependencyProperty HeaderNameProperty = DependencyProperty.RegisterAttached(
        "HeaderName", typeof(string), typeof(WorkflowNodeTitleBehavior),
        new PropertyMetadata("PART_Header", OnConfigurationChanged));

    /// <summary>Gets whether the title binding and zoom scaling are active on <paramref name="element"/>.</summary>
    public static bool GetIsEnabled(Control element) => (bool)GetRequired(element, IsEnabledProperty)!;

    /// <summary>Starts or stops the title binding and zoom scaling on <paramref name="element"/>.</summary>
    public static void SetIsEnabled(Control element, bool value) => SetRequired(element, IsEnabledProperty, value);

    /// <summary>Gets the name of the title label.</summary>
    public static string? GetLabelName(Control element) => (string?)GetRequired(element, LabelNameProperty);

    /// <summary>Names the title label.</summary>
    public static void SetLabelName(Control element, string? value) => SetRequired(element, LabelNameProperty, value);

    /// <summary>Gets the name of the title strip.</summary>
    public static string? GetHeaderName(Control element) => (string?)GetRequired(element, HeaderNameProperty);

    /// <summary>Names the title strip.</summary>
    public static void SetHeaderName(Control element, string? value) => SetRequired(element, HeaderNameProperty, value);

    private static void OnIsEnabledChanged(Control target, DependencyPropertyChangedEventArgs e)
        => GetState(target).SetEnabled(e.NewValue is true);

    private static void OnConfigurationChanged(Control target, DependencyPropertyChangedEventArgs e)
    {
        var state = GetState(target);
        state.Reconfigure(GetLabelName(target) ?? "PART_Title", GetHeaderName(target) ?? "PART_Header");
    }

    private static object? GetRequired(Control element, DependencyProperty property)
        => property.GetValue(element);

    private static void SetRequired(Control element, DependencyProperty property, object? value)
        => property.SetValue(element, value);

    private static TitleState GetState(Control element)
    {
        if (element is null) throw new ArgumentNullException(nameof(element));
        return States.GetValue(element, static card => new TitleState(card));
    }

    private sealed class TitleState
    {
        private readonly Control card;
        private bool enabled;
        private WorkflowNodeAttachment? attachment;
        private Label? label;
        private Control? header;
        private Font? titleFont;
        private int baseHeaderHeight = 36;
        private int basePaddingX = 12;
        private float baseFontSize = 9f;
        private double appliedCollapse = double.NaN;

        public TitleState(Control card)
        {
            this.card = card;
        }

        public string LabelName { get; set; } = "PART_Title";

        public string HeaderName { get; set; } = "PART_Header";

        public void SetEnabled(bool value)
        {
            if (enabled == value)
            {
                return;
            }

            enabled = value;
            if (value)
            {
                Attach();
            }
            else
            {
                Detach();
            }
        }

        public void Apply()
        {
            if (!enabled || card.IsDisposed)
            {
                return;
            }

            ResolveParts();
            UpdateTitle();
        }

        public void Reconfigure(string labelName, string headerName)
        {
            LabelName = labelName;
            HeaderName = headerName;
            label = null;
            header = null;
            appliedCollapse = double.NaN;
            Apply();
        }

        private void Attach()
        {
            card.ControlAdded += OnStructureChanged;
            card.HandleCreated += OnStructureChanged;
            card.BackColorChanged += OnVisualChanged;
            card.ForeColorChanged += OnVisualChanged;
            card.Disposed += OnDisposed;

            attachment = WorkflowNodeAttachment.For(card) ?? WorkflowNodeAttachment.Attach(card);
            attachment.NodeTitle ??= ReadDefaultTitle;
            attachment.Rebound += OnRebound;
            attachment.TitleChanged += OnTitleChanged;
            attachment.CollapseChanged += OnCollapseChanged;

            ResolveParts();
            UpdateTitle();
        }

        private void Detach()
        {
            card.ControlAdded -= OnStructureChanged;
            card.HandleCreated -= OnStructureChanged;
            card.BackColorChanged -= OnVisualChanged;
            card.ForeColorChanged -= OnVisualChanged;
            card.Disposed -= OnDisposed;

            if (attachment is not null)
            {
                attachment.Rebound -= OnRebound;
                attachment.TitleChanged -= OnTitleChanged;
                attachment.CollapseChanged -= OnCollapseChanged;
                attachment = null;
            }

            var previous = titleFont;
            titleFont = null;

            // 控件还活着时字体仍挂在标签上（换掉会让文字跳回默认字体）；只有随卡片一起销毁才交还这份 GDI 资源。
            if (card.IsDisposed)
            {
                previous?.Dispose();
            }
        }

        private void ResolveParts()
        {
            if (label is not null || card.IsDisposed)
            {
                return;
            }

            var foundLabel = Find(card, LabelName) as Label;
            if (foundLabel is null)
            {
                return;
            }

            label = foundLabel;
            header = Find(card, HeaderName);

            // 设计值只取一次：之后所有缩放都从这里乘同一个因子，来回缩放不会逐次累积取整误差。
            baseFontSize = Math.Max(1f, label.Font.Size);
            basePaddingX = Math.Max(0, label.Padding.Left);
            baseHeaderHeight = header is { Height: > 0 } ? header.Height : 36;

            ApplyVisuals();
            appliedCollapse = double.NaN;
            ApplyCollapse(attachment?.Collapse ?? 1d);
        }

        private void ApplyCollapse(double collapse)
        {
            var scale = collapse <= 0d ? 1d : collapse;
            if (scale == appliedCollapse)
            {
                return;
            }

            appliedCollapse = scale;

            if (header is not null)
            {
                header.Height = Math.Max(1, (int)Math.Round(baseHeaderHeight * scale));
            }

            if (label is not null)
            {
                var padding = Math.Max(0, (int)Math.Round(basePaddingX * scale));
                label.Padding = new Padding(padding, 0, padding, 0);

                var next = new Font(
                    label.Font.FontFamily,
                    Math.Max(1f, baseFontSize * (float)scale),
                    FontStyle.Bold);
                var previous = titleFont;
                titleFont = next;
                label.Font = next;
                if (previous is not null && !ReferenceEquals(previous, next))
                {
                    previous.Dispose();
                }
            }

            card.Invalidate();
        }

        private void UpdateTitle()
        {
            if (label is not null && attachment is not null)
            {
                label.Text = attachment.Title;
            }
        }

        private void QueueTitleUpdate()
        {
            if (!card.IsHandleCreated || card.IsDisposed)
            {
                UpdateTitle();
                return;
            }

            card.BeginInvoke(new Action(UpdateTitle));
        }

        private void ApplyVisuals()
        {
            if (label is null)
            {
                return;
            }

            label.BackColor = card.BackColor;
            label.ForeColor = card.ForeColor;
        }

        private void OnStructureChanged(object? sender, EventArgs e) => Apply();

        private void OnVisualChanged(object? sender, EventArgs e) => ApplyVisuals();

        private void OnRebound(object? sender, EventArgs e)
        {
            ResolveParts();
            QueueTitleUpdate();
        }

        private void OnTitleChanged(object? sender, EventArgs e) => UpdateTitle();

        private void OnCollapseChanged(object? sender, WorkflowNodeAttachment.CollapseChangedEventArgs e)
            => ApplyCollapse(e.Collapse);

        private void OnDisposed(object? sender, EventArgs e) => Detach();

        private static Control? Find(Control root, string? name)
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

        // 标题不是端口：节点视图不知道模型类型时，用最常见的两个成员名兜底；两者都不存在就没有标题。
        private static string ReadDefaultTitle(IWorkflowNodeViewModel node)
        {
            foreach (var name in new[] { "Name", "Title" })
            {
                var property = TypeDescriptor.GetProperties(node).Find(name, ignoreCase: false);
                if (property is not null && property.PropertyType == typeof(string))
                {
                    return property.GetValue(node) as string ?? string.Empty;
                }
            }

            return string.Empty;
        }
    }
}
