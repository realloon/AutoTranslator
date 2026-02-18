using Translator.Services;
using Verse;

namespace Translator.Helpers;

internal static class KeyStatsHelper {
    public static StaticTranslateStats BuildStats(ModMetaData mod, LoadedLanguage activeLanguage,
        LoadedLanguage defaultLanguage) {
        var defaultKeys = CollectDefaultKeyedKeysForMod(mod, defaultLanguage);
        var stats = new StaticTranslateStats {
            UniqueLiteralKeyCount = defaultKeys.Count,
            MissingKeyCount = CountMissingKeys(activeLanguage, defaultLanguage, defaultKeys)
        };
        return stats;
    }

    private static int CountMissingKeys(LoadedLanguage activeLanguage, LoadedLanguage defaultLanguage,
        IReadOnlyCollection<string> defaultKeys) {
        return activeLanguage != defaultLanguage
            ? defaultKeys.Count(key => !activeLanguage.HaveTextForKey(key))
            : 0;
    }

    private static IReadOnlyCollection<string> CollectDefaultKeyedKeysForMod(ModMetaData mod,
        LoadedLanguage defaultLanguage) {
        var modRoot = NormalizePath(mod.RootDir.FullName);
        var keys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var keyedReplacement in defaultLanguage.keyedReplacements) {
            var source = keyedReplacement.Value.fileSourceFullPath;
            if (source.NullOrEmpty() || !IsPathUnderRoot(source, modRoot)) continue;

            keys.Add(keyedReplacement.Key);
        }

        return keys;
    }

    private static bool IsPathUnderRoot(string path, string root) {
        var normalizedPath = NormalizePath(path);
        return normalizedPath.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string path) {
        try {
            return Path.GetFullPath(path)
                .Replace('\\', '/')
                .TrimEnd('/');
        } catch {
            return path.Replace('\\', '/').TrimEnd('/');
        }
    }
}