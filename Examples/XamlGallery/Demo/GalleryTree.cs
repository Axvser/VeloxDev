using System.ComponentModel;
using System.Windows.Forms;
using VeloxDev.WinForms.Xaml.Controls;
using XamlTreeView = VeloxDev.WinForms.Xaml.Controls.TreeView;

namespace Demo;

/// <summary>A native tree node that remembers the catalogue group or entry it represents.</summary>
internal sealed class GalleryTreeItem : TreeViewItem
{
    /// <summary>The group when this is a category row; null for a leaf.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public GalleryGroup? Group { get; init; }

    /// <summary>The entry when this is a leaf; null for a category row.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public GalleryEntry? Entry { get; init; }
}

/// <summary>
/// Builds the authored <see cref="TreeViewItem"/> hierarchy from <see cref="GalleryCatalog"/>.
/// </summary>
/// <remarks>
/// The tree is native-backed and renders header text only: no ItemTemplate, no icons, no per-row colours.
/// Authored items are used instead of ItemsSource because only authored items expose IsExpanded.
/// </remarks>
internal sealed class GalleryTreeBuilder
{
    private readonly XamlTreeView _tree;
    private readonly List<GalleryTreeItem> _built = [];

    public GalleryTreeBuilder(XamlTreeView tree)
    {
        _tree = tree;
    }

    /// <summary>The filter used by the last build; empty shows every entry.</summary>
    public string Filter { get; private set; } = string.Empty;

    /// <summary>The number of leaf rows currently visible.</summary>
    public int VisibleLeafCount { get; private set; }

    /// <summary>The selected row, when it is one of ours.</summary>
    public GalleryTreeItem? Selected => _tree.SelectedItem as GalleryTreeItem;

    /// <summary>Rebuilds the tree, keeping each matching group expanded.</summary>
    public void Build(string? filter)
    {
        Filter = filter?.Trim() ?? string.Empty;
        VisibleLeafCount = 0;

        _tree.Items.Clear();
        foreach (var old in _built)
        {
            DisposeItem(old);
        }

        _built.Clear();

        foreach (var group in GalleryCatalog.Groups)
        {
            var leaves = new List<GalleryEntry>();
            foreach (var entry in group.Entries)
            {
                if (Matches(entry, Filter))
                {
                    leaves.Add(entry);
                }
            }

            if (Filter.Length > 0 && leaves.Count == 0)
            {
                continue;
            }

            var node = new GalleryTreeItem
            {
                Header = $"{group.Title}  ({leaves.Count})",
                Group = group,
                IsExpanded = true,
            };

            foreach (var entry in leaves)
            {
                node.Items.Add(new GalleryTreeItem { Header = entry.Title, Entry = entry });
                VisibleLeafCount++;
            }

            _built.Add(node);
            _tree.Items.Add(node);
        }
    }

    /// <summary>Selects the row for an entry id or group id without raising the selection event.</summary>
    public bool TrySelect(string id)
    {
        foreach (var root in _built)
        {
            if (MatchesId(root, id))
            {
                _tree.SelectedItem = root;
                return true;
            }

            foreach (var child in root.Items)
            {
                if (child is GalleryTreeItem item && MatchesId(item, id))
                {
                    _tree.SelectedItem = item;
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>True when the id is currently visible in the tree.</summary>
    public bool Contains(string id) => Find(id) is not null;

    /// <summary>Finds the row for an entry id or group id.</summary>
    public GalleryTreeItem? Find(string id)
    {
        foreach (var root in _built)
        {
            if (MatchesId(root, id))
            {
                return root;
            }

            foreach (var child in root.Items)
            {
                if (child is GalleryTreeItem item && MatchesId(item, id))
                {
                    return item;
                }
            }
        }

        return null;
    }

    /// <summary>Clears the native selection.</summary>
    public void ClearSelection() => _tree.SelectedItem = null;

    private static bool MatchesId(GalleryTreeItem item, string id)
        => string.Equals(item.Entry?.Id, id, StringComparison.OrdinalIgnoreCase)
            || string.Equals(item.Group?.Id, id, StringComparison.OrdinalIgnoreCase);

    private static bool Matches(GalleryEntry entry, string filter)
    {
        if (filter.Length == 0)
        {
            return true;
        }

        return entry.Title.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || entry.Summary.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || entry.Keywords.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || entry.Id.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || entry.GroupTitle.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    private static void DisposeItem(TreeViewItem item)
    {
        foreach (var child in item.Items)
        {
            DisposeItem(child);
        }

        if (!item.IsDisposed)
        {
            item.Dispose();
        }
    }
}
