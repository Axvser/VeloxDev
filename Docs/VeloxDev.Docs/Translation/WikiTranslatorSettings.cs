using System;
using System.IO;

namespace VeloxDev.Docs.Translation;

/// <summary>
/// Holds the DashScope API key used by <see cref="WikiTranslator"/>.
/// The key is read from the <c>DASHSCOPE_API_KEY</c> environment variable on startup
/// and can be overridden at runtime via <see cref="ApiKey"/> (e.g. from a settings dialog).
/// The runtime override is persisted per-user in a plain-text file so it survives restarts
/// without requiring the user to set a system environment variable.
/// </summary>
public static class WikiTranslatorSettings
{
    private const string EnvironmentVariableName = "DASHSCOPE_API_KEY";
    private const string DefaultEndpoint = "https://dashscope.aliyuncs.com/compatible-mode/v1";
    private const string DefaultModel = "qwen-plus";

    // Per-user key file: %APPDATA%\VeloxDev\Docs\dashscope.key  (or ~/.config/... on Linux/macOS)
    private static readonly string _keyFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "VeloxDev", "Docs", "dashscope.key");

    private static string _apiKey = string.Empty;

    /// <summary>Raised when <see cref="ApiKey"/> changes.</summary>
    public static event Action? ApiKeyChanged;

    static WikiTranslatorSettings()
    {
        // Priority: runtime file > environment variable
        _apiKey = LoadFromFile() ?? Environment.GetEnvironmentVariable(EnvironmentVariableName) ?? string.Empty;
    }

    /// <summary>
    /// The DashScope API key. Setting this persists the value to the user profile so it
    /// survives process restarts without requiring a system environment variable.
    /// </summary>
    public static string ApiKey
    {
        get => _apiKey;
        set
        {
            if (string.Equals(_apiKey, value, StringComparison.Ordinal))
                return;
            _apiKey = value ?? string.Empty;
            SaveToFile(_apiKey);
            ApiKeyChanged?.Invoke();
        }
    }

    /// <summary>The LLM model name used for translation. Defaults to <c>qwen-plus</c>.</summary>
    public static string Model { get; set; } = DefaultModel;

    /// <summary>The API endpoint URL. Defaults to the DashScope compatible-mode endpoint.</summary>
    public static string Endpoint { get; set; } = DefaultEndpoint;

    /// <summary>
    /// Controls what scope is translated and whether the document language is updated
    /// when the user clicks the Translate button. Defaults to <see cref="WikiTranslationMode.CurrentPage"/>.
    /// </summary>
    public static WikiTranslationMode TranslationMode { get; set; } = WikiTranslationMode.CurrentPage;

    /// <summary>
    /// Creates a configured <see cref="WikiTranslator"/> using the current settings.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when no API key is configured.</exception>
    public static WikiTranslator CreateTranslator()
        => WikiTranslator.Create(
            apiKey: string.IsNullOrWhiteSpace(ApiKey) ? null : ApiKey,
            model: string.IsNullOrWhiteSpace(Model) ? null : Model,
            endpoint: string.IsNullOrWhiteSpace(Endpoint) ? null : Endpoint);

    // ── Persistence ──────────────────────────────────────────────────────────

    private static string? LoadFromFile()
    {
        try
        {
            if (File.Exists(_keyFilePath))
            {
                var key = File.ReadAllText(_keyFilePath).Trim();
                return string.IsNullOrEmpty(key) ? null : key;
            }
        }
        catch
        {
            // Non-critical: fall back to environment variable silently
        }
        return null;
    }

    private static void SaveToFile(string key)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_keyFilePath)!);
            File.WriteAllText(_keyFilePath, key);
        }
        catch
        {
            // Non-critical: saving is best-effort
        }
    }
}
