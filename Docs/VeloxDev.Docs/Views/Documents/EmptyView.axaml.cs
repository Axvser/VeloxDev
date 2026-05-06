using Avalonia.Controls;
using System.Collections.Generic;
using System.Linq;
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
        var convertItems = BuildConvertItems(empty);

        yield return CreateSubMenu("Convert", convertItems);
    }

    private List<object> BuildConvertItems(EmptyProvider empty)
    {
        var items = new List<object>();

        var headingItems = Enumerable.Range(1, 4)
            .Select(level => (object)CreateMenuItem($"H{level}", () => ConvertToHeading(empty, level)))
            .ToList();
        items.Add(CreateSubMenu("Heading", headingItems));

        items.AddRange(empty.ConvertibleTypes
            .Where(type => type != typeof(TitleProvider))
            .OrderBy(type => GetDisplayCategoryOrder(type))
            .ThenBy(type => GetDisplayName(type))
            .GroupBy(GetDisplayCategory)
            .Select(group =>
            {
                var groupItems = group
                    .Select(type => (object)CreateMenuItem(GetDisplayName(type), () =>
                    {
                        if (GetOwnerDocument() is { } document)
                            document.ReplaceElement(empty, type);
                    }))
                    .ToList();

                return group.Key == "Other"
                    ? groupItems
                    : [CreateSubMenu(group.Key, groupItems)];
            })
            .SelectMany(groupItems => groupItems));

        return items;
    }

    private void ConvertToHeading(EmptyProvider empty, int level)
    {
        if (GetOwnerDocument() is not { } document)
            return;

        document.ReplaceElement(empty, new TitleProvider
        {
            Level = level.ToString(),
            Text = "New title"
        });
    }

    private static int GetDisplayCategoryOrder(System.Type type)
    {
        if (type == typeof(TitleProvider))
            return 0;
        if (type == typeof(ParagraphProvider) || type == typeof(SubtitleProvider))
            return 1;
        if (type == typeof(LinkProvider))
            return 2;
        if (type == typeof(CodeProvider) || type == typeof(MarkdownProvider))
            return 3;
        if (type == typeof(TableProvider) || type == typeof(ImageProvider))
            return 4;

        return 5;
    }

    private static string GetDisplayCategory(System.Type type)
    {
        if (type == typeof(ParagraphProvider) || type == typeof(SubtitleProvider))
            return "Text";
        if (type == typeof(LinkProvider))
            return "Link";
        if (type == typeof(CodeProvider) || type == typeof(MarkdownProvider))
            return "Code";
        if (type == typeof(TableProvider) || type == typeof(ImageProvider))
            return "Media";

        return "Other";
    }

    private static string GetDisplayName(System.Type type)
    {
        if (type == typeof(ParagraphProvider))
            return "Paragraph";
        if (type == typeof(SubtitleProvider))
            return "Quote / Subtitle";
        if (type == typeof(LinkProvider))
            return "Link";
        if (type == typeof(CodeProvider))
            return "Code Block";
        if (type == typeof(MarkdownProvider))
            return "Markdown";
        if (type == typeof(TableProvider))
            return "Table";
        if (type == typeof(ImageProvider))
            return "Image";

        var name = type.Name;
        return name.EndsWith("Provider") ? name[..^"Provider".Length] : name;
    }
}
