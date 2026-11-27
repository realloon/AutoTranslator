using UnityEngine;

namespace Translator.UI;

internal static class UiTheme {
    public static readonly Color DarkGray = new(0.16f, 0.16f, 0.17f);
    public static readonly Color White60 = new(1f, 1f, 1f, 0.6f);
    public static readonly Color White20 = new(1f, 1f, 1f, 0.2f);
    public static readonly Color White12 = new(1f, 1f, 1f, 0.12f);
    public static readonly Color White04 = new(1f, 1f, 1f, 0.04f);
    public static readonly Color SoftRed = new(0.95f, 0.35f, 0.35f);
    public static readonly Color SoftGreen = new(0.35f, 0.95f, 0.35f);
    public static readonly Color SoftYellow = new(1f, 1f, 0.35f);
}

internal readonly struct GuiColorScope : IDisposable {
    private readonly Color _previous;

    private GuiColorScope(Color color) {
        _previous = GUI.color;
        GUI.color = color;
    }

    public void Dispose() {
        GUI.color = _previous;
    }

    public static GuiColorScope Using(Color color) => new(color);
}
