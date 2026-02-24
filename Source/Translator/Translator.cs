using JetBrains.Annotations;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace Translator;

[UsedImplicitly]
[StaticConstructorOnStartup]
public static class Translator {
    public static readonly Texture2D GeneralIcon;

    static Translator() {
        var harmony = new Harmony("Vortex.AutoTranslator");
        harmony.PatchAll();

        GeneralIcon = ContentFinder<Texture2D>.Get("UI/Icons/Options/OptionsGeneral");
    }
}
