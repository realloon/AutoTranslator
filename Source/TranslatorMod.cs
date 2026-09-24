using JetBrains.Annotations;
using UnityEngine;
using Verse;
using Translator.Services;

namespace Translator;

[UsedImplicitly]
public sealed class TranslatorMod : Mod {
    public static TranslatorSettings Settings { get; private set; } = null!;
    private string _lastValidationStatus = string.Empty;
    private bool _lastValidationFailed;
    private Task<LlmConfigValidationResult>? _validateConfigTask;
    private Vector2 _settingsScrollPosition = Vector2.zero;

    public TranslatorMod(ModContentPack content) : base(content) {
        Settings = GetSettings<TranslatorSettings>();
    }

    public override string SettingsCategory() => "Translator_ModSettingsCategory".Translate();

    public override void DoSettingsWindowContents(Rect inRect) {
        ConsumeValidationTaskResultIfReady();

        const float scrollbarWidth = 16f;
        const float minContentHeight = 780f;
        var viewHeight = Mathf.Max(minContentHeight, inRect.height - 1f);
        var viewRect = new Rect(0f, 0f, inRect.width - scrollbarWidth, viewHeight);
        Widgets.BeginScrollView(inRect, ref _settingsScrollPosition, viewRect);

        var listing = new Listing_Standard();
        listing.Begin(viewRect);

        listing.Label("Translator_ModSettingApiUrl".Translate());
        Settings.ApiUrl = listing.TextEntry(Settings.ApiUrl);
        listing.Gap(6f);

        listing.Label("Translator_ModSettingApiKey".Translate());
        Settings.ApiKey = listing.TextEntry(Settings.ApiKey);
        listing.Gap(6f);

        listing.Label("Translator_ModSettingModel".Translate());
        Settings.Model = listing.TextEntry(Settings.Model);
        listing.Gap(6f);

        if (_validateConfigTask is not null) {
            GUI.color = Color.yellow;
            listing.Label("Translator_ModSettingsValidating".Translate());
            GUI.color = Color.white;
            listing.Gap(8f);
        } else if (!_lastValidationStatus.NullOrEmpty()) {
            GUI.color = _lastValidationFailed ? new Color(0.95f, 0.35f, 0.35f) : new Color(0.35f, 0.95f, 0.35f);
            listing.Label(_lastValidationStatus);
            GUI.color = Color.white;
            listing.Gap(8f);
        }

        if (listing.ButtonText("Translator_ModSettingsValidateConfig".Translate())) {
            BeginValidateConfig();
        }

        listing.Gap(6f);
        listing.GapLine();
        listing.Gap(10f);

        listing.Label($"{"Translator_ModSettingBatchSize".Translate()}: {Settings.BatchSize}");
        GUI.color = ColoredText.SubtleGrayColor;
        listing.Label("Translator_ModSettingBatchSizeDescription".Translate());
        GUI.color = Color.white;
        listing.Gap(2f);

        Settings.BatchSize = Mathf.RoundToInt(listing.Slider(Settings.BatchSize,
            TranslatorSettings.MinBatchSize, TranslatorSettings.MaxBatchSize));
        listing.Gap(6f);

        listing.Label($"{"Translator_ModSettingConcurrency".Translate()}: {Settings.Concurrency}");
        GUI.color = ColoredText.SubtleGrayColor;
        listing.Label("Translator_ModSettingConcurrencyDescription".Translate());
        GUI.color = Color.white;
        listing.Gap(2f);

        Settings.Concurrency = Mathf.RoundToInt(listing.Slider(Settings.Concurrency,
            TranslatorSettings.MinConcurrency, TranslatorSettings.MaxConcurrency));
        listing.Gap(6f);

        listing.Label($"{"Translator_ModSettingRetryCount".Translate()}: {Settings.RetryCount}");
        GUI.color = ColoredText.SubtleGrayColor;
        listing.Label("Translator_ModSettingRetryCountDescription".Translate());
        GUI.color = Color.white;
        listing.Gap(2f);

        Settings.RetryCount = Mathf.RoundToInt(listing.Slider(Settings.RetryCount,
            TranslatorSettings.MinRetryCount, TranslatorSettings.MaxRetryCount));
        listing.Gap(6f);

        listing.GapLine();
        listing.Gap(6f);
        listing.Label("Translator_ModSettingDefaultOutputLocation".Translate());
        var outputLocationLabel = GetOutputLocationLabel(Settings.DefaultOutputLocationMode);
        if (listing.ButtonText(outputLocationLabel)) {
            Settings.DefaultOutputLocationMode = Settings.DefaultOutputLocationMode == OutputLocationMode.GeneratedMod
                ? OutputLocationMode.OriginalMod
                : OutputLocationMode.GeneratedMod;
        }

        GUI.color = ColoredText.SubtleGrayColor;
        listing.Label("Translator_ModSettingDefaultOutputLocationDescription".Translate());
        GUI.color = Color.white;
        listing.Gap(6f);
        listing.GapLine();
        listing.Gap(6f);

        if (listing.ButtonText("Translator_ModSettingsResetDefaults".Translate())) {
            Settings.ResetToDefaults();
        }

        listing.End();
        Widgets.EndScrollView();
    }

    private void BeginValidateConfig() {
        if (_validateConfigTask is not null) return;

        WriteSettings();

        _lastValidationStatus = "Translator_ModSettingsValidating".Translate();
        _lastValidationFailed = false;
        _validateConfigTask = Task.Run(() => LlmTranslateService.ValidateCurrentConfig(testConnection: true));
    }

    private void ConsumeValidationTaskResultIfReady() {
        if (_validateConfigTask is null || !_validateConfigTask.IsCompleted) return;

        var completedTask = _validateConfigTask;
        _validateConfigTask = null;

        var result = completedTask.GetAwaiter().GetResult();

        if (result.Success) {
            _lastValidationFailed = false;
            _lastValidationStatus = "Translator_ModSettingsValidationSuccess".Translate();
            return;
        }

        _lastValidationFailed = true;
        _lastValidationStatus = "Translator_ModSettingsValidationFailed".Translate(result.Message);
    }

    private static string GetOutputLocationLabel(OutputLocationMode mode) {
        var modeText = mode == OutputLocationMode.OriginalMod
            ? "Translator_OutputModeOriginalMod".Translate()
            : "Translator_OutputModeGeneratedMod".Translate();
        return $"{"Translator_ModSettingDefaultOutputLocation".Translate()}: {modeText}";
    }
}