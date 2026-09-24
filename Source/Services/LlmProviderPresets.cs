namespace Translator.Services;

public enum LlmApiProtocol {
    ChatCompletions = 0,
    Responses = 1
}

internal sealed class LlmProviderPreset {
    public string Id = string.Empty;
    public string Label = string.Empty;
    public string BaseUrl = string.Empty;
    public LlmApiProtocol Protocol = LlmApiProtocol.ChatCompletions;
    public bool DisableThinking;
}

internal static class LlmProviderPresets {
    public const string CustomId = "custom";
    public const string CustomLabelKey = "Translator_ModSettingProviderCustom";

    public static readonly LlmProviderPreset DeepSeek = new() {
        Id = "deepseek",
        Label = "DeepSeek",
        BaseUrl = "https://api.deepseek.com",
        Protocol = LlmApiProtocol.Responses,
        DisableThinking = true
    };

    public static readonly LlmProviderPreset Vercel = new() {
        Id = "vercel",
        Label = "Vercel AI Gateway",
        BaseUrl = "https://ai-gateway.vercel.sh/v1",
        Protocol = LlmApiProtocol.Responses,
        DisableThinking = true
    };

    public static readonly LlmProviderPreset OpenRouter = new() {
        Id = "openrouter",
        Label = "OpenRouter",
        BaseUrl = "https://openrouter.ai/api/v1",
        Protocol = LlmApiProtocol.ChatCompletions
    };

    public static readonly IReadOnlyList<LlmProviderPreset> All = [DeepSeek, Vercel, OpenRouter];

    public static LlmProviderPreset? Find(string providerId) {
        return All.FirstOrDefault(preset => preset.Id == providerId);
    }
}
