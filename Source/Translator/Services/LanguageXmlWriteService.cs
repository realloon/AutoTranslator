using System.Text;
using System.Xml;
using System.Xml.Linq;
using Verse;

namespace Translator.Services;

internal sealed class LanguageXmlWriteResult {
    public bool Success;
    public string Message = string.Empty;
    public int WrittenEntryCount;
    public int WrittenFileCount;
}

internal static class LanguageXmlWriteService {
    public static LanguageXmlWriteResult WriteFromWorkset(string outputModDir, LanguageWorksetFile workset) {
        try {
            if (outputModDir.NullOrEmpty()) {
                return new LanguageXmlWriteResult {
                    Success = false,
                    Message = "Output mod directory is empty."
                };
            }

            var languageFolderName = workset.LanguageFolderName;
            if (languageFolderName.NullOrEmpty()) {
                return new LanguageXmlWriteResult {
                    Success = false,
                    Message = "Language folder name is empty."
                };
            }

            var writtenEntryCount = 0;
            var writtenFileCount = 0;

            var keyedOutputPath = Path.Combine(
                outputModDir,
                "Languages",
                languageFolderName,
                LoadedLanguage.KeyedTranslationsFolderName,
                BuildOutputFileName(LoadedLanguage.KeyedTranslationsFolderName));

            var keyedEntries = workset.Keyed
                .Where(item => !item.Tag.NullOrEmpty() && !item.Translation.NullOrEmpty())
                .OrderBy(item => item.Tag, StringComparer.Ordinal)
                .Select(item => new XmlEntry {
                    Tag = item.Tag,
                    Translation = item.Translation
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
                    !item.Translation.NullOrEmpty())
                .GroupBy(item => item.DefType, StringComparer.Ordinal);

            foreach (var group in defGroups) {
                var defType = group.Key;
                if (defType.NullOrEmpty()) {
                    continue;
                }

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
                        Translation = item.Translation
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
                Message = ex.Message,
                WrittenEntryCount = 0,
                WrittenFileCount = 0
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

            root.Add(element);
            writtenCount += 1;
        }

        if (writtenCount == 0) {
            return 0;
        }

        var directory = Path.GetDirectoryName(outputFilePath)!;
        Directory.CreateDirectory(directory);

        var doc = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            root);
        File.WriteAllText(outputFilePath, doc.ToString(), Encoding.UTF8);
        return writtenCount;
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

    private static string BuildOutputFileName(string parentFolderName) {
        var safeName = SanitizeFileNamePart(parentFolderName);
        return safeName + ".xml";
    }

    private static string SanitizeFileNamePart(string value) {
        if (value.NullOrEmpty()) {
            return "Translation";
        }

        var invalidChars = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value) {
            builder.Append(invalidChars.Contains(ch) ? '_' : ch);
        }

        return builder.ToString();
    }

    private sealed class XmlEntry {
        public string Tag = string.Empty;
        public string Translation = string.Empty;
    }
}