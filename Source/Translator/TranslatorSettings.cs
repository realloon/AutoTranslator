using Verse;

namespace Translator;

public sealed class TranslatorSettings : ModSettings {
    private const string DefaultApiUrl = "https://api.openai.com/v1/chat/completions";
    private const string DefaultModel = "gpt-5.2";

    public string ApiUrl = DefaultApiUrl;
    public string ApiKey = string.Empty;
    public string Model = DefaultModel;

    public void ResetToDefaults() {
        ApiUrl = DefaultApiUrl;
        ApiKey = string.Empty;
        Model = DefaultModel;
    }

    public override void ExposeData() {
        Scribe_Values.Look(ref ApiUrl, "apiUrl", DefaultApiUrl);
        Scribe_Values.Look(ref ApiKey, "apiKey", string.Empty);
        Scribe_Values.Look(ref Model, "model", DefaultModel);
    }
}