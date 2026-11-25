using Verse;

namespace Translator.Utilities;

internal static class ModKeyedTexts {
    /// <summary>Keyed entries whose source file lives inside the given mod.</summary>
    public static IEnumerable<(string Key, LoadedLanguage.KeyedReplacement Replacement)> Of(
        ModMetaData mod,
        LoadedLanguage language) {
        var root = ModPath.Normalize(mod.RootDir.FullName);

        foreach (var (key, replacement) in language.keyedReplacements) {
            if (!replacement.fileSourceFullPath.NullOrEmpty() &&
                ModPath.IsUnderRoot(replacement.fileSourceFullPath, root)) {
                yield return (key, replacement);
            }
        }
    }
}
