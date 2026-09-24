using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Verse;

namespace Translator.Services;

internal sealed class TermbaseEntry {
    public string Source = string.Empty;
    public string Target = string.Empty;
    public string TargetLanguageFolder = string.Empty;
}

internal sealed class TermbaseTranslation {
    public string Language = string.Empty;
    public string Target = string.Empty;
}

internal sealed class TermbaseSourceTerm {
    public string Source = string.Empty;
    public List<TermbaseTranslation> Translations = [];
}

internal sealed class TermbaseStoreResult {
    public bool Success;
    public string Message = string.Empty;
}

internal static class TermbaseService {
    private static readonly JsonSerializerSettings JsonSettings = new() {
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        Formatting = Formatting.Indented
    };

    public static IReadOnlyList<TermbaseEntry> LoadEntries() {
        try {
            var filePath = GetTermbaseFilePath();
            if (!File.Exists(filePath)) {
                return [];
            }

            var json = File.ReadAllText(filePath);
            var sourceTerms = JsonConvert.DeserializeObject<List<TermbaseSourceTerm>>(json, JsonSettings) ?? [];
            return [
                .. sourceTerms
                    .SelectMany(sourceTerm => sourceTerm.Translations.Select(translation => new TermbaseEntry {
                        Source = sourceTerm.Source.Trim(),
                        Target = translation.Target.Trim(),
                        TargetLanguageFolder = translation.Language.Trim()
                    }))
                    .Where(entry => !entry.Source.NullOrEmpty() && !entry.Target.NullOrEmpty())
            ];
        } catch (Exception ex) {
            Log.Error($"[Translator] Failed to load termbase: {ex}");
            return [];
        }
    }

    public static TermbaseStoreResult SaveEntries(IEnumerable<TermbaseEntry> entries) {
        try {
            var normalizedEntries = entries
                .Select(entry => new TermbaseEntry {
                    Source = entry.Source.Trim(),
                    Target = entry.Target.Trim(),
                    TargetLanguageFolder = entry.TargetLanguageFolder.Trim()
                })
                .Where(entry => !entry.TargetLanguageFolder.NullOrEmpty() &&
                                !entry.Source.NullOrEmpty() &&
                                !entry.Target.NullOrEmpty())
                .ToList();

            var sourceTerms = normalizedEntries
                .GroupBy(entry => entry.Source, StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group => new TermbaseSourceTerm {
                    Source = group.First().Source,
                    Translations = [
                        .. group
                            .GroupBy(entry => entry.TargetLanguageFolder, StringComparer.OrdinalIgnoreCase)
                            .OrderBy(languageGroup => languageGroup.Key, StringComparer.OrdinalIgnoreCase)
                            .Select(languageGroup => new TermbaseTranslation {
                                Language = languageGroup.First().TargetLanguageFolder,
                                Target = languageGroup.Last().Target
                            })
                    ]
                })
                .ToList();
            var json = JsonConvert.SerializeObject(sourceTerms, JsonSettings);

            var filePath = GetTermbaseFilePath();
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            File.WriteAllText(filePath, json);
            return new TermbaseStoreResult {
                Success = true
            };
        } catch (Exception ex) {
            Log.Error($"[Translator] Failed to save termbase: {ex}");
            return new TermbaseStoreResult {
                Success = false,
                Message = ex.Message
            };
        }
    }

    public static IReadOnlyDictionary<string, string> GetGlossaryForLanguage(string targetLanguageFolder) {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in LoadEntries()) {
            if (string.Equals(entry.TargetLanguageFolder, targetLanguageFolder, StringComparison.OrdinalIgnoreCase)) {
                map[entry.Source] = entry.Target;
            }
        }

        return map;
    }

    private static string GetTermbaseFilePath() {
        return Path.Combine(GenFilePaths.SaveDataFolderPath, "Translator", "termbase.json");
    }
}