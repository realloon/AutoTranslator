using Verse;

namespace Translator;

public sealed class TranslatorSettings : ModSettings {
    private const string DefaultApiUrl = "https://api.openai.com/v1/chat/completions";
    private const string DefaultModel = "gpt-5.2";
    private const int DefaultBatchSize = 200;
    private const int DefaultConcurrency = 4;
    private const int DefaultRetryCount = 3;
    public const int MinBatchSize = 20;
    public const int MaxBatchSize = 2000;
    public const int MinConcurrency = 1;
    public const int MaxConcurrency = 16;
    public const int MinRetryCount = 0;
    public const int MaxRetryCount = 10;

    public string ApiUrl = DefaultApiUrl;
    public string ApiKey = string.Empty;
    public string Model = DefaultModel;
    public int BatchSize = DefaultBatchSize;
    public int Concurrency = DefaultConcurrency;
    public int RetryCount = DefaultRetryCount;

    public void ResetToDefaults() {
        ApiUrl = DefaultApiUrl;
        ApiKey = string.Empty;
        Model = DefaultModel;
        BatchSize = DefaultBatchSize;
        Concurrency = DefaultConcurrency;
        RetryCount = DefaultRetryCount;
    }

    public override void ExposeData() {
        Scribe_Values.Look(ref ApiUrl, "apiUrl", DefaultApiUrl);
        Scribe_Values.Look(ref ApiKey, "apiKey", string.Empty);
        Scribe_Values.Look(ref Model, "model", DefaultModel);
        Scribe_Values.Look(ref BatchSize, "batchSize", DefaultBatchSize);
        Scribe_Values.Look(ref Concurrency, "concurrency", DefaultConcurrency);
        Scribe_Values.Look(ref RetryCount, "retryCount", DefaultRetryCount);
    }
}
