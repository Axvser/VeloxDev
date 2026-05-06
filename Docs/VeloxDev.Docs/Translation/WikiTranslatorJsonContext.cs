using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace VeloxDev.Docs.Translation;

internal sealed record ChatRequestMessage(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] string Content);

internal sealed record ChatRequest(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("messages")] IReadOnlyList<ChatRequestMessage> Messages);

internal sealed record ChatResponseChoice(
    [property: JsonPropertyName("message")] ChatResponseMessage? Message);

internal sealed record ChatResponseMessage(
    [property: JsonPropertyName("content")] string? Content);

internal sealed record ChatResponse(
    [property: JsonPropertyName("choices")] IReadOnlyList<ChatResponseChoice>? Choices);

[JsonSerializable(typeof(ChatRequest))]
[JsonSerializable(typeof(ChatResponse))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class WikiTranslatorJsonContext : JsonSerializerContext;
