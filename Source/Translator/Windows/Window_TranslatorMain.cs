using RimWorld;
using Translator.Services;
using UnityEngine;
using Verse;

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

        var exportButtonLabel = "Translator_ExportIrButton".Translate();
        var exportButtonWidth = Mathf.Min(rect.width, Mathf.Max(210f, Text.CalcSize(exportButtonLabel).x + 28f));
        var exportButtonRect = new Rect(rect.x, y, exportButtonWidth, 30f);
        if (Widgets.ButtonText(exportButtonRect, exportButtonLabel)) {
            OpenExportLanguagePicker(selectedMod);
        }

        y += 36f;
        if (!_lastExportStatus.NullOrEmpty()) {
            var statusText = _lastExportStatus!;
            var separatorIndex = statusText.IndexOf('\n');
            if (separatorIndex < 0) {
                GUI.color = _lastExportFailed ? ErrorColor : SuccessColor;
                var statusHeight = Mathf.Max(40f, Text.CalcHeight(statusText, rect.width));
                Widgets.Label(new Rect(rect.x, y, rect.width, statusHeight), statusText);
                GUI.color = Color.white;
            } else {
                var title = statusText[..separatorIndex];
                var outputLine = statusText[(separatorIndex + 1)..];

                GUI.color = _lastExportFailed ? ErrorColor : SuccessColor;
                var titleHeight = Mathf.Max(20f, Text.CalcHeight(title, rect.width));
                Widgets.Label(new Rect(rect.x, y, rect.width, titleHeight), title);

                GUI.color = ColoredText.SubtleGrayColor;
                var outputHeight = Mathf.Max(20f, Text.CalcHeight(outputLine, rect.width));
                Widgets.Label(new Rect(rect.x, y + titleHeight + 2f, rect.width, outputHeight), outputLine);
                GUI.color = Color.white;
            }
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

    private void OpenLanguagePicker(ModMetaData selectedMod,
        Action<ModMetaData, IReadOnlyCollection<string>> onConfirmAction) {
        Find.WindowStack.Add(new Window_ExportLanguagePicker(
            _exportLanguages,
            _selectedExportLanguageFolders,
            selectedLanguageFolders => {
                _selectedExportLanguageFolders.Clear();
                _selectedExportLanguageFolders.UnionWith(selectedLanguageFolders);
                onConfirmAction(selectedMod, selectedLanguageFolders);
            }));
    }

    private void TryExportAndAiTranslate(ModMetaData selectedMod, IReadOnlyCollection<string> selectedLanguageFolders) {
        var configValidation = LlmTranslateService.ValidateCurrentConfig(testConnection: false);
        if (!configValidation.Success) {
            _lastExportFailed = true;
            _lastExportStatus = "Translator_AiTranslateConfigInvalid".Translate(configValidation.Message);
            Messages.Message(_lastExportStatus, MessageTypeDefOf.RejectInput);
            return;
        }

        var selectedLanguages = ResolveSelectedLanguages(selectedLanguageFolders);
        if (selectedLanguages.Count == 0) {
            _lastExportFailed = true;
            _lastExportStatus = "Translator_ExportNoLanguageSelected".Translate();
            Messages.Message("Translator_ExportNoLanguageSelected".Translate(), MessageTypeDefOf.RejectInput);
            return;
        }

        var exportResult = IrExportService.Export(
            selectedMod,
            selectedLanguages,
            LanguageDatabase.defaultLanguage);
        if (!exportResult.Success || exportResult.FilePath.NullOrEmpty()) {
            var error = exportResult.Message.NullOrEmpty() ? "Export failed." : exportResult.Message;
            _lastExportFailed = true;
            _lastExportStatus = "Translator_AiTranslateFailed".Translate(error);
            Messages.Message(_lastExportStatus, MessageTypeDefOf.RejectInput);
            return;
        }

        var worksets = exportResult.Worksets
            .Where(workset => workset is not null && !workset.LanguageFolderName.NullOrEmpty())
            .ToList();
        if (worksets.Count == 0) {
            _lastExportFailed = true;
            _lastExportStatus = "Translator_AiTranslateFailed".Translate("No worksets were generated.");
            Messages.Message(_lastExportStatus, MessageTypeDefOf.RejectInput);
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

        _lastExportFailed = false;
        _lastExportStatus = "Translator_AiTranslateInProgress".Translate();

        var runResult = new AiTranslateRunResult {
            OutputModPath = exportResult.FilePath!
        };
        LongEventHandler.QueueLongEvent(() => {
            try {
                foreach (var target in targets) {
                    var translateResult = LlmTranslateService.TranslateWorkset(target.Workset, target.TargetLanguage);
                    if (!translateResult.Success) {
                        runResult.Failures.Add($"{target.FolderName}: {translateResult.Message}");
                        continue;
                    }

                    var xmlWriteResult = LanguageXmlWriteService.WriteFromWorkset(
                        runResult.OutputModPath,
                        target.Workset);

                    if (xmlWriteResult.Success) continue;

                    runResult.Failures.Add($"{target.FolderName}/XML: {xmlWriteResult.Message}");
                }
            } catch (Exception ex) {
                runResult.ErrorMessage = ex.Message;
            }
        }, "Translator_AiTranslateInProgress", doAsynchronously: true, null);

        LongEventHandler.ExecuteWhenFinished(() => {
            if (!runResult.ErrorMessage.NullOrEmpty()) {
                _lastExportFailed = true;
                _lastExportStatus = "Translator_AiTranslateFailed".Translate(runResult.ErrorMessage);
                Messages.Message(_lastExportStatus, MessageTypeDefOf.RejectInput);
                return;
            }

            if (runResult.Failures.Count == 0) {
                _lastExportFailed = false;
                _lastExportStatus = BuildExportStatusWithOutput(
                    "Translator_AiTranslateSuccess".Translate(),
                    runResult.OutputModPath);
                Messages.Message(_lastExportStatus, MessageTypeDefOf.TaskCompletion);
                return;
            }

            var failureSummary = BuildFailureSummary(runResult.Failures);
            _lastExportFailed = true;
            _lastExportStatus = BuildExportStatusWithOutput(
                "Translator_AiTranslatePartialFailedWithDetails".Translate(failureSummary),
                runResult.OutputModPath);
            Messages.Message(_lastExportStatus, MessageTypeDefOf.RejectInput);
        });
    }

    private static string BuildExportStatusWithOutput(string title, string outputPath) {
        return $"{title}\n{"Translator_OutputDirectory".Translate(outputPath)}";
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

    private List<LoadedLanguage> ResolveSelectedLanguages(IReadOnlyCollection<string> selectedLanguageFolders) {
        return _exportLanguages
            .Where(language => selectedLanguageFolders.Contains(language.folderName))
            .ToList();
    }

    private sealed class AiTranslateRunResult {
        public string OutputModPath = string.Empty;
        public string ErrorMessage = string.Empty;
        public readonly List<string> Failures = [];
    }

    private sealed class AiTranslateTarget {
        public LanguageWorksetFile Workset = new();
        public string FolderName = string.Empty;
        public string TargetLanguage = string.Empty;
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
