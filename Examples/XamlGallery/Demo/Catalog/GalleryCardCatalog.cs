

namespace Demo;

/// <summary>Pairs the catalog metadata with the visual factory; the factory stays a view concern.</summary>
internal sealed class GalleryCardCatalog
{
    private readonly Dictionary<string, GalleryCard> _byId;

    /// <summary>Creates every card and caches the metadata list used by the shell view model.</summary>
    /// <param name="model">The shared sample data.</param>
    /// <param name="theme">The palette service used by the theme cards.</param>
    public GalleryCardCatalog(GalleryModel model, IThemeService theme)
    {
        Cards = GalleryVisualCards.Create(model, theme);
        Infos = Cards.Select(static card => card.Info).ToArray();
        _byId = Cards.ToDictionary(static card => card.Id, StringComparer.Ordinal);
    }

    /// <summary>Every card in catalog order.</summary>
    public IReadOnlyList<GalleryCard> Cards { get; }

    /// <summary>The metadata the view model filters and selects.</summary>
    public IReadOnlyList<GalleryCardInfo> Infos { get; }

    /// <summary>Finds a card by id.</summary>
    /// <param name="id">The card id.</param>
    public GalleryCard? Find(string id) => _byId.TryGetValue(id, out var card) ? card : null;
}
