using Translator.Export;
using Verse;

namespace Translator.UI;

internal static class OutputModeText {
    public static string Of(OutputLocationMode mode) => mode == OutputLocationMode.OriginalMod
        ? "Translator_OriginalMod".Translate()
        : "Translator_GeneratedMod".Translate();
}