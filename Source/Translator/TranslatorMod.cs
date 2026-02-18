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
    private string _batchSizeBuffer = string.Empty;
    private string _concurrencyBuffer = string.Empty;
    private string _retryCountBuffer = string.Empty;

    public TranslatorMod(ModContentPack content) : base(content) {
        Settings = GetSettings<TranslatorSettings>();
    }

    public override string SettingsCategory() {
        return "Translator_ModSettingsCategory".Translate();
    }

    public override void DoSettingsWindowContents(Rect inRect) {
        ConsumeValidationTaskResultIfReady();

        var listing = new Listing_Standard();
        listing.Begin(inRect);

        listing.Label("Translator_ModSettingsDescription".Translate());
        listing.GapLine();

        listing.Label("Translator_ModSettingApiUrl".Translate());
        Settings.ApiUrl = listing.TextEntry(Settings.ApiUrl);
        listing.Gap(6f);

        listing.Label("Translator_ModSettingApiKey".Translate());
        Settings.ApiKey = listing.TextEntry(Settings.ApiKey);
        listing.Gap(6f);

        listing.Label("Translator_ModSettingModel".Translate());
        Settings.Model = listing.TextEntry(Settings.Model);
        listing.Gap(6f);

        listing.Label("Translator_ModSettingBatchSize".Translate());
        if (_batchSizeBuffer.NullOrEmpty()) {
            _batchSizeBuffer = Settings.BatchSize.ToString();
        }

        var newBatchSizeBuffer = listing.TextEntry(_batchSizeBuffer);
        if (newBatchSizeBuffer != _batchSizeBuffer) {
            _batchSizeBuffer = newBatchSizeBuffer;
            if (int.TryParse(_batchSizeBuffer.Trim(), out var parsedBatchSize)) {
                Settings.BatchSize = parsedBatchSize;
            }
        }
        GUI.color = ColoredText.SubtleGrayColor;
        listing.Label("Translator_ModSettingBatchSizeDescription".Translate());
        GUI.color = Color.white;
        listing.Gap(6f);

        listing.Label("Translator_ModSettingConcurrency".Translate());
        if (_concurrencyBuffer.NullOrEmpty()) {
            _concurrencyBuffer = Settings.Concurrency.ToString();
        }

        var newConcurrencyBuffer = listing.TextEntry(_concurrencyBuffer);
        if (newConcurrencyBuffer != _concurrencyBuffer) {
            _concurrencyBuffer = newConcurrencyBuffer;
            if (int.TryParse(_concurrencyBuffer.Trim(), out var parsedConcurrency)) {
                Settings.Concurrency = parsedConcurrency;
            }
        }

        GUI.color = ColoredText.SubtleGrayColor;
        listing.Label("Translator_ModSettingConcurrencyDescription".Translate());
        GUI.color = Color.white;
        listing.Gap(6f);

        listing.Label("Translator_ModSettingRetryCount".Translate());
        if (_retryCountBuffer.NullOrEmpty()) {
            _retryCountBuffer = Settings.RetryCount.ToString();
        }

        var newRetryCountBuffer = listing.TextEntry(_retryCountBuffer);
        if (newRetryCountBuffer != _retryCountBuffer) {
            _retryCountBuffer = newRetryCountBuffer;
            if (int.TryParse(_retryCountBuffer.Trim(), out var parsedRetryCount)) {
                Settings.RetryCount = parsedRetryCount;
            }
        }

        GUI.color = ColoredText.SubtleGrayColor;
        listing.Label("Translator_ModSettingRetryCountDescription".Translate());
        GUI.color = Color.white;
        listing.Gap();

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

        if (listing.ButtonText("Translator_ModSettingsResetDefaults".Translate())) {
            Settings.ResetToDefaults();
        }

        listing.End();

        Settings.ApiUrl = Settings.ApiUrl.Trim();
        Settings.ApiKey = Settings.ApiKey.Trim();
        Settings.Model = Settings.Model.Trim();
        NormalizeNumericSettings();
    }

    private void BeginValidateConfig() {
        if (_validateConfigTask is not null) {
            return;
        }

        Settings.ApiUrl = Settings.ApiUrl.Trim();
        Settings.ApiKey = Settings.ApiKey.Trim();
        Settings.Model = Settings.Model.Trim();
        NormalizeNumericSettings();
        WriteSettings();

        _lastValidationStatus = "Translator_ModSettingsValidating".Translate();
        _lastValidationFailed = false;
        _validateConfigTask = Task.Run(() => LlmTranslateService.ValidateCurrentConfig(testConnection: true));
    }

    private void ConsumeValidationTaskResultIfReady() {
        if (_validateConfigTask is null || !_validateConfigTask.IsCompleted) {
            return;
        }

        var completedTask = _validateConfigTask;
        _validateConfigTask = null;

        LlmConfigValidationResult result;
        try {
            result = completedTask.GetAwaiter().GetResult();
        } catch (Exception ex) {
            result = new LlmConfigValidationResult {
                Success = false,
                Message = ex.Message
            };
        }

        if (result.Success) {
            _lastValidationFailed = false;
            _lastValidationStatus = "Translator_ModSettingsValidationSuccess".Translate();
            return;
        }

        _lastValidationFailed = true;
        _lastValidationStatus = "Translator_ModSettingsValidationFailed".Translate(result.Message);
    }

    private void NormalizeNumericSettings() {
        Settings.BatchSize = Mathf.Clamp(Settings.BatchSize, TranslatorSettings.MinBatchSize,
            TranslatorSettings.MaxBatchSize);
        Settings.Concurrency = Mathf.Clamp(Settings.Concurrency, TranslatorSettings.MinConcurrency,
            TranslatorSettings.MaxConcurrency);
        Settings.RetryCount = Mathf.Clamp(Settings.RetryCount, TranslatorSettings.MinRetryCount,
            TranslatorSettings.MaxRetryCount);
        _batchSizeBuffer = Settings.BatchSize.ToString();
        _concurrencyBuffer = Settings.Concurrency.ToString();
        _retryCountBuffer = Settings.RetryCount.ToString();
    }
}
