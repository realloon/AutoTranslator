using System.Buffers;

namespace Translator.Utilities;

public static class ModPath {
    private static readonly SearchValues<char> InvalidFileNameChars =
        SearchValues.Create(Path.GetInvalidFileNameChars());

    /// <summary>Absolute path with forward slashes and no trailing directory separator.</summary>
    public static string Normalize(string path) {
        var fullPath = Path.GetFullPath(path).Replace('\\', '/');
        return Path.TrimEndingDirectorySeparator(fullPath);
    }

    /// <summary>
    /// True when <paramref name="path"/> is the root itself or a descendant of it.
    /// The separator check prevents "/mods/FooBar" from matching root "/mods/Foo".
    /// </summary>
    public static bool IsUnderRoot(string path, string normalizedRoot) {
        var normalized = Normalize(path);
        if (normalized.Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase)) {
            return true;
        }

        var prefix = normalizedRoot.EndsWith('/') ? normalizedRoot : normalizedRoot + "/";
        return normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    public static string SanitizeFileNamePart(string value, string fallback) {
        if (string.IsNullOrEmpty(value)) {
            return fallback;
        }

        return string.Create(value.Length, value, static (destination, source) => {
            for (var i = 0; i < source.Length; i++) {
                destination[i] = InvalidFileNameChars.Contains(source[i]) ? '_' : source[i];
            }
        });
    }
}