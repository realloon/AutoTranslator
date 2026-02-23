using UnityEngine;
using RimWorld;
using Verse;
using Translator;
using Translator.Services;

namespace Translator.Windows;

// ReSharper disable once InconsistentNaming
public class Window_TranslatorMain : Window {
    private static readonly Color SectionDividerColor = new(1f, 1f, 1f, 0.2f);
    private static readonly Color ErrorColor = new(0.95f, 0.35f, 0.35f);
    private static readonly Color SuccessColor = new(0.35f, 0.95f, 0.35f);

    public override Vector2 InitialSize => new(760f, 520f);

    private readonly List<ModMetaData> _allMods = [];
    private readonly List<ModMetaData> _filteredMods = [];
    private readonly List<LoadedLanguage> _exportLanguages = [];
    private readonly HashSet<string> _selectedExportLanguageFolders = new(StringComparer.OrdinalIgnoreCase);

    private Vector2 _modsScrollPos = Vector2.zero;
    private string _searchTerm = string.Empty;
    private string? _selectedPackageId;
    private string? _lastExportStatus;
    private bool _lastExportFailed;
    private string? _lastOutputPath;
    private int _activeTranslateRunToken;
    private bool _translateInProgress;

    public Window_TranslatorMain() {
        doCloseX = true;
        absorbInputAroundWindow = true;

        _allMods.AddRange(ModsConfig.ActiveModsInLoadOrder.Where(mod => !IsOfficialLudeonMod(mod)));
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

        var searchLabelRect = new Rect(rect.x, y, 84f, 24f);
        Widgets.Label(searchLabelRect, "Translator_ModSearchLabel".Translate());
        var searchFieldRect = new Rect(searchLabelRect.xMax + 6f, y, rect.width - searchLabelRect.width - 6f, 24f);
        var newSearchTerm = Widgets.TextField(searchFieldRect, _searchTerm);
        if (newSearchTerm != _searchTerm) {
            _searchTerm = newSearchTerm;
            RefreshFilteredMods();
        }

        y += 28f;

        Widgets.Label(new Rect(rect.x, y, rect.width, 24f),
            "Translator_ModCount".Translate(_filteredMods.Count, _allMods.Count));
        y += 24f;

        var listRect = new Rect(rect.x, y, rect.width, rect.yMax - y);
        var viewRect = new Rect(0f, 0f, listRect.width, _filteredMods.Count * 46f);
        Widgets.BeginScrollView(listRect, ref _modsScrollPos, viewRect, showScrollbars: false);

        var rowY = 0f;
        foreach (var mod in _filteredMods) {
            var rowRect = new Rect(0f, rowY, viewRect.width, 42f);
            var isSelected = _selectedPackageId == mod.PackageId;
            if (isSelected) {
                Widgets.DrawHighlightSelected(rowRect);
            } else if (Mouse.IsOver(rowRect)) {
                Widgets.DrawHighlight(rowRect);
            }

            if (Widgets.ButtonInvisible(rowRect)) {
                _selectedPackageId = mod.PackageId;
            }

            Widgets.Label(new Rect(rowRect.x + 8f, rowRect.y + 2f, rowRect.width - 16f, 22f), mod.Name);
            GUI.color = ColoredText.SubtleGrayColor;
            Widgets.Label(new Rect(rowRect.x + 8f, rowRect.y + 20f, rowRect.width - 16f, 20f),
                mod.PackageIdPlayerFacing);
            GUI.color = Color.white;

            rowY += 42f;
        }

        Widgets.EndScrollView();
    }

    private void DrawWorkflowPanel(Rect rect) {
        var y = rect.y + 2f;

        var selectedMod = GetSelectedMod();
        if (selectedMod is null) {
            Widgets.Label(new Rect(rect.x, y, rect.width, 48f), "Translator_NoModSelected".Translate());
            return;
        }

        Widgets.Label(new Rect(rect.x, y, rect.width, 24f), "Translator_SelectedModLabel".Translate(selectedMod.Name));
        y += 22f;
        GUI.color = ColoredText.SubtleGrayColor;
        Widgets.Label(new Rect(rect.x, y, rect.width, 22f), selectedMod.PackageIdPlayerFacing);
        GUI.color = Color.white;
        y += 34f;

        var (stats, translateStats) = StatsService.GetOrBuildStats(selectedMod);

        Widgets.Label(new Rect(rect.x, y, rect.width, 24f), "Translator_DefStatsTitle".Translate());
        y += 26f;
        Widgets.Label(new Rect(rect.x, y, rect.width, 22f),
            "Translator_DefStatsFields".Translate(stats.TranslatableInjectionItemCount));
        y += 22f;
        Widgets.Label(new Rect(rect.x, y, rect.width, 22f),
            "Translator_DefStatsMissingDefInjection".Translate(stats.MissingDefInjectionCount));
        y += 36f;

        Widgets.DrawLineHorizontal(rect.x, y, rect.width, SectionDividerColor);
        y += 16f;

        Widgets.Label(new Rect(rect.x, y, rect.width, 24f), "Translator_StaticScanTitle".Translate());
        y += 26f;
        Widgets.Label(new Rect(rect.x, y, rect.width, 22f),
            "Translator_StaticScanUniqueKeys".Translate(translateStats.UniqueLiteralKeyCount));
        y += 22f;
        Widgets.Label(new Rect(rect.x, y, rect.width, 22f),
            "Translator_StaticScanMissingKeys".Translate(translateStats.MissingKeyCount));
        y += 30f;

        var termbaseButtonLabel = "Translator_TermbaseButton".Translate();
        var exportButtonLabel = "Translator_ExportIrButton".Translate();
        const float buttonGap = 8f;
        var termbaseButtonWidth = Mathf.Max(130f, Text.CalcSize(termbaseButtonLabel).x + 28f);
        var exportButtonWidth = Mathf.Max(210f, Text.CalcSize(exportButtonLabel).x + 28f);
        if (termbaseButtonWidth + buttonGap + exportButtonWidth > rect.width) {
            var halfWidth = (rect.width - buttonGap) / 2f;
            termbaseButtonWidth = halfWidth;
            exportButtonWidth = halfWidth;
        }

        var termbaseButtonRect = new Rect(rect.x, y, termbaseButtonWidth, 30f);
        var exportButtonRect = new Rect(termbaseButtonRect.xMax + buttonGap, y, exportButtonWidth, 30f);
        if (Widgets.ButtonText(termbaseButtonRect, termbaseButtonLabel)) {
            OpenTermbaseWindow();
        }

        if (Widgets.ButtonText(exportButtonRect, exportButtonLabel)) {
            OpenExportLanguagePicker(selectedMod);
        }

        y += 36f;

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
            _lastExportFailed = true;
            _lastExportStatus = "Translator_AiTranslateFailed".Translate("A translation task is already running.");
            Messages.Message(_lastExportStatus, MessageTypeDefOf.RejectInput);
            return;
        }

        var runToken = ++_activeTranslateRunToken;
        _translateInProgress = true;
        _lastOutputPath = null;

        var configValidation = LlmTranslateService.ValidateCurrentConfig(testConnection: false);
        if (!configValidation.Success) {
            _lastExportFailed = true;
            _lastExportStatus = "Translator_AiTranslateConfigInvalid".Translate(configValidation.Message);
            Messages.Message(_lastExportStatus, MessageTypeDefOf.RejectInput);
            _translateInProgress = false;
            return;
        }

        var selectedLanguages = ResolveSelectedLanguages(selectedLanguageFolders);
        if (selectedLanguages.Count == 0) {
            _lastExportFailed = true;
            _lastExportStatus = "Translator_ExportNoLanguageSelected".Translate();
            Messages.Message("Translator_ExportNoLanguageSelected".Translate(), MessageTypeDefOf.RejectInput);
            _translateInProgress = false;
            return;
        }

        var exportResult = IrExportService.Export(
            selectedMod,
            selectedLanguages,
            LanguageDatabase.defaultLanguage,
            outputLocationMode);
        if (!exportResult.Success || exportResult.FilePath.NullOrEmpty()) {
            var error = exportResult.Message.NullOrEmpty() ? "Export failed." : exportResult.Message;
            _lastExportFailed = true;
            _lastExportStatus = "Translator_AiTranslateFailed".Translate(error);
            Messages.Message(_lastExportStatus, MessageTypeDefOf.RejectInput);
            _translateInProgress = false;
            return;
        }

        _lastOutputPath = exportResult.FilePath;

        var worksets = exportResult.Worksets
            .Where(workset => workset is not null && !workset.LanguageFolderName.NullOrEmpty())
            .ToList();
        if (worksets.Count == 0) {
            _lastExportFailed = true;
            _lastExportStatus = "Translator_AiTranslateFailed".Translate("No worksets were generated.");
            Messages.Message(_lastExportStatus, MessageTypeDefOf.RejectInput);
            _translateInProgress = false;
            return;
        }

        var preflightCounts = worksets
            .Select(workset => new LanguageWorksetSnapshot {
                LanguageFolderName = workset.LanguageFolderName,
                Counts = CountWorksetItems(workset)
            })
            .ToList();
        var totalCollectedEntries = preflightCounts.Sum(item => item.Counts.TotalCount);
        if (totalCollectedEntries == 0) {
            var reason =
                $"No translatable entries were collected for selected languages. {BuildSnapshotSummary(preflightCounts)}";
            _lastExportFailed = true;
            _lastExportStatus = BuildExportStatusWithOutput(
                "Translator_AiTranslateFailed".Translate(reason),
                exportResult.FilePath!);
            Messages.Message(_lastExportStatus, MessageTypeDefOf.RejectInput);
            _translateInProgress = false;
            return;
        }

        var targetLanguageByFolder = selectedLanguages.ToDictionary(
            language => language.folderName,
            language => language.DisplayName.NullOrEmpty() ? language.folderName : language.DisplayName,
            StringComparer.OrdinalIgnoreCase);
        var targets = worksets.Select(workset => new AiTranslateTarget {
            Workset = workset,
            FolderName = workset.LanguageFolderName,
            TargetLanguage = targetLanguageByFolder.TryGetValue(workset.LanguageFolderName, out var value)
                ? value
                : workset.LanguageFolderName
        }).ToList();
        if (targets.Count == 0) {
            _lastExportFailed = true;
            _lastExportStatus = BuildExportStatusWithOutput(
                "Translator_AiTranslateFailed".Translate("No translation targets were created."),
                exportResult.FilePath!);
            Messages.Message(_lastExportStatus, MessageTypeDefOf.RejectInput);
            _translateInProgress = false;
            return;
        }

        _lastExportFailed = false;
        _lastExportStatus = "Translator_AiTranslateInProgress".Translate();

        var runResult = new AiTranslateRunResult {
            OutputModPath = exportResult.FilePath!
        };
        var runResultGate = new object();
        LongEventHandler.QueueLongEvent(() => {
            try {
                foreach (var target in targets) {
                    lock (runResultGate) {
                        runResult.ProcessedTargetCount += 1;
                    }

                    var beforeCounts = CountWorksetItems(target.Workset);
                    var translateResult = LlmTranslateService.TranslateWorkset(
                        target.Workset,
                        target.FolderName,
                        target.TargetLanguage);
                    if (!translateResult.Success) {
                        lock (runResultGate) {
                            runResult.Failures.Add($"{target.FolderName}: {translateResult.Message}");
                        }

                        continue;
                    }

                    if (beforeCounts.PendingCount > 0 && translateResult.PendingCount == 0) {
                        lock (runResultGate) {
                            runResult.Failures.Add(
                                $"{target.FolderName}: Pending count mismatch (beforePending={beforeCounts.PendingCount}, translatePending=0).");
                        }

                        continue;
                    }

                    if (translateResult is { PendingCount: > 0, UpdatedCount: 0 }) {
                        lock (runResultGate) {
                            runResult.Failures.Add(
                                $"{target.FolderName}: LLM returned no usable translations (pending={translateResult.PendingCount}, updated=0).");
                        }

                        continue;
                    }

                    var xmlWriteResult = LanguageXmlWriteService.WriteFromWorkset(
                        runResult.OutputModPath,
                        target.Workset);
                    if (!xmlWriteResult.Success) {
                        lock (runResultGate) {
                            runResult.Failures.Add($"{target.FolderName}/XML: {xmlWriteResult.Message}");
                        }

                        continue;
                    }

                    var afterCounts = CountWorksetItems(target.Workset);
                    lock (runResultGate) {
                        runResult.UpdatedCount += translateResult.UpdatedCount;
                        runResult.WrittenEntryCount += xmlWriteResult.WrittenEntryCount;
                        runResult.WrittenFileCount += xmlWriteResult.WrittenFileCount;
                    }

                    if (xmlWriteResult.WrittenEntryCount == 0) {
                        lock (runResultGate) {
                            runResult.Failures.Add(
                                $"{target.FolderName}: No XML entries written (pending={translateResult.PendingCount}, updated={translateResult.UpdatedCount}, total={afterCounts.TotalCount}, translated={afterCounts.TranslatedCount}).");
                        }
                    }
                }
            } catch (Exception ex) {
                lock (runResultGate) {
                    runResult.ErrorMessage = ex.Message;
                }
            } finally {
                AiTranslateRunSnapshot snapshot;
                lock (runResultGate) {
                    snapshot = new AiTranslateRunSnapshot {
                        OutputModPath = runResult.OutputModPath,
                        ErrorMessage = runResult.ErrorMessage,
                        ProcessedTargetCount = runResult.ProcessedTargetCount,
                        UpdatedCount = runResult.UpdatedCount,
                        WrittenEntryCount = runResult.WrittenEntryCount,
                        WrittenFileCount = runResult.WrittenFileCount,
                        Failures = runResult.Failures.ToList()
                    };
                }

                LongEventHandler.ExecuteWhenFinished(() => ApplyRunResult(runToken, snapshot));
            }
        }, "Translator_AiTranslateInProgress", doAsynchronously: true, null);
    }

    private void ApplyRunResult(int runToken, AiTranslateRunSnapshot snapshot) {
        if (runToken != _activeTranslateRunToken) {
            return;
        }

        _translateInProgress = false;

        if (!snapshot.ErrorMessage.NullOrEmpty()) {
            _lastExportFailed = true;
            _lastExportStatus = "Translator_AiTranslateFailed".Translate(snapshot.ErrorMessage);
            Messages.Message(_lastExportStatus, MessageTypeDefOf.RejectInput);
            return;
        }

        if (snapshot.ProcessedTargetCount == 0) {
            _lastExportFailed = true;
            _lastExportStatus = BuildExportStatusWithOutput(
                "Translator_AiTranslateFailed".Translate("Translation job did not process any language targets."),
                snapshot.OutputModPath);
            Messages.Message(_lastExportStatus, MessageTypeDefOf.RejectInput);
            return;
        }

        if (snapshot is { WrittenEntryCount: 0, UpdatedCount: 0 }) {
            _lastExportFailed = true;
            var noOutputReason =
                $"Translation completed with no output. {BuildRunSummary(snapshot.ProcessedTargetCount, snapshot.UpdatedCount, snapshot.WrittenEntryCount, snapshot.WrittenFileCount)}";
            _lastExportStatus = BuildExportStatusWithOutput(
                "Translator_AiTranslateFailed".Translate(noOutputReason),
                snapshot.OutputModPath);
            Messages.Message(_lastExportStatus, MessageTypeDefOf.RejectInput);
            return;
        }

        if (snapshot.Failures.Count == 0) {
            _lastExportFailed = false;
            _lastExportStatus = "Translated mod exported.";
            Messages.Message(_lastExportStatus, MessageTypeDefOf.TaskCompletion);
            return;
        }

        var failureSummary = BuildFailureSummary(snapshot.Failures);
        _lastExportFailed = true;
        _lastExportStatus = BuildExportStatusWithOutput(
            "Translator_AiTranslatePartialFailedWithDetails".Translate(failureSummary),
            snapshot.OutputModPath);
        Messages.Message(_lastExportStatus, MessageTypeDefOf.RejectInput);
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

    private static string BuildRunSummary(int processedTargetCount, int updatedCount, int writtenEntryCount,
        int writtenFileCount) {
        return
            $"processedTargets={processedTargetCount}, updated={updatedCount}, writtenEntries={writtenEntryCount}, writtenFiles={writtenFileCount}";
    }

    private static WorksetItemCounts CountWorksetItems(LanguageWorksetFile workset) {
        var keyedTotal = workset.Keyed.Count;
        var keyedTranslated = workset.Keyed.Count(item => !item.Translation.NullOrEmpty());
        var defTotal = workset.DefInjected.Count;
        var defTranslated = workset.DefInjected.Count(item => !item.Translation.NullOrEmpty());
        return new WorksetItemCounts {
            KeyedTotal = keyedTotal,
            KeyedTranslated = keyedTranslated,
            DefTotal = defTotal,
            DefTranslated = defTranslated
        };
    }

    private static string BuildSnapshotSummary(IReadOnlyCollection<LanguageWorksetSnapshot> snapshots) {
        return string.Join(" | ",
            snapshots.Select(snapshot => $"{snapshot.LanguageFolderName}({snapshot.Counts.Summary})"));
    }

    private List<LoadedLanguage> ResolveSelectedLanguages(IReadOnlyCollection<string> selectedLanguageFolders) {
        return _exportLanguages
            .Where(language => selectedLanguageFolders.Contains(language.folderName))
            .ToList();
    }

    private sealed class AiTranslateRunResult {
        public string OutputModPath = string.Empty;
        public string ErrorMessage = string.Empty;
        public readonly List<string> Failures = [];
        public int ProcessedTargetCount;
        public int UpdatedCount;
        public int WrittenEntryCount;
        public int WrittenFileCount;
    }

    private sealed class AiTranslateRunSnapshot {
        public string OutputModPath = string.Empty;
        public string ErrorMessage = string.Empty;
        public List<string> Failures = [];
        public int ProcessedTargetCount;
        public int UpdatedCount;
        public int WrittenEntryCount;
        public int WrittenFileCount;
    }

    private sealed class AiTranslateTarget {
        public LanguageWorksetFile Workset = new();
        public string FolderName = string.Empty;
        public string TargetLanguage = string.Empty;
    }

    private sealed class LanguageWorksetSnapshot {
        public string LanguageFolderName = string.Empty;
        public WorksetItemCounts Counts = new();
    }

    private sealed class WorksetItemCounts {
        public int KeyedTotal;
        public int KeyedTranslated;
        public int DefTotal;
        public int DefTranslated;

        public int TotalCount => KeyedTotal + DefTotal;
        public int TranslatedCount => KeyedTranslated + DefTranslated;
        public int PendingCount => TotalCount - TranslatedCount;

        public string Summary =>
            $"total={TotalCount}, translated={TranslatedCount}, pending={PendingCount}, keyed={KeyedTotal}/{KeyedTranslated}, def={DefTotal}/{DefTranslated}";
    }

    private ModMetaData? GetSelectedMod() {
        if (_selectedPackageId is null) {
            return null;
        }

        var selected = _allMods.FirstOrDefault(m => m.PackageId == _selectedPackageId);
        if (selected is null) {
            _selectedPackageId = null;
        }

        return selected;
    }

    private void RefreshFilteredMods() {
        _filteredMods.Clear();

        if (string.IsNullOrWhiteSpace(_searchTerm)) {
            _filteredMods.AddRange(_allMods);
        } else {
            var keyword = _searchTerm.Trim();
            _filteredMods.AddRange(_allMods.Where(m =>
                m.Name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0
                || m.PackageIdPlayerFacing.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0));
        }

        if (_selectedPackageId is null && _filteredMods.Count > 0) {
            _selectedPackageId = _filteredMods[0].PackageId;
        }
    }

    private static bool IsOfficialLudeonMod(ModMetaData mod) {
        return mod.PackageIdPlayerFacing.StartsWith("Ludeon.RimWorld", StringComparison.OrdinalIgnoreCase);
    }
}
