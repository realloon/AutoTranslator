using Translator.Services;
using UnityEngine;
using Verse;

namespace Translator;

public sealed class TranslatorSettings : ModSettings {
    private const string DefaultApiUrl = "https://api.openai.com/v1/chat/completions";
    private const string DefaultModel = "gpt-5.2";
    private const int DefaultBatchSize = 80;
    private const int DefaultConcurrency = 2;
    private const int DefaultRetryCount = 1;
    public const int MinBatchSize = 10;
    public const int MaxBatchSize = 400;
    public const int MinConcurrency = 1;
    public const int MaxConcurrency = 8;
    public const int MinRetryCount = 0;
    public const int MaxRetryCount = 5;

    public string ApiUrl = DefaultApiUrl;
    public string ApiKey = string.Empty;
    public string Model = DefaultModel;
    public int BatchSize = DefaultBatchSize;
    public int Concurrency = DefaultConcurrency;
    public int RetryCount = DefaultRetryCount;
    public OutputLocationMode DefaultOutputLocationMode = OutputLocationMode.GeneratedMod;

    public void ResetToDefaults() {
        ApiUrl = DefaultApiUrl;
        ApiKey = string.Empty;
        Model = DefaultModel;
        BatchSize = DefaultBatchSize;
        Concurrency = DefaultConcurrency;
        RetryCount = DefaultRetryCount;
        DefaultOutputLocationMode = OutputLocationMode.GeneratedMod;
    }

    public override void ExposeData() {
        Scribe_Values.Look(ref ApiUrl, "apiUrl", DefaultApiUrl);
        Scribe_Values.Look(ref ApiKey, "apiKey", string.Empty);
        Scribe_Values.Look(ref Model, "model", DefaultModel);
        Scribe_Values.Look(ref BatchSize, "batchSize", DefaultBatchSize);
        Scribe_Values.Look(ref Concurrency, "concurrency", DefaultConcurrency);
        Scribe_Values.Look(ref RetryCount, "retryCount", DefaultRetryCount);
        Scribe_Values.Look(ref DefaultOutputLocationMode, "defaultOutputLocationMode");

        // Normalize once at the load/save boundary so downstream code can trust these values.
        BatchSize = Mathf.Clamp(BatchSize, MinBatchSize, MaxBatchSize);
        Concurrency = Mathf.Clamp(Concurrency, MinConcurrency, MaxConcurrency);
        RetryCount = Mathf.Clamp(RetryCount, MinRetryCount, MaxRetryCount);
    }
}