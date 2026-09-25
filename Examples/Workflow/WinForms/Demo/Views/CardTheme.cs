using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;

namespace Demo.Views;

/// <summary>Which corners of a rounded shape are actually rounded.</summary>
[Flags]
internal enum CardCorners
{
    None = 0,
    TopLeft = 1,
    TopRight = 2,
    BottomRight = 4,
    BottomLeft = 8,
    All = TopLeft | TopRight | BottomRight | BottomLeft,
}

/// <summary>
/// The one place the node cards' palette and metrics live.
///
/// The Avalonia demo keeps the same set in <c>CardTheme.axaml</c> and the five cards there only carry class
/// names; the five WinForms cards would otherwise repeat the same colour values and paddings five times, and
/// this repository has already been bitten by that drift more than once. Everything below is in the Avalonia
/// file's own units — design pixels at scale 1 — because the WinForms card authors its interior at
/// <c>[DefaultSize]</c> and re-scales it uniformly by <c>k</c> on every layout.
///
/// Colours: surface #161B22 / border #2A313C / divider #222A34 / title #E6EAF2 / label #7C8798 /
/// value #D6DCE6 / field #0E1218 / hover #1E2731 / disabled #5A6474.
/// </summary>
internal static class CardTheme
{
    // ── Surfaces ────────────────────────────────────────────────────────────────
    public static readonly Color Surface = FromHex("#161B22");
    public static readonly Color Border = FromHex("#2A313C");
    public static readonly Color Divider = FromHex("#222A34");
    public static readonly Color Field = FromHex("#0E1218");
    public static readonly Color Hover = FromHex("#1E2731");
    public static readonly Color HoverBorder = FromHex("#3A4553");
    public static readonly Color DisabledBorder = FromHex("#232A34");
    public static readonly Color DisabledText = FromHex("#5A6474");

    /// <summary>The colour around a card, so the square corners outside its rounded border blend in.</summary>
    public static readonly Color Ground = FromHex("#1E1E1E");

    // ── Text ────────────────────────────────────────────────────────────────────
    public static readonly Color Title = FromHex("#E6EAF2");
    public static readonly Color Label = FromHex("#7C8798");
    public static readonly Color Value = FromHex("#D6DCE6");
    public static readonly Color BadgeText = FromHex("#9AA6B5");

    // ── Type accents (the 2px bar left of the title row) ─────────────────────────
    public static readonly Color AccentController = FromHex("#38BDF8");
    public static readonly Color AccentAgent = FromHex("#6EC6FF");
    public static readonly Color AccentEnum = FromHex("#D6A0FF");
    public static readonly Color AccentFallback = FromHex("#94A3B8");

    // ── Action semantics — the ghost buttons differ only in their text colour ────
    public static readonly Color ActionCompile = FromHex("#7EC8FF");
    public static readonly Color ActionRun = FromHex("#6EE7A8");
    public static readonly Color ActionStop = FromHex("#FCA5A5");
    public static readonly Color ActionClose = FromHex("#9AA6B5");

    // ── Script editor ───────────────────────────────────────────────────────────
    public static readonly Color EditorSurface = FromHex("#0D1117");
    public static readonly Color EditorChrome = FromHex("#161B22");
    public static readonly Color EditorFile = FromHex("#6EE7A8");
    public static readonly Color EditorMeta = FromHex("#5A6474");

    // ── Metrics, in design pixels ───────────────────────────────────────────────
    public const float CardRadius = 8f;
    public const float FieldRadius = 6f;
    public const float CapsuleRadius = 9f;
    public const float HeaderHeight = 32f;
    public const float AccentWidth = 2f;
    public const float DividerThickness = 1f;
    public const float ControllerFooterHeight = 66f;
    public const float BodyPaddingX = 12f;
    public const float BodyPaddingY = 9f;
    public const float ButtonGap = 6f;
    public const float ButtonMarginX = 12f;
    public const float ButtonMarginY = 7f;
    public const float CapsulePaddingX = 7f;
    public const float CapsulePaddingY = 1f;

    // ── Type ────────────────────────────────────────────────────────────────────
    public const float TitleSize = 12.5f;
    public const float LabelSize = 10f;
    public const float ValueSize = 11.5f;
    public const float ButtonSize = 11f;
    public const float CapsuleSize = 10.5f;
    public const float PortNameSize = 11f;

    public const string UiFamily = "Microsoft YaHei UI";
    public const string MonoFamily = "Consolas";

    // Fonts are cached by (family, size, style): a card re-creates its fonts on every zoom step, and a fresh
    // Font per step would leak one per control per step for the lifetime of the process. Sizes are quantised
    // to a half pixel, which is invisible at the sizes in play but keeps the cache bounded.
    private static readonly Dictionary<(string, float, FontStyle), Font> Fonts = [];

    public static Font Font(float designPixels, FontStyle style = FontStyle.Regular)
        => Font(UiFamily, designPixels, style);

    public static Font Mono(float designPixels, FontStyle style = FontStyle.Regular)
        => Font(MonoFamily, designPixels, style);

    private static Font Font(string family, float designPixels, FontStyle style)
    {
        var size = Math.Max(0.5f, MathF.Round(Math.Max(0.5f, designPixels) * 2f) / 2f);
        var key = (family, size, style);

        if (!Fonts.TryGetValue(key, out var font))
        {
            // GraphicsUnit.Pixel rather than the default Point: every metric in this file is a design pixel
            // (the Avalonia reference's own unit), so 12.5 must mean 12.5 pixels here as well. In points it
            // would render a third larger and the 32-unit title row would no longer hold its own title.
            font = new Font(family, size, style, GraphicsUnit.Pixel);
            Fonts[key] = font;
        }

        return font;
    }

    /// <summary>Parses <c>#RRGGBB</c> / <c>#AARRGGBB</c> into a <see cref="Color"/>.</summary>
    public static Color FromHex(string hex)
    {
        var value = hex.Trim();
        if (value.StartsWith('#')) value = value[1..];

        return value.Length switch
        {
            8 => Color.FromArgb(
                byte.Parse(value[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                byte.Parse(value[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                byte.Parse(value[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                byte.Parse(value[6..8], NumberStyles.HexNumber, CultureInfo.InvariantCulture)),
            6 => Color.FromArgb(
                byte.Parse(value[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                byte.Parse(value[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                byte.Parse(value[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture)),
            _ => Color.FromName(value),
        };
    }

    /// <summary>
    /// A rectangle with any selection of its four corners rounded. GDI+ has no rounded rectangle, and the
    /// corners here are not all-or-nothing: the type accent bar rounds only its top-left (to sit inside the
    /// card's own top-left curve) and the script editor's header rounds only its top two.
    /// </summary>
    public static GraphicsPath RoundedPath(RectangleF r, float radius, CardCorners corners = CardCorners.All)
    {
        var path = new GraphicsPath();

        var d = Math.Min(radius, Math.Min(r.Width, r.Height) * 0.5f) * 2f;
        if (d <= 0.5f || corners == CardCorners.None)
        {
            path.AddRectangle(r);
            return path;
        }

        var m = d / 2f;
        var topLeft = new PointF(r.X + m, r.Y);
        var topRight = new PointF(r.Right - m, r.Y);
        var rightTop = new PointF(r.Right, r.Y + m);
        var rightBottom = new PointF(r.Right, r.Bottom - m);
        var bottomRight = new PointF(r.Right - m, r.Bottom);
        var bottomLeft = new PointF(r.X + m, r.Bottom);
        var leftBottom = new PointF(r.X, r.Bottom - m);
        var leftTop = new PointF(r.X, r.Y + m);
        var cornerTopLeft = new PointF(r.X, r.Y);
        var cornerTopRight = new PointF(r.Right, r.Y);
        var cornerBottomRight = new PointF(r.Right, r.Bottom);
        var cornerBottomLeft = new PointF(r.X, r.Bottom);

        // `arcX/arcY` is the top-left of the corner circle's bounding box (the arc is a quarter of it), and
        // `pivot` is the square corner the two edges would have met at.
        void Corner(bool round, PointF from, PointF to, PointF pivot, float arcX, float arcY, float startAngle)
        {
            if (round)
            {
                path.AddArc(arcX, arcY, d, d, startAngle, 90f);
                return;
            }

            path.AddLine(from, pivot);
            path.AddLine(pivot, to);
        }

        Corner(corners.HasFlag(CardCorners.TopLeft), leftTop, topLeft, cornerTopLeft, r.X, r.Y, 180f);
        path.AddLine(topLeft, topRight);
        Corner(corners.HasFlag(CardCorners.TopRight), topRight, rightTop, cornerTopRight, r.Right - d, r.Y, 270f);
        path.AddLine(rightTop, rightBottom);
        Corner(corners.HasFlag(CardCorners.BottomRight), rightBottom, bottomRight, cornerBottomRight, r.Right - d, r.Bottom - d, 0f);
        path.AddLine(bottomRight, bottomLeft);
        Corner(corners.HasFlag(CardCorners.BottomLeft), bottomLeft, leftBottom, cornerBottomLeft, r.X, r.Bottom - d, 90f);
        path.AddLine(leftBottom, leftTop);
        path.CloseFigure();

        return path;
    }
}
