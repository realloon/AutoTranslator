using JetBrains.Annotations;
using HarmonyLib;
using Verse;

namespace Translator;

[UsedImplicitly]
[StaticConstructorOnStartup]
public static class Translator {
    static Translator() {
        var harmony = new Harmony("Vortex.AutoTranslator");
        harmony.PatchAll();
    }
}