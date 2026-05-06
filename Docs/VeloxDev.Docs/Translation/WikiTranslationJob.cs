using System;
using System.Reflection;

namespace VeloxDev.Docs.Translation;

/// <summary>
/// Represents a single translatable string value that needs one LLM call.
/// The <see cref="Apply"/> callback writes the result back to wherever the
/// original value came from — a property, a collection slot, or anything else.
/// </summary>
public sealed class WikiTranslationJob
{
    private readonly Action<string> _apply;

    /// <summary>The element instance that owns this text (for display in progress reports).</summary>
    public object Element { get; }

    /// <summary>Short label for this slot, used in progress reports (e.g. "Text", "Cells[2]").</summary>
    public string PropertyName { get; }

    /// <summary>Hint passed to the LLM describing the role of this content.</summary>
    public string Hint { get; }

    /// <summary>The text value to be translated.</summary>
    public string OriginalText { get; }

    /// <summary>Creates a job backed by a reflected property setter.</summary>
    internal WikiTranslationJob(object element, PropertyInfo property, string hint)
        : this(element, property.Name, hint, (string?)property.GetValue(element) ?? string.Empty,
               v => property.SetValue(element, v))
    { }

    /// <summary>Creates a job with an arbitrary write-back delegate (e.g. a collection slot).</summary>
    internal WikiTranslationJob(object element, string propertyName, string hint, string originalText, Action<string> apply)
    {
        Element = element;
        PropertyName = propertyName;
        Hint = string.IsNullOrWhiteSpace(hint) ? propertyName : hint;
        OriginalText = originalText;
        _apply = apply;
    }

    /// <summary>Writes the translated string back. Must be called on the UI thread.</summary>
    public void Apply(string translated) => _apply(translated);
}
