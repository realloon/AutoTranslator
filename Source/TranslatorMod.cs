using JetBrains.Annotations;
using RimWorld;
using UnityEngine;
using Verse;
using Translator.Services;

namespace Translator;

[UsedImplicitly]
public sealed class TranslatorMod : Mod {
    private const float Grid = 8f;
    private const float TabsAreaHeight = Grid * 5f;
    public static TranslatorSettings Settings { get; private set; } = null!;
    private string _lastValidationStatus = string.Empty;
    private bool _lastValidationFailed;
    private Task<LlmConfigValidationResult>? _validateConfigTask;
    private Task<LlmModelListResult>? _fetchModelsTask;
    private readonly List<string> _availableModels = [];
    private readonly HashSet<string> _customModelProviders = [];
    private string _modelsFetchedProvider = string.Empty;
    private int _activeSettingsTab;

    public TranslatorMod(ModContentPack content) : base(content) {
        Settings = GetSettings<TranslatorSettings>();
        if (!Settings.PendingReconfigureNotice) return;

        Settings.PendingReconfigureNotice = false;
        WriteSettings();
        LongEventHandler.ExecuteWhenFinished(() =>
            Messages.Message("Translator_ModSettingsResetNotice".Translate(), MessageTypeDefOf.SilentInput));
    }

    public override string SettingsCategory() => "Translator_ModSettingsCategory".Translate();

    public override void DoSettingsWindowContents(Rect inRect) {
        ConsumeValidationTaskResultIfReady();
        ConsumeModelFetchTaskResultIfReady();

        var y = inRect.y;
        DrawSettingsTabs(new Rect(inRect.x, y, inRect.width, Grid * 4f));

        var contentRect = new Rect(inRect.x, y + TabsAreaHeight, inRect.width,
            inRect.height - TabsAreaHeight);
        var listing = new Listing_Standard();
        listing.Begin(contentRect);
        if (_activeSettingsTab == 0) {
            DrawConnectionSettings(listing);
        } else {
            DrawAdvancedSettings(listing);
        }

        listing.End();
    }

    private void DrawSettingsTabs(Rect rect) {
        var tabs = new[] {
            (Index: 0, Label: "Translator_ModSettingsTabConnection".Translate()),
            (Index: 1, Label: "Translator_ModSettingsTabAdvanced".Translate())
        };
        const float tabHeight = Grid * 4f;
        var tabWidth = (rect.width - Grid * (tabs.Length - 1)) / tabs.Length;

        for (var i = 0; i < tabs.Length; i++) {
            var tab = tabs[i];
            var tabRect = new Rect(rect.x + i * (tabWidth + Grid), rect.y, tabWidth, tabHeight);
            var selected = _activeSettingsTab == tab.Index;
            var background = selected ? new Color(1f, 1f, 1f, 0.12f) : new Color(1f, 1f, 1f, 0.035f);

            Widgets.DrawBoxSolid(tabRect, background);
            if (selected) {
                Widgets.DrawHighlightSelected(tabRect);
            } else {
                Widgets.DrawHighlightIfMouseover(tabRect);
            }

            if (Widgets.ButtonInvisible(tabRect)) {
                _activeSettingsTab = tab.Index;
            }

            var oldAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(tabRect, tab.Label);
            Text.Anchor = oldAnchor;
        }
    }

    private void DrawConnectionSettings(Listing_Standard listing) {
        listing.Label("Translator_ModSettingProvider".Translate());
        if (listing.ButtonText(GetProviderLabel(Settings.ProviderId))) {
            OpenProviderMenu();
        }

        listing.Gap(6f);

        var preset = LlmProviderPresets.Find(Settings.ProviderId);
        if (preset is null) {
            listing.Label("Translator_ModSettingProtocol".Translate());
            if (listing.ButtonText(GetProtocolLabel(Settings.CustomProtocol))) {
                Settings.CustomProtocol = Settings.CustomProtocol == LlmApiProtocol.ChatCompletions
                    ? LlmApiProtocol.Responses
                    : LlmApiProtocol.ChatCompletions;
            }

            listing.Gap(6f);

            listing.Label("Translator_ModSettingApiUrl".Translate());
            Settings.ApiUrl = listing.TextEntry(Settings.ApiUrl);
            listing.Gap(6f);
        }

        listing.Label("Translator_ModSettingApiKey".Translate());
        var apiKey = Settings.GetApiKey(Settings.ProviderId);
        var newApiKey = listing.TextEntry(apiKey);
        if (newApiKey != apiKey) {
            Settings.SetApiKey(Settings.ProviderId, newApiKey);
        }

        listing.Gap(6f);

        if (preset is not null && !Settings.GetApiKey(Settings.ProviderId).NullOrEmpty() &&
            _modelsFetchedProvider != Settings.ProviderId) {
            _modelsFetchedProvider = Settings.ProviderId;
            StartFetchModels();
        }

        listing.Label("Translator_ModSettingModel".Translate());
        var model = Settings.GetModel(Settings.ProviderId);
        if (preset is null) {
            var newModel = listing.TextEntry(model);
            if (newModel != model) {
                Settings.SetModel(Settings.ProviderId, newModel);
            }
        } else {
            if (listing.ButtonText(model.NullOrEmpty() ? "Translator_ModSettingModelSelect".Translate() : model)) {
                OpenModelMenu();
            }

            if (IsCustomModel(Settings.ProviderId, model)) {
                var newModel = listing.TextEntry(model);
                if (newModel != model) {
                    Settings.SetModel(Settings.ProviderId, newModel);
                }
            }
        }

        listing.Gap(6f);

        if (listing.ButtonText("Translator_ModSettingsValidateConfig".Translate())) {
            BeginValidateConfig();
        }

        if (_validateConfigTask is not null) {
            GUI.color = Color.yellow;
            listing.Label("Translator_ModSettingsValidating".Translate());
            GUI.color = Color.white;
        } else if (!_lastValidationStatus.NullOrEmpty()) {
            GUI.color = _lastValidationFailed ? new Color(0.95f, 0.35f, 0.35f) : new Color(0.35f, 0.95f, 0.35f);
            listing.Label(_lastValidationStatus);
            GUI.color = Color.white;
        }
    }

    private void DrawAdvancedSettings(Listing_Standard listing) {
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

    private void OpenProviderMenu() {
        var options = LlmProviderPresets.All
            .Select(preset => new FloatMenuOption(preset.Label, () => SelectProvider(preset.Id)))
            .ToList();
        options.Add(new FloatMenuOption(LlmProviderPresets.CustomLabelKey.Translate(),
            () => SelectProvider(LlmProviderPresets.CustomId)));
        Find.WindowStack.Add(new FloatMenu(options));
    }

    private void SelectProvider(string providerId) {
        Settings.ProviderId = providerId;
        _availableModels.Clear();
        _modelsFetchedProvider = string.Empty;
        _validateConfigTask = null;
        _lastValidationStatus = string.Empty;
        _lastValidationFailed = false;
    }

    private void OpenModelMenu() {
        if (_availableModels.Count == 0) {
            StartFetchModels();
        }

        var options = new List<FloatMenuOption>();
        if (_fetchModelsTask is not null) {
            options.Add(new FloatMenuOption("Translator_ModSettingModelsLoading".Translate(), null));
        } else {
            options.AddRange(_availableModels.Select(model => new FloatMenuOption(model, () => SelectModel(model))));
        }

        options.Add(new FloatMenuOption("Translator_ModSettingModelCustom".Translate(),
            () => _customModelProviders.Add(Settings.ProviderId)));
        Find.WindowStack.Add(new FloatMenu(options));
    }

    private void SelectModel(string model) {
        Settings.SetModel(Settings.ProviderId, model);
        _customModelProviders.Remove(Settings.ProviderId);
    }

    private bool IsCustomModel(string providerId, string model) {
        if (_customModelProviders.Contains(providerId)) return true;
        return !model.NullOrEmpty() && _availableModels.Count > 0 && !_availableModels.Contains(model);
    }

    private void StartFetchModels() {
        if (_fetchModelsTask is not null) return;

        _availableModels.Clear();
        _fetchModelsTask = Task.Run(LlmTranslateService.FetchModels);
    }

    private void ConsumeModelFetchTaskResultIfReady() {
        if (_fetchModelsTask is null || !_fetchModelsTask.IsCompleted) return;

        var completedTask = _fetchModelsTask;
        _fetchModelsTask = null;
        var result = completedTask.GetAwaiter().GetResult();
        if (!result.Success) {
            Messages.Message("Translator_ModSettingModelsFailed".Translate(result.Message),
                MessageTypeDefOf.SilentInput);
            return;
        }

        _availableModels.Clear();
        _availableModels.AddRange(result.Models);
    }

    private static string GetProtocolLabel(LlmApiProtocol protocol) {
        return protocol == LlmApiProtocol.Responses
            ? "Translator_ModSettingProtocolResponses".Translate()
            : "Translator_ModSettingProtocolChat".Translate();
    }

    private static string GetProviderLabel(string providerId) {
        return LlmProviderPresets.Find(providerId)?.Label
               ?? LlmProviderPresets.CustomLabelKey.Translate();
    }

    private static string GetOutputLocationLabel(OutputLocationMode mode) {
        var modeText = mode == OutputLocationMode.OriginalMod
            ? "Translator_OutputModeOriginalMod".Translate()
            : "Translator_OutputModeGeneratedMod".Translate();
        return $"{"Translator_ModSettingDefaultOutputLocation".Translate()}: {modeText}";
    }
}