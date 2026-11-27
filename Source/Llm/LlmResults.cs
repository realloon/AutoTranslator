namespace Translator.Llm;

internal sealed record LlmError(string ReasonKey, string Details);

internal sealed record LlmConfigValidationResult(bool Success, LlmError? Error = null);

internal sealed record LlmModelListResult(bool Success, LlmError? Error, IReadOnlyList<string> Models) {
    public static LlmModelListResult Failure(LlmError error) => new(false, error, []);
}

internal sealed record BatchTranslationResult(
    bool Success,
    int BatchNumber,
    IReadOnlyDictionary<string, string> Translations,
    LlmError? Error) {
    public static BatchTranslationResult Ok(int batchNumber, IReadOnlyDictionary<string, string> translations) =>
        new(true, batchNumber, translations, null);

    public static BatchTranslationResult Failed(int batchNumber, LlmError error) =>
        new(false, batchNumber, new Dictionary<string, string>(StringComparer.Ordinal), error);
}

internal sealed record BatchFailure(int BatchNumber, LlmError Error);

internal sealed record TranslationOutcome(bool Success, string Message, IReadOnlyList<BatchFailure> Failures);
