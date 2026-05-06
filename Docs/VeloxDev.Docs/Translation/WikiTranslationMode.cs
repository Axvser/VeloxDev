namespace VeloxDev.Docs.Translation;

/// <summary>
/// Controls what scope is translated when the user clicks the Translate button
/// and whether the document language metadata is updated after translation.
/// </summary>
public enum WikiTranslationMode
{
    /// <summary>Translate only the currently selected page. Document language is not changed.</summary>
    CurrentPage,

    /// <summary>Translate all pages in the document. Document language is not changed.</summary>
    FullDocument,

    /// <summary>
    /// Translate all pages in the document AND update the document's <c>Language</c> property
    /// to the target language so UI labels switch to the translated locale.
    /// </summary>
    FullDocumentAndUpdateLanguage,
}
