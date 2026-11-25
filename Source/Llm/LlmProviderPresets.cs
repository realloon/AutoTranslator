using System.Collections.Frozen;

namespace Translator.Llm;

public enum LlmApiProtocol {
    ChatCompletions = 0,
    Responses = 1
}

public sealed record LlmProviderPreset(
    string Id,
    string Label,
    string BaseUrl,
    LlmApiProtocol Protocol,
    bool DisableThinking = false);

public static class LlmProviderPresets {
    public const string CustomId = "custom";
    public const string CustomLabelKey = "Translator_ModSettingProviderCustom";

    public static LlmProviderPreset DeepSeek { get; } = new(
        "deepseek", "DeepSeek", "https://api.deepseek.com", LlmApiProtocol.Responses, DisableThinking: true);

    public static LlmProviderPreset Vercel { get; } = new(
        "vercel", "Vercel AI Gateway", "https://ai-gateway.vercel.sh/v1", LlmApiProtocol.Responses,
        DisableThinking: true);

    public static LlmProviderPreset OpenRouter { get; } = new(
        "openrouter", "OpenRouter", "https://openrouter.ai/api/v1", LlmApiProtocol.ChatCompletions);

    public static IReadOnlyList<LlmProviderPreset> All { get; } = [DeepSeek, Vercel, OpenRouter];

    private static readonly FrozenDictionary<string, LlmProviderPreset> ById =
        All.ToFrozenDictionary(preset => preset.Id, StringComparer.Ordinal);

    public static LlmProviderPreset? Find(string providerId) => ById.GetValueOrDefault(providerId);
}