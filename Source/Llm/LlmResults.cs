namespace Translator.Llm;

internal sealed record LlmConfigValidationResult(bool Success, string Message);

internal sealed record LlmModelListResult(bool Success, string Message, IReadOnlyList<string> Models) {
    public static LlmModelListResult Failure(string message) => new(false, message, []);
}

internal sealed record BatchTranslationResult(
    bool Success,
    int BatchNumber,
    IReadOnlyDictionary<string, string> Translations,
    string ErrorMessage) {
    public static BatchTranslationResult Ok(int batchNumber, IReadOnlyDictionary<string, string> translations) =>
        new(true, batchNumber, translations, string.Empty);

    public static BatchTranslationResult Failed(int batchNumber, string errorMessage) =>
        new(false, batchNumber, new Dictionary<string, string>(StringComparer.Ordinal), errorMessage);
}

internal sealed record TranslationOutcome(bool Success, string Message);