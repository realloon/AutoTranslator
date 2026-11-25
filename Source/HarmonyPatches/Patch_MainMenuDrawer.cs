using JetBrains.Annotations;
using HarmonyLib;
using RimWorld;
using Verse;
using Translator.UI;

namespace Translator.HarmonyPatches;

[HarmonyPatch(typeof(MainMenuDrawer), nameof(MainMenuDrawer.DoMainMenuControls))]
// ReSharper disable once InconsistentNaming
internal static class Patch_MainMenuDrawer {
    private const string ButtonHighlightTag = "MenuButton-Translator";

    [UsedImplicitly]
    [HarmonyInfix(typeof(OptionListingUtility), nameof(OptionListingUtility.DrawOptionListing), Positions = [1])]
    [HarmonyPrefix]
    private static void AddTranslatorButton(List<ListableOption> optList) {
        if (Current.ProgramState != ProgramState.Entry) return;

        var insertIndex = Math.Max(0, optList.Count - 1);
        optList.Insert(insertIndex,
            new ListableOption("Translator_AutoTranslator".Translate(), OnTranslatorButtonClicked, ButtonHighlightTag));
    }

    private static void OnTranslatorButtonClicked() => Find.WindowStack.Add(new Window_TranslatorMain());
}