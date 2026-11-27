using RimWorld;
using Translator.Export;
using Translator.Llm;
using Translator.Stats;
using Translator.Utilities;
using UnityEngine;
using Verse;

namespace Translator.UI;

// ReSharper disable once InconsistentNaming
public class Window_TranslatorMain : Window {
    private const float RowHeight = 42f;
    private readonly List<ModMetaData> _allMods = [];
    private readonly Dictionary<string, ModMetaData> _allModsByPackageId;
    private readonly List<ModMetaData> _filteredMods = [];
    private readonly List<LoadedLanguage> _exportLanguages = [];
    private readonly HashSet<string> _selectedExportLanguageFolders = new(StringComparer.OrdinalIgnoreCase);
    private readonly QuickSearchWidget _quickSearchWidget = new();
    private Vector2 _modsScrollPos;
    private string? _selectedPackageId;
    private string? _lastExportStatus;
    private string? _lastOutputPath;
    private bool _lastExportFailed;
    private bool _translateInProgress;

    public override Vector2 InitialSize => new(760f, 520f);

    public Window_TranslatorMain() {
        doCloseX = true;
        absorbInputAroundWindow = true;

        _allMods.AddRange(ModsConfig.ActiveModsInLoadOrder.Where(mod => !IsOfficialLudeonMod(mod)));
        _allModsByPackageId = _allMods.ToDictionary(mod => mod.PackageId, StringComparer.Ordinal);

        InitializeExportLanguages();
        RefreshFilteredMods();
    }

    public override void DoWindowContents(Rect inRect) {
        var y = 0f;

        Text.Font = GameFont.Medium;
        Widgets.Label(new Rect(0f, y, inRect.width, 36f), "Translator_AutoTranslator".Translate());
        y += 42f;
        Text.Font = GameFont.Small;

        DrawContent(new Rect(0f, y, inRect.width, inRect.height - y));
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
        _quickSearchWidget.OnGUI(
            new Rect(rect.x, y, rect.width - 16f, QuickSearchWidget.WidgetHeight), RefreshFilteredMods);

        y += 28f;

        if (_quickSearchWidget.filter.Active) {
            Widgets.Label(new Rect(rect.x, y, rect.width, 24f),
                "Translator_ModCount".Translate(_filteredMods.Count, _allMods.Count));
            y += 24f;
        }

        var listRect = new Rect(rect.x, y, rect.width, rect.yMax - y);
        var viewRect = new Rect(0f, 0f, listRect.width - 16f, _filteredMods.Count * 46f);
        Widgets.BeginScrollView(listRect, ref _modsScrollPos, viewRect);

        var firstVisible = Mathf.Max(0, Mathf.FloorToInt(_modsScrollPos.y / RowHeight));
        var lastVisible = Mathf.Min(
            _filteredMods.Count - 1,
            Mathf.CeilToInt((_modsScrollPos.y + listRect.height) / RowHeight));

        for (var i = firstVisible; i <= lastVisible; i++) {
            var mod = _filteredMods[i];
            var rowRect = new Rect(0f, i * RowHeight, viewRect.width, RowHeight);

            if (_selectedPackageId == mod.PackageId) {
                Widgets.DrawHighlightSelected(rowRect);
            } else if (Mouse.IsOver(rowRect)) {
                Widgets.DrawHighlight(rowRect);
            }

            if (Widgets.ButtonInvisible(rowRect)) {
                _selectedPackageId = mod.PackageId;
            }

            Widgets.LabelEllipses(
                new Rect(rowRect.x + 8f, rowRect.y + 2f, rowRect.width - 16f, 22f), mod.Name);
            using (GuiColorScope.Using(ColoredText.SubtleGrayColor)) {
                Widgets.LabelEllipses(
                    new Rect(rowRect.x + 8f, rowRect.y + 20f, rowRect.width - 16f, 20f),
                    mod.PackageIdPlayerFacing);
            }
        }

        Widgets.EndScrollView();
    }

    private void DrawWorkflowPanel(Rect rect) {
        var selectedMod = GetSelectedMod();
        if (selectedMod is null) {
            Widgets.Label(new Rect(rect.x, rect.y, rect.width, 24f), "Translator_NoModsFound".Translate());
            return;
        }

        var y = rect.y;
        var stats = StatsService.Request(selectedMod);

        Widgets.Label(new Rect(rect.x, y, rect.width, 24f), selectedMod.Name);
        y += 24f;

        if (stats is null) {
            using (GuiColorScope.Using(ColoredText.SubtleGrayColor)) {
                Widgets.Label(new Rect(rect.x, y, rect.width, 24f), "Translator_CalculatingStatistics".Translate());
            }

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

            DrawSection("Translator_FromDef", stats.DefStats, 32f);

            Widgets.DrawLineHorizontal(rect.x, y, rect.width, UiTheme.SectionDividerColor);
            y += 12f;

            DrawSection("Translator_FromKey", stats.KeyStats, 36f);
        }

        DrawActionButtons(rect, selectedMod);
        DrawStatus(rect, y);
    }

    private void DrawActionButtons(Rect rect, ModMetaData selectedMod) {
        var termbaseButtonLabel = "Translator_Termbase".Translate();
        var exportButtonLabel = "Translator_ExtractAndExport".Translate();
        var translateButtonLabel = "Translator_TranslateExport".Translate();
        const float buttonGap = 8f;
        const float buttonHeight = 30f;

        var termbaseButtonWidth = Mathf.Max(130f, Text.CalcSize(termbaseButtonLabel).x + 8f);
        var exportButtonWidth = Mathf.Max(150f, Text.CalcSize(exportButtonLabel).x + 8f);
        var translateButtonWidth = Mathf.Max(170f, Text.CalcSize(translateButtonLabel).x + 8f);

        if (termbaseButtonWidth + buttonGap + exportButtonWidth + buttonGap + translateButtonWidth >
            rect.width) {
            var thirdWidth = (rect.width - buttonGap * 2f) / 3f;
            termbaseButtonWidth = thirdWidth;
            exportButtonWidth = thirdWidth;
            translateButtonWidth = thirdWidth;
        }

        var buttonY = rect.yMax - buttonHeight;
        var termbaseButtonRect = new Rect(rect.x, buttonY, termbaseButtonWidth, buttonHeight);
        var exportButtonRect = new Rect(
            termbaseButtonRect.xMax + buttonGap, buttonY, exportButtonWidth, buttonHeight);
        var translateButtonRect = new Rect(
            exportButtonRect.xMax + buttonGap, buttonY, translateButtonWidth, buttonHeight);

        if (Widgets.ButtonText(termbaseButtonRect, termbaseButtonLabel)) {
            Find.WindowStack.Add(new Window_Termbase());
        }

        if (Widgets.ButtonText(exportButtonRect, exportButtonLabel)) {
            OpenExportLanguagePicker(selectedMod, ExtractOnly);
        }

        if (Widgets.ButtonText(translateButtonRect, translateButtonLabel)) {
            OpenExportLanguagePicker(selectedMod, TranslateLatestExport);
        }
    }

    private void DrawStatus(Rect rect, float y) {
        if (_lastExportStatus is not { Length: > 0 } statusText) {
            return;
        }

        var separator = statusText.IndexOf('\n');
        var title = separator < 0 ? statusText : statusText[..separator];
        var titleHeight = Mathf.Max(22f, Text.CalcHeight(title, rect.width));

        using (GuiColorScope.Using(_lastExportFailed ? UiTheme.ErrorColor : UiTheme.SuccessColor)) {
            Widgets.Label(new Rect(rect.x, y, rect.width, titleHeight), title);
        }

        y += titleHeight + 2f;

        if (separator >= 0) {
            var detail = statusText[(separator + 1)..];
            var detailHeight = Mathf.Max(20f, Text.CalcHeight(detail, rect.width));
            using (GuiColorScope.Using(ColoredText.SubtleGrayColor)) {
                Widgets.Label(new Rect(rect.x, y, rect.width, detailHeight), detail);
            }

            y += detailHeight;
        }

        if (_lastOutputPath is not { Length: > 0 } outputPath) {
            return;
        }

        var openFolderLabel = "Translator_OpenOutputFolder".Translate();
        var openFolderButtonWidth =
            Mathf.Clamp(Text.CalcSize(openFolderLabel).x + 24f, 140f, rect.width);
        if (Widgets.ButtonText(new Rect(rect.x, y, openFolderButtonWidth, 28f), openFolderLabel)) {
            TryOpenOutputFolder(outputPath);
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

    private void OpenExportLanguagePicker(
        ModMetaData selectedMod,
        Action<ModMetaData, IReadOnlyCollection<string>, OutputLocationMode> action) {
        Find.WindowStack.Add(new Window_ExportLanguagePicker(
            _exportLanguages,
            _selectedExportLanguageFolders,
            TranslatorMod.Settings.DefaultOutputLocationMode,
            (selectedLanguageFolders, outputLocationMode) => {
                _selectedExportLanguageFolders.Clear();
                _selectedExportLanguageFolders.UnionWith(selectedLanguageFolders);
                action(selectedMod, selectedLanguageFolders, outputLocationMode);
            }));
    }

    private void ExtractOnly(
        ModMetaData selectedMod,
        IReadOnlyCollection<string> selectedLanguageFolders,
        OutputLocationMode outputLocationMode) {
        if (_translateInProgress) {
            return;
        }

        var selectedLanguages = ResolveSelectedLanguages(selectedLanguageFolders);
        var result = IrExportService.Export(
            selectedMod, selectedLanguages, LanguageDatabase.defaultLanguage, outputLocationMode);

        if (!result.Success || result.FilePath is not { Length: > 0 } outputDir) {
            ShowExportFailure(result.Message);
            return;
        }

        var written = 0;
        foreach (var workset in result.Worksets) {
            var write = LanguageXmlWriteService.WriteFromWorkset(
                workset, outputDir, includePlaceholders: true);
            if (!write.Success) {
                ShowExportFailure(write.Message);
                return;
            }

            written += write.WrittenEntryCount;
        }

        _lastOutputPath = outputDir;
        _lastExportFailed = false;
        _lastExportStatus =
            $"Extracted {written} entries.\n{"Translator_OutputDirectory".Translate(outputDir)}";
    }

    private void TranslateLatestExport(
        ModMetaData selectedMod,
        IReadOnlyCollection<string> selectedLanguageFolders,
        OutputLocationMode outputLocationMode) {
        var prefix =
            $"TranslatorExport_{ModPath.SanitizeFileNamePart(selectedMod.PackageIdPlayerFacing, "unknown")}_";

        var output = !string.IsNullOrEmpty(_lastOutputPath) && Directory.Exists(_lastOutputPath)
            ? _lastOutputPath
            : Directory.Exists(GenFilePaths.ModsFolderPath)
                ? Directory.GetDirectories(GenFilePaths.ModsFolderPath, prefix + "*",
                        SearchOption.TopDirectoryOnly)
                    .OrderByDescending(Directory.GetCreationTimeUtc)
                    .FirstOrDefault()
                : null;

        if (output is not { Length: > 0 } exportPath) {
            ShowExportFailure("No extracted translation export found.");
            return;
        }

        TryTranslateExport(exportPath, selectedLanguageFolders);
    }

    private void TryTranslateExport(
        string outputModPath,
        IReadOnlyCollection<string> selectedLanguageFolders) {
        if (_translateInProgress) {
            return;
        }

        if (!LlmConfigResolver.TryCreate(TranslatorMod.Settings, out _, out var configError)) {
            ShowExportFailure(configError);
            return;
        }

        var worksets = LanguageXmlWriteService.ReadWorksets(outputModPath, selectedLanguageFolders);
        var targetNames = ResolveSelectedLanguages(selectedLanguageFolders)
            .ToDictionary(
                language => language.folderName,
                language => language.DisplayName.NullOrEmpty() ? language.folderName : language.DisplayName,
                StringComparer.OrdinalIgnoreCase);

        _translateInProgress = true;
        _lastOutputPath = outputModPath;

        LongEventHandler.QueueLongEvent(() => {
            var failures = new List<string>();

            try {
                foreach (var workset in worksets) {
                    if (!targetNames.TryGetValue(workset.LanguageFolderName, out var targetName)) {
                        continue;
                    }

                    var translated = TranslationService.TranslateAsync(
                            workset,
                            workset.LanguageFolderName,
                            targetName,
                            () => LanguageXmlWriteService.WriteFromWorkset(
                                workset, outputModPath, includePlaceholders: true))
                        .GetAwaiter().GetResult();

                    var write = LanguageXmlWriteService.WriteFromWorkset(
                        workset, outputModPath, includePlaceholders: true);

                    if (!write.Success) {
                        failures.Add($"{workset.LanguageFolderName}: {write.Message}");
                    }

                    if (!translated.Success) {
                        failures.Add($"{workset.LanguageFolderName}: {translated.Message}");
                    }
                }
            } catch (Exception ex) {
                failures.Add(ex.Message);
            }

            LongEventHandler.ExecuteWhenFinished(() => {
                _translateInProgress = false;
                if (failures.Count == 0) {
                    _lastExportFailed = false;
                    _lastExportStatus = "Translated exported files.";
                } else {
                    ShowExportFailure(BuildFailureSummary(failures));
                }
            });
        }, "Translator_Translating", doAsynchronously: true, null);
    }

    private void ShowExportFailure(string status) {
        _translateInProgress = false;
        _lastExportFailed = true;
        _lastExportStatus = status;
        Messages.Message(status, MessageTypeDefOf.RejectInput);
    }

    private static void TryOpenOutputFolder(string outputPath) {
        if (!Directory.Exists(outputPath)) {
            Messages.Message("Translator_OutputFolderNotFound".Translate(), MessageTypeDefOf.RejectInput);
            return;
        }

        try {
            Application.OpenURL(new Uri(Path.GetFullPath(outputPath)).AbsoluteUri);
        } catch (Exception ex) {
            Messages.Message(
                "Translator_OpenOutputFolderFailed".Translate(ex.Message), MessageTypeDefOf.RejectInput);
        }
    }

    private static string BuildFailureSummary(IReadOnlyList<string> failures) {
        const int maxDisplayedFailures = 3;
        var summary = string.Join(" | ", failures
            .Take(maxDisplayedFailures)
            .Select(failure => failure.Length > 160 ? $"{failure[..160]}..." : failure));

        var remaining = failures.Count - maxDisplayedFailures;
        if (remaining > 0) {
            summary += $" (+{remaining} more)";
        }

        return summary;
    }

    private List<LoadedLanguage> ResolveSelectedLanguages(
        IReadOnlyCollection<string> selectedLanguageFolders) => [
        .. _exportLanguages.Where(language => selectedLanguageFolders.Contains(language.folderName))
    ];

    private ModMetaData? GetSelectedMod() =>
        _selectedPackageId is not null && _allModsByPackageId.TryGetValue(_selectedPackageId, out var mod)
            ? mod
            : null;

    private void RefreshFilteredMods() {
        _filteredMods.Clear();

        _filteredMods.AddRange(_quickSearchWidget.filter.Active
            ? _allMods.Where(mod => _quickSearchWidget.filter.Matches(mod.Name) ||
                                    _quickSearchWidget.filter.Matches(mod.PackageIdPlayerFacing))
            : _allMods);

        if (_selectedPackageId is null && _filteredMods.Count > 0) {
            _selectedPackageId = _filteredMods[0].PackageId;
        }
    }

    private static bool IsOfficialLudeonMod(ModMetaData mod) =>
        mod.PackageIdPlayerFacing.StartsWith("Ludeon.RimWorld", StringComparison.OrdinalIgnoreCase);
}