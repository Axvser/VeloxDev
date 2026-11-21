namespace VeloxDev.WinForms.Xaml.Controls;

/// <summary>
/// A navigation host that shows one <see cref="Page"/> at a time and keeps a back/forward journal.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Navigate"/> appends the page to the journal and drops any forward entries. <see cref="GoBack"/> and
/// <see cref="GoForward"/> move through the journal and show the page they land on. Assigning
/// <see cref="ContentControl.Content"/> a <see cref="Page"/> directly behaves like <see cref="Navigate"/>; assigning
/// any other content ends navigation without touching the journal.
/// </para>
/// <para>
/// This is a single-window journal, not the WPF navigation stack: there is no page lifetime, no
/// <c>NavigationService</c>, no URI navigation and no navigation modes.
/// </para>
/// </remarks>
public class Frame : ContentControl
{
    private readonly List<Page> _journal = [];
    private int _journalIndex = -1;

    /// <summary>Creates an empty frame.</summary>
    public Frame()
    {
    }

    /// <summary>The page currently shown, or null when the content is not a <see cref="Page"/>.</summary>
    public Page? CurrentPage => Content as Page;

    /// <summary>Whether the journal has an entry before the current one.</summary>
    public bool CanGoBack => _journalIndex > 0;

    /// <summary>Whether the journal has an entry after the current one.</summary>
    public bool CanGoForward => _journalIndex >= 0 && _journalIndex < _journal.Count - 1;

    /// <summary>The pages in the journal, oldest first.</summary>
    public IReadOnlyList<Page> Journal => _journal;

    /// <summary>Raised after the current page changes through navigation.</summary>
    public event EventHandler? NavigationChanged;

    /// <summary>Shows <paramref name="page"/> and appends it to the journal, discarding forward entries.</summary>
    /// <param name="page">The page to show.</param>
    /// <exception cref="ArgumentNullException"><paramref name="page"/> is <see langword="null"/>.</exception>
    public void Navigate(Page page)
    {
        if (page is null) throw new ArgumentNullException(nameof(page));
        if (ReferenceEquals(Content, page))
        {
            return;
        }

        Content = page;
    }

    /// <summary>Shows the previous journal entry.</summary>
    /// <returns><see langword="true"/> when the frame moved back.</returns>
    public bool GoBack()
    {
        if (!CanGoBack)
        {
            return false;
        }

        _journalIndex--;
        ShowJournalEntry();
        return true;
    }

    /// <summary>Shows the next journal entry.</summary>
    /// <returns><see langword="true"/> when the frame moved forward.</returns>
    public bool GoForward()
    {
        if (!CanGoForward)
        {
            return false;
        }

        _journalIndex++;
        ShowJournalEntry();
        return true;
    }

    /// <inheritdoc />
    protected override void OnXamlPropertyChanged(DependencyProperty property, object? oldValue, object? newValue)
    {
        base.OnXamlPropertyChanged(property, oldValue, newValue);

        if (!ReferenceEquals(property, ContentProperty) || newValue is not Page page)
        {
            return;
        }

        // GoBack/GoForward 先把索引移到目标项再改 Content；索引已经指向这一页时不再入账。
        if (_journalIndex >= 0 && _journalIndex < _journal.Count && ReferenceEquals(_journal[_journalIndex], page))
        {
            return;
        }

        if (_journalIndex < _journal.Count - 1)
        {
            _journal.RemoveRange(_journalIndex + 1, _journal.Count - _journalIndex - 1);
        }

        _journal.Add(page);
        _journalIndex = _journal.Count - 1;
        NavigationChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ShowJournalEntry()
    {
        var page = _journal[_journalIndex];
        if (!ReferenceEquals(Content, page))
        {
            Content = page;
        }

        NavigationChanged?.Invoke(this, EventArgs.Empty);
    }
}
