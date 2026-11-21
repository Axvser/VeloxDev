using System.Collections;
using System.ComponentModel;
using System.Windows.Forms;

namespace VeloxDev.WinForms.Xaml.Controls;

/// <summary>
/// Hosts the items of an <see cref="ItemsControl"/>, repeating its source, template and container style.
/// </summary>
/// <remarks>
/// <para>
/// With <see cref="Owner"/> set, the presenter follows the owner's <see cref="ItemsControl.ItemsSource"/>,
/// <see cref="ItemsControl.ItemTemplate"/>, <see cref="ItemsControl.ItemTemplateSelector"/> and
/// <see cref="ItemsControl.ItemContainerStyle"/> and re-materializes the item visuals when any of them changes.
/// With no owner, the presenter repeats its own <see cref="ItemsControl.ItemsSource"/>.
/// </para>
/// <para>
/// A WinForms control has a single parent, so the presenter creates its own item visuals instead of reparenting the
/// owner's generated controls; the owner keeps its own tree untouched. Set <see cref="Owner"/> or the three item
/// properties, not both: the owner wins while it is attached.
/// </para>
/// </remarks>
public class ItemsPresenter : ItemsControl
{
    /// <summary>The control whose items and item settings this presenter mirrors, or <see langword="null"/>.</summary>
    public static readonly DependencyProperty OwnerProperty = DependencyProperty.Register(
        nameof(Owner),
        typeof(ItemsControl),
        typeof(ItemsPresenter),
        new PropertyMetadata(null, OnOwnerPropertyChanged));

    private ItemsControl? _owner;
    private bool _syncingOwner;

    // 脱离 Owner 时恢复；Owner 期间这三项由宿主提供。
    private IEnumerable? _ownItemsSource;
    private DataTemplate? _ownItemTemplate;
    private TemplateSelector? _ownSelector;
    private Style? _ownItemContainerStyle;

    /// <summary>Creates the presenter with double buffering.</summary>
    public ItemsPresenter()
    {
        DoubleBuffered = true;
    }

    /// <summary>Gets or sets the control whose items and item settings this presenter mirrors.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ItemsControl? Owner
    {
        get => (ItemsControl?)GetValue(OwnerProperty);
        set => SetValue(OwnerProperty, value);
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            DetachOwner();
        }

        base.Dispose(disposing);
    }

    private static void OnOwnerPropertyChanged(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e)
    {
        if (target is ItemsPresenter presenter && !presenter.IsDisposed)
        {
            presenter.AttachOwner(e.NewValue as ItemsControl);
        }
    }

    private void AttachOwner(ItemsControl? owner)
    {
        // 自己当自己的宿主会形成无意义的镜像环。
        if (ReferenceEquals(owner, this))
        {
            owner = null;
        }

        if (ReferenceEquals(_owner, owner))
        {
            return;
        }

        if (_owner is not null)
        {
            _owner.DependencyPropertyChanged -= OnOwnerDependencyPropertyChanged;
        }

        if (_owner is null && owner is not null)
        {
            _ownItemsSource = ItemsSource;
            _ownItemTemplate = ItemTemplate;
            _ownSelector = ItemTemplateSelector;
            _ownItemContainerStyle = ItemContainerStyle;
        }

        _owner = owner;
        if (_owner is null)
        {
            RestoreOwnSettings();
            return;
        }

        _owner.DependencyPropertyChanged += OnOwnerDependencyPropertyChanged;
        MirrorOwner();
    }

    private void DetachOwner()
    {
        if (_owner is not null)
        {
            _owner.DependencyPropertyChanged -= OnOwnerDependencyPropertyChanged;
            _owner = null;
        }
    }

    private void OnOwnerDependencyPropertyChanged(object? sender, DependencyPropertyChangedEventArgs e)
    {
        if (_syncingOwner || IsDisposed)
        {
            return;
        }

        if (ReferenceEquals(e.Property, ItemsSourceProperty)
            || ReferenceEquals(e.Property, ItemTemplateProperty)
            || ReferenceEquals(e.Property, ItemTemplateSelectorProperty)
            || ReferenceEquals(e.Property, ItemContainerStyleProperty))
        {
            MirrorOwner();
        }
    }

    private void MirrorOwner()
    {
        if (_owner is null || _syncingOwner || IsDisposed)
        {
            return;
        }

        _syncingOwner = true;
        try
        {
            if (!ReferenceEquals(ItemsSource, _owner.ItemsSource))
            {
                ItemsSource = _owner.ItemsSource;
            }

            if (!ReferenceEquals(ItemTemplate, _owner.ItemTemplate))
            {
                ItemTemplate = _owner.ItemTemplate;
            }

            if (!ReferenceEquals(ItemTemplateSelector, _owner.ItemTemplateSelector))
            {
                ItemTemplateSelector = _owner.ItemTemplateSelector;
            }

            if (!ReferenceEquals(ItemContainerStyle, _owner.ItemContainerStyle))
            {
                ItemContainerStyle = _owner.ItemContainerStyle;
            }
        }
        finally
        {
            _syncingOwner = false;
        }

        PerformLayout();
        Invalidate();
    }

    private void RestoreOwnSettings()
    {
        _syncingOwner = true;
        try
        {
            ItemsSource = _ownItemsSource;
            ItemTemplate = _ownItemTemplate;
            ItemTemplateSelector = _ownSelector;
            ItemContainerStyle = _ownItemContainerStyle;
        }
        finally
        {
            _syncingOwner = false;
        }

        PerformLayout();
        Invalidate();
    }
}
