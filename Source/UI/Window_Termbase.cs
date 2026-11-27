using RimWorld;
using Translator.Termbase;
using UnityEngine;
using Verse;

namespace Translator.UI;

// ReSharper disable once InconsistentNaming
public class Window_Termbase : Window {
    private readonly List<TermbaseEntry> _entries = [];
    private readonly List<LoadedLanguage> _languages = [];
    private string _selectedLanguageFolder;
    private Vector2 _scrollPos;

    public override Vector2 InitialSize => new(600f, 560f);

    public Window_Termbase() {
        doCloseX = true;
        closeOnClickedOutside = false;
        absorbInputAroundWindow = true;

        _languages.AddRange(LanguageDatabase.AllLoadedLanguages
            .OrderBy(language => language.folderName, StringComparer.OrdinalIgnoreCase));
        _selectedLanguageFolder = LanguageDatabase.activeLanguage?.folderName ??
                                  _languages.FirstOrDefault()?.folderName ?? string.Empty;

        _entries.AddRange(TermbaseService.LoadEntries());
    }

    public override void DoWindowContents(Rect inRect) {
        var y = 0f;

        Text.Font = GameFont.Medium;
        Widgets.Label(new Rect(0f, y, inRect.width, 34f), "Translator_Termbase".Translate());
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

        DrawFooter(new Rect(0f, inRect.height - footerHeight, inRect.width, footerHeight));
    }

    private float DrawHeaderActions(Rect rect) {
        const float rowHeight = 30f;
        const float minButtonWidth = 90f;
        const float rowGap = 8f;

        var addLabel = "Translator_Add".Translate();
        var clearLabel = "Translator_ClearAll".Translate();
        var saveLabel = "Translator_Save".Translate();

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
        if (Widgets.ButtonText(addRect, addLabel)) {
            if (_selectedLanguageFolder.NullOrEmpty()) {
                return rowHeight;
            }

            _entries.Add(new TermbaseEntry { TargetLanguageFolder = _selectedLanguageFolder });
        }

        var clearRect = new Rect(addRect.xMax + rowGap, rect.y, clearWidth, rowHeight);
        if (Widgets.ButtonText(clearRect, clearLabel)) {
            _entries.RemoveAll(entry => string.Equals(
                entry.TargetLanguageFolder, _selectedLanguageFolder, StringComparison.OrdinalIgnoreCase));
        }

        var saveRect = new Rect(clearRect.xMax + rowGap, rect.y, saveWidth, rowHeight);
        if (Widgets.ButtonText(saveRect, saveLabel)) {
            Save();
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
        const float headerHeight = 30f;

        var currentEvent = Event.current;
        var selectedEntries = GetSelectedEntries();

        Widgets.DrawBoxSolidWithOutline(rect, UiTheme.DarkGray, UiTheme.White20);

        var scrollRect = new Rect(
            rect.x + contentPadding,
            rect.y + contentPadding,
            Mathf.Max(0f, rect.width - contentPadding * 2f),
            Mathf.Max(0f, rect.height - contentPadding * 2f));
        var contentHeight = headerHeight + Mathf.Max(1, selectedEntries.Count) * (rowHeight + rowGap);
        var viewRect = new Rect(
            0f, 0f, scrollRect.width - 16f, Mathf.Max(scrollRect.height, contentHeight));

        Widgets.BeginScrollView(scrollRect, ref _scrollPos, viewRect);

        using (GuiColorScope.Using(UiTheme.White60)) {
            Widgets.Label(new Rect(fieldGap, 0f, viewRect.width - fieldGap, 24f),
                BuildColumnHeaderLabel());
        }

        if (selectedEntries.Count == 0) {
            Widgets.Label(new Rect(0f, 38f, viewRect.width, 60f), "Translator_NoTermsYet".Translate());
            Widgets.EndScrollView();
            return;
        }

        for (var i = 0; i < selectedEntries.Count; i++) {
            var entry = selectedEntries[i];
            var rowY = 32f + i * (rowHeight + rowGap);
            var rowRect = new Rect(0f, rowY, viewRect.width, rowHeight);

            var fieldWidth = Mathf.Max(
                80f, (rowRect.width - removeButtonWidth - arrowWidth - fieldGap * 4f) / 2f);
            var sourceRect = new Rect(
                rowRect.x + fieldGap, rowRect.y + 2f, fieldWidth, rowRect.height - 4f);
            var arrowRect = new Rect(
                sourceRect.xMax + fieldGap, rowRect.y + 4f, arrowWidth, rowRect.height - 8f);
            var targetRect = new Rect(
                arrowRect.xMax + fieldGap, rowRect.y + 2f, fieldWidth, rowRect.height - 4f);
            var removeRect = new Rect(
                targetRect.xMax + fieldGap, rowRect.y + 2f, removeButtonWidth, rowRect.height - 4f);

            var entryIndex = _entries.IndexOf(entry);
            var sourceControlName = $"Translator_Termbase_Source_{entryIndex}";
            var targetControlName = $"Translator_Termbase_Target_{entryIndex}";

            GUI.SetNextControlName(sourceControlName);
            entry.Source = Widgets.TextField(sourceRect, entry.Source);

            var shouldFocusTarget =
                currentEvent.type == EventType.KeyDown &&
                currentEvent is { keyCode: KeyCode.Tab, shift: false } &&
                GUI.GetNameOfFocusedControl() == sourceControlName;
            if (shouldFocusTarget) {
                currentEvent.Use();
                GUI.FocusControl(targetControlName);
            }

            Widgets.Label(arrowRect, "->");

            GUI.SetNextControlName(targetControlName);
            entry.Target = Widgets.TextField(targetRect, entry.Target);

            if (!Widgets.ButtonText(removeRect, "X")) {
                continue;
            }

            _entries.Remove(entry);
            break;
        }

        Widgets.EndScrollView();
    }

    private void DrawFooter(Rect rect) {
        var closeLabel = "Close".Translate();
        var closeWidth = Mathf.Max(120f, Text.CalcSize(closeLabel).x + 24f);
        var langAndCountLabel = $"{GetSelectedLanguageDisplayName()} ({GetSelectedEntries().Count})";
        var langButtonWidth = Mathf.Max(180f, Text.CalcSize(langAndCountLabel).x + 24f);
        langButtonWidth = Mathf.Min(langButtonWidth, Mathf.Max(120f, rect.width - closeWidth - 12f));

        var langButtonRect = new Rect(rect.x, rect.y, langButtonWidth, rect.height);
        if (Widgets.ButtonText(langButtonRect, langAndCountLabel)) {
            OpenLanguageMenu();
        }

        var closeRect = new Rect(rect.xMax - closeWidth, rect.y, closeWidth, rect.height);
        if (Widgets.ButtonText(closeRect, closeLabel)) {
            Close();
        }
    }

    private void Save() {
        var result = TermbaseService.SaveEntries(_entries);
        if (result.Success) {
            return;
        }

        var errorText = "Translator_SaveTermbaseFailed".Translate(
            result.Message.NullOrEmpty() ? "unknown error" : result.Message);
        Messages.Message(errorText, MessageTypeDefOf.RejectInput);
    }

    private string GetSelectedLanguageDisplayName() {
        if (_selectedLanguageFolder.NullOrEmpty()) {
            return "Translator_NoAvailableLanguage".Translate();
        }

        var language = _languages.FirstOrDefault(item => item.folderName.Equals(
            _selectedLanguageFolder, StringComparison.OrdinalIgnoreCase));
        return language is null ? _selectedLanguageFolder : DisplayNameOf(language);
    }

    private List<TermbaseEntry> GetSelectedEntries() => [
        .. _entries
            .Where(entry => string.Equals(entry.TargetLanguageFolder, _selectedLanguageFolder,
                StringComparison.OrdinalIgnoreCase))
    ];

    private void OpenLanguageMenu() {
        if (_languages.Count == 0) {
            Messages.Message("Translator_NoAvailableLanguage".Translate(),
                MessageTypeDefOf.RejectInput);
            return;
        }

        var options = _languages
            .Select(language => {
                var folder = language.folderName;
                return new FloatMenuOption(
                    DisplayNameOf(language), () => _selectedLanguageFolder = folder);
            })
            .ToList();
        Find.WindowStack.Add(new FloatMenu(options));
    }

    private string BuildColumnHeaderLabel() =>
        $"{GetSourceLanguageDisplayName()} -> {GetSelectedLanguageDisplayName()}";

    private static string GetSourceLanguageDisplayName() {
        var sourceLanguage = LanguageDatabase.defaultLanguage ?? LanguageDatabase.activeLanguage;
        return sourceLanguage is null
            ? "Translator_NoAvailableLanguage".Translate()
            : DisplayNameOf(sourceLanguage);
    }

    private static string DisplayNameOf(LoadedLanguage language) =>
        language.DisplayName.NullOrEmpty() ? language.folderName : language.DisplayName;
}
