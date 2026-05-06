using Microsoft.Extensions.AI;
using OpenAI;
using System;
using System.ClientModel;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VeloxDev.Docs.ViewModels;

namespace VeloxDev.Docs.Translation;

/// <summary>
/// Translates an entire wiki document or a single page element-by-element using an LLM.
/// <para>
/// Each translatable property (marked with <see cref="TranslateTargetAttribute"/>) becomes one
/// stateless LLM call �?no conversation history is preserved between jobs.
/// This keeps context clean and avoids session-length limits.
/// </para>
/// <para>
/// To create an instance use <see cref="WikiTranslator.Create"/> which reads the API key
/// from the <c>DASHSCOPE_API_KEY</c> environment variable.  You can also pass any
/// <see cref="IChatClient"/> directly via <see cref="WikiTranslator(IChatClient)"/>.
/// </para>
/// </summary>
public sealed class WikiTranslator
{
    // ── Default provider (DashScope / Qwen) ─────────────────────────────────
    // Set the API key in your environment before running:
    //   Windows PowerShell:  $env:DASHSCOPE_API_KEY = "sk-xxxx"
    //   macOS / Linux:       export DASHSCOPE_API_KEY="sk-xxxx"
    private const string EnvironmentVariableName = "DASHSCOPE_API_KEY";
    private const string DefaultEndpoint = "https://dashscope.aliyuncs.com/compatible-mode/v1";
    private const string DefaultModel = "qwen-plus";

    private readonly IChatClient _chatClient;

    /// <summary>Initialise with an arbitrary <see cref="IChatClient"/>.</summary>
    public WikiTranslator(IChatClient chatClient)
    {
        ArgumentNullException.ThrowIfNull(chatClient);
        _chatClient = chatClient;
    }

    /// <summary>
    /// Creates a <see cref="WikiTranslator"/> backed by the DashScope/Qwen endpoint.
    /// When <paramref name="apiKey"/> is null or empty, reads <c>DASHSCOPE_API_KEY</c> from the environment.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when no API key is available.</exception>
    public static WikiTranslator Create(string? apiKey = null, string? model = null, string? endpoint = null)
    {
        var key = string.IsNullOrWhiteSpace(apiKey)
            ? Environment.GetEnvironmentVariable(EnvironmentVariableName)
            : apiKey;

        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException(
                $"No API key configured. Set the '{EnvironmentVariableName}' environment variable " +
                "or enter a key in the translation settings panel.");

        var chatClient = new OpenAIClient(
            new ApiKeyCredential(key),
            new OpenAIClientOptions { Endpoint = new Uri(endpoint ?? DefaultEndpoint) })
            .GetChatClient(string.IsNullOrWhiteSpace(model) ? DefaultModel : model)
            .AsIChatClient();

        return new WikiTranslator(chatClient);
    }

    // ── Translation entry-points ────────────────────────────────────────────

    /// <summary>
    /// Translates the full document tree. Progress is reported for each completed job.
    /// </summary>
    /// <param name="document">Root document to translate.</param>
    /// <param name="targetLanguage">IETF language tag, e.g. "zh", "ja", "fr".</param>
    /// <param name="progress">Optional progress callback �?receives (completedCount, totalCount, currentJob).</param>
    /// <param name="cancellationToken">Allows cancellation mid-run.</param>
    public async Task TranslateDocumentAsync(
        DocumentProvider document,
        string targetLanguage,
        IProgress<WikiTranslationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var jobs = WikiTranslationCollector.Collect(document);
        await RunJobsAsync(jobs, targetLanguage, progress, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Translates a single page (and its child pages recursively).
    /// </summary>
    public async Task TranslateNodeAsync(
        NodeProvider node,
        string targetLanguage,
        IProgress<WikiTranslationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var jobs = WikiTranslationCollector.Collect(node);
        await RunJobsAsync(jobs, targetLanguage, progress, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Translates a single wiki element in-place and returns the number of properties translated.
    /// Useful for translating one element at a time from the UI.
    /// </summary>
    public async Task<int> TranslateElementAsync(
        IWikiElement element,
        string targetLanguage,
        CancellationToken cancellationToken = default)
    {
        var jobs = WikiTranslationCollector.Collect(element);
        int translated = 0;
        foreach (var job in jobs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await TranslateJobAsync(job, targetLanguage, cancellationToken).ConfigureAwait(true);
            job.Apply(result);
            translated++;
        }
        return translated;
    }

    // ── Core loop ───────────────────────────────────────────────────────────

    private async Task RunJobsAsync(
        IReadOnlyList<WikiTranslationJob> jobs,
        string targetLanguage,
        IProgress<WikiTranslationProgress>? progress,
        CancellationToken cancellationToken)
    {
        for (int i = 0; i < jobs.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var job = jobs[i];
            // ConfigureAwait(true): HTTP work runs off-thread inside TranslateJobAsync,
            // but the continuation (Apply) is marshalled back to the calling sync context
            // (UI thread) so that property setters can safely mutate Avalonia objects.
            var translated = await TranslateJobAsync(job, targetLanguage, cancellationToken).ConfigureAwait(true);
            job.Apply(translated);

            progress?.Report(new WikiTranslationProgress(i + 1, jobs.Count, job));
        }
    }

    /// <summary>
    /// Issues a single, stateless LLM call to translate one property value.
    /// No conversation history is carried over from previous calls.
    /// </summary>
    private async Task<string> TranslateJobAsync(
        WikiTranslationJob job,
        string targetLanguage,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(job.OriginalText))
            return job.OriginalText;

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, BuildSystemPrompt(targetLanguage, job.Hint)),
            new(ChatRole.User, job.OriginalText)
        };

        var response = await _chatClient.GetResponseAsync(messages, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return response.Text?.Trim() ?? job.OriginalText;
    }

    private static string BuildSystemPrompt(string targetLanguage, string hint)
    {
        var isTable = hint.Contains("headers") && hint.Contains("rows");

        // Table addendum is appended only when the payload is a table JSON object
        var tableRules = isTable ? """

            TABLE-SPECIFIC RULES (only when input is a JSON object with "headers" and "rows" keys):
            - Input shape: {"headers": [...], "rows": [[...], ...]}. Return EXACTLY the same shape and lengths — no extra keys, no wrapper, no markdown fences.
            - Classify every string value with the THREE CATEGORIES below and act accordingly.
            - Mixed values (prose + identifiers in same cell): translate only the prose; keep CATEGORY 2/3 tokens verbatim in place.
            - Empty strings must remain empty strings.
            """ : string.Empty;

        return $"""
            You are a professional technical documentation translator.
            Target language: {targetLanguage}
            Content hint: {hint}

            Classify every piece of text using the THREE CATEGORIES below, then act accordingly.

            CATEGORY 1 — DESCRIPTIVE TEXT
              What it is: Human-readable prose intended for end users: descriptions, explanations,
                          UI labels in natural language, notes, remarks.
              Action    : TRANSLATE into {targetLanguage}.
                          Inline programming identifiers inside the prose (type/method/property names)
                          must be kept verbatim; only the surrounding natural language is translated.

            CATEGORY 2 — TECHNICAL / PARAMETER TEXT
              What it is: Any formal identifier in a programming or technical context:
                          API/type/method/property/field names, namespaces, enum values, config keys,
                          CLI flags, file paths, URLs, version strings, code snippets, or any token
                          written in camelCase, PascalCase, snake_case, kebab-case, SCREAMING_SNAKE,
                          dotted.notation, or prefixed with -- / - / /.
              Action    : Copy EXACTLY as-is. Do NOT translate, paraphrase, reformat, or modify.

            CATEGORY 3 — NON-LINGUISTIC SYMBOLS
              What it is: Unicode emoji and symbolic characters with visual/iconic meaning, not
                          linguistic meaning. Includes all emoji, dingbats, checkmarks, crosses,
                          arrows, bullets, stars, etc. (e.g. ✅ ❌ ✔ ✗ ➡   ⚠ → •).
              Action    : Copy EXACTLY as-is. Do NOT translate, replace, remove, or alter.

            Additional rules:
            - Preserve all Markdown syntax, code fences, HTML tags, and special characters exactly.
            - Inside code blocks: translate ONLY inline comments (// ..., /* ... */, # ..., <!-- ... -->).
              All other code tokens are CATEGORY 2.
            - Do NOT add explanations, notes, or any surrounding text.
            - Return ONLY the translated result, nothing else.
            {tableRules}
            """;
    }
}