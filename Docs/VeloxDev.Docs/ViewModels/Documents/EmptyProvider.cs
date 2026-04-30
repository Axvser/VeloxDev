using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using VeloxDev.MVVM;

namespace VeloxDev.Docs.ViewModels;

public partial class EmptyProvider : IWikiElement
{
    private static readonly Lazy<IReadOnlyList<Type>> _convertibleTypes = new(FindConvertibleTypes);

    [VeloxProperty] public partial IWikiElement? Parent { get; set; }

    public IReadOnlyList<Type> ConvertibleTypes => _convertibleTypes.Value;

    public static IWikiElement CreateDefault(Type type, IWikiElement? parent)
    {
        if (type == typeof(TitleProvider))
            return new TitleProvider { Parent = parent, Level = "1", Text = "New title" };
        if (type == typeof(ParagraphProvider))
            return new ParagraphProvider { Parent = parent, Text = "New paragraph" };
        if (type == typeof(SubtitleProvider))
            return new SubtitleProvider { Parent = parent, Text = "New subtitle" };
        if (type == typeof(LinkProvider))
            return new LinkProvider { Parent = parent, Text = "VeloxDev", Url = "https://github.com/Axvser/VeloxDev" };
        if (type == typeof(CodeProvider))
            return new CodeProvider { Parent = parent, Language = "csharp", Code = "Console.WriteLine(\"Hello VeloxDev\");" };
        if (type == typeof(MarkdownProvider))
            return new MarkdownProvider { Parent = parent, Text = "# Markdown\n\nWrite **Markdown** here." };
        if (type == typeof(TableProvider))
            return TableProvider.CreateDefault(parent);
        if (type == typeof(ImageProvider))
            return new ImageProvider { Parent = parent };

        if (Activator.CreateInstance(type) is IWikiElement element)
        {
            element.Parent = parent;
            return element;
        }

        return new EmptyProvider { Parent = parent };
    }

    private static IReadOnlyList<Type> FindConvertibleTypes()
    {
        return typeof(IWikiElement).Assembly
            .GetTypes()
            .Where(t => typeof(IWikiElement).IsAssignableFrom(t))
            .Where(t => t is { IsAbstract: false, IsInterface: false })
            .Where(t => t != typeof(DocumentProvider))
            .Where(t => t != typeof(NodeProvider))
            .Where(t => t != typeof(EmptyProvider))
            .OrderBy(t => t.Name)
            .ToArray();
    }
}
