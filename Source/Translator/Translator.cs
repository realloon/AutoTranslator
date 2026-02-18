using JetBrains.Annotations;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace Translator;

[UsedImplicitly]
[StaticConstructorOnStartup]
public static class Translator {
    static Translator() {
        var harmony = new Harmony("Vortex.Translator");
        harmony.PatchAll(Assembly.GetExecutingAssembly());
    }
}