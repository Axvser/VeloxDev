using System;

namespace VeloxDev.Docs.Translation;

/// <summary>
/// Marks a string property as translatable content.
/// The <see cref="WikiTranslationCollector"/> reflects over every <see cref="IWikiElement"/>
/// and harvests all properties carrying this attribute into <see cref="WikiTranslationJob"/> instances.
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
public sealed class TranslateTargetAttribute : Attribute
{
    /// <summary>
    /// Optional hint passed to the LLM describing what this field represents
    /// (e.g. "heading text", "body paragraph", "Markdown document body").
    /// When empty the collector uses the property name instead.
    /// </summary>
    public string Hint { get; }

    public TranslateTargetAttribute(string hint = "")
    {
        Hint = hint;
    }
}
