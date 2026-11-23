using VeloxDev.MVVM;

namespace Demo;

/// <summary>The gallery shell state: filter text, visible groups, selected card and the derived chrome text.</summary>
/// <remarks>
/// The view model owns only catalog metadata and strings. The visual factory, tree-item construction, preview
/// disposal and inspector rows stay in the view; switching the palette rebuilds the view with this same instance.
/// </remarks>
internal sealed partial class GalleryShellViewModel : IDisposable
{
    private readonly IReadOnlyList<GalleryCardInfo> _cards;
    private readonly IThemeService _theme;
    private readonly List<GalleryCardInfo> _visible = [];
    private readonly List<GalleryCardGroup> _groups = [];
    private bool _hadMatches;

    [VeloxProperty] private string _filterText = string.Empty;
    [VeloxProperty] private GalleryCardInfo? _selectedCard;

    /// <summary>Creates the shell state and selects the initial card without raising events for a not-yet-created view.</summary>
    /// <param name="cards">The catalog metadata, in catalog order.</param>
    /// <param name="theme">The palette service.</param>
    /// <param name="initialId">The card to select, or <see langword="null"/> for the first visible card.</param>
    /// <param name="filter">The initial filter text.</param>
    public GalleryShellViewModel(
        IReadOnlyList<GalleryCardInfo> cards,
        IThemeService theme,
        string? initialId = null,
        string? filter = null)
    {
        _cards = cards;
        _theme = theme;
        _filterText = filter ?? string.Empty;
        ApplyFilterCore();

        if (!string.IsNullOrEmpty(initialId)
            && _cards.FirstOrDefault(card => string.Equals(card.Id, initialId, StringComparison.Ordinal)) is { } initial)
        {
            // 和旧的 SelectEntry 一样：目标被筛选隐藏时先清掉筛选，再选它。
            if (!_visible.Any(card => card.Id == initial.Id))
            {
                _filterText = string.Empty;
                ApplyFilterCore();
            }

            _selectedCard = initial;
        }

        _selectedCard ??= _visible.FirstOrDefault() ?? _cards.FirstOrDefault();
        _hadMatches = HasMatches;
        _theme.Changed += OnThemeChanged;
    }

    /// <summary>Raised after the filter changed the visible groups.</summary>
    public event EventHandler? CatalogChanged;

    /// <summary>Raised when the preview should show a card, or clear itself for <see langword="null"/>.</summary>
    public event EventHandler<GalleryCardInfo?>? PreviewChanged;

    /// <summary>Every card's metadata in catalog order.</summary>
    public IReadOnlyList<GalleryCardInfo> Cards => _cards;

    /// <summary>The groups that match the current filter, in first-appearance order.</summary>
    public IReadOnlyList<GalleryCardGroup> VisibleGroups => _groups;

    /// <summary>Every card that matches the current filter, in catalog order.</summary>
    public IReadOnlyList<GalleryCardInfo> VisibleCards => _visible;

    /// <summary>Whether anything matches the filter.</summary>
    public bool HasMatches => _visible.Count > 0;

    /// <summary>The preview title.</summary>
    public string Title => !HasMatches ? "No matches" : SelectedCard?.Title ?? string.Empty;

    /// <summary>The preview summary.</summary>
    public string Summary => !HasMatches
        ? "Clear the filter to show the catalog again."
        : SelectedCard?.Summary ?? string.Empty;

    /// <summary>The usage snippet of the selected card.</summary>
    public string Usage => HasMatches ? SelectedCard?.Usage ?? string.Empty : string.Empty;

    /// <summary>The status-bar text.</summary>
    public string StatusText => !HasMatches
        ? "No matches"
        : SelectedCard is { } card ? $"{card.Group} · {card.Id}" : "Ready";

    /// <summary>The label for the mode the next theme cycle will select.</summary>
    public string ThemeModeLabel => _theme.Label;

    /// <summary>Selects a card by id; a card hidden by the filter clears the filter first.</summary>
    /// <param name="id">The card id.</param>
    /// <returns><see langword="false"/> when the id is unknown.</returns>
    public bool SelectCard(string id)
    {
        var card = _cards.FirstOrDefault(candidate => string.Equals(candidate.Id, id, StringComparison.Ordinal));
        if (card is null)
        {
            return false;
        }

        if (!_visible.Any(candidate => candidate.Id == card.Id))
        {
            FilterText = string.Empty;
        }

        var alreadySelected = ReferenceEquals(SelectedCard, card);
        SelectedCard = card;
        if (alreadySelected)
        {
            // 生成属性在值相同时不发通知；捕捉/重复选择仍要重建预览。
            PreviewChanged?.Invoke(this, card);
        }

        return true;
    }

    /// <inheritdoc />
    public void Dispose() => _theme.Changed -= OnThemeChanged;

    [VeloxCommand]
    private void CycleTheme() => _theme.Cycle();

    partial void OnFilterTextChanged(string oldValue, string newValue)
    {
        ApplyFilterCore();
        RaiseComputed();
        if (HasMatches)
        {
            if (SelectedCard is null || !_visible.Any(card => card.Id == SelectedCard.Id))
            {
                SelectedCard = _visible[0];
            }
            else if (!_hadMatches)
            {
                // 筛选从“无匹配”回到有匹配时，选中的卡还是同一张，必须再请视图重建预览。
                PreviewChanged?.Invoke(this, SelectedCard);
            }
        }
        else
        {
            PreviewChanged?.Invoke(this, null);
        }

        _hadMatches = HasMatches;
        CatalogChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnSelectedCardChanged(GalleryCardInfo? oldValue, GalleryCardInfo? newValue)
    {
        RaiseComputed();
        PreviewChanged?.Invoke(this, newValue);
    }

    private void OnThemeChanged(object? sender, EventArgs e)
        => OnPropertyChanged(nameof(ThemeModeLabel));

    private void RaiseComputed()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(Usage));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(HasMatches));
    }

    private void ApplyFilterCore()
    {
        var filter = _filterText.Trim();
        _visible.Clear();
        _groups.Clear();
        foreach (var group in _cards.GroupBy(static card => card.Group, StringComparer.Ordinal))
        {
            var matches = group.Where(card => Matches(card, filter)).ToList();
            if (matches.Count == 0)
            {
                continue;
            }

            _visible.AddRange(matches);
            _groups.Add(new GalleryCardGroup(group.Key, matches));
        }

        OnPropertyChanged(nameof(VisibleGroups));
        OnPropertyChanged(nameof(VisibleCards));
        OnPropertyChanged(nameof(HasMatches));
    }

    private static bool Matches(GalleryCardInfo card, string filter)
        => filter.Length == 0
           || card.Id.Contains(filter, StringComparison.OrdinalIgnoreCase)
           || card.Title.Contains(filter, StringComparison.OrdinalIgnoreCase)
           || card.Summary.Contains(filter, StringComparison.OrdinalIgnoreCase)
           || card.Group.Contains(filter, StringComparison.OrdinalIgnoreCase);
}
