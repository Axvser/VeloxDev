using System.Drawing;

using Demo.Views.Workflow;

namespace Demo.Views.Workbench;

/// <summary>
/// The workbench shell's palette — the same tokens the Avalonia demo keeps in <c>Theme/Tokens.axaml</c>.
///
/// The node cards have their own palette in <see cref="CardTheme"/>; this one covers the frame around them:
/// activity bar, sidebar pages, editor tabs, inspector cards and the status bar. Keeping it in one place is
/// what stops the shell's dozen controls from each carrying their own copy of #1F1F1F.
/// </summary>
internal static class WorkbenchPalette
{
    // Surfaces: darkest is the canvas/editor, panels sit one step above it.
    public static readonly Color Window = CardTheme.FromHex("#141414");
    public static readonly Color Panel = CardTheme.FromHex("#1F1F1F");
    public static readonly Color PanelRaised = CardTheme.FromHex("#252526");
    public static readonly Color Surface = CardTheme.FromHex("#252526");
    public static readonly Color SurfaceElevated = CardTheme.FromHex("#2D2D2D");
    public static readonly Color ActivityBar = CardTheme.FromHex("#181818");
    public static readonly Color Editor = CardTheme.FromHex("#141414");
    public static readonly Color Input = CardTheme.FromHex("#2A2A2A");
    public static readonly Color Hover = CardTheme.FromHex("#2A2D2E");

    // Dividers.
    public static readonly Color BorderSubtle = CardTheme.FromHex("#262626");
    public static readonly Color Border = CardTheme.FromHex("#333333");
    public static readonly Color Splitter = CardTheme.FromHex("#262626");

    // Text.
    public static readonly Color TextPrimary = CardTheme.FromHex("#E7E7E7");
    public static readonly Color TextSecondary = CardTheme.FromHex("#A0A0A0");
    public static readonly Color TextTertiary = CardTheme.FromHex("#6E7681");
    public static readonly Color TextDisabled = CardTheme.FromHex("#585858");

    // Accent and state.
    public static readonly Color Accent = CardTheme.FromHex("#0078D4");
    public static readonly Color AccentText = CardTheme.FromHex("#4FC1FF");
    public static readonly Color Success = CardTheme.FromHex("#3FB950");
    public static readonly Color Warning = CardTheme.FromHex("#D29922");
    public static readonly Color Danger = CardTheme.FromHex("#F85149");

    // Status bar.
    public static readonly Color StatusBar = CardTheme.FromHex("#181818");
    public static readonly Color StatusBarBorder = CardTheme.FromHex("#2B2B2B");
    public static readonly Color StatusBarText = CardTheme.FromHex("#B8B8B8");
}
