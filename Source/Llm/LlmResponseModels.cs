namespace Translator.Llm;

/// <summary>Response shapes for the two supported APIs plus model discovery.</summary>
internal sealed class ChatCompletionResponse {
    public List<ChatChoice> Choices { get; init; } = [];
}

internal sealed class ChatChoice {
    public ChatMessage Message { get; init; } = new();
}

internal sealed class ChatMessage {
    public string Content { get; init; } = string.Empty;
}

internal sealed class ResponsesApiResponse {
    public List<ResponsesOutputItem> Output { get; init; } = [];
}

internal sealed class ResponsesOutputItem {
    public string Type { get; init; } = string.Empty;
    public List<ResponsesContentPart> Content { get; init; } = [];
}

internal sealed class ResponsesContentPart {
    public string Type { get; init; } = string.Empty;
    public string Text { get; init; } = string.Empty;
}

internal sealed class ModelListResponse {
    public List<ModelEntry> Data { get; init; } = [];
}

internal sealed class ModelEntry {
    public string Id { get; init; } = string.Empty;
}

internal sealed class TranslationBatchResponse {
    public List<TranslatedEntry> Translations { get; init; } = [];
}

internal sealed class TranslatedEntry {
    public string Id { get; init; } = string.Empty;
    public string Translation { get; init; } = string.Empty;
}
