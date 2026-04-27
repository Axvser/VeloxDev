using Avalonia.Controls;
using System.Collections.Generic;
using VeloxDev.Docs.ViewModels;

namespace VeloxDev.Docs;

public partial class EmptyView : WikiElementViewBase
{
    public EmptyView()
    {
        InitializeComponent();
        InitializeEditChrome(ChromeBorder, DisplayPanel, EditPanel);
    }

    protected override IEnumerable<Control> CreateAdditionalContextMenuItems()
    {
        if (DataContext is not EmptyProvider empty)
            yield break;

        yield return new Separator();
        foreach (var type in empty.ConvertibleTypes)
        {
            yield return CreateMenuItem($"Convert to {GetDisplayName(type)}", () =>
            {
                if (GetOwnerDocument() is { } document)
                    document.ReplaceElement(empty, type);
            });
        }
    }

    private static string GetDisplayName(System.Type type)
    {
        var name = type.Name;
        return name.EndsWith("Provider") ? name[..^"Provider".Length] : name;
    }
}
