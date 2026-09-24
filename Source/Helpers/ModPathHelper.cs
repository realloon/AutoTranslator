using System.Text;

namespace Translator.Helpers;

internal static class ModPathHelper {
    /// <param name="normalizedRoot">A path already passed through <see cref="Normalize"/>.</param>
    public static bool IsPathUnderRoot(string path, string normalizedRoot) {
        return Normalize(path).StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }

    public static string Normalize(string path) {
        return Path.GetFullPath(path).Replace('\\', '/').TrimEnd('/');
    }

    public static string SanitizeFileNamePart(string value, string fallback) {
        if (string.IsNullOrEmpty(value)) return fallback;

        var invalidChars = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value) {
            builder.Append(invalidChars.Contains(ch) ? '_' : ch);
        }

        return builder.ToString();
    }
}