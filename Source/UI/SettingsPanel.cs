using RimWorld;
using Translator.Export;
using Translator.Llm;
using UnityEngine;
using Verse;

namespace Translator.UI;

internal sealed class SettingsPanel(Action writeSettings) {
    private const float Grid = 8f;
    private const float TabsAreaHeight = Grid * 5f;
    private const string ApiKeyControlName = "ApiKeyField";
    private const int ApiKeyVisiblePrefixLength = 8;

    private static readonly string[] TabKeys = [
        "Translator_Connection",
        "Translator_Advanced"
    ];

    private readonly AsyncOperation<LlmConfigValidationResult> _validation = new();
    private readonly AsyncOperation<LlmModelListResult> _modelFetch = new();
    private readonly List<string> _availableModels = [];
    private readonly HashSet<string> _customModelProviders = new(StringComparer.Ordinal);

    private string _validationStatus = string.Empty;
    private string _validationDetails = string.Empty;
    private string _validatingConfig = string.Empty;
    private bool _validationSucceeded;
    private int _activeTab;

    public void Draw(Rect inRect) {
        ConsumeValidationResult();
        ConsumeModelFetchResult();

        var y = inRect.y;
        DrawTabs(new Rect(inRect.x, y, inRect.width, Grid * 4f));

        var contentRect = new Rect(inRect.x, y + TabsAreaHeight, inRect.width, inRect.height - TabsAreaHeight);
        var listing = new Listing_Standard();
        listing.Begin(contentRect);

        if (_activeTab == 0) {
            DrawConnectionSettings(listing);
        } else {
            DrawAdvancedSettings(listing);
        }

        listing.End();
    }

    private void DrawTabs(Rect rect) {
        const float tabHeight = Grid * 4f;
        var tabWidth = (rect.width - Grid * (TabKeys.Length - 1)) / TabKeys.Length;

        for (var i = 0; i < TabKeys.Length; i++) {
            var tabRect = new Rect(rect.x + i * (tabWidth + Grid), rect.y, tabWidth, tabHeight);
            var selected = _activeTab == i;

            Widgets.DrawBoxSolid(tabRect, selected ? UiTheme.White12 : UiTheme.White04);

            if (selected) {
                Widgets.DrawHighlightSelected(tabRect);
            } else {
                Widgets.DrawHighlightIfMouseover(tabRect);
            }

            if (Widgets.ButtonInvisible(tabRect)) {
                _activeTab = i;
            }

            var previousAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(tabRect, TabKeys[i].Translate());
            Text.Anchor = previousAnchor;
        }
    }

    private void DrawConnectionSettings(Listing_Standard listing) {
        var settings = TranslatorMod.Settings;

        listing.Label("Translator_Provider".Translate());
        if (listing.ButtonText(GetProviderLabel(settings.ProviderId))) {
            OpenProviderMenu();
        }

        listing.Gap(6f);

        var preset = LlmProviderPresets.Find(settings.ProviderId);
        if (preset is null) {
            listing.Label("Translator_ApiProtocol".Translate());
            if (listing.ButtonText(GetProtocolLabel(settings.CustomProtocol))) {
                settings.CustomProtocol = settings.CustomProtocol == LlmApiProtocol.ChatCompletions
                    ? LlmApiProtocol.Responses
                    : LlmApiProtocol.ChatCompletions;
            }

            listing.Gap(6f);

            listing.Label("Translator_ApiUrl".Translate());
            settings.ApiUrl = listing.TextEntry(settings.ApiUrl);
            listing.Gap(6f);
        }

        listing.Label("Translator_ApiKey".Translate());
        var apiKey = settings.GetApiKey(settings.ProviderId);
        GUI.SetNextControlName(ApiKeyControlName);
        var apiKeyFocused = GUI.GetNameOfFocusedControl() == ApiKeyControlName;
        var newApiKey = listing.TextEntry(apiKeyFocused ? apiKey : MaskApiKey(apiKey));
        if (apiKeyFocused && newApiKey != apiKey) {
            settings.SetApiKey(settings.ProviderId, newApiKey);
        }

        listing.Gap(6f);

        listing.Label("Translator_Model".Translate());
        var model = settings.GetModel(settings.ProviderId);
        if (preset is null) {
            var newModel = listing.TextEntry(model);
            if (newModel != model) {
                settings.SetModel(settings.ProviderId, newModel);
            }
        } else {
            if (listing.ButtonText(model.NullOrEmpty() ? "Translator_SelectModel".Translate() : model)) {
                OpenModelMenu();
            }

            if (IsCustomModel(settings.ProviderId, model)) {
                var newModel = listing.TextEntry(model);
                if (newModel != model) {
                    settings.SetModel(settings.ProviderId, newModel);
                }
            }
        }

        if (!_validation.IsRunning && _validatingConfig.Length > 0 &&
            _validatingConfig != ConnectionFingerprint()) {
            ClearValidationState();
        }

        listing.Gap(6f);

        var previousEnabled = GUI.enabled;
        GUI.enabled = !_validation.IsRunning;
        if (listing.ButtonText("Translator_Validate".Translate())) {
            BeginValidation();
        }

        GUI.enabled = previousEnabled;

        if (_validation.IsRunning) {
            using var color = GuiColorScope.Using(UiTheme.SoftYellow);
            listing.Label("Translator_ValidatingConfiguration".Translate());
        } else if (!_validationStatus.NullOrEmpty()) {
            using (GuiColorScope.Using(_validationSucceeded
                ? UiTheme.SoftGreen
                : UiTheme.SoftRed)) {
                listing.Label(_validationStatus);
            }

            if (!_validationDetails.NullOrEmpty()) {
                using (GuiColorScope.Using(ColoredText.SubtleGrayColor)) {
                    listing.Label(_validationDetails);
                }
            }
        }
    }

    private static void DrawAdvancedSettings(Listing_Standard listing) {
        var settings = TranslatorMod.Settings;

        listing.Label($"{"Translator_RequestChunking".Translate()}: {settings.BatchSize}");
        using (GuiColorScope.Using(ColoredText.SubtleGrayColor)) {
            listing.Label("Translator_RequestChunkingDescription".Translate());
        }

        listing.Gap(2f);
        settings.BatchSize = Mathf.RoundToInt(listing.Slider(
            settings.BatchSize, TranslatorSettings.MinBatchSize, TranslatorSettings.MaxBatchSize));
        listing.Gap(6f);

        listing.Label($"{"Translator_Concurrency".Translate()}: {settings.Concurrency}");
        using (GuiColorScope.Using(ColoredText.SubtleGrayColor)) {
            listing.Label("Translator_ConcurrencyDescription".Translate());
        }

        listing.Gap(2f);
        settings.Concurrency = Mathf.RoundToInt(listing.Slider(
            settings.Concurrency, TranslatorSettings.MinConcurrency, TranslatorSettings.MaxConcurrency));
        listing.Gap(6f);

        listing.Label($"{"Translator_RetryAttempts".Translate()}: {settings.RetryCount}");
        using (GuiColorScope.Using(ColoredText.SubtleGrayColor)) {
            listing.Label("Translator_RetryAttemptsDescription".Translate());
        }

        listing.Gap(2f);
        settings.RetryCount = Mathf.RoundToInt(listing.Slider(
            settings.RetryCount, TranslatorSettings.MinRetryCount, TranslatorSettings.MaxRetryCount));
        listing.Gap(6f);

        listing.GapLine();
        listing.Gap(6f);

        listing.Label("Translator_DefaultOutputLocation".Translate());
        if (listing.ButtonText(GetOutputLocationLabel(settings.DefaultOutputLocationMode))) {
            settings.DefaultOutputLocationMode = settings.DefaultOutputLocationMode == OutputLocationMode.GeneratedMod
                ? OutputLocationMode.OriginalMod
                : OutputLocationMode.GeneratedMod;
        }

        using (GuiColorScope.Using(ColoredText.SubtleGrayColor)) {
            listing.Label("Translator_DefaultOutputLocationDescription".Translate());
        }

        listing.Gap(6f);
        listing.GapLine();
        listing.Gap(6f);

        if (listing.ButtonText("Translator_ResetToDefaults".Translate())) {
            settings.ResetAdvancedSettings();
        }
    }

    private static string MaskApiKey(string apiKey) {
        if (apiKey.Length <= ApiKeyVisiblePrefixLength) {
            return apiKey;
        }

        return apiKey[..ApiKeyVisiblePrefixLength] +
               new string('*', apiKey.Length - ApiKeyVisiblePrefixLength);
    }

    private static string ConnectionFingerprint() {
        var settings = TranslatorMod.Settings;
        return string.Join('\n', settings.ProviderId, settings.CustomProtocol, settings.ApiUrl,
            settings.GetApiKey(settings.ProviderId), settings.GetModel(settings.ProviderId));
    }

    private static bool HasApiKey =>
        TranslatorMod.Settings.GetApiKey(TranslatorMod.Settings.ProviderId).Length > 0;

    private static bool HasModel =>
        TranslatorMod.Settings.GetModel(TranslatorMod.Settings.ProviderId).Length > 0;

    private static void ShowMissing(string key) {
        Messages.Message(key.Translate(), MessageTypeDefOf.RejectInput);
    }

    private void ClearValidationState() {
        _validationStatus = string.Empty;
        _validationDetails = string.Empty;
        _validationSucceeded = false;
        _validatingConfig = string.Empty;
    }

    private void BeginValidation() {
        if (!HasApiKey) {
            ShowMissing("Translator_ApiKeyNotConfigured");
            return;
        }

        if (!HasModel) {
            ShowMissing("Translator_ModelNotConfigured");
            return;
        }

        writeSettings();
        ClearValidationState();
        _validatingConfig = ConnectionFingerprint();
        _validation.Start(LlmClient.ValidateAsync);
    }

    private void ConsumeValidationResult() {
        if (!_validation.TryTakeResult(out var result, out var error)) return;

        if (_validatingConfig != ConnectionFingerprint()) {
            ClearValidationState();
            return;
        }

        if (error is not null) {
            _validationSucceeded = false;
            _validationStatus = "Translator_ValidationFailed".Translate();
            _validationDetails = error.Message;
            return;
        }

        if (result.Success) {
            _validationSucceeded = true;
            _validationStatus = "Translator_ValidationSucceeded".Translate();
            _validationDetails = string.Empty;
        } else {
            var errorInfo = result.Error!;
            _validationSucceeded = false;
            _validationDetails = errorInfo.Details;
            _validationStatus = errorInfo is { ReasonKey.Length: > 0 } info
                ? "Translator_ValidationFailedWithReason".Translate(LlmErrorText.Reason(info))
                : "Translator_ValidationFailed".Translate();
        }
    }

    private void OpenProviderMenu() {
        var options = LlmProviderPresets.All
            .Select(preset => new FloatMenuOption(preset.Label, () => SelectProvider(preset.Id)))
            .ToList();
        options.Add(new FloatMenuOption(
            LlmProviderPresets.CustomLabelKey.Translate(),
            () => SelectProvider(LlmProviderPresets.CustomId)));
        Find.WindowStack.Add(new FloatMenu(options));
    }

    private void SelectProvider(string providerId) {
        TranslatorMod.Settings.ProviderId = providerId;
        _availableModels.Clear();
        _validation.Cancel();
        _modelFetch.Cancel();
        ClearValidationState();
    }

    private void OpenModelMenu() {
        if (!HasApiKey) {
            ShowMissing("Translator_ApiKeyNotConfigured");
            return;
        }

        ConsumeModelFetchResult();

        if (_availableModels.Count == 0 && !_modelFetch.IsRunning) {
            StartModelFetch();
        }

        var options = _availableModels
            .Select(model => new FloatMenuOption(model, () => SelectModel(model)))
            .ToList();
        if (_modelFetch.IsRunning) {
            options.Add(new FloatMenuOption("Translator_LoadingModels".Translate(), null));
        }

        options.Add(new FloatMenuOption(
            "Translator_CustomModel".Translate(),
            () => _customModelProviders.Add(TranslatorMod.Settings.ProviderId)));
        Find.WindowStack.Add(new FloatMenu(options));
    }

    private void SelectModel(string model) {
        TranslatorMod.Settings.SetModel(TranslatorMod.Settings.ProviderId, model);
        _customModelProviders.Remove(TranslatorMod.Settings.ProviderId);
    }

    private bool IsCustomModel(string providerId, string model) {
        if (_customModelProviders.Contains(providerId)) {
            return true;
        }

        return !model.NullOrEmpty() && _availableModels.Count > 0 && !_availableModels.Contains(model);
    }

    private void StartModelFetch() {
        _availableModels.Clear();
        _modelFetch.Start(LlmClient.FetchModelsAsync);
    }

    private static void ShowModelFetchFailure(string message) {
        Messages.Message("Translator_LoadModelsFailed".Translate(message), MessageTypeDefOf.SilentInput);
    }

    private void ConsumeModelFetchResult() {
        if (!_modelFetch.TryTakeResult(out var result, out var error)) return;

        if (error is not null) {
            ShowModelFetchFailure(error.Message);
            return;
        }

        if (!result.Success) {
            ShowModelFetchFailure(LlmErrorText.Format(result.Error!));
            return;
        }

        _availableModels.Clear();
        _availableModels.AddRange(result.Models);
    }

    private static string GetProtocolLabel(LlmApiProtocol protocol) => protocol == LlmApiProtocol.Responses
        ? "Translator_ResponsesApi".Translate()
        : "Translator_ChatCompletionsApi".Translate();

    private static string GetProviderLabel(string providerId) =>
        LlmProviderPresets.Find(providerId)?.Label ?? LlmProviderPresets.CustomLabelKey.Translate();

    private static string GetOutputLocationLabel(OutputLocationMode mode) =>
        $"{"Translator_DefaultOutputLocation".Translate()}: {OutputModeText.Of(mode)}";
}
