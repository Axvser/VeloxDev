using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using VeloxDev.WinForms.Xaml;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// Turns a markup-declared node view into a workflow card: it attaches <see cref="WorkflowNodeAttachment"/>, points
/// drag and slot layout at the surface canvas, and keeps the card's chrome colours in step with the surface.
/// </summary>
/// <remarks>
/// <para>
/// Set <see cref="IsEnabledProperty"/> on the view's root. The behavior is applied while the markup is still being
/// built, so every part lookup tolerates the child tree appearing later; the parts are resolved by
/// <see cref="System.Windows.Forms.Control.Name"/> and the view's own paint stays untouched.
/// </para>
/// <para>
/// The backdrop is read from the nearest ancestor with an opaque <c>BackColor</c> and written to both the attachment
/// and the declared <see cref="VeloxDev.WinForms.Xaml.Controls.Border"/>, so a semi-transparent card background composites over the surface
/// instead of an uninitialized window buffer.
/// </para>
/// </remarks>
public sealed class WorkflowNodeViewBehavior
{
    private static readonly ConditionalWeakTable<Control, ViewState> States = new();

    /// <summary>Whether the node-card lifecycle is active on the view.</summary>
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(WorkflowNodeViewBehavior),
        new PropertyMetadata(false, OnIsEnabledChanged));

    /// <summary>Name of the child <see cref="VeloxDev.WinForms.Xaml.Controls.Border"/> that paints the card chrome; defaults to <c>PART_Card</c>.</summary>
    public static readonly DependencyProperty CardNameProperty = DependencyProperty.RegisterAttached(
        "CardName", typeof(string), typeof(WorkflowNodeViewBehavior),
        new PropertyMetadata("PART_Card", OnConfigurationChanged));

    /// <summary>Name of the canvas that supplies coordinates; when unset the nearest <see cref="VeloxDev.WinForms.Xaml.Controls.Canvas"/> is used.</summary>
    public static readonly DependencyProperty CoordinateHostNameProperty = DependencyProperty.RegisterAttached(
        "CoordinateHostName", typeof(string), typeof(WorkflowNodeViewBehavior),
        new PropertyMetadata(null, OnConfigurationChanged));

    /// <summary>Starts or stops the node-card lifecycle on <paramref name="element"/>.</summary>
    public static bool GetIsEnabled(Control element) => (bool)GetRequired(element, IsEnabledProperty)!;

    /// <summary>Starts or stops the node-card lifecycle on <paramref name="element"/>.</summary>
    public static void SetIsEnabled(Control element, bool value) => SetRequired(element, IsEnabledProperty, value);

    /// <summary>Gets the name of the child border that paints the card chrome.</summary>
    public static string? GetCardName(Control element) => (string?)GetRequired(element, CardNameProperty);

    /// <summary>Names the child border that paints the card chrome.</summary>
    public static void SetCardName(Control element, string? value) => SetRequired(element, CardNameProperty, value);

    /// <summary>Gets the name of the canvas that supplies coordinates.</summary>
    public static string? GetCoordinateHostName(Control element) => (string?)GetRequired(element, CoordinateHostNameProperty);

    /// <summary>Names the canvas that supplies coordinates.</summary>
    public static void SetCoordinateHostName(Control element, string? value)
        => SetRequired(element, CoordinateHostNameProperty, value);

    private static void OnIsEnabledChanged(Control target, DependencyPropertyChangedEventArgs e)
        => GetState(target).SetEnabled(e.NewValue is true);

    private static void OnConfigurationChanged(Control target, DependencyPropertyChangedEventArgs e)
    {
        var state = GetState(target);
        state.CardName = GetCardName(target) ?? "PART_Card";
        state.CoordinateHostName = GetCoordinateHostName(target);
        state.Apply();
    }

    private static object? GetRequired(Control element, DependencyProperty property)
        => property.GetValue(element);

    private static void SetRequired(Control element, DependencyProperty property, object? value)
        => property.SetValue(element, value);

    private static ViewState GetState(Control element)
    {
        if (element is null) throw new ArgumentNullException(nameof(element));
        return States.GetValue(element, static card => new ViewState(card));
    }

    private sealed class ViewState
    {
        private readonly Control card;
        private bool enabled;

        public ViewState(Control card)
        {
            this.card = card;
        }

        public string CardName { get; set; } = "PART_Card";

        public string? CoordinateHostName { get; set; }

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

            ApplyCoordinateHost();
            ApplySurface();
        }

        private void Attach()
        {
            card.ControlAdded += OnStructureChanged;
            card.ControlRemoved += OnStructureChanged;
            card.ParentChanged += OnAncestryChanged;
            card.HandleCreated += OnStructureChanged;
            card.Disposed += OnDisposed;

            _ = WorkflowNodeAttachment.For(card) ?? WorkflowNodeAttachment.Attach(card);

            WorkflowNodeDragBehavior.SetCoordinateHostType(card, typeof(VeloxDev.WinForms.Xaml.Controls.Canvas));
            WorkflowSlotLayoutBehavior.SetCoordinateHostType(card, typeof(VeloxDev.WinForms.Xaml.Controls.Canvas));
            ApplyCoordinateHost();

            WorkflowNodeDragBehavior.SetIsEnabled(card, true);
            WorkflowSlotLayoutBehavior.SetIsEnabled(card, true);

            ApplySurface();
        }

        private void Detach()
        {
            card.ControlAdded -= OnStructureChanged;
            card.ControlRemoved -= OnStructureChanged;
            card.ParentChanged -= OnAncestryChanged;
            card.HandleCreated -= OnStructureChanged;
            card.Disposed -= OnDisposed;

            if (!card.IsDisposed)
            {
                WorkflowNodeDragBehavior.SetIsEnabled(card, false);
                WorkflowSlotLayoutBehavior.SetIsEnabled(card, false);
            }
        }

        private void ApplyCoordinateHost()
        {
            WorkflowNodeDragBehavior.SetCoordinateHostName(card, CoordinateHostName);
            WorkflowSlotLayoutBehavior.SetCoordinateHostName(card, CoordinateHostName);
        }

        private void ApplySurface()
        {
            if (card.IsDisposed)
            {
                return;
            }

            var backdrop = ResolveBackdrop();

            // 先给附加写底色：它的 setter 会把控件的 BackColor 置成不透明底色，随后的合成色再覆盖回去。
            if (WorkflowNodeAttachment.For(card) is { } attachment)
            {
                attachment.SurfaceBackdrop = backdrop;
            }

            var border = FindBorder();
            var body = border?.Background?.ApproximateColor ?? Color.Empty;
            var effective = Blend(body, backdrop);
            if (card.BackColor != effective)
            {
                card.BackColor = effective;
            }

            if (border is not null)
            {
                border.Backdrop = backdrop;
                border.Invalidate();
            }

            // 行面板在重建时读卡片的 BackColor；底色刚变过就让行跟着换，否则它会停在上一份颜色上。
            WorkflowNodeRowsBehavior.Refresh(card);
        }

        private Color ResolveBackdrop()
        {
            for (var current = card.Parent; current is not null; current = current.Parent)
            {
                if (current.BackColor.A == 255)
                {
                    return current.BackColor;
                }
            }

            return Color.FromArgb(0x1E, 0x1E, 0x1E);
        }

        private VeloxDev.WinForms.Xaml.Controls.Border? FindBorder()
        {
            if (!string.IsNullOrWhiteSpace(CardName))
            {
                foreach (var control in EnumerateDescendants(card))
                {
                    if (control is VeloxDev.WinForms.Xaml.Controls.Border named
                        && string.Equals(named.Name, CardName, StringComparison.Ordinal))
                    {
                        return named;
                    }
                }
            }

            foreach (var control in EnumerateDescendants(card))
            {
                if (control is VeloxDev.WinForms.Xaml.Controls.Border fallback)
                {
                    return fallback;
                }
            }

            return null;
        }

        // 带 alpha 的卡片色按表面底色合成为不透明色；子控件要求 A == 0xFF，而合成结果正好也是屏幕上看到的那一色。
        private static Color Blend(Color foreground, Color background)
        {
            if (foreground.IsEmpty || foreground.A == 0)
            {
                return Color.FromArgb(255, background);
            }

            if (foreground.A == 255)
            {
                return foreground;
            }

            var alpha = foreground.A / 255f;
            return Color.FromArgb(
                255,
                (int)Math.Round(foreground.R * alpha + background.R * (1f - alpha)),
                (int)Math.Round(foreground.G * alpha + background.G * (1f - alpha)),
                (int)Math.Round(foreground.B * alpha + background.B * (1f - alpha)));
        }

        private void OnStructureChanged(object? sender, EventArgs e) => Apply();

        private void OnAncestryChanged(object? sender, EventArgs e) => Apply();

        private void OnDisposed(object? sender, EventArgs e) => Detach();

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
}
