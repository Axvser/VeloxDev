using System.Drawing;
using System.Windows.Forms;
using VeloxDev.WinForms.Xaml;
using XamlControl = VeloxDev.WinForms.Xaml.Controls.Control;
using XamlContentControl = VeloxDev.WinForms.Xaml.Controls.ContentControl;
using XamlRenderScope = VeloxDev.WinForms.Xaml.Controls.XamlRenderScope;
using XamlScrollableControl = VeloxDev.WinForms.Xaml.Controls.ScrollableControl;
using XamlStackPanel = VeloxDev.WinForms.Xaml.Controls.StackPanel;
using XamlThickness = VeloxDev.WinForms.Xaml.Controls.Thickness;
using XamlUserControl = VeloxDev.WinForms.Xaml.Controls.UserControl;

namespace Demo;

/// <summary>
/// Swaps the center pane between section pages and single extracted cards.
/// </summary>
/// <remarks>
/// A card is taken from a freshly built section instance so every generated dependency property, event handler and
/// style resolved during construction is kept. The section instance stays alive while its card is shown: its
/// code-behind handlers and its resource dictionary (merged into the preview host) are still needed.
/// </remarks>
internal sealed class GalleryPreviewController : IDisposable
{
    private const int DesignWidth = 680;

    private readonly XamlContentControl _host;
    private readonly List<XamlScrollableControl> _scrollHosts = [];
    private XamlUserControl? _section;
    private XamlControl? _card;
    private XamlScrollableControl? _cardScroll;
    private XamlStackPanel? _cardContent;
    private LayoutEventHandler? _fitHandler;
    private bool _fitting;

    public GalleryPreviewController(XamlContentControl host)
    {
        _host = host;
    }

    /// <summary>The element the inspector should select after a swap.</summary>
    public XamlControl? DefaultSelection { get; private set; }

    /// <summary>The scroll hosts of the current preview; the adorner follows their scroll.</summary>
    public IReadOnlyList<XamlScrollableControl> ScrollHosts => _scrollHosts;

    /// <summary>The id of the entry currently shown.</summary>
    public string? CurrentEntryId { get; private set; }

    /// <summary>Shows one card or section page.</summary>
    public void ShowEntry(GalleryEntry entry, GalleryModel model)
    {
        // The old view, the new section and the card fit are three separate layouts; freeze the host so the
        // intermediate trees are never painted.
        using var render = XamlRenderScope.Begin(_host);
        Clear();
        CurrentEntryId = entry.Id;

        if (Activator.CreateInstance(entry.SectionType) is not XamlUserControl section)
        {
            return;
        }

        _section = section;
        section.DataContext = model;

        if (!entry.IsPage && entry.CardName.Length > 0)
        {
            var card = section.FindName(entry.CardName) as XamlControl;
            if (card is not null && card.Parent is { } parent)
            {
                parent.Controls.Remove(card);
                ShowCard(section, card, model);
                return;
            }
        }

        ShowPage(section, entry.CardName, model);
    }

    /// <summary>Shows a group overview page.</summary>
    public void ShowGroup(GalleryGroup group, GalleryModel model)
        => ShowEntry(group.Page, model);

    /// <summary>Removes the current preview and releases its section instance.</summary>
    public void Clear()
    {
        if (_cardScroll is not null && _fitHandler is not null)
        {
            _cardScroll.Layout -= _fitHandler;
        }

        var cardScroll = _cardScroll;
        _fitHandler = null;
        _card = null;
        _cardScroll = null;
        _cardContent = null;
        _scrollHosts.Clear();

        // ContentControl removes the child without disposing it; the preview owns the lifetime.
        _host.Content = null;

        // Disposing the scroll host disposes the content panel and the extracted card with it.
        if (cardScroll is not null && !cardScroll.IsDisposed)
        {
            cardScroll.Dispose();
        }

        if (_section is not null && !_section.IsDisposed)
        {
            _section.Dispose();
        }

        _section = null;
        DefaultSelection = null;
        CurrentEntryId = null;
    }

    /// <summary>Scrolls the current preview to a pixel offset.</summary>
    public void SetScrollOffset(int offset)
    {
        if (_scrollHosts.Count == 0 || offset <= 0)
        {
            return;
        }

        _scrollHosts[0].AutoScrollPosition = new Point(0, offset);
    }

    /// <summary>Scrolls a page preview so its named card starts near the top.</summary>
    public void ScrollToCard()
    {
        if (_card is null || _card.IsDisposed || _scrollHosts.Count == 0)
        {
            return;
        }

        var host = _scrollHosts[0];
        if (host.IsDisposed)
        {
            return;
        }

        // Page mode puts the card in the section's outer content panel; card mode already fits the card.
        if (_card.Parent is not { } parent)
        {
            return;
        }

        var top = parent.Top + _card.Top;
        host.AutoScrollPosition = new Point(0, Math.Max(0, top - 8));
    }

    /// <inheritdoc />
    public void Dispose() => Clear();

    private void ShowCard(XamlUserControl section, XamlControl card, GalleryModel model)
    {
        var scroll = new XamlScrollableControl
        {
            AutoScroll = true,
            DataContext = model,
            Background = new SolidColorBrush(Theme.Current.Surface),
        };

        // Section-local DynamicResource keys (LiveBrush, WarmGradient, ...) must keep resolving after the card
        // leaves the section. A merged dictionary forwards replacements made by the section code-behind.
        scroll.Resources.MergedDictionaries.Add(section.Resources);

        var content = new XamlStackPanel
        {
            DataContext = model,
            Spacing = 0,
            Padding = new XamlThickness(24f),
        };
        content.Controls.Add(card);
        scroll.Controls.Add(content);

        _host.Content = scroll;
        _card = card;
        _cardScroll = scroll;
        _cardContent = content;
        _scrollHosts.Add(scroll);
        DefaultSelection = card;

        _fitHandler = (_, _) => FitCard(scroll, content);
        scroll.Layout += _fitHandler;
        FitCard(scroll, content);
    }

    private void ShowPage(XamlUserControl section, string cardName, GalleryModel model)
    {
        section.Dock = DockStyle.Fill;
        _host.Content = section;
        GalleryLayout.FitScrolledContent(section);

        foreach (var host in GalleryLayout.Descendants<XamlScrollableControl>(section))
        {
            _scrollHosts.Add(host);
        }

        _card = !string.IsNullOrEmpty(cardName) ? section.FindName(cardName) as XamlControl : null;
        DefaultSelection = _card ?? section;
    }

    private void FitCard(XamlScrollableControl scroll, XamlStackPanel content)
    {
        if (_fitting || scroll.IsDisposed || content.IsDisposed)
        {
            return;
        }

        var viewport = scroll.ClientSize.Width;
        if (viewport <= 0)
        {
            return;
        }

        _fitting = true;
        try
        {
            // A stable design width keeps text wrapping predictable while the center pane is resized; narrower
            // windows get a horizontal scrollbar instead of clipped fixed-width compositions.
            // Leave room for the vertical bar so a fitting card does not create a horizontal one.
            var width = Math.Max(DesignWidth, viewport - 24);
            var measured = content.Measure(new Size(width, 0));
            var contentWidth = Math.Max(width, measured.Width) + 4;
            var contentHeight = Math.Max(1, measured.Height);

            if (content.Width != contentWidth)
            {
                content.Width = contentWidth;
            }

            if (content.Height != contentHeight)
            {
                content.Height = contentHeight;
            }

            if (content.Left != 0 || content.Top != 0)
            {
                content.Location = new Point(0, 0);
            }
        }
        finally
        {
            _fitting = false;
        }
    }
}
