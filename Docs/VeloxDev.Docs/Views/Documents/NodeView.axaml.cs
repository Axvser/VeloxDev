using Avalonia.Controls;
using System.Collections.Generic;
using VeloxDev.Docs.ViewModels;

namespace VeloxDev.Docs;

public partial class NodeView : WikiElementViewBase
{
    public NodeView()
    {
        InitializeComponent();
        InitializeEditChrome(ChromeBorder, DisplayPanel, EditPanel);
    }

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
