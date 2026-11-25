using JetBrains.Annotations;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace Translator;

[UsedImplicitly]
[StaticConstructorOnStartup]
public static class Translator {
    public static Texture2D GeneralIcon { get; }

    static Translator() {
        new Harmony("Vortex.AutoTranslator").PatchAll(typeof(Translator).Assembly);

        GeneralIcon = ContentFinder<Texture2D>.Get("UI/Icons/Options/OptionsGeneral");
    }
}