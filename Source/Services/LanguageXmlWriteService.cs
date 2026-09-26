using System.Xml;
using System.Xml.Linq;
using Translator.Helpers;
using Verse;

namespace Translator.Services;

internal sealed class LanguageXmlWriteResult {
    public bool Success;
    public string Message = string.Empty;
    public int WrittenEntryCount;
    public int WrittenFileCount;
}

internal static class LanguageXmlWriteService {
    public static List<LanguageWorksetFile> ReadWorksets(string outputModDir,
        IReadOnlyCollection<string> languageFolders) {
        var result = new List<LanguageWorksetFile>();
        foreach (var languageFolder in languageFolders.Distinct(StringComparer.OrdinalIgnoreCase)) {
            var root = Path.Combine(outputModDir, "Languages", languageFolder);
            if (!Directory.Exists(root)) continue;

            var workset = new LanguageWorksetFile { LanguageFolderName = languageFolder };
            foreach (var file in Directory.EnumerateFiles(root, "*.xml", SearchOption.AllDirectories)) {
                var relative = Path.GetRelativePath(root, file).Split(Path.DirectorySeparatorChar);
                if (relative.Length >= 2 && relative[0].Equals(LoadedLanguage.KeyedTranslationsFolderName,
                        StringComparison.OrdinalIgnoreCase)) {
                    ReadEntries(file, workset.Keyed, null);
                } else if (relative.Length >= 3 && relative[0].Equals(LoadedLanguage.DefInjectionsFolderName,
                               StringComparison.OrdinalIgnoreCase)) {
                    ReadEntries(file, workset.DefInjected, relative[1]);
                }
            }

            result.Add(workset);
        }

        return result;
    }

    public static LanguageXmlWriteResult WriteFromWorkset(string outputModDir, LanguageWorksetFile workset,
        bool includePlaceholders = false) {
        try {
            var languageFolderName = workset.LanguageFolderName;
            var writtenEntryCount = 0;
            var writtenFileCount = 0;

            var keyedOutputPath = Path.Combine(
                outputModDir,
                "Languages",
                languageFolderName,
                LoadedLanguage.KeyedTranslationsFolderName,
                BuildOutputFileName(LoadedLanguage.KeyedTranslationsFolderName));

            var keyedEntries = workset.Keyed
                .Where(item => !item.Tag.NullOrEmpty() &&
                               (includePlaceholders || !item.Translation.NullOrEmpty()))
                .OrderBy(item => item.Tag, StringComparer.Ordinal)
                .Select(item => new XmlEntry {
                    Tag = item.Tag,
                    Original = item.Original,
                    Translation = item.Translation.NullOrEmpty() ? LoadedLanguage.PlaceholderText : item.Translation
                })
                .ToList();

            var keyedWrittenCount = WriteLanguageDataFile(keyedOutputPath, keyedEntries, languageFolderName);
            if (keyedWrittenCount > 0) {
                writtenEntryCount += keyedWrittenCount;
                writtenFileCount += 1;
            }

            var defGroups = workset.DefInjected
                .Where(item =>
                    !item.DefType.NullOrEmpty() &&
                    !item.Tag.NullOrEmpty() &&
                    (includePlaceholders || !item.Translation.NullOrEmpty()))
                .GroupBy(item => item.DefType, StringComparer.Ordinal);

            foreach (var group in defGroups) {
                var defType = group.Key;
                var defOutputPath = Path.Combine(
                    outputModDir,
                    "Languages",
                    languageFolderName,
                    LoadedLanguage.DefInjectionsFolderName,
                    defType,
                    BuildOutputFileName(defType));

                var entries = group
                    .OrderBy(item => item.Tag, StringComparer.Ordinal)
                    .Select(item => new XmlEntry {
                        Tag = item.Tag,
                        Original = item.Original,
                        Translation = item.Translation.NullOrEmpty() ? LoadedLanguage.PlaceholderText : item.Translation
                    })
                    .ToList();

                var writtenCount = WriteLanguageDataFile(defOutputPath, entries, languageFolderName);
                if (writtenCount <= 0) {
                    continue;
                }

                writtenEntryCount += writtenCount;
                writtenFileCount += 1;
            }

            return new LanguageXmlWriteResult {
                Success = true,
                Message = "OK",
                WrittenEntryCount = writtenEntryCount,
                WrittenFileCount = writtenFileCount
            };
        } catch (Exception ex) {
            return new LanguageXmlWriteResult {
                Success = false,
                Message = ex.Message
            };
        }
    }

    private static int WriteLanguageDataFile(string outputFilePath, IReadOnlyCollection<XmlEntry> entries,
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
                root.Add(new XComment(SanitizeXComment($" EN: {entry.Original.Replace("\n", "\\n")} ")));
            }

            root.Add(element);
            writtenCount += 1;
        }

        if (writtenCount == 0) {
            return 0;
        }

        var directory = Path.GetDirectoryName(outputFilePath)!;
        Directory.CreateDirectory(directory);

        new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            root).Save(outputFilePath);
        return writtenCount;
    }

    private static void ReadEntries<T>(string path, ICollection<T> destination, string? defType) {
        var document = XDocument.Load(path);
        string original = string.Empty;
        foreach (var node in document.Root?.Nodes() ?? []) {
            if (node is XComment comment && comment.Value.TrimStart().StartsWith("EN:", StringComparison.OrdinalIgnoreCase)) {
                original = comment.Value.Trim().Length > 3 ? comment.Value.Trim()[3..].Trim() : string.Empty;
                original = original.Replace("\\n", "\n");
                continue;
            }

            if (node is not XElement element) continue;
            var translation = element.Value == LoadedLanguage.PlaceholderText ? string.Empty : element.Value;
            if (defType is null) {
                ((ICollection<LanguageWorksetKeyedItem>)destination).Add(new LanguageWorksetKeyedItem {
                    Tag = element.Name.LocalName,
                    Original = original,
                    Translation = translation
                });
            } else {
                ((ICollection<LanguageWorksetDefInjectedItem>)destination).Add(new LanguageWorksetDefInjectedItem {
                    Tag = element.Name.LocalName,
                    Original = original,
                    Translation = translation,
                    DefType = defType,
                    IsCollectionItem = element.Name.LocalName.LastIndexOf('.') >= 0
                });
            }

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

    private static string SanitizeXComment(string comment) {
        while (comment.Contains("-----")) {
            comment = comment.Replace("-----", "- - -");
        }

        while (comment.Contains("--")) {
            comment = comment.Replace("--", "- -");
        }

        return comment;
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

    private static string BuildOutputFileName(string parentFolderName) {
        return ModPathHelper.SanitizeFileNamePart(parentFolderName, "Translation") + ".xml";
    }

    private sealed class XmlEntry {
        public string Tag = string.Empty;
        public string Original = string.Empty;
        public string Translation = string.Empty;
    }
}
