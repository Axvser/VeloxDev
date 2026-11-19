using System.Collections.Generic;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.ViewHosting;

/// <summary>Resolves a MAUI view for one workflow item and caches the selector's answer by runtime type.</summary>
/// <remarks>
/// <para>
/// The pool asks this factory repeatedly while a visible set materializes, so a selector failure is
/// cached: a selector that throws or returns <see langword="null"/> marks that runtime type unsupported
/// for the lifetime of this factory, and later items of the same type are skipped without asking again.
/// </para>
/// <para>
/// Selector failures never escape. A failure inside <see cref="DataTemplate.CreateContent"/> is left to
/// the pool's per-item failure isolation, because it describes one item instead of the whole type.
/// </para>
/// </remarks>
public sealed class ViewFactory : IViewFactory<IWorkflowViewModel, View>
{
    private readonly Layout _host;
    private readonly DataTemplateSelector? _selector;
    private readonly Dictionary<Type, DataTemplate> _templates = [];
    private readonly HashSet<Type> _unsupportedTypes = [];

    /// <summary>Creates a factory that resolves templates from <paramref name="selector"/> and caches them by runtime type.</summary>
    /// <param name="host">The layout passed to the selector as the container.</param>
    /// <param name="selector">The selector consulted for every new item type, or <see langword="null"/> to materialize nothing.</param>
    /// <exception cref="ArgumentNullException"><paramref name="host"/> is <see langword="null"/>.</exception>
    public ViewFactory(Layout host, DataTemplateSelector? selector)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _selector = selector;
    }

    /// <inheritdoc />
    public bool WantsItem(IWorkflowViewModel item) => true;

    /// <inheritdoc />
    public View? CreateView(IWorkflowViewModel item)
    {
        var itemType = item.GetType();
        if (_unsupportedTypes.Contains(itemType))
        {
            return null;
        }

        if (!_templates.TryGetValue(itemType, out var template))
        {
            template = SelectTemplate(item);
            if (template is null)
            {
                _unsupportedTypes.Add(itemType);
                return null;
            }

            _templates[itemType] = template;
        }

        // 模板产出非 View 是模板本身写错：抛给池的单项失败隔离，不把整个类型永久判死。
        return template.CreateContent() as View
            ?? throw new InvalidOperationException(
                $"DataTemplate returned no View for {item.GetType().FullName}.");
    }

    private DataTemplate? SelectTemplate(IWorkflowViewModel item)
    {
        if (_selector is null)
        {
            return null;
        }

        try
        {
            return _selector.SelectTemplate(item, _host);
        }
        catch (Exception)
        {
            // 选择器不认识这一类（典型：只声明 NodeTemplate 的选择器遇到插槽/树节点）：按类型跳过。
            return null;
        }
    }
}
