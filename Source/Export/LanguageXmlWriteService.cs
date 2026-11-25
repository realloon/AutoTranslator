using System.Xml;
using System.Xml.Linq;
using Translator.Utilities;
using Verse;

namespace Translator.Export;

internal sealed record LanguageXmlWriteResult(bool Success, string Message, int WrittenEntryCount);

internal static class LanguageXmlWriteService {
    public static List<LanguageWorksetFile> ReadWorksets(
        string outputModDir,
        IReadOnlyCollection<string> languageFolders) {
        var result = new List<LanguageWorksetFile>();

        foreach (var languageFolder in languageFolders.Distinct(StringComparer.OrdinalIgnoreCase)) {
            var root = Path.Combine(outputModDir, "Languages", languageFolder);
            if (!Directory.Exists(root)) {
                continue;
            }

            var workset = new LanguageWorksetFile { LanguageFolderName = languageFolder };

            foreach (var file in Directory.EnumerateFiles(root, "*.xml", SearchOption.AllDirectories)) {
                var relative = Path.GetRelativePath(root, file).Split(Path.DirectorySeparatorChar);

                switch (relative.Length) {
                    case >= 2 when
                        relative[0].Equals(LoadedLanguage.KeyedTranslationsFolderName,
                            StringComparison.OrdinalIgnoreCase): {
                        foreach (var entry in ReadEntries(file)) {
                            workset.Keyed.Add(new LanguageWorksetKeyedItem {
                                Tag = entry.Tag,
                                Original = entry.Original,
                                Translation = entry.Translation
                            });
                        }

                        break;
                    }
                    case >= 3 when
                        relative[0].Equals(LoadedLanguage.DefInjectionsFolderName,
                            StringComparison.OrdinalIgnoreCase): {
                        foreach (var entry in ReadEntries(file)) {
                            workset.DefInjected.Add(new LanguageWorksetDefInjectedItem {
                                Tag = entry.Tag,
                                Original = entry.Original,
                                Translation = entry.Translation,
                                DefType = relative[1],
                                IsCollectionItem = entry.Tag.LastIndexOf('.') >= 0
                            });
                        }

                        break;
                    }
                }
            }

            result.Add(workset);
        }

        return result;
    }

    public static LanguageXmlWriteResult WriteFromWorkset(
        LanguageWorksetFile workset,
        string outputModDir,
        bool includePlaceholders = false) {
        try {
            var languageFolderName = workset.LanguageFolderName;
            var writtenEntryCount = 0;

            var keyedOutputPath = LanguagePath(
                outputModDir, languageFolderName,
                LoadedLanguage.KeyedTranslationsFolderName,
                BuildOutputFileName(LoadedLanguage.KeyedTranslationsFolderName));

            var keyedEntries = ToXmlEntries(workset.Keyed, includePlaceholders);

            var keyedWrittenCount = WriteLanguageDataFile(keyedOutputPath, keyedEntries, languageFolderName);
            if (keyedWrittenCount > 0) {
                writtenEntryCount += keyedWrittenCount;
            }

            var defGroups = workset.DefInjected
                .Where(item => !item.DefType.NullOrEmpty())
                .GroupBy(item => item.DefType, StringComparer.Ordinal);

            foreach (var group in defGroups) {
                var defType = group.Key;
                var defOutputPath = LanguagePath(
                    outputModDir, languageFolderName,
                    LoadedLanguage.DefInjectionsFolderName,
                    defType,
                    BuildOutputFileName(defType));

                var entries = ToXmlEntries(group, includePlaceholders);

                var writtenCount = WriteLanguageDataFile(defOutputPath, entries, languageFolderName);
                if (writtenCount <= 0) {
                    continue;
                }

                writtenEntryCount += writtenCount;
            }

            return new LanguageXmlWriteResult(true, "OK", writtenEntryCount);
        } catch (Exception ex) {
            return new LanguageXmlWriteResult(false, ex.Message, 0);
        }
    }

    private static List<XmlEntry> ToXmlEntries(IEnumerable<WorksetTextItem> items, bool includePlaceholders) => [
        .. items
            .Where(item => !item.Tag.NullOrEmpty() && (includePlaceholders || !item.Translation.NullOrEmpty()))
            .OrderBy(item => item.Tag, StringComparer.Ordinal)
            .Select(item => new XmlEntry(item.Tag, item.Original,
                item.Translation.NullOrEmpty() ? LoadedLanguage.PlaceholderText : item.Translation))
    ];

    private static int WriteLanguageDataFile(
        string outputFilePath,
        IReadOnlyList<XmlEntry> entries,
        string languageFolderName) {
        if (entries.Count == 0) {
            return 0;
        }

        var root = new XElement("LanguageData");
        var writtenCount = 0;

        foreach (var entry in entries) {
            if (!TryCreateElement(entry.Tag, entry.Translation, out var element)) {
                Log.Warning(
                    $"[Translator] Skip invalid tag '{entry.Tag}' while writing {outputFilePath} (language {languageFolderName}).");
                continue;
            }

            if (!entry.Original.NullOrEmpty()) {
                // Vanilla translation files carry the source text as an " EN: ... " comment before each entry.
                root.Add(new XComment(SanitizeComment($" EN: {entry.Original.Replace("\n", "\\n")} ")));
            }

            root.Add(element);
            writtenCount++;
        }

        if (writtenCount == 0) {
            return 0;
        }

        var directory = Path.GetDirectoryName(outputFilePath)!;
        Directory.CreateDirectory(directory);

        new XDocument(new XDeclaration("1.0", "UTF-8", null), root).Save(outputFilePath);
        return writtenCount;
    }

    private static IEnumerable<XmlEntry> ReadEntries(string path) {
        var document = XDocument.Load(path);
        var original = string.Empty;

        foreach (var node in document.Root?.Nodes() ?? []) {
            if (node is XComment comment &&
                comment.Value.TrimStart().StartsWith("EN:", StringComparison.OrdinalIgnoreCase)) {
                var text = comment.Value.Trim();
                original = text.Length > 3 ? text[3..].Trim() : string.Empty;
                original = original.Replace("\\n", "\n");
                continue;
            }

            if (node is not XElement element) continue;

            var translation = element.Value == LoadedLanguage.PlaceholderText ? string.Empty : element.Value;
            yield return new XmlEntry(element.Name.LocalName, original, translation);
            original = string.Empty;
        }
    }

    private static bool TryCreateElement(string tag, string translation, out XElement element) {
        element = null!;
        var normalizedTag = NormalizeTagForXmlName(tag);
        if (normalizedTag.NullOrEmpty()) {
            return false;
        }

        try {
            XmlConvert.VerifyName(normalizedTag);
            element = new XElement(normalizedTag, translation);
            return true;
        } catch {
            return false;
        }
    }

    private static string NormalizeTagForXmlName(string tag) {
        if (tag.NullOrEmpty()) {
            return string.Empty;
        }

        return tag
            .Replace("]", string.Empty)
            .Replace("[", ".")
            .Trim();
    }

    private static string SanitizeComment(string comment) {
        while (comment.Contains("-----", StringComparison.Ordinal)) {
            comment = comment.Replace("-----", "- - -", StringComparison.Ordinal);
        }

        while (comment.Contains("--", StringComparison.Ordinal)) {
            comment = comment.Replace("--", "- -", StringComparison.Ordinal);
        }

        return comment;
    }

    private static string LanguagePath(string outputModDir, string languageFolderName, params string[] parts) =>
        Path.Combine(outputModDir, "Languages", languageFolderName, Path.Combine(parts));

    private static string BuildOutputFileName(string parentFolderName) =>
        ModPath.SanitizeFileNamePart(parentFolderName, "Translation") + ".xml";

    private sealed record XmlEntry(string Tag, string Original, string Translation);
}