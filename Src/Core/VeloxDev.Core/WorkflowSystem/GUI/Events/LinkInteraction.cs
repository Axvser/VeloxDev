using System;
using System.Runtime.CompilerServices;

namespace VeloxDev.WorkflowSystem;

/// <summary>
/// Turns forwarded input into the link interaction every GUI shares: which link is under the pointer, and
/// what the pointer and keyboard just asked to do with it.
///
/// Each adapter does only the part it alone can do — hook the platform's pointer and key events and translate
/// them into <see cref="PointerEvent"/> / <see cref="KeyEvent"/>. The meaning of those events is decided here,
/// once: hover is resolved against the curves the link views published (see <see cref="ILinkHitTestable"/>),
/// and a consumer subscribes to the outcome instead of re-deriving it per platform.
///
/// A host owns one instance per surface, forwards into it, and subscribes to what comes out. The
/// <c>sender</c> is the visual that drew the link when the platform has one per link — that is what a
/// subscriber needs to focus it, or to hang a menu off it:
/// <code>
/// interaction.HoverChanged += (sender, e) => Select(e.Link);                    // hover == selection in the demos
/// interaction.LinkPressed  += (sender, e) => { if (e.Button is PointerButtonKind.Right) ShowMenu(e.Link, sender); };
/// interaction.LinkDeleteRequested += (sender, e) => e.Link.DeleteCommand.Execute(null);
/// </code>
/// </summary>
/// <remarks>
/// <para>
/// The instance holds no GUI object of its own: it resolves against <see cref="LinkCurve"/>s that link views
/// published into their Helpers, plus the visual each of them named, so it is the same class on all seven
/// platforms.
/// </para>
/// <para>
/// Deleting is requested, not performed — see <see cref="LinkDeleteRequestedEventArgs"/>.
/// </para>
/// </remarks>
/// <seealso cref="LinkHitTestEx"/>
public sealed class LinkInteraction
{
    // 一棵树一个实例：这是「hub 在哪」的唯一答案。适配器只往这里转发，宿主只从这里订阅，
    // 于是七家的取用方式是同一个调用，不需要各家再各自发明 GetLinkInteraction / Interaction / …
    private static readonly ConditionalWeakTable<IWorkflowTreeViewModel, LinkInteraction> Hubs = new();

    private readonly IWorkflowTreeViewModel tree;
    private IWorkflowLinkViewModel? hovered;

    /// <summary>
    /// The one interaction hub for this tree, created on first use and kept as long as the tree lives.
    ///
    /// Adapters forward into it; hosts and link views subscribe to it. Both use this call, so the hub has a
    /// single location rather than one per adapter surface.
    /// </summary>
    public static LinkInteraction For(IWorkflowTreeViewModel tree)
    {
        if (tree is null) throw new ArgumentNullException(nameof(tree));
        return Hubs.GetValue(tree, static t => new LinkInteraction(t));
    }

    /// <summary>Creates the interaction hub for one workflow surface.</summary>
    /// <param name="tree">The tree whose links are interactive.</param>
    /// <param name="hitRadius">Reach either side of a drawn line; <see cref="LinkHitTestEx.DefaultHitRadius"/> by default.</param>
    /// <exception cref="ArgumentNullException"><paramref name="tree"/> is <see langword="null"/>.</exception>
    public LinkInteraction(IWorkflowTreeViewModel tree, double hitRadius = LinkHitTestEx.DefaultHitRadius)
    {
        this.tree = tree ?? throw new ArgumentNullException(nameof(tree));
        HitRadius = hitRadius;
    }

    /// <summary>The link the pointer is currently on, or <see langword="null"/>.</summary>
    public IWorkflowLinkViewModel? HoveredLink => hovered;

    /// <summary>Reach either side of a drawn line, in the same units the curves were published in.</summary>
    public double HitRadius { get; set; }

    /// <summary>
    /// Whether hovering lights the link by itself, through <see cref="ILinkHighlight"/> on the published visual.
    /// On by default, which is what gives every platform the same hover feedback with no per-platform code;
    /// turn it off to drive the highlight from <see cref="HoverChanged"/> instead.
    /// </summary>
    public bool AutoHighlight { get; set; } = true;

    /// <summary>
    /// Whether Delete removes the hovered link by itself (<c>link.DeleteCommand</c>). On by default, so a
    /// generated project deletes out of the box; turn it off to confirm first and handle
    /// <see cref="LinkDeleteRequested"/> yourself.
    /// </summary>
    public bool AutoDelete { get; set; } = true;

    /// <summary>
    /// When set, pointer movement no longer changes the hover. A host that has opened a menu raises this while
    /// it is open: the pointer is then over the menu, and the move that got it there must not clear the very
    /// selection the menu is about to act on.
    /// </summary>
    public bool IsSuspended { get; set; }

    /// <summary>
    /// Raised when the hovered link changes. The <c>sender</c> is the visual that drew the new link, or
    /// <see langword="null"/> when nothing is hovered or the platform draws every link on one surface.
    /// </summary>
    public event EventHandler<LinkHoverEventArgs>? HoverChanged;

    /// <summary>
    /// Raised when a link was pressed. Not raised for a press on empty canvas.
    /// The <c>sender</c> is the visual that drew the link, or <see langword="null"/> on the immediate-mode platforms.
    /// </summary>
    public event EventHandler<LinkPressedEventArgs>? LinkPressed;

    /// <summary>
    /// Raised when Delete was pressed while a link was hovered. The consumer performs the deletion.
    /// The <c>sender</c> is the visual that drew the link, or <see langword="null"/> on the immediate-mode platforms.
    /// </summary>
    public event EventHandler<LinkDeleteRequestedEventArgs>? LinkDeleteRequested;

    /// <summary>Feeds one translated pointer event in.</summary>
    public void Publish(PointerEvent e)
    {
        switch (e.Phase)
        {
            case PointerPhase.Exited:
                SetHovered(null);
                break;

            case PointerPhase.Entered:
            case PointerPhase.Moved:
                if (!IsSuspended) SetHovered(Find(e.Position));
                break;

            case PointerPhase.Pressed:
                // 按下的那条就是选中的那条 —— 菜单里的动作作用于「当前这条」，先落选中再报事件。
                var link = Find(e.Position);
                SetHovered(link);
                if (link is not null)
                {
                    LinkPressed?.Invoke(VisualOf(link), new LinkPressedEventArgs(link, e.Button));
                }
                break;

            case PointerPhase.Released:
                break;
        }
    }

    /// <summary>Feeds one translated key event in.</summary>
    public void Publish(KeyEvent e)
    {
        if (e.Key != InputKey.Delete) return;
        if (hovered is not { } link) return;

        // 先报事件再执行：宿主想观察（或已经关掉 AutoDelete 想自己确认）都拿得到这条链接的引用。
        LinkDeleteRequested?.Invoke(VisualOf(link), new LinkDeleteRequestedEventArgs(link));
        if (AutoDelete) link.DeleteCommand.Execute(null);
    }

    private IWorkflowLinkViewModel? Find(Anchor position)
        => tree.HitTestVisibleLinks(position.Horizontal, position.Vertical, HitRadius);

    private static object? VisualOf(IWorkflowLinkViewModel link) => link.HitTarget()?.Visual;

    // 高亮在这里同进同出：只让新的一条亮、旧的一条灭 —— 互斥不需要各家再拿一个 SelectionManager 去保证。
    private static void Highlight(IWorkflowLinkViewModel? link, bool on)
    {
        if (link?.HitTarget()?.Visual is ILinkHighlight visual) visual.IsHighlighted = on;
    }

    private void SetHovered(IWorkflowLinkViewModel? link)
    {
        if (ReferenceEquals(hovered, link)) return;

        var previous = hovered;
        hovered = link;

        if (AutoHighlight)
        {
            Highlight(previous, false);
            Highlight(link, true);
        }

        HoverChanged?.Invoke(link is null ? null : VisualOf(link), new LinkHoverEventArgs(link));
    }
}
