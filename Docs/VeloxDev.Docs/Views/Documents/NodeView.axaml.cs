using Avalonia.Controls;
using System.Collections.Generic;
using System.ComponentModel;
using VeloxDev.Docs.ViewModels;

namespace VeloxDev.Docs;

public partial class NodeView : WikiElementViewBase
{
    private NodeProvider? _provider;

    public NodeView()
    {
        InitializeComponent();
        InitializeEditChrome(ChromeBorder, DisplayPanel, EditPanel);
        DataContextChanged += (_, _) => AttachProvider();
        AttachProvider();
    }

    private void AttachProvider()
    {
        if (_provider is not null)
            _provider.PropertyChanged -= OnProviderChanged;

        _provider = DataContext as NodeProvider;
        if (_provider is not null)
            _provider.PropertyChanged += OnProviderChanged;

        Refresh();
    }

    private void OnProviderChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NodeProvider.Title))
            Refresh();
    }

    private void Refresh() => SetTextViaRun(DisplayPanel, _provider?.Title);

    protected override IEnumerable<Control> CreateAdditionalContextMenuItems()
    {
        var addChild = new MenuItem { Header = "Add Child" };
        addChild.Click += (_, _) =>
        {
            if (GetOwnerDocument() is { } document && DataContext is NodeProvider node)
            {
                document.SelectedNode = node;
                document.AddChildCommand.Execute(null);
            }
        };

        yield return new Separator();
        yield return addChild;
    }
}
