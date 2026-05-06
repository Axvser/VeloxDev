using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using VeloxDev.Docs.ViewModels;

namespace VeloxDev.Docs.Translation;

/// <summary>
/// Walks the document tree and harvests <see cref="WikiTranslationJob"/> instances for every
/// property marked with <see cref="TranslateTargetAttribute"/>.
/// The collector is stateless �?call <see cref="Collect(DocumentProvider)"/> once per translation run.
/// </summary>
public static class WikiTranslationCollector
{
    /// <summary>
    /// Traverses the full document tree rooted at <paramref name="document"/> and
    /// returns one <see cref="WikiTranslationJob"/> per translatable property found.
    /// The jobs are ordered depth-first: pages in tree order, elements within each page top-to-bottom.
    /// </summary>
    public static IReadOnlyList<WikiTranslationJob> Collect(DocumentProvider document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var jobs = new List<WikiTranslationJob>();
        foreach (var node in document.Nodes.OfType<NodeProvider>())
            CollectNode(node, jobs);
        return jobs;
    }

    /// <summary>
    /// Collects jobs from a single element (e.g. one paragraph, heading, or table).
    /// </summary>
    public static IReadOnlyList<WikiTranslationJob> Collect(IWikiElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        var jobs = new List<WikiTranslationJob>();
        CollectElement(element, jobs);
        return jobs;
    }

    /// <summary>
    /// Collects jobs from a single page (node) and all its child pages recursively.
    /// </summary>
    public static IReadOnlyList<WikiTranslationJob> Collect(NodeProvider node)
    {
        ArgumentNullException.ThrowIfNull(node);

        var jobs = new List<WikiTranslationJob>();
        CollectNode(node, jobs);
        return jobs;
    }

    private static void CollectNode(NodeProvider node, List<WikiTranslationJob> jobs)
    {
        // Translate the page title
        CollectElement(node, jobs);

        // Translate every content element on this page
        foreach (var element in node.Children)
            CollectElement(element, jobs);

        // Recurse into child pages
        foreach (var child in node.Nodes.OfType<NodeProvider>())
            CollectNode(child, jobs);
    }

    private static void CollectElement(object element, List<WikiTranslationJob> jobs)
    {
        // ── Standard [TranslateTarget] properties ───────────────────────────
        var type = element.GetType();
        foreach (var property in GetTranslatableProperties(type))
        {
            var attr = property.GetCustomAttribute<TranslateTargetAttribute>()!;
            var value = (string?)property.GetValue(element) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(value))
                continue;

            jobs.Add(new WikiTranslationJob(element, property, attr.Hint));
        }

        // ── Table: headers as one job, each row's cells as one job ────────
        if (element is TableProvider table)
        {
            CollectStringCollectionAsBatch(table, table.Headers, "table column headers �?a JSON array of header strings", "Headers", jobs);
            foreach (var row in table.Rows)
                CollectStringCollectionAsBatch(row, row.Cells, "table row cells �?a JSON array of cell strings in the same column order as the headers", "Cells", jobs);
        }
    }

    /// <summary>
    /// Packs all non-empty slots of <paramref name="collection"/> into a single JSON-array job.
    /// The LLM receives <c>["val1","val2",...]</c> and must return a JSON array of the same length.
    /// Empty slots are included as empty strings so indices stay aligned, then written back on apply.
    /// </summary>
    private static void CollectStringCollectionAsBatch(
        object owner,
        ObservableCollection<string> collection,
        string hint,
        string propertyName,
        List<WikiTranslationJob> jobs)
    {
        // Skip tables that are entirely empty
        if (collection.Count == 0 || collection.All(string.IsNullOrWhiteSpace))
            return;

        // Serialise the whole row/header as a JSON array (empty cells become "")
        var originalJson = JsonSerializer.Serialize(collection.ToArray());

        jobs.Add(new WikiTranslationJob(
            owner,
            propertyName,
            hint,
            originalJson,
            translatedJson =>
            {
                string[]? values = null;
                try { values = JsonSerializer.Deserialize<string[]>(translatedJson); }
                catch { /* fall back to no-op if LLM returns malformed JSON */ }

                if (values is null || values.Length != collection.Count)
                    return;

                for (int i = 0; i < values.Length; i++)
                    collection[i] = values[i];
            }));
    }

    // Cache reflection results per type to avoid repeated scanning
    private static readonly Dictionary<Type, PropertyInfo[]> _propertyCache = [];

    private static PropertyInfo[] GetTranslatableProperties(Type type)
    {
        if (_propertyCache.TryGetValue(type, out var cached))
            return cached;

        var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead
                     && p.CanWrite
                     && p.PropertyType == typeof(string)
                     && p.GetCustomAttribute<TranslateTargetAttribute>() is not null)
            .ToArray();

        _propertyCache[type] = props;
        return props;
    }
}
