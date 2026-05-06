using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;
using VeloxDev.Docs.Translation;

namespace VeloxDev.Docs.ViewModels;

/// <summary>
/// ViewModel for the translation settings panel.
/// Binds to <see cref="WikiTranslatorSettings"/> as the single source of truth.
/// </summary>
public sealed partial class TranslatorSettingsViewModel : ObservableObject
{
    [ObservableProperty]
    private string _apiKey = WikiTranslatorSettings.ApiKey;

    [ObservableProperty]
    private string _model = WikiTranslatorSettings.Model;

    [ObservableProperty]
    private string _endpoint = WikiTranslatorSettings.Endpoint;

    [ObservableProperty]
    private WikiTranslationModeOption _selectedMode;

    /// <summary>All available translation modes exposed to the UI.</summary>
    public IReadOnlyList<WikiTranslationModeOption> TranslationModes { get; } =
    [
        new(WikiTranslationMode.CurrentPage,                "Current Page",                       "仅翻译当前页，不修改文档语言"),
        new(WikiTranslationMode.FullDocument,               "Full Document",                      "翻译全部页面，不修改文档语言"),
        new(WikiTranslationMode.FullDocumentAndUpdateLanguage, "Full Document + Update Language", "翻译全部页面，并将文档语言切换为目标语言"),
    ];

    public TranslatorSettingsViewModel()
    {
        _selectedMode = TranslationModes
            .FirstOrMatchingMode(WikiTranslatorSettings.TranslationMode)
            ?? TranslationModes[0];
    }

    /// <summary>Persists all settings fields to <see cref="WikiTranslatorSettings"/>.</summary>
    public void Apply()
    {
        WikiTranslatorSettings.ApiKey = ApiKey?.Trim() ?? string.Empty;
        WikiTranslatorSettings.Model = string.IsNullOrWhiteSpace(Model)
            ? "qwen-plus"
            : Model.Trim();
        WikiTranslatorSettings.Endpoint = string.IsNullOrWhiteSpace(Endpoint)
            ? "https://dashscope.aliyuncs.com/compatible-mode/v1"
            : Endpoint.Trim();
        WikiTranslatorSettings.TranslationMode = SelectedMode?.Mode ?? WikiTranslationMode.CurrentPage;
    }

    /// <summary>Resets fields to what <see cref="WikiTranslatorSettings"/> currently holds.</summary>
    public void Reload()
    {
        ApiKey = WikiTranslatorSettings.ApiKey;
        Model = WikiTranslatorSettings.Model;
        Endpoint = WikiTranslatorSettings.Endpoint;
        SelectedMode = TranslationModes
            .FirstOrMatchingMode(WikiTranslatorSettings.TranslationMode)
            ?? TranslationModes[0];
    }
}

/// <summary>Wraps a <see cref="WikiTranslationMode"/> with display text for ComboBox binding.</summary>
public sealed record WikiTranslationModeOption(WikiTranslationMode Mode, string DisplayName, string Description);

file static class TranslationModeOptionExtensions
{
    internal static WikiTranslationModeOption? FirstOrMatchingMode(
        this IReadOnlyList<WikiTranslationModeOption> list, WikiTranslationMode mode)
    {
        foreach (var item in list)
            if (item.Mode == mode) return item;
        return null;
    }
}
