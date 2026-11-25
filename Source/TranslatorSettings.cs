using Translator.Export;
using Translator.Llm;
using Verse;

namespace Translator;

public sealed class TranslatorSettings : ModSettings {
    private const int DefaultBatchSize = 80;
    private const int DefaultConcurrency = 2;
    private const int DefaultRetryCount = 1;
    private Dictionary<string, string> _apiKeys = [];
    private Dictionary<string, string> _models = [];

    public const int MinBatchSize = 10;
    public const int MaxBatchSize = 400;
    public const int MinConcurrency = 1;
    public const int MaxConcurrency = 8;
    public const int MinRetryCount = 0;
    public const int MaxRetryCount = 5;
    public string ProviderId = LlmProviderPresets.DeepSeek.Id;
    public LlmApiProtocol CustomProtocol = LlmApiProtocol.ChatCompletions;
    public string ApiUrl = string.Empty;
    public int BatchSize = DefaultBatchSize;
    public int Concurrency = DefaultConcurrency;
    public int RetryCount = DefaultRetryCount;
    public OutputLocationMode DefaultOutputLocationMode = OutputLocationMode.GeneratedMod;

    public string GetApiKey(string providerId) => _apiKeys.GetValueOrDefault(providerId, string.Empty);
    public void SetApiKey(string providerId, string key) => _apiKeys[providerId] = key;
    public string GetModel(string providerId) => _models.GetValueOrDefault(providerId, string.Empty);
    public void SetModel(string providerId, string model) => _models[providerId] = model;

    public void ResetToDefaults() {
        ProviderId = LlmProviderPresets.DeepSeek.Id;
        CustomProtocol = LlmApiProtocol.ChatCompletions;
        ApiUrl = string.Empty;
        _apiKeys.Clear();
        _models.Clear();
        BatchSize = DefaultBatchSize;
        Concurrency = DefaultConcurrency;
        RetryCount = DefaultRetryCount;
        DefaultOutputLocationMode = OutputLocationMode.GeneratedMod;
    }

    public override void ExposeData() {
        Scribe_Values.Look(ref ProviderId, "providerId", LlmProviderPresets.DeepSeek.Id);
        Scribe_Values.Look(ref CustomProtocol, "customProtocol");
        Scribe_Values.Look(ref ApiUrl, "apiUrl", string.Empty);
        Scribe_Collections.Look(ref _apiKeys, "apiKeys", LookMode.Value, LookMode.Value);
        Scribe_Collections.Look(ref _models, "models", LookMode.Value, LookMode.Value);
        Scribe_Values.Look(ref BatchSize, "batchSize", DefaultBatchSize);
        Scribe_Values.Look(ref Concurrency, "concurrency", DefaultConcurrency);
        Scribe_Values.Look(ref RetryCount, "retryCount", DefaultRetryCount);
        Scribe_Values.Look(ref DefaultOutputLocationMode, "defaultOutputLocationMode");

        // Normalize once at the load/save boundary so downstream code can trust these values.
        BatchSize = Math.Clamp(BatchSize, MinBatchSize, MaxBatchSize);
        Concurrency = Math.Clamp(Concurrency, MinConcurrency, MaxConcurrency);
        RetryCount = Math.Clamp(RetryCount, MinRetryCount, MaxRetryCount);
    }
}