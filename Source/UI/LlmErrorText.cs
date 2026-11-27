using Translator.Llm;
using Verse;

namespace Translator.UI;

internal static class LlmErrorText {
    public static string Reason(LlmError error) => error.ReasonKey.Translate();

    public static string Format(LlmError error) =>
        error.ReasonKey.Length == 0 ? error.Details : $"{Reason(error)}\n{error.Details}";
}
