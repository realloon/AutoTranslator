namespace Translator.Termbase;

/// <summary>Mutable because the termbase window edits rows in place.</summary>
public sealed class TermbaseEntry {
    public TermbaseEntry() { }

    public TermbaseEntry(string source, string target, string targetLanguageFolder) {
        Source = source;
        Target = target;
        TargetLanguageFolder = targetLanguageFolder;
    }

    public string Source { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public string TargetLanguageFolder { get; set; } = string.Empty;
}

public sealed class TermbaseSourceTerm {
    public string Source { get; init; } = string.Empty;
    public List<TermbaseTranslation> Translations { get; init; } = [];
}

public sealed class TermbaseTranslation {
    public string Language { get; init; } = string.Empty;
    public string Target { get; init; } = string.Empty;
}

/// <summary>
/// Pure transforms between the on-disk termbase document and flat entries.
/// </summary>
public static class TermbaseDocument {
    public static IReadOnlyList<TermbaseEntry> Flatten(IEnumerable<TermbaseSourceTerm> sourceTerms) {
        var entries = new List<TermbaseEntry>();

        foreach (var sourceTerm in sourceTerms) {
            var source = sourceTerm.Source.Trim();
            if (source.Length == 0) {
                continue;
            }

            foreach (var translation in sourceTerm.Translations) {
                var target = translation.Target.Trim();
                if (target.Length == 0) {
                    continue;
                }

                entries.Add(new TermbaseEntry(source, target, translation.Language.Trim()));
            }
        }

        return entries;
    }

    public static List<TermbaseSourceTerm> Group(IEnumerable<TermbaseEntry> entries) {
        var normalized = entries
            .Select(entry => new TermbaseEntry(
                entry.Source.Trim(), entry.Target.Trim(), entry.TargetLanguageFolder.Trim()))
            .Where(entry => entry.Source.Length > 0 && entry.Target.Length > 0 &&
                            entry.TargetLanguageFolder.Length > 0)
            .ToList();

        return normalized
            .GroupBy(entry => entry.Source, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(sourceGroup => new TermbaseSourceTerm {
                Source = sourceGroup.First().Source,
                Translations = sourceGroup
                    .GroupBy(entry => entry.TargetLanguageFolder, StringComparer.OrdinalIgnoreCase)
                    .OrderBy(languageGroup => languageGroup.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(languageGroup => new TermbaseTranslation {
                        Language = languageGroup.First().TargetLanguageFolder,
                        Target = languageGroup.Last().Target
                    })
                    .ToList()
            })
            .ToList();
    }

    public static Dictionary<string, string> BuildGlossary(
        IEnumerable<TermbaseEntry> entries,
        string targetLanguageFolder) {
        var glossary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries) {
            if (string.Equals(entry.TargetLanguageFolder, targetLanguageFolder, StringComparison.OrdinalIgnoreCase)) {
                glossary[entry.Source] = entry.Target;
            }
        }

        return glossary;
    }
}
