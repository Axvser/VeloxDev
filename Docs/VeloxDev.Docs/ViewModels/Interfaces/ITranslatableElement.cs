using VeloxDev.Docs.Translation;

namespace VeloxDev.Docs.ViewModels;

/// <summary>
/// Opt-in marker interface for wiki elements that expose translatable content.
/// Elements implementing this interface are automatically picked up by
/// <see cref="WikiTranslationCollector"/> even without explicit reflection over all types.
/// The actual translatable properties are identified via <see cref="TranslateTargetAttribute"/>.
/// </summary>
public interface ITranslatableElement : IWikiElement
{
}
