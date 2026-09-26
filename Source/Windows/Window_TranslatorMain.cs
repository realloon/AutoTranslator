using UnityEngine;
using RimWorld;
using Verse;
using Translator.Services;

namespace Translator.Windows;

// ReSharper disable once InconsistentNaming
public class Window_TranslatorMain : Window {
    private static readonly Color SectionDividerColor = new(1f, 1f, 1f, 0.2f);
    private static readonly Color ErrorColor = new(0.95f, 0.35f, 0.35f);
    private static readonly Color SuccessColor = new(0.35f, 0.95f, 0.35f);
    private readonly List<ModMetaData> _allMods = [];
    private readonly Dictionary<string, ModMetaData> _allModsByPackageId = new(StringComparer.Ordinal);
    private readonly List<ModMetaData> _filteredMods = [];
    private readonly List<LoadedLanguage> _exportLanguages = [];
    private readonly HashSet<string> _selectedExportLanguageFolders = new(StringComparer.OrdinalIgnoreCase);
    private readonly QuickSearchWidget _quickSearchWidget = new();
    private Vector2 _modsScrollPos = Vector2.zero;
    private string? _selectedPackageId;
    private string? _lastExportStatus;
    private bool _lastExportFailed;
    private string? _lastOutputPath;
    private bool _translateInProgress;

    public override Vector2 InitialSize => new(760f, 520f);

    public Window_TranslatorMain() {
        doCloseX = true;
        absorbInputAroundWindow = true;

        _allMods.AddRange(ModsConfig.ActiveModsInLoadOrder.Where(mod => !IsOfficialLudeonMod(mod)));
        foreach (var mod in _allMods) {
            _allModsByPackageId[mod.PackageId] = mod;
        }

        InitializeExportLanguages();
        RefreshFilteredMods();
    }

    public override void DoWindowContents(Rect inRect) {
        var y = 0f;

        Text.Font = GameFont.Medium;
        Widgets.Label(new Rect(0f, y, inRect.width, 36f), "Translator_WindowTitle".Translate());
        y += 42f;
        Text.Font = GameFont.Small;

        var contentRect = new Rect(0f, y, inRect.width, inRect.height - y);
        DrawContent(contentRect);
    }

    private void DrawContent(Rect rect) {
        const float gap = 12f;
        var leftWidth = rect.width * 0.3f;
        var leftRect = new Rect(rect.x, rect.y, leftWidth, rect.height);
        var rightRect = new Rect(leftRect.xMax + gap, rect.y, rect.width - leftWidth - gap, rect.height);

        DrawModListPanel(leftRect);
        DrawWorkflowPanel(rightRect);
    }

    private void DrawModListPanel(Rect rect) {
        var y = rect.y + 2f;

        _quickSearchWidget.noResultsMatched = _filteredMods.Count == 0;
        _quickSearchWidget.OnGUI(new Rect(rect.x, y, rect.width - 16f,
            QuickSearchWidget.WidgetHeight), RefreshFilteredMods);

        y += 28f;

        if (_quickSearchWidget.filter.Active) {
            Widgets.Label(new Rect(rect.x, y, rect.width, 24f),
                "Translator_ModCount".Translate(_filteredMods.Count, _allMods.Count));
            y += 24f;
        }

        var listRect = new Rect(rect.x, y, rect.width, rect.yMax - y);
        var viewWidth = listRect.width - 16f;
        var viewRect = new Rect(0f, 0f, viewWidth, _filteredMods.Count * 46f);
        Widgets.BeginScrollView(listRect, ref _modsScrollPos, viewRect);

        const float rowHeight = 42f;
        var firstVisible = Mathf.Max(0, Mathf.FloorToInt(_modsScrollPos.y / rowHeight));
        var lastVisible = Mathf.Min(_filteredMods.Count - 1,
            Mathf.CeilToInt((_modsScrollPos.y + listRect.height) / rowHeight));
        for (var i = firstVisible; i <= lastVisible; i++) {
            var mod = _filteredMods[i];
            var rowRect = new Rect(0f, i * rowHeight, viewRect.width, rowHeight);
            var isSelected = _selectedPackageId == mod.PackageId;
            if (isSelected) {
                Widgets.DrawHighlightSelected(rowRect);
            } else if (Mouse.IsOver(rowRect)) {
                Widgets.DrawHighlight(rowRect);
            }

            if (Widgets.ButtonInvisible(rowRect)) {
                _selectedPackageId = mod.PackageId;
            }

            Widgets.LabelEllipses(new Rect(rowRect.x + 8f, rowRect.y + 2f, rowRect.width - 16f, 22f), mod.Name);
            GUI.color = ColoredText.SubtleGrayColor;
            Widgets.LabelEllipses(new Rect(rowRect.x + 8f, rowRect.y + 20f, rowRect.width - 16f, 20f),
                mod.PackageIdPlayerFacing);
            GUI.color = Color.white;
        }

        Widgets.EndScrollView();
    }

    private void DrawWorkflowPanel(Rect rect) {
        var y = rect.y;
        var selectedMod = GetSelectedMod();
        var stats = StatsService.RequestStats(selectedMod);

        Widgets.Label(new Rect(rect.x, y, rect.width, 24f), selectedMod.Name);
        y += 24f;

        if (stats is null) {
            GUI.color = ColoredText.SubtleGrayColor;
            Widgets.Label(new Rect(rect.x, y, rect.width, 24f), "Translator_StatsComputing".Translate());
            GUI.color = Color.white;
            y += 24f;
        } else {
            void DrawLabel(string text, float spacing = 24f) {
                Widgets.Label(new Rect(rect.x, y, rect.width, 24f), text);
                y += spacing;
            }

            void DrawSection(string titleKey, TranslationSectionStats section, float trailingSpacing) {
                DrawLabel(titleKey.Translate());
                DrawLabel("Translator_TranslatableEntries".Translate(section.TranslatableCount));
                DrawLabel("Translator_MissingEntries".Translate(section.MissingCount), trailingSpacing);
            }

            DrawSection("Translator_DefStatsTitle", stats.DefStats, 32f);

            Widgets.DrawLineHorizontal(rect.x, y, rect.width, SectionDividerColor);
            y += 12f;

            DrawSection("Translator_StaticScanTitle", stats.KeyStats, 36f);
        }

        var termbaseButtonLabel = "Translator_TermbaseButton".Translate();
        var exportButtonLabel = "Translator_ExportIrButton".Translate();
        const float buttonGap = 8f;
        const float buttonHeight = 30f;
        var termbaseButtonWidth = Mathf.Max(130f, Text.CalcSize(termbaseButtonLabel).x + 8f);
        var exportButtonWidth = Mathf.Max(210f, Text.CalcSize(exportButtonLabel).x + 8f);
        if (termbaseButtonWidth + buttonGap + exportButtonWidth > rect.width) {
            var halfWidth = (rect.width - buttonGap) / 2f;
            termbaseButtonWidth = halfWidth;
            exportButtonWidth = halfWidth;
        }

        var buttonY = rect.yMax - buttonHeight;
        var termbaseButtonRect = new Rect(rect.x, buttonY, termbaseButtonWidth, buttonHeight);
        var exportButtonRect = new Rect(termbaseButtonRect.xMax + buttonGap, buttonY, exportButtonWidth, buttonHeight);
        if (Widgets.ButtonText(termbaseButtonRect, termbaseButtonLabel)) {
            OpenTermbaseWindow();
        }

        if (Widgets.ButtonText(exportButtonRect, exportButtonLabel)) {
            OpenExportLanguagePicker(selectedMod);
        }

        if (_lastExportStatus.NullOrEmpty()) return;

        var statusText = _lastExportStatus!;
        var separatorIndex = statusText.IndexOf('\n');

        if (separatorIndex < 0) {
            GUI.color = _lastExportFailed ? ErrorColor : SuccessColor;
            var statusHeight = Mathf.Max(22f, Text.CalcHeight(statusText, rect.width));
            Widgets.Label(new Rect(rect.x, y, rect.width, statusHeight), statusText);
            y += statusHeight + 2f;
        } else {
            var title = statusText[..separatorIndex];
            var outputLine = statusText[(separatorIndex + 1)..];

            GUI.color = _lastExportFailed ? ErrorColor : SuccessColor;
            var titleHeight = Mathf.Max(20f, Text.CalcHeight(title, rect.width));
            Widgets.Label(new Rect(rect.x, y, rect.width, titleHeight), title);

            GUI.color = ColoredText.SubtleGrayColor;
            var outputHeight = Mathf.Max(20f, Text.CalcHeight(outputLine, rect.width));
            Widgets.Label(new Rect(rect.x, y + titleHeight + 2f, rect.width, outputHeight), outputLine);
            y += titleHeight + outputHeight;
        }

        GUI.color = Color.white;

        if (_lastOutputPath.NullOrEmpty()) return;

        var openFolderLabel = "Translator_OpenOutputFolder".Translate();
        var openFolderButtonWidth = Mathf.Clamp(Text.CalcSize(openFolderLabel).x + 24f, 140f, rect.width);
        if (Widgets.ButtonText(new Rect(rect.x, y, openFolderButtonWidth, 28f), openFolderLabel)) {
            TryOpenOutputFolder(_lastOutputPath!);
        }
    }

    private void InitializeExportLanguages() {
        _exportLanguages.Clear();
        _selectedExportLanguageFolders.Clear();

        _exportLanguages.AddRange(LanguageDatabase.AllLoadedLanguages
            .OrderBy(language => language.folderName, StringComparer.OrdinalIgnoreCase));
        if (LanguageDatabase.activeLanguage is not null) {
            _selectedExportLanguageFolders.Add(LanguageDatabase.activeLanguage.folderName);
        }
    }

    private void OpenExportLanguagePicker(ModMetaData selectedMod) {
        OpenLanguagePicker(selectedMod, TryExportAndAiTranslate);
    }

    private static void OpenTermbaseWindow() {
        Find.WindowStack.Add(new Window_Termbase());
    }

    private void OpenLanguagePicker(ModMetaData selectedMod,
        Action<ModMetaData, IReadOnlyCollection<string>, OutputLocationMode> onConfirmAction) {
        Find.WindowStack.Add(new Window_ExportLanguagePicker(
            _exportLanguages,
            _selectedExportLanguageFolders,
            TranslatorMod.Settings.DefaultOutputLocationMode,
            (selectedLanguageFolders, outputLocationMode) => {
                _selectedExportLanguageFolders.Clear();
                _selectedExportLanguageFolders.UnionWith(selectedLanguageFolders);
                onConfirmAction(selectedMod, selectedLanguageFolders, outputLocationMode);
            }));
    }

    private void TryExportAndAiTranslate(ModMetaData selectedMod, IReadOnlyCollection<string> selectedLanguageFolders,
        OutputLocationMode outputLocationMode) {
        if (_translateInProgress) {
            ShowExportFailure("Translator_AiTranslateFailed".Translate("A translation task is already running."));
            return;
        }

        _translateInProgress = true;
        _lastOutputPath = null;

        var configValidation = LlmTranslateService.ValidateCurrentConfig(testConnection: false);
        if (!configValidation.Success) {
            ShowExportFailure("Translator_AiTranslateConfigInvalid".Translate(configValidation.Message));
            return;
        }

        var selectedLanguages = ResolveSelectedLanguages(selectedLanguageFolders);
        if (selectedLanguages.Count == 0) {
            ShowExportFailure("Translator_ExportNoLanguageSelected".Translate());
            return;
        }

        var exportResult = IrExportService.Export(
            selectedMod,
            selectedLanguages,
            LanguageDatabase.defaultLanguage,
            outputLocationMode);
        if (!exportResult.Success || exportResult.FilePath.NullOrEmpty()) {
            var error = exportResult.Message.NullOrEmpty() ? "Export failed." : exportResult.Message;
            ShowExportFailure("Translator_AiTranslateFailed".Translate(error));
            return;
        }

        var outputModPath = exportResult.FilePath!;
        _lastOutputPath = outputModPath;

        var targetLanguageByFolder = selectedLanguages.ToDictionary(
            language => language.folderName,
            language => language.DisplayName.NullOrEmpty() ? language.folderName : language.DisplayName,
            StringComparer.OrdinalIgnoreCase);
        var targets = exportResult.Worksets.Select(workset => new AiTranslateTarget {
            Workset = workset,
            FolderName = workset.LanguageFolderName,
            TargetLanguage = targetLanguageByFolder[workset.LanguageFolderName]
        }).ToList();

        _lastExportFailed = false;
        _lastExportStatus = "Translator_AiTranslateInProgress".Translate();

        var runResult = new AiTranslateRun {
            OutputModPath = outputModPath
        };
        LongEventHandler.QueueLongEvent(() => {
            try {
                foreach (var target in targets) {
                    var translateResult = LlmTranslateService.TranslateWorkset(
                        target.Workset,
                        target.FolderName,
                        target.TargetLanguage);
                    if (!translateResult.Success) {
                        runResult.Failures.Add($"{target.FolderName}: {translateResult.Message}");
                        continue;
                    }

                    var xmlWriteResult = LanguageXmlWriteService.WriteFromWorkset(
                        runResult.OutputModPath,
                        target.Workset);
                    if (!xmlWriteResult.Success) {
                        runResult.Failures.Add($"{target.FolderName}/XML: {xmlWriteResult.Message}");
                        continue;
                    }

                    runResult.UpdatedCount += translateResult.UpdatedCount;
                    runResult.WrittenEntryCount += xmlWriteResult.WrittenEntryCount;
                    runResult.WrittenFileCount += xmlWriteResult.WrittenFileCount;

                    if (xmlWriteResult.WrittenEntryCount == 0) {
                        runResult.Failures.Add(
                            $"{target.FolderName}: No XML entries written (pending={translateResult.PendingCount}, updated={translateResult.UpdatedCount}).");
                    }
                }
            } catch (Exception ex) {
                runResult.ErrorMessage = ex.Message;
            } finally {
                var snapshot = new AiTranslateRun {
                    OutputModPath = runResult.OutputModPath,
                    ErrorMessage = runResult.ErrorMessage,
                    UpdatedCount = runResult.UpdatedCount,
                    WrittenEntryCount = runResult.WrittenEntryCount,
                    WrittenFileCount = runResult.WrittenFileCount,
                    Failures = [.. runResult.Failures]
                };

                LongEventHandler.ExecuteWhenFinished(() => ApplyRunResult(snapshot));
            }
        }, "Translator_AiTranslateInProgress", doAsynchronously: true, null);
    }

    private void ApplyRunResult(AiTranslateRun snapshot) {
        _translateInProgress = false;

        if (!snapshot.ErrorMessage.NullOrEmpty()) {
            ShowExportFailure("Translator_AiTranslateFailed".Translate(snapshot.ErrorMessage));
            return;
        }

        if (snapshot is { WrittenEntryCount: 0, UpdatedCount: 0 }) {
            var noOutputReason =
                $"Translation completed with no output. {BuildRunSummary(snapshot.UpdatedCount, snapshot.WrittenEntryCount, snapshot.WrittenFileCount)}";
            ShowExportFailure(BuildExportStatusWithOutput(
                "Translator_AiTranslateFailed".Translate(noOutputReason),
                snapshot.OutputModPath));
            return;
        }

        if (snapshot.Failures.Count == 0) {
            _lastExportFailed = false;
            _lastExportStatus = "Translated mod exported.";
            Messages.Message(_lastExportStatus, MessageTypeDefOf.TaskCompletion);
            return;
        }

        var failureSummary = BuildFailureSummary(snapshot.Failures);
        ShowExportFailure(BuildExportStatusWithOutput(
            "Translator_AiTranslatePartialFailedWithDetails".Translate(failureSummary),
            snapshot.OutputModPath));
    }

    private void ShowExportFailure(string status) {
        _translateInProgress = false;
        _lastExportFailed = true;
        _lastExportStatus = status;
        Messages.Message(status, MessageTypeDefOf.RejectInput);
    }

    private static string BuildExportStatusWithOutput(string title, string outputPath) {
        return $"{title}\n{"Translator_OutputDirectory".Translate(outputPath)}";
    }

    private static void TryOpenOutputFolder(string outputPath) {
        if (outputPath.NullOrEmpty() || !Directory.Exists(outputPath)) {
            Messages.Message("Translator_OutputFolderNotFound".Translate(), MessageTypeDefOf.RejectInput);
            return;
        }

        try {
            var folderUri = new Uri(Path.GetFullPath(outputPath)).AbsoluteUri;
            Application.OpenURL(folderUri);
        } catch (Exception ex) {
            Messages.Message("Translator_OpenOutputFolderFailed".Translate(ex.Message), MessageTypeDefOf.RejectInput);
        }
    }

    private static string BuildFailureSummary(IReadOnlyList<string> failures) {
        const int maxDisplayedFailures = 3;
        var topFailures = failures
            .Take(maxDisplayedFailures)
            .Select(failure => failure.Length > 160 ? $"{failure[..160]}..." : failure)
            .ToList();

        var summary = string.Join(" | ", topFailures);
        var remaining = failures.Count - maxDisplayedFailures;
        if (remaining > 0) {
            summary += $" (+{remaining} more)";
        }

        return summary;
    }

    private static string BuildRunSummary(int updatedCount, int writtenEntryCount, int writtenFileCount) {
        return $"updated={updatedCount}, writtenEntries={writtenEntryCount}, writtenFiles={writtenFileCount}";
    }

    private List<LoadedLanguage> ResolveSelectedLanguages(IReadOnlyCollection<string> selectedLanguageFolders) {
        return [
            .. _exportLanguages
                .Where(language => selectedLanguageFolders.Contains(language.folderName))
        ];
    }

    private sealed class AiTranslateRun {
        public string OutputModPath = string.Empty;
        public string ErrorMessage = string.Empty;
        public List<string> Failures = [];
        public int UpdatedCount;
        public int WrittenEntryCount;
        public int WrittenFileCount;
    }

    private sealed class AiTranslateTarget {
        public LanguageWorksetFile Workset = new();
        public string FolderName = string.Empty;
        public string TargetLanguage = string.Empty;
    }

    private ModMetaData GetSelectedMod() {
        var selectedPackageId = _selectedPackageId ?? throw new InvalidOperationException("No mod selected.");
        return _allModsByPackageId[selectedPackageId];
    }

    private void RefreshFilteredMods() {
        _filteredMods.Clear();

        if (!_quickSearchWidget.filter.Active) {
            _filteredMods.AddRange(_allMods);
        } else {
            _filteredMods.AddRange(_allMods.Where(m =>
                _quickSearchWidget.filter.Matches(m.Name)
                || _quickSearchWidget.filter.Matches(m.PackageIdPlayerFacing)));
        }

        if (_selectedPackageId is null && _filteredMods.Count > 0) {
            _selectedPackageId = _filteredMods[0].PackageId;
        }
    }

    private static bool IsOfficialLudeonMod(ModMetaData mod) {
        return mod.PackageIdPlayerFacing.StartsWith("Ludeon.RimWorld", StringComparison.OrdinalIgnoreCase);
    }
}