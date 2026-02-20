using UnityEngine;
using RimWorld;
using Verse;
using Translator.Services;

namespace Translator.Windows;

// ReSharper disable once InconsistentNaming
public class Window_Termbase : Window {
    private static readonly Color ListBackgroundColor = new ColorInt(42, 43, 44).ToColor;
    private static readonly Color ListBorderColor = new(0.78f, 0.78f, 0.78f, 0.2f);
    private static readonly Color HeaderColor = new(1f, 1f, 1f, 0.6f);

    private Vector2 _scrollPos = Vector2.zero;
    private readonly List<TermbaseEntry> _entries = [];
    private readonly List<LoadedLanguage> _languages = [];
    private string _selectedLanguageFolder;

    public override Vector2 InitialSize => new(600f, 560f);

    public Window_Termbase() {
        doCloseX = true;
        closeOnClickedOutside = false;
        absorbInputAroundWindow = true;

        _languages.AddRange(LanguageDatabase.AllLoadedLanguages
            .Where(language => language is not null && !language.folderName.NullOrEmpty())
            .OrderBy(language => language.folderName, StringComparer.OrdinalIgnoreCase));
        _selectedLanguageFolder = LanguageDatabase.activeLanguage?.folderName
                                  ?? _languages.FirstOrDefault()?.folderName
                                  ?? string.Empty;

        _entries.AddRange(TermbaseService.LoadEntries()
            .Select(entry => new TermbaseEntry {
                Source = entry.Source,
                Target = entry.Target,
                TargetLanguageFolder = entry.TargetLanguageFolder
            }));
    }

    public override void DoWindowContents(Rect inRect) {
        var y = 0f;

        Text.Font = GameFont.Medium;
        Widgets.Label(new Rect(0f, y, inRect.width, 34f), "Translator_TermbaseDialogTitle".Translate());
        y += 40f;
        Text.Font = GameFont.Small;

        Widgets.Label(new Rect(0f, y, inRect.width, 24f), "Translator_TermbaseDescription".Translate());
        y += 30f;

        var headerActionsHeight = DrawHeaderActions(new Rect(0f, y, inRect.width, 30f));
        y += headerActionsHeight + 8f;

        const float footerHeight = 34f;
        const float footerGap = 10f;
        var listRect = new Rect(0f, y, inRect.width, inRect.height - y - footerHeight - footerGap);
        DrawEntriesList(listRect);

        var footerRect = new Rect(0f, inRect.height - footerHeight, inRect.width, footerHeight);
        DrawFooter(footerRect);
    }

    private float DrawHeaderActions(Rect rect) {
        const float rowHeight = 30f;
        const float minButtonWidth = 90f;
        const float rowGap = 8f;

        var addLabel = "Translator_TermbaseAddRow".Translate();
        var clearLabel = "Translator_TermbaseClearAll".Translate();
        var saveLabel = "Translator_TermbaseSave".Translate();

        var addWidth = Mathf.Clamp(Text.CalcSize(addLabel).x + 24f, minButtonWidth, 160f);
        var clearWidth = Mathf.Clamp(Text.CalcSize(clearLabel).x + 24f, minButtonWidth, 200f);
        var saveWidth = Mathf.Clamp(Text.CalcSize(saveLabel).x + 24f, minButtonWidth, 160f);
        var availableButtonsWidth = Mathf.Max(0f, rect.width - rowGap * 2f);
        var requestedButtonsWidth = addWidth + clearWidth + saveWidth;
        if (requestedButtonsWidth > availableButtonsWidth && requestedButtonsWidth > 0f) {
            var ratio = availableButtonsWidth / requestedButtonsWidth;
            addWidth = Mathf.Max(minButtonWidth, addWidth * ratio);
            clearWidth = Mathf.Max(minButtonWidth, clearWidth * ratio);
            saveWidth = Mathf.Max(minButtonWidth, saveWidth * ratio);
        }

        var addRect = new Rect(rect.x, rect.y, addWidth, rowHeight);
        if (Widgets.ButtonText(addRect, "Translator_TermbaseAddRow".Translate())) {
            if (_selectedLanguageFolder.NullOrEmpty()) {
                return rowHeight;
            }

            _entries.Add(new TermbaseEntry {
                TargetLanguageFolder = _selectedLanguageFolder
            });
        }

        var clearRect = new Rect(addRect.xMax + rowGap, rect.y, clearWidth, rowHeight);
        if (Widgets.ButtonText(clearRect, "Translator_TermbaseClearAll".Translate())) {
            _entries.RemoveAll(entry => string.Equals(entry.TargetLanguageFolder, _selectedLanguageFolder,
                StringComparison.OrdinalIgnoreCase));
        }

        var saveRect = new Rect(clearRect.xMax + rowGap, rect.y, saveWidth, rowHeight);
        if (Widgets.ButtonText(saveRect, saveLabel)) {
            SaveOnly();
        }

        return rowHeight;
    }

    private void DrawEntriesList(Rect rect) {
        const float contentPadding = 8f;
        const float rowHeight = 32f;
        const float rowGap = 6f;
        const float removeButtonWidth = 34f;
        const float arrowWidth = 24f;
        const float fieldGap = 8f;
        var currentEvent = Event.current;
        Widgets.DrawBoxSolidWithOutline(rect, ListBackgroundColor, ListBorderColor);

        var scrollRect = new Rect(
            rect.x + contentPadding,
            rect.y + contentPadding,
            Mathf.Max(0f, rect.width - contentPadding * 2f),
            Mathf.Max(0f, rect.height - contentPadding * 2f));
        var selectedIndexes = GetSelectedLanguageEntryIndexes();
        var rowCount = Mathf.Max(1, selectedIndexes.Count);
        var contentHeight = 30f + rowCount * (rowHeight + rowGap);
        var viewRect = new Rect(0f, 0f, scrollRect.width - 16f, Mathf.Max(scrollRect.height, contentHeight));

        Widgets.BeginScrollView(scrollRect, ref _scrollPos, viewRect);

        GUI.color = HeaderColor;
        Widgets.Label(new Rect(fieldGap, 0f, viewRect.width - fieldGap, 24f), BuildColumnHeaderLabel());
        GUI.color = Color.white;

        if (selectedIndexes.Count == 0) {
            Widgets.Label(new Rect(0f, 38f, viewRect.width, 60f), "Translator_TermbaseEmpty".Translate());
            Widgets.EndScrollView();
            return;
        }

        for (var i = 0; i < selectedIndexes.Count; i++) {
            var entryIndex = selectedIndexes[i];
            var entry = _entries[entryIndex];
            var rowY = 32f + i * (rowHeight + rowGap);
            var rowRect = new Rect(0f, rowY, viewRect.width, rowHeight);

            var fieldWidth = Mathf.Max(
                80f,
                (rowRect.width - removeButtonWidth - arrowWidth - fieldGap * 4f) / 2f);
            var sourceRect = new Rect(rowRect.x + fieldGap, rowRect.y + 2f, fieldWidth, rowRect.height - 4f);
            var arrowRect = new Rect(sourceRect.xMax + fieldGap, rowRect.y + 4f, arrowWidth, rowRect.height - 8f);
            var targetRect = new Rect(arrowRect.xMax + fieldGap, rowRect.y + 2f, fieldWidth, rowRect.height - 4f);
            var removeRect = new Rect(targetRect.xMax + fieldGap, rowRect.y + 2f, removeButtonWidth,
                rowRect.height - 4f);

            var sourceControlName = $"Translator_Termbase_Source_{entryIndex}";
            var targetControlName = $"Translator_Termbase_Target_{entryIndex}";

            GUI.SetNextControlName(sourceControlName);
            entry.Source = Widgets.TextField(sourceRect, entry.Source);
            var shouldFocusTarget = currentEvent.type == EventType.KeyDown
                                    && currentEvent is { keyCode: KeyCode.Tab, shift: false }
                                    && GUI.GetNameOfFocusedControl() == sourceControlName;
            if (shouldFocusTarget) {
                currentEvent.Use();
                GUI.FocusControl(targetControlName);
            }

            Widgets.Label(arrowRect, "->");
            GUI.SetNextControlName(targetControlName);
            entry.Target = Widgets.TextField(targetRect, entry.Target);

            if (!Widgets.ButtonText(removeRect, "X")) continue;

            _entries.RemoveAt(entryIndex);
            i -= 1;
            selectedIndexes = GetSelectedLanguageEntryIndexes();
        }

        Widgets.EndScrollView();
    }

    private void DrawFooter(Rect rect) {
        var closeLabel = "Close".Translate();
        var closeWidth = Mathf.Max(120f, Text.CalcSize(closeLabel).x + 24f);
        var langAndCountLabel = $"{GetSelectedLanguageDisplayName()} ({GetSelectedLanguageEntryCount()})";
        var langButtonWidth = Mathf.Max(180f, Text.CalcSize(langAndCountLabel).x + 24f);
        var maxLangButtonWidth = Mathf.Max(120f, rect.width - closeWidth - 12f);
        langButtonWidth = Mathf.Min(langButtonWidth, maxLangButtonWidth);
        var langButtonRect = new Rect(rect.x, rect.y, langButtonWidth, rect.height);
        if (Widgets.ButtonText(langButtonRect, langAndCountLabel)) {
            OpenLanguageMenu();
        }

        var closeRect = new Rect(rect.xMax - closeWidth, rect.y, closeWidth, rect.height);
        if (Widgets.ButtonText(closeRect, closeLabel)) {
            Close();
        }
    }

    private void SaveOnly() {
        var result = TermbaseService.SaveEntries(_entries);
        if (result.Success) return;

        var errorText = "Translator_TermbaseSaveFailed".Translate(result.Message.NullOrEmpty()
            ? "unknown error"
            : result.Message);

        Messages.Message(errorText, MessageTypeDefOf.RejectInput);
    }

    private string GetSelectedLanguageDisplayName() {
        if (_selectedLanguageFolder.NullOrEmpty()) {
            return "Translator_TermbaseNoLanguage".Translate().ToString();
        }

        var language = _languages.FirstOrDefault(item =>
            string.Equals(item.folderName, _selectedLanguageFolder, StringComparison.OrdinalIgnoreCase));
        if (language is null) {
            return _selectedLanguageFolder;
        }

        return language.DisplayName.NullOrEmpty() ? language.folderName : language.DisplayName;
    }

    private List<int> GetSelectedLanguageEntryIndexes() {
        var indexes = new List<int>();
        for (var i = 0; i < _entries.Count; i++) {
            if (string.Equals(_entries[i].TargetLanguageFolder, _selectedLanguageFolder,
                    StringComparison.OrdinalIgnoreCase)) {
                indexes.Add(i);
            }
        }

        return indexes;
    }

    private int GetSelectedLanguageEntryCount() {
        return GetSelectedLanguageEntryIndexes().Count;
    }

    private void OpenLanguageMenu() {
        if (_languages.Count == 0) {
            Messages.Message("Translator_TermbaseNoLanguage".Translate(), MessageTypeDefOf.RejectInput);
            return;
        }

        var options = new List<FloatMenuOption>();

        foreach (var language in _languages) {
            var label = language.DisplayName.NullOrEmpty() ? language.folderName : language.DisplayName;
            var folder = language.folderName;
            options.Add(new FloatMenuOption(label, () => { _selectedLanguageFolder = folder; }));
        }

        Find.WindowStack.Add(new FloatMenu(options));
    }

    private string BuildColumnHeaderLabel() {
        return $"{GetSourceLanguageDisplayName()} -> {GetSelectedLanguageDisplayName()}";
    }

    private static string GetSourceLanguageDisplayName() {
        var sourceLanguage = LanguageDatabase.defaultLanguage ?? LanguageDatabase.activeLanguage;
        if (sourceLanguage is null) {
            return "Translator_TermbaseNoLanguage".Translate().ToString();
        }

        return sourceLanguage.DisplayName.NullOrEmpty() ? sourceLanguage.folderName : sourceLanguage.DisplayName;
    }
}