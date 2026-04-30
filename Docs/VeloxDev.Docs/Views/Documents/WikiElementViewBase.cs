using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia;
using Avalonia.VisualTree;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using VeloxDev.Docs.ViewModels;

namespace VeloxDev.Docs;

public abstract class WikiElementViewBase : UserControl
{
    protected Border? Chrome { get; set; }
    protected Control? Display { get; set; }
    protected Control? Editor { get; set; }

    protected bool IsEditing { get; private set; }

    private DocumentProvider? _attachedDocument;
    private bool IsGlobalEditMode => _attachedDocument?.IsEditMode ?? false;

    protected void InitializeEditChrome(Border chrome, Control display, Control editor)
    {
        Chrome = chrome;
        Display = display;
        Editor = editor;

        DataContextChanged += (_, _) => ContextMenu = CreateContextMenu();
        ContextMenu = CreateContextMenu();

        AddEditorHandlers(editor);

        // 双击进入编辑
        chrome.PointerPressed += (_, e) =>
        {
            if (e.ClickCount == 2 && IsGlobalEditMode)
                EnterEdit();
        };

        AttachedToVisualTree += (_, _) => AttachDocument();
        DetachedFromVisualTree += (_, _) => DetachDocument();

        ExitEdit();
    }

    private void AttachDocument()
    {
        DetachDocument();
        _attachedDocument = GetOwnerDocument();
        if (_attachedDocument is not null)
            _attachedDocument.PropertyChanged += OnDocumentPropertyChanged;

        ContextMenu = CreateContextMenu();
        UpdateChrome();
    }

    private void DetachDocument()
    {
        if (_attachedDocument is not null)
        {
            _attachedDocument.PropertyChanged -= OnDocumentPropertyChanged;
            _attachedDocument = null;
        }
    }

    private void OnDocumentPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DocumentProvider.IsEditMode))
        {
            if (!IsGlobalEditMode && IsEditing)
                ExitEdit();

            ContextMenu = CreateContextMenu();
            UpdateChrome();
        }
    }

    private ContextMenu CreateContextMenu()
    {
        var items = new List<Control>();

        if (IsGlobalEditMode)
        {
            var edit = new MenuItem { Header = "✏ Edit" };
            edit.Click += (_, _) => EnterEdit();
            items.Add(edit);

            if (IsEditing)
            {
                var browse = new MenuItem { Header = "✔ Done Editing" };
                browse.Click += (_, _) => ExitEdit();
                items.Add(browse);
            }

            if (DataContext is IWikiElement and not NodeProvider)
            {
                items.Add(new Separator());
                items.Add(CreateMenuItem("↑ Insert Empty Above", () => InsertEmpty(below: false)));
                items.Add(CreateMenuItem("↓ Insert Empty Below", () => InsertEmpty(below: true)));
            }

            items.AddRange(CreateAdditionalContextMenuItems());

            items.Add(new Separator());
            var remove = new MenuItem { Header = "🗑 Remove" };
            remove.Click += (_, _) => RemoveFromDocument();
            items.Add(remove);
        }
        else
        {
            var hint = new MenuItem { Header = "Switch to Edit Mode to modify", IsEnabled = false };
            items.Add(hint);
        }

        return new ContextMenu { ItemsSource = items };
    }

    protected virtual IEnumerable<Control> CreateAdditionalContextMenuItems() => [];

    protected static MenuItem CreateMenuItem(string header, System.Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    protected virtual void EnterEdit()
    {
        if (!IsGlobalEditMode)
            return;

        IsEditing = true;
        if (Display is not null) Display.IsVisible = false;
        if (Editor is not null)
        {
            Editor.IsVisible = true;
            FocusEditor();
        }
        UpdateChrome();
    }

    protected virtual void ExitEdit()
    {
        IsEditing = false;
        if (Display is not null) Display.IsVisible = true;
        if (Editor is not null) Editor.IsVisible = false;
        ContextMenu = CreateContextMenu();
        UpdateChrome();
    }

    protected virtual void UpdateChrome()
    {
        if (Chrome is null) return;
        if (IsEditing)
        {
            Chrome.BorderThickness = new Thickness(1);
            Chrome.BorderBrush = new SolidColorBrush(Color.Parse("#4D9EF5"));
            Chrome.Background = new SolidColorBrush(Color.Parse("#0A9EF5"));
        }
        else if (IsGlobalEditMode)
        {
            Chrome.Background = new SolidColorBrush(Color.Parse("#08ffffff"));
            Chrome.BorderThickness = new Thickness(1);
            Chrome.BorderBrush = new SolidColorBrush(Color.Parse("#22ffffff"));
        }
        else
        {
            Chrome.Background = new SolidColorBrush(Color.Parse("#01ffffff"));
            Chrome.BorderThickness = new Thickness(0);
            Chrome.BorderBrush = Brushes.Transparent;
        }
    }

    private void AddEditorHandlers(Control editor)
    {
        editor.LostFocus += (_, _) => ExitWhenFocusLeavesEditor();

        foreach (var child in editor.GetVisualDescendants().OfType<Control>())
        {
            child.LostFocus += (_, _) => ExitWhenFocusLeavesEditor();
        }
    }

    private void ExitWhenFocusLeavesEditor()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!IsEditing || Editor is null)
                return;

            if (Editor.GetVisualDescendants().OfType<ComboBox>().Any(comboBox => comboBox.IsDropDownOpen))
                return;

            var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Visual;
            if (focused is null)
            {
                ExitEdit();
                return;
            }

            if (ReferenceEquals(focused, Editor) || Editor.GetVisualDescendants().Contains(focused))
                return;

            if (focused is PopupRoot || focused.GetVisualAncestors().OfType<PopupRoot>().Any())
                return;

            ExitEdit();
        }, DispatcherPriority.Background);
    }

    private void FocusEditor()
    {
        if (Editor is null) return;

        if (Editor is TextBox textBox)
        {
            textBox.Focus();
            textBox.SelectAll();
            return;
        }

        var innerTextBox = Editor.GetVisualDescendants().OfType<TextBox>().FirstOrDefault();
        if (innerTextBox is not null)
        {
            innerTextBox.Focus();
            innerTextBox.SelectAll();
            return;
        }

        Editor.Focus();
    }

    protected DocumentProvider? GetOwnerDocument()
        => this.GetVisualAncestors()
            .OfType<DocumentView>()
            .FirstOrDefault()
            ?.DataContext as DocumentProvider;

    private void RemoveFromDocument()
    {
        if (GetOwnerDocument() is not { } document)
            return;

        if (DataContext is NodeProvider node)
        {
            document.SelectedNode = node;
            document.RemoveNodeCommand.Execute(null);
            return;
        }

        document.RemoveElementCommand.Execute(DataContext);
    }

    private void InsertEmpty(bool below)
    {
        if (GetOwnerDocument() is { } document && DataContext is IWikiElement element)
            document.InsertEmptyAround(element, below);
    }
}
