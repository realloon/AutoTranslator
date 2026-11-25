using RimWorld;
using Translator.Export;
using UnityEngine;
using Verse;

namespace Translator.UI;

// ReSharper disable once InconsistentNaming
public class Window_ExportLanguagePicker : Window {
    private readonly List<LoadedLanguage> _languages;
    private readonly HashSet<string> _selectedLanguageFolders;
    private readonly Action<IReadOnlyCollection<string>, OutputLocationMode> _onConfirm;
    private OutputLocationMode _outputLocationMode;
    private Vector2 _scrollPos;

    public override Vector2 InitialSize => new(540f, 580f);

    public Window_ExportLanguagePicker(IEnumerable<LoadedLanguage> languages,
        IEnumerable<string> selectedLanguageFolders, OutputLocationMode defaultOutputLocationMode,
        Action<IReadOnlyCollection<string>, OutputLocationMode> onConfirm) {
        _languages = [.. languages.OrderBy(language => language.folderName, StringComparer.OrdinalIgnoreCase)];
        _selectedLanguageFolders =
            new HashSet<string>(selectedLanguageFolders, StringComparer.OrdinalIgnoreCase);

        if (_selectedLanguageFolders.Count == 0 && LanguageDatabase.activeLanguage is not null) {
            _selectedLanguageFolders.Add(LanguageDatabase.activeLanguage.folderName);
        }

        _onConfirm = onConfirm;
        _outputLocationMode = defaultOutputLocationMode;

        doCloseX = true;
        closeOnClickedOutside = false;
        absorbInputAroundWindow = true;
    }

    public override void DoWindowContents(Rect inRect) {
        var y = 0f;

        Text.Font = GameFont.Medium;
        Widgets.Label(new Rect(0f, y, inRect.width, 34f), "Translator_ExportLanguageDialogTitle".Translate());
        y += 44f;
        Text.Font = GameFont.Small;

        DrawHeaderActions(new Rect(0f, y, inRect.width, 30f));
        y += 36f;

        const float footerHeight = 34f;
        const float footerGap = 8f;
        var listRect = new Rect(0f, y, inRect.width, inRect.height - y - footerHeight - footerGap);
        DrawLanguageList(listRect);

        DrawFooter(new Rect(0f, inRect.height - footerHeight, inRect.width, footerHeight));
    }

    private void DrawHeaderActions(Rect rect) {
        const float buttonWidth = 116f;
        var selectAllRect = new Rect(rect.x, rect.y, buttonWidth, rect.height);
        if (Widgets.ButtonText(selectAllRect, "Translator_SelectAllLanguages".Translate())) {
            _selectedLanguageFolders.Clear();
            foreach (var language in _languages) {
                _selectedLanguageFolders.Add(language.folderName);
            }
        }

        var clearRect = new Rect(selectAllRect.xMax + 8f, rect.y, buttonWidth, rect.height);
        if (Widgets.ButtonText(clearRect, "Translator_ClearLanguageSelection".Translate())) {
            _selectedLanguageFolders.Clear();
        }

        var countRect = new Rect(clearRect.xMax + 10f, rect.y + 4f,
            rect.width - clearRect.xMax - 10f, rect.height);
        Widgets.Label(countRect,
            "Translator_ExportLanguageSelectedCount".Translate(
                _selectedLanguageFolders.Count, _languages.Count));
    }

    private void DrawLanguageList(Rect rect) {
        const float contentPaddingVertical = 8f;
        const float contentPaddingRight = 8f;
        const float rowContentHorizontalPadding = 8f;
        const float rowHeight = 24f;

        Widgets.DrawBoxSolidWithOutline(rect, UiTheme.ListBackgroundColor, UiTheme.ListBorderColor);

        var scrollRect = new Rect(
            rect.x,
            rect.y + contentPaddingVertical,
            Mathf.Max(0f, rect.width - contentPaddingRight),
            Mathf.Max(0f, rect.height - contentPaddingVertical * 2f));
        var viewRect = new Rect(
            0f, 0f, Mathf.Max(0f, scrollRect.width - 16f), _languages.Count * rowHeight + 2f);

        Widgets.BeginScrollView(scrollRect, ref _scrollPos, viewRect);

        for (var i = 0; i < _languages.Count; i++) {
            var language = _languages[i];
            var rowRect = new Rect(0f, i * rowHeight, viewRect.width, rowHeight);
            var contentRect = new Rect(
                rowRect.x + rowContentHorizontalPadding,
                rowRect.y,
                Mathf.Max(0f, rowRect.width - rowContentHorizontalPadding * 2f),
                rowRect.height);

            var selected = _selectedLanguageFolders.Contains(language.folderName);
            if (selected) {
                Widgets.DrawHighlightSelected(rowRect);
            } else if (Mouse.IsOver(rowRect)) {
                Widgets.DrawHighlight(rowRect);
            }

            var label = language.DisplayName.NullOrEmpty() ? language.folderName : language.DisplayName;
            Widgets.CheckboxLabeled(contentRect, label, ref selected);

            if (selected) {
                _selectedLanguageFolders.Add(language.folderName);
            } else {
                _selectedLanguageFolders.Remove(language.folderName);
            }
        }

        Widgets.EndScrollView();
    }

    private void DrawFooter(Rect rect) {
        var modeButtonRect = new Rect(rect.x, rect.y, rect.height, rect.height);
        if (Widgets.ButtonImage(modeButtonRect, Translator.GeneralIcon)) {
            OpenOutputModeMenu();
        }

        TooltipHandler.TipRegion(modeButtonRect,
            "Translator_OutputModeButtonTip".Translate(OutputModeText.Of(_outputLocationMode)));

        var confirmLabel = "Translator_ExportLanguageConfirm".Translate();
        var cancelLabel = "Cancel".Translate();
        var confirmWidth = Mathf.Max(120f, Text.CalcSize(confirmLabel).x + 24f);
        var cancelWidth = Mathf.Max(120f, Text.CalcSize(cancelLabel).x + 24f);

        var confirmRect = new Rect(rect.xMax - confirmWidth, rect.y, confirmWidth, rect.height);
        if (Widgets.ButtonText(confirmRect, confirmLabel)) {
            ConfirmAndClose();
        }

        var cancelRect = new Rect(confirmRect.x - cancelWidth - 8f, rect.y, cancelWidth, rect.height);
        if (Widgets.ButtonText(cancelRect, cancelLabel)) {
            Close();
        }
    }

    private void ConfirmAndClose() {
        if (_selectedLanguageFolders.Count == 0) {
            Messages.Message("Translator_ExportNoLanguageSelected".Translate(),
                MessageTypeDefOf.RejectInput);
            return;
        }

        _onConfirm([.. _selectedLanguageFolders], _outputLocationMode);
        Close();
    }

    private void OpenOutputModeMenu() {
        var options = new List<FloatMenuOption> {
            new(OutputModeText.Of(OutputLocationMode.GeneratedMod),
                () => _outputLocationMode = OutputLocationMode.GeneratedMod),
            new(OutputModeText.Of(OutputLocationMode.OriginalMod),
                () => _outputLocationMode = OutputLocationMode.OriginalMod)
        };
        Find.WindowStack.Add(new FloatMenu(options));
    }
}