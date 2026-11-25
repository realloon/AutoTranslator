using JetBrains.Annotations;
using Translator.UI;
using UnityEngine;
using Verse;

namespace Translator;

[UsedImplicitly]
public sealed class TranslatorMod : Mod {
    private readonly TranslatorSettingsPanel _settingsPanel;

    public static TranslatorSettings Settings { get; private set; } = null!;

    public TranslatorMod(ModContentPack content) : base(content) {
        Settings = GetSettings<TranslatorSettings>();
        _settingsPanel = new TranslatorSettingsPanel(WriteSettings);
    }

    public override string SettingsCategory() => "Translator_AutoTranslator".Translate();

    public override void DoSettingsWindowContents(Rect inRect) => _settingsPanel.Draw(inRect);
}