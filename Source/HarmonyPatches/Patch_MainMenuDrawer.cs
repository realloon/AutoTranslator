using System.Reflection.Emit;
using HarmonyLib;
using JetBrains.Annotations;
using RimWorld;
using Translator.UI;
using Verse;

namespace Translator.HarmonyPatches;

[HarmonyPatch(typeof(MainMenuDrawer), nameof(MainMenuDrawer.DoMainMenuControls))]
internal static class Patch_MainMenuDrawer {
    private const string ButtonHighlightTag = "MenuButton-Translator";

    [UsedImplicitly]
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> AddTranslatorButton(
        IEnumerable<CodeInstruction> instructions) {
        var drawOptionListing = AccessTools.Method(
            typeof(OptionListingUtility), nameof(OptionListingUtility.DrawOptionListing));
        var addButton = AccessTools.Method(
            typeof(Patch_MainMenuDrawer), nameof(TryAddTranslatorButton));

        var matcher = new CodeMatcher(instructions)
            .MatchStartForward(new CodeMatch(OpCodes.Call, drawOptionListing));

        if (matcher.IsInvalid) {
            Log.Error("[Translator] Could not inject main menu button: DrawOptionListing call was not found.");
            return matcher.InstructionEnumeration();
        }

        // The list is on top of the stack right before DrawOptionListing(rect, list).
        // Duplicating it lets us pass the same list to TryAddTranslatorButton without
        // making assumptions about local variable indexes.
        return matcher
            .InsertAndAdvance(
                new CodeInstruction(OpCodes.Dup),
                new CodeInstruction(OpCodes.Call, addButton))
            .InstructionEnumeration();
    }

    private static void TryAddTranslatorButton(List<ListableOption> options) {
        if (Current.ProgramState != ProgramState.Entry) {
            return;
        }

        var insertIndex = Math.Max(0, options.Count - 1);
        options.Insert(insertIndex, new ListableOption(
            "Translator_MainMenuButton".Translate(), OnTranslatorButtonClicked, ButtonHighlightTag));
    }

    private static void OnTranslatorButtonClicked() =>
        Find.WindowStack.Add(new Window_TranslatorMain());
}
