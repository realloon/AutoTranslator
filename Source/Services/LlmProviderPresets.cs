namespace Translator.Services;

public enum LlmApiProtocol {
    ChatCompletions = 0,
    Responses = 1
}

internal sealed class LlmProviderPreset {
    public string Id = string.Empty;
    public string LabelKey = string.Empty;
    public string BaseUrl = string.Empty;
    public LlmApiProtocol Protocol = LlmApiProtocol.ChatCompletions;
    public bool DisableThinking;
}

internal static class LlmProviderPresets {
    public const string CustomId = "custom";
    public const string CustomLabelKey = "Translator_ModSettingProviderCustom";

    public static readonly LlmProviderPreset DeepSeek = new() {
        Id = "deepseek",
        LabelKey = "Translator_ModSettingProviderDeepSeek",
        BaseUrl = "https://api.deepseek.com",
        Protocol = LlmApiProtocol.Responses,
        DisableThinking = true
    };

    public static readonly IReadOnlyList<LlmProviderPreset> All = [DeepSeek];

    public static LlmProviderPreset? Find(string providerId) {
        return All.FirstOrDefault(preset => preset.Id == providerId);
    }
}
