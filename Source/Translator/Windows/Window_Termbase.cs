using RimWorld;
using UnityEngine;
using Verse;

namespace Translator.Windows;

// ReSharper disable once InconsistentNaming
public class Window_Termbase : Window {
    private static readonly Color ListBackgroundColor = new ColorInt(42, 43, 44).ToColor;
    private static readonly Color ListBorderColor = new(0.78f, 0.78f, 0.78f, 0.2f);
    private static readonly Color HeaderColor = new(1f, 1f, 1f, 0.6f);

    private Vector2 _scrollPos = Vector2.zero;

    public override Vector2 InitialSize => new(600f, 560f);

    public Window_Termbase() {
        doCloseX = true;
        closeOnClickedOutside = false;
        absorbInputAroundWindow = true;
    }

    public override void DoWindowContents(Rect inRect) {
        var y = 0f;

        Text.Font = GameFont.Medium;
        Widgets.Label(new Rect(0f, y, inRect.width, 34f), "Translator_TermbaseDialogTitle".Translate());
        y += 40f;
        Text.Font = GameFont.Small;

        Widgets.Label(new Rect(0f, y, inRect.width, 24f), "Translator_TermbaseDescription".Translate());
        y += 30f;

        const float footerHeight = 34f;
        const float footerGap = 10f;
        var listRect = new Rect(0f, y, inRect.width, inRect.height - y - footerHeight - footerGap);
        DrawPlaceholderList(listRect);

        var footerRect = new Rect(0f, inRect.height - footerHeight, inRect.width, footerHeight);
        DrawFooter(footerRect);
    }

    private void DrawPlaceholderList(Rect rect) {
        const float contentPadding = 8f;
        Widgets.DrawBoxSolidWithOutline(rect, ListBackgroundColor, ListBorderColor);

        var scrollRect = new Rect(
            rect.x + contentPadding,
            rect.y + contentPadding,
            Mathf.Max(0f, rect.width - contentPadding * 2f),
            Mathf.Max(0f, rect.height - contentPadding * 2f));
        var viewRect = new Rect(0f, 0f, scrollRect.width - 16f, Mathf.Max(scrollRect.height, 140f));

        Widgets.BeginScrollView(scrollRect, ref _scrollPos, viewRect);

        GUI.color = HeaderColor;
        Widgets.Label(new Rect(0f, 0f, viewRect.width, 24f), "Translator_TermbaseColumns".Translate());
        GUI.color = Color.white;

        Widgets.DrawLineHorizontal(0f, 26f, viewRect.width);
        Widgets.Label(new Rect(0f, 38f, viewRect.width, 60f), "Translator_TermbaseEmpty".Translate());

        Widgets.EndScrollView();
    }

    private void DrawFooter(Rect rect) {
        var closeLabel = "Close".Translate();
        var closeWidth = Mathf.Max(120f, Text.CalcSize(closeLabel).x + 24f);
        var closeRect = new Rect(rect.xMax - closeWidth, rect.y, closeWidth, rect.height);
        if (Widgets.ButtonText(closeRect, closeLabel)) {
            Close();
        }
    }
}
