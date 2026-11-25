using Translator.Export;
using Verse;

namespace Translator.UI;

internal static class OutputModeText {
    public static string Of(OutputLocationMode mode) => mode == OutputLocationMode.OriginalMod
        ? "Translator_OutputModeOriginalMod".Translate()
        : "Translator_OutputModeGeneratedMod".Translate();
}