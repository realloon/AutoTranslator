using RimWorld;
using Verse;
using UnityEngine;

namespace Translator.UI;

internal static class UiTheme {
    public static readonly Color SectionDividerColor = new(1f, 1f, 1f, 0.2f);
    public static readonly Color ErrorColor = new(0.95f, 0.35f, 0.35f);
    public static readonly Color SuccessColor = new(0.35f, 0.95f, 0.35f);
    public static readonly Color ListBackgroundColor = new ColorInt(42, 43, 44).ToColor;
    public static readonly Color ListBorderColor = new(0.78f, 0.78f, 0.78f, 0.2f);
    public static readonly Color HeaderColor = new(1f, 1f, 1f, 0.6f);
}

internal readonly struct GuiColorScope : IDisposable {
    private readonly Color _previous;

    private GuiColorScope(Color color) {
        _previous = GUI.color;
        GUI.color = color;
    }

    public void Dispose() => GUI.color = _previous;

    public static GuiColorScope Using(Color color) => new(color);
}