using JetBrains.Annotations;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;
using Translator.Windows;

namespace Translator.HarmonyPatches;

[HarmonyPatch(typeof(MainMenuDrawer), nameof(MainMenuDrawer.DoMainMenuControls))]
// ReSharper disable once InconsistentNaming
public static class Patch_MainMenuDrawer {
    private const string ButtonHighlightTag = "MenuButton-Translator";

    [UsedImplicitly]
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction>
        DoMainMenuControlsTranspiler(IEnumerable<CodeInstruction> instructions) {
        var codes = new List<CodeInstruction>(instructions);
        var drawMethod =
            AccessTools.Method(typeof(OptionListingUtility), nameof(OptionListingUtility.DrawOptionListing));
        var addButtonMethod = AccessTools.Method(typeof(Patch_MainMenuDrawer), nameof(TryAddTranslatorButton));

        for (var i = 0; i < codes.Count; i++) {
            if (!codes[i].Calls(drawMethod)) {
                continue;
            }

            if (i < 1 || !IsLoadLocalInstruction(codes[i - 1])) {
                Log.Error("[Translator] Could not inject main menu button: unexpected IL pattern.");
                return codes;
            }

            // Insert one instruction before DrawOptionListing: TryAddTranslatorButton(list).
            // We reuse the existing list-local load opcode to avoid assumptions about local index layout.
            codes.Insert(i - 1, new CodeInstruction(codes[i - 1]));
            codes.Insert(i, new CodeInstruction(OpCodes.Call, addButtonMethod));
            return codes;
        }

        Log.Error("[Translator] Could not inject main menu button: DrawOptionListing call was not found.");
        return codes;
    }

    private static bool IsLoadLocalInstruction(CodeInstruction instruction) {
        return instruction.opcode == OpCodes.Ldloc
               || instruction.opcode == OpCodes.Ldloc_0
               || instruction.opcode == OpCodes.Ldloc_1
               || instruction.opcode == OpCodes.Ldloc_2
               || instruction.opcode == OpCodes.Ldloc_3
               || instruction.opcode == OpCodes.Ldloc_S;
    }

    private static void TryAddTranslatorButton(List<ListableOption> options) {
        if (Current.ProgramState != ProgramState.Entry) {
            return;
        }

        var insertIndex = Math.Max(0, options.Count - 1);
        options.Insert(insertIndex, new ListableOption("Translator_MainMenuButton".Translate(),
            OnTranslatorButtonClicked,
            ButtonHighlightTag));
    }

    private static void OnTranslatorButtonClicked() {
        Find.WindowStack.Add(new Window_TranslatorMain());
    }
}