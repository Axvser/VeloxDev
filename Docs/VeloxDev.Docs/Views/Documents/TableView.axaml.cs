using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using System.Collections.Specialized;
using VeloxDev.Docs.ViewModels;

namespace VeloxDev.Docs;

public partial class TableView : WikiElementViewBase
{
    private static readonly FontFamily ContentFontFamily = new("Microsoft YaHei UI,Microsoft JhengHei UI,PingFang SC,Hiragino Sans GB,Noto Sans CJK SC,Segoe UI,Inter,sans-serif");

    private const double ColumnZoneHeight = 28;
    private const double HeaderRowHeight = 36;
    private const double BodyRowHeight = 34;

    private bool _isRendering;
    private TableProvider? _attachedTable;

    public TableView()
    {
        InitializeComponent();
        InitializeEditChrome(ChromeBorder, DisplayPanel, EditPanel);
        DataContextChanged += (_, _) => AttachAndRender();
    }

    protected override void ExitEdit()
    {
        base.ExitEdit();
        RenderDisplay();
    }

    private void AttachAndRender()
    {
        if (_attachedTable is not null)
        {
            _attachedTable.Headers.CollectionChanged -= TableChanged;
            _attachedTable.Alignments.CollectionChanged -= AlignmentChanged;
            _attachedTable.Rows.CollectionChanged -= RowsChanged;
            foreach (var row in _attachedTable.Rows)
                row.Cells.CollectionChanged -= TableChanged;
            _attachedTable = null;
        }

        if (DataContext is TableProvider table)
        {
            _attachedTable = table;
            table.Headers.CollectionChanged += TableChanged;
            table.Alignments.CollectionChanged += AlignmentChanged;
            table.Rows.CollectionChanged += RowsChanged;
            foreach (var row in table.Rows)
                row.Cells.CollectionChanged += TableChanged;
        }

        RenderDisplay();
        RenderEdit();
    }

    private void AlignmentChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_isRendering)
            return;

        RenderDisplay();
        RenderEdit();
    }

    private void RowsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_isRendering)
            return;

        if (e.OldItems is not null)
        {
            foreach (TableRowProvider row in e.OldItems)
                row.Cells.CollectionChanged -= TableChanged;
        }

        if (e.NewItems is not null)
        {
            foreach (TableRowProvider row in e.NewItems)
                row.Cells.CollectionChanged += TableChanged;
        }

        RenderDisplay();
        RenderEdit();
    }

    private void TableChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_isRendering)
            return;

        RenderDisplay();

        if (e.Action != NotifyCollectionChangedAction.Replace)
            RenderEdit();
    }

    private void RenderDisplay()
    {
        if (_isRendering)
            return;

        _isRendering = true;
        try
        {
            DisplayHost.Children.Clear();
            if (DataContext is not TableProvider table)
                return;

            var tableHost = CreateTableFrame();
            var headerGrid = CreateGrid(table.Headers.Count);
            for (int i = 0; i < table.Headers.Count; i++)
                headerGrid.Children.Add(CreateDisplayCell(table.Headers[i], i, true, GetAlignment(table, i)));
            tableHost.Children.Add(headerGrid);

            foreach (var row in table.Rows)
            {
                var rowGrid = CreateGrid(table.Headers.Count);
                for (int i = 0; i < table.Headers.Count; i++)
                {
                    var text = i < row.Cells.Count ? row.Cells[i] : string.Empty;
                    rowGrid.Children.Add(CreateDisplayCell(text, i, false, GetAlignment(table, i)));
                }
                tableHost.Children.Add(rowGrid);
            }

            DisplayHost.Children.Add(WrapTableFrame(tableHost));
        }
        finally
        {
            _isRendering = false;
        }
    }

    private void RenderEdit()
    {
        if (_isRendering)
            return;

        _isRendering = true;
        try
        {
            EditHost.Children.Clear();
            if (DataContext is not TableProvider table)
                return;

            EditHost.Children.Add(new TextBlock
            {
                Text = "Right-click the top column zones or right row zones to insert/remove rows and columns.",
                Opacity = 0.6,
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 4)
            });

            var editor = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,*"),
                ColumnDefinitions = new ColumnDefinitions("*,Auto")
            };

            var columnZones = CreateColumnZones(table);
            Grid.SetRow(columnZones, 0);
            Grid.SetColumn(columnZones, 0);
            editor.Children.Add(columnZones);

            var rowZoneSpacer = CreateOperationZone(string.Empty, new ContextMenu { ItemsSource = System.Array.Empty<Control>() }, ColumnZoneHeight);
            Grid.SetRow(rowZoneSpacer, 0);
            Grid.SetColumn(rowZoneSpacer, 1);
            editor.Children.Add(rowZoneSpacer);

            var tableHost = CreateTableFrame();
            var headerGrid = CreateGrid(table.Headers.Count);
            for (int i = 0; i < table.Headers.Count; i++)
            {
                var index = i;
                headerGrid.Children.Add(CreateHeaderEditorCell(table, index));
            }
            tableHost.Children.Add(headerGrid);

            for (int r = 0; r < table.Rows.Count; r++)
            {
                var rowIndex = r;
                var row = table.Rows[rowIndex];
                var rowGrid = CreateGrid(table.Headers.Count);
                for (int i = 0; i < table.Headers.Count; i++)
                {
                    while (row.Cells.Count <= i)
                        row.Cells.Add(string.Empty);

                    var index = i;
                    rowGrid.Children.Add(CreateEditorCell(row.Cells[index], index, GetAlignment(table, index), BodyRowHeight, value =>
                    {
                        if (row.Cells[index] != value)
                            row.Cells[index] = value;
                    }));
                }

                tableHost.Children.Add(rowGrid);
            }

            var framedTable = WrapTableFrame(tableHost);
            Grid.SetRow(framedTable, 1);
            Grid.SetColumn(framedTable, 0);
            editor.Children.Add(framedTable);

            var rowZones = CreateRowZones(table, includeHeaderOffset: true);
            Grid.SetRow(rowZones, 1);
            Grid.SetColumn(rowZones, 1);
            editor.Children.Add(rowZones);

            EditHost.Children.Add(editor);
        }
        finally
        {
            _isRendering = false;
        }
    }

    private static Grid CreateGrid(int columns)
    {
        var grid = new Grid();
        for (int i = 0; i < columns; i++)
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        return grid;
    }

    private static Grid CreateColumnZones(TableProvider table)
    {
        var grid = CreateGrid(table.Headers.Count);
        for (int i = 0; i < table.Headers.Count; i++)
        {
            var zone = CreateOperationZone($"Column {i + 1}", CreateColumnContextMenu(table, i), ColumnZoneHeight);
            Grid.SetColumn(zone, i);
            grid.Children.Add(zone);
        }
        return grid;
    }

    private static StackPanel CreateRowZones(TableProvider table, bool includeHeaderOffset)
    {
        var stack = new StackPanel { Spacing = 0, Width = 88 };
        if (includeHeaderOffset)
            stack.Children.Add(CreateOperationZone(string.Empty, new ContextMenu { ItemsSource = System.Array.Empty<Control>() }, HeaderRowHeight));

        for (int i = 0; i < table.Rows.Count; i++)
            stack.Children.Add(CreateOperationZone($"Row {i + 1}", CreateRowContextMenu(table, i), BodyRowHeight));
        return stack;
    }

    private static Border CreateOperationZone(string text, ContextMenu menu, double height) => new()
    {
        Height = height,
        MinWidth = 88,
        BorderBrush = Brushes.Gray,
        BorderThickness = new Thickness(0, 0, 1, 1),
        Background = new SolidColorBrush(Color.FromArgb(24, 128, 128, 128)),
        ContextMenu = menu,
        Child = new TextBlock
        {
            Text = text,
            FontSize = 11,
            Opacity = 0.7,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        }
    };

    private static StackPanel CreateTableFrame() => new()
    {
        Spacing = 0
    };

    private static Border WrapTableFrame(Control content) => new()
    {
        BorderBrush = Brushes.Gray,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(6),
        ClipToBounds = true,
        Child = content
    };

    private static Border CreateDisplayCell(string text, int column, bool isHeader, TextAlignment alignment)
    {
        // Don't set FontWeight on header cells: Skia's CJK fallback fails to find a
        // SemiBold CJK face on many systems and renders .notdef glyphs (mojibake).
        // Match MarkdownView headings — distinguish headers via background only.
        var textBlock = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = alignment,
            VerticalAlignment = VerticalAlignment.Center,
            FontFamily = ContentFontFamily
        };

        // Use Inlines+Run for per-glyph font fallback (same path as MarkdownView)
        textBlock.Inlines ??= [];
        textBlock.Inlines.Add(new Avalonia.Controls.Documents.Run(text));

        var cell = new Border
        {
            Height = isHeader ? HeaderRowHeight : BodyRowHeight,
            BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(0, 0, 1, 1),
            Background = CreateCellBackground(isHeader),
            Padding = new Thickness(6),
            Child = textBlock
        };

        Grid.SetColumn(cell, column);
        return cell;
    }

    private static TextBox CreateEditorCell(string text, int column, TextAlignment alignment, double height, System.Action<string> update)
    {
        var editor = new TextBox
        {
            Text = text,
            Height = height,
            MinWidth = 120,
            BorderThickness = new Thickness(0),
            Margin = new Thickness(0),
            Padding = new Thickness(6),
            Background = Brushes.Transparent,
            TextAlignment = alignment,
            VerticalContentAlignment = VerticalAlignment.Center,
            FontFamily = ContentFontFamily
        };
        editor.TextChanged += (_, _) => update(editor.Text ?? string.Empty);
        Grid.SetColumn(editor, column);
        return editor;
    }

    private static Grid CreateHeaderEditorCell(TableProvider table, int index)
    {
        var host = new Grid
        {
            Background = CreateCellBackground(true),
            Height = HeaderRowHeight
        };
        host.Children.Add(CreateEditorCell(table.Headers[index], 0, GetAlignment(table, index), HeaderRowHeight, value =>
        {
            if (table.Headers[index] != value)
                table.Headers[index] = value;
        }));

        Grid.SetColumn(host, index);
        return host;
    }

    private static IBrush CreateCellBackground(bool isHeader) => isHeader
        ? new SolidColorBrush(Color.FromArgb(48, 128, 128, 128))
        : new SolidColorBrush(Color.FromArgb(12, 128, 128, 128));

    private static ContextMenu CreateColumnContextMenu(TableProvider table, int index) => new()
    {
        ItemsSource = new Control[]
        {
            CreateMenuItem("Insert Column Left", () => table.InsertColumnCommand.Execute(index)),
            CreateMenuItem("Insert Column Right", () => table.InsertColumnCommand.Execute(index + 1)),
            new Separator(),
            CreateMenuItem("Align Left", () => SetAlignment(table, index, "Left")),
            CreateMenuItem("Align Center", () => SetAlignment(table, index, "Center")),
            CreateMenuItem("Align Right", () => SetAlignment(table, index, "Right")),
            new Separator(),
            CreateMenuItem("Remove Column", () => table.RemoveColumnCommand.Execute(index))
        }
    };

    private static TextAlignment GetAlignment(TableProvider table, int index)
    {
        if (index < 0 || index >= table.Alignments.Count)
            return TextAlignment.Left;

        return table.Alignments[index] switch
        {
            "Center" => TextAlignment.Center,
            "Right" => TextAlignment.Right,
            _ => TextAlignment.Left
        };
    }

    private static void SetAlignment(TableProvider table, int index, string alignment)
    {
        while (table.Alignments.Count <= index)
            table.Alignments.Add("Left");

        table.Alignments[index] = alignment;
    }

    private static ContextMenu CreateRowContextMenu(TableProvider table, int index) => new()
    {
        ItemsSource = new Control[]
        {
            CreateMenuItem("Insert Row Above", () => table.InsertRowCommand.Execute(index)),
            CreateMenuItem("Insert Row Below", () => table.InsertRowCommand.Execute(index + 1)),
            new Separator(),
            CreateMenuItem("Remove Row", () => table.RemoveRowCommand.Execute(index))
        }
    };

    private static Button CreateButton(string text, System.Action action)
    {
        var button = new Button
        {
            Content = text,
            Margin = new Thickness(0, 0, 4, 4)
        };
        button.Click += (_, _) => action();
        return button;
    }
}
