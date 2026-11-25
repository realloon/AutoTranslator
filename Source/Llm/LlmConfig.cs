namespace Translator.Llm;

internal sealed record LlmSessionConfig(
    LlmApiProtocol Protocol,
    bool DisableThinking,
    string ApiKey,
    string Model,
    LlmEndpoints Endpoints,
    int BatchSize,
    int Concurrency,
    int RetryCount);

internal static class LlmConfigResolver {
    public static bool TryCreate(
        TranslatorSettings settings,
        out LlmSessionConfig config,
        out string error,
        bool requireModel = true) {
        config = null!;
        error = string.Empty;

        var preset = LlmProviderPresets.Find(settings.ProviderId);
        var protocol = preset?.Protocol ?? settings.CustomProtocol;
        var apiKey = settings.GetApiKey(settings.ProviderId).Trim();
        var model = settings.GetModel(settings.ProviderId).Trim();

        if (!LlmEndpointResolver.TryResolve(preset, settings.ApiUrl, out var endpoints, out error)) {
            return false;
        }

        if (apiKey.Length == 0) {
            error = "Translator API key is empty. Configure it in Mod Settings.";
            return false;
        }

        if (requireModel && model.Length == 0) {
            error = "Translator model is empty. Configure it in Mod Settings.";
            return false;
        }

        config = new(
            protocol,
            preset?.DisableThinking ?? false,
            apiKey,
            model,
            endpoints,
            settings.BatchSize,
            settings.Concurrency,
            settings.RetryCount);
        return true;
    }
}
