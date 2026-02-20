using System.Text.Json;
using Verse;

namespace Translator.Services;

internal sealed class TermbaseEntry {
    public string Source { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public string TargetLanguageFolder { get; set; } = string.Empty;
}

internal sealed class TermbaseTranslation {
    public string Language { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
}

internal sealed class TermbaseSourceTerm {
    public string Source { get; set; } = string.Empty;
    public List<TermbaseTranslation> Translations { get; set; } = [];
}

internal sealed class TermbaseStoreResult {
    public bool Success;
    public string Message = string.Empty;
}

internal static class TermbaseService {
    private static readonly JsonSerializerOptions JsonOptions = new() {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static IReadOnlyList<TermbaseEntry> LoadEntries() {
        try {
            var filePath = GetTermbaseFilePath();
            if (!File.Exists(filePath)) {
                return [];
            }

            var json = File.ReadAllText(filePath);
            if (json.NullOrEmpty()) {
                return [];
            }

            var sourceTerms = JsonSerializer.Deserialize<List<TermbaseSourceTerm>>(json, JsonOptions) ?? [];
            return sourceTerms
                .Where(sourceTerm => sourceTerm is not null)
                .SelectMany(sourceTerm => {
                    var source = sourceTerm.Source.NullOrEmpty() ? string.Empty : sourceTerm.Source.Trim();
                    return (sourceTerm.Translations)
                        .Where(translation => translation is not null)
                        .Select(translation => new TermbaseEntry {
                            Source = source,
                            Target = translation.Target.NullOrEmpty() ? string.Empty : translation.Target.Trim(),
                            TargetLanguageFolder = translation.Language.NullOrEmpty()
                                ? string.Empty
                                : translation.Language.Trim()
                        });
                })
                .Where(entry => !entry.Source.NullOrEmpty())
                .Where(entry => !entry.Target.NullOrEmpty())
                .ToList();
        } catch (Exception ex) {
            Log.Error($"[Translator] Failed to load termbase: {ex}");
            return [];
        }
    }

    public static TermbaseStoreResult SaveEntries(IEnumerable<TermbaseEntry> entries) {
        try {
            var normalizedEntries = entries
                .Where(entry => entry is not null)
                .Select(entry => new TermbaseEntry {
                    Source = entry.Source.NullOrEmpty() ? string.Empty : entry.Source.Trim(),
                    Target = entry.Target.NullOrEmpty() ? string.Empty : entry.Target.Trim(),
                    TargetLanguageFolder = entry.TargetLanguageFolder.NullOrEmpty()
                        ? string.Empty
                        : entry.TargetLanguageFolder.Trim()
                })
                .Where(entry => !entry.TargetLanguageFolder.NullOrEmpty())
                .Where(entry => !entry.Source.NullOrEmpty())
                .Where(entry => !entry.Target.NullOrEmpty())
                .ToList();

            var sourceTerms = normalizedEntries
                .GroupBy(entry => entry.Source, StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group => new TermbaseSourceTerm {
                    Source = group.First().Source,
                    Translations = group
                        .GroupBy(entry => entry.TargetLanguageFolder, StringComparer.OrdinalIgnoreCase)
                        .OrderBy(languageGroup => languageGroup.Key, StringComparer.OrdinalIgnoreCase)
                        .Select(languageGroup => new TermbaseTranslation {
                            Language = languageGroup.First().TargetLanguageFolder,
                            Target = languageGroup.Last().Target
                        })
                        .ToList()
                })
                .ToList();
            var json = JsonSerializer.Serialize(sourceTerms, JsonOptions);

            var filePath = GetTermbaseFilePath();
            var directory = Path.GetDirectoryName(filePath);
            if (!directory.NullOrEmpty()) {
                Directory.CreateDirectory(directory!);
            }

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
        if (targetLanguageFolder.NullOrEmpty()) {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in LoadEntries()) {
            if (!string.Equals(entry.TargetLanguageFolder, targetLanguageFolder, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            if (entry.Source.NullOrEmpty() || entry.Target.NullOrEmpty()) {
                continue;
            }

            map[entry.Source] = entry.Target;
        }

        return map;
    }

    private static string GetTermbaseFilePath() {
        return Path.Combine(GenFilePaths.SaveDataFolderPath, "Translator", "termbase.json");
    }
}