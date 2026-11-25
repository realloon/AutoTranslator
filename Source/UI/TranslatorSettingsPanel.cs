using RimWorld;
using Translator.Export;
using Translator.Llm;
using UnityEngine;
using Verse;

namespace Translator.UI;

/// <summary>Contents of the mod settings dialog; lifecycle stays in <see cref="TranslatorMod"/>.</summary>
internal sealed class TranslatorSettingsPanel {
    private const float Grid = 8f;
    private const float TabsAreaHeight = Grid * 5f;

    private static readonly string[] TabKeys = [
        "Translator_ModSettingsTabConnection",
        "Translator_ModSettingsTabAdvanced"
    ];

    private readonly Action _writeSettings;
    private readonly AsyncOperation<LlmConfigValidationResult> _validation = new();
    private readonly AsyncOperation<LlmModelListResult> _modelFetch = new();
    private readonly List<string> _availableModels = [];
    private readonly HashSet<string> _customModelProviders = new(StringComparer.Ordinal);

    private string _modelsFetchedProvider = string.Empty;
    private string _validationStatus = string.Empty;
    private bool _validationFailed;
    private int _activeTab;

    public TranslatorSettingsPanel(Action writeSettings) => _writeSettings = writeSettings;

    public void Draw(Rect inRect) {
        ConsumeValidationResult();
        ConsumeModelFetchResult();

        var y = inRect.y;
        DrawTabs(new Rect(inRect.x, y, inRect.width, Grid * 4f));

        var contentRect = new Rect(
            inRect.x, y + TabsAreaHeight, inRect.width, inRect.height - TabsAreaHeight);
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

            Widgets.DrawBoxSolid(tabRect, selected
                ? new Color(1f, 1f, 1f, 0.12f)
                : new Color(1f, 1f, 1f, 0.035f));

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

        listing.Label("Translator_ModSettingProvider".Translate());
        if (listing.ButtonText(GetProviderLabel(settings.ProviderId))) {
            OpenProviderMenu();
        }

        listing.Gap(6f);

        var preset = LlmProviderPresets.Find(settings.ProviderId);
        if (preset is null) {
            listing.Label("Translator_ModSettingProtocol".Translate());
            if (listing.ButtonText(GetProtocolLabel(settings.CustomProtocol))) {
                settings.CustomProtocol = settings.CustomProtocol == LlmApiProtocol.ChatCompletions
                    ? LlmApiProtocol.Responses
                    : LlmApiProtocol.ChatCompletions;
            }

            listing.Gap(6f);

            listing.Label("Translator_ModSettingApiUrl".Translate());
            settings.ApiUrl = listing.TextEntry(settings.ApiUrl);
            listing.Gap(6f);
        }

        listing.Label("Translator_ModSettingApiKey".Translate());
        var apiKey = settings.GetApiKey(settings.ProviderId);
        var newApiKey = listing.TextEntry(apiKey);
        if (newApiKey != apiKey) {
            settings.SetApiKey(settings.ProviderId, newApiKey);
        }

        listing.Gap(6f);

        if (preset is not null && !settings.GetApiKey(settings.ProviderId).NullOrEmpty() &&
            _modelsFetchedProvider != settings.ProviderId) {
            _modelsFetchedProvider = settings.ProviderId;
            StartModelFetch();
        }

        listing.Label("Translator_ModSettingModel".Translate());
        var model = settings.GetModel(settings.ProviderId);
        if (preset is null) {
            var newModel = listing.TextEntry(model);
            if (newModel != model) {
                settings.SetModel(settings.ProviderId, newModel);
            }
        } else {
            if (listing.ButtonText(model.NullOrEmpty()
                    ? "Translator_ModSettingModelSelect".Translate()
                    : model)) {
                OpenModelMenu();
            }

            if (IsCustomModel(settings.ProviderId, model)) {
                var newModel = listing.TextEntry(model);
                if (newModel != model) {
                    settings.SetModel(settings.ProviderId, newModel);
                }
            }
        }

        listing.Gap(6f);

        if (listing.ButtonText("Translator_ModSettingsValidateConfig".Translate())) {
            BeginValidation();
        }

        if (_validation.IsRunning) {
            using var color = GuiColorScope.Using(Color.yellow);
            listing.Label("Translator_ModSettingsValidating".Translate());
        } else if (!_validationStatus.NullOrEmpty()) {
            using var color = GuiColorScope.Using(_validationFailed
                ? UiTheme.ErrorColor
                : UiTheme.SuccessColor);
            listing.Label(_validationStatus);
        }
    }

    private void DrawAdvancedSettings(Listing_Standard listing) {
        var settings = TranslatorMod.Settings;

        listing.Label($"{"Translator_ModSettingBatchSize".Translate()}: {settings.BatchSize}");
        using (GuiColorScope.Using(ColoredText.SubtleGrayColor)) {
            listing.Label("Translator_ModSettingBatchSizeDescription".Translate());
        }

        listing.Gap(2f);
        settings.BatchSize = Mathf.RoundToInt(listing.Slider(
            settings.BatchSize, TranslatorSettings.MinBatchSize, TranslatorSettings.MaxBatchSize));
        listing.Gap(6f);

        listing.Label($"{"Translator_ModSettingConcurrency".Translate()}: {settings.Concurrency}");
        using (GuiColorScope.Using(ColoredText.SubtleGrayColor)) {
            listing.Label("Translator_ModSettingConcurrencyDescription".Translate());
        }

        listing.Gap(2f);
        settings.Concurrency = Mathf.RoundToInt(listing.Slider(
            settings.Concurrency, TranslatorSettings.MinConcurrency, TranslatorSettings.MaxConcurrency));
        listing.Gap(6f);

        listing.Label($"{"Translator_ModSettingRetryCount".Translate()}: {settings.RetryCount}");
        using (GuiColorScope.Using(ColoredText.SubtleGrayColor)) {
            listing.Label("Translator_ModSettingRetryCountDescription".Translate());
        }

        listing.Gap(2f);
        settings.RetryCount = Mathf.RoundToInt(listing.Slider(
            settings.RetryCount, TranslatorSettings.MinRetryCount, TranslatorSettings.MaxRetryCount));
        listing.Gap(6f);

        listing.GapLine();
        listing.Gap(6f);

        listing.Label("Translator_ModSettingDefaultOutputLocation".Translate());
        if (listing.ButtonText(GetOutputLocationLabel(settings.DefaultOutputLocationMode))) {
            settings.DefaultOutputLocationMode =
                settings.DefaultOutputLocationMode == OutputLocationMode.GeneratedMod
                    ? OutputLocationMode.OriginalMod
                    : OutputLocationMode.GeneratedMod;
        }

        using (GuiColorScope.Using(ColoredText.SubtleGrayColor)) {
            listing.Label("Translator_ModSettingDefaultOutputLocationDescription".Translate());
        }

        listing.Gap(6f);
        listing.GapLine();
        listing.Gap(6f);

        if (listing.ButtonText("Translator_ModSettingsResetDefaults".Translate())) {
            settings.ResetToDefaults();
        }
    }

    private void BeginValidation() {
        if (_validation.IsRunning) {
            return;
        }

        _writeSettings();
        _validationStatus = string.Empty;
        _validationFailed = false;
        _validation.Start(token => LlmClient.ValidateAsync(token));
    }

    private void ConsumeValidationResult() {
        if (!_validation.TryTakeResult(out var result, out var error)) {
            return;
        }

        if (error is not null) {
            _validationFailed = true;
            _validationStatus = "Translator_ModSettingsValidationFailed".Translate(error.Message);
            return;
        }

        if (result is null) {
            return;
        }

        _validationFailed = !result.Success;
        _validationStatus = result.Success
            ? "Translator_ModSettingsValidationSuccess".Translate()
            : "Translator_ModSettingsValidationFailed".Translate(result.Message);
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
        _modelsFetchedProvider = string.Empty;
        _validation.Cancel();
        _modelFetch.Cancel();
        _validationStatus = string.Empty;
        _validationFailed = false;
    }

    private void OpenModelMenu() {
        ConsumeModelFetchResult();

        if (_availableModels.Count == 0 && !_modelFetch.IsRunning) {
            StartModelFetch();
        }

        var options = new List<FloatMenuOption>();
        if (_modelFetch.IsRunning) {
            options.Add(new FloatMenuOption("Translator_ModSettingModelsLoading".Translate(), null));
        } else {
            options.AddRange(_availableModels.Select(model =>
                new FloatMenuOption(model, () => SelectModel(model))));
        }

        options.Add(new FloatMenuOption(
            "Translator_ModSettingModelCustom".Translate(),
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
        if (_modelFetch.IsRunning) {
            return;
        }

        _availableModels.Clear();
        _modelFetch.Start(token => LlmClient.FetchModelsAsync(token));
    }

    private void ConsumeModelFetchResult() {
        if (!_modelFetch.TryTakeResult(out var result, out var error)) {
            return;
        }

        if (error is not null) {
            Messages.Message("Translator_ModSettingModelsFailed".Translate(error.Message),
                MessageTypeDefOf.SilentInput);
            return;
        }

        if (result is null) {
            return;
        }

        if (!result.Success) {
            Messages.Message("Translator_ModSettingModelsFailed".Translate(result.Message),
                MessageTypeDefOf.SilentInput);
            return;
        }

        _availableModels.Clear();
        _availableModels.AddRange(result.Models);
    }

    private static string GetProtocolLabel(LlmApiProtocol protocol) =>
        protocol == LlmApiProtocol.Responses
            ? "Translator_ModSettingProtocolResponses".Translate()
            : "Translator_ModSettingProtocolChat".Translate();

    private static string GetProviderLabel(string providerId) =>
        LlmProviderPresets.Find(providerId)?.Label ?? LlmProviderPresets.CustomLabelKey.Translate();

    private static string GetOutputLocationLabel(OutputLocationMode mode) =>
        $"{"Translator_ModSettingDefaultOutputLocation".Translate()}: {OutputModeText.Of(mode)}";
}
