using System.Text.Json;
using Translator.Utilities;
using Verse;

namespace Translator.Termbase;

internal sealed record TermbaseStoreResult(bool Success, string Message);

/// <summary>
/// Termbase persistence. The document transform itself lives in
/// <see cref="TermbaseDocument"/> so it can be tested without RimWorld.
/// </summary>
internal static class TermbaseService {
    private static readonly Lock Gate = new();
    private static IReadOnlyList<TermbaseEntry>? _cachedEntries;
    private static DateTime _cachedWriteTimeUtc;

    public static IReadOnlyList<TermbaseEntry> LoadEntries() {
        try {
            var filePath = GetTermbaseFilePath();
            if (!File.Exists(filePath)) {
                lock (Gate) {
                    _cachedEntries = null;
                }

                return [];
            }

            var writeTimeUtc = File.GetLastWriteTimeUtc(filePath);
            lock (Gate) {
                if (_cachedEntries is not null && writeTimeUtc == _cachedWriteTimeUtc) {
                    return _cachedEntries;
                }
            }

            var json = File.ReadAllText(filePath);
            var sourceTerms = JsonSerializer.Deserialize<List<TermbaseSourceTerm>>(json, TranslatorJson.File)
                              ?? [];
            var entries = TermbaseDocument.Flatten(sourceTerms);

            lock (Gate) {
                _cachedEntries = entries;
                _cachedWriteTimeUtc = writeTimeUtc;
            }

            return entries;
        } catch (Exception ex) {
            Log.Error($"[Translator] Failed to load termbase: {ex}");
            return [];
        }
    }

    public static TermbaseStoreResult SaveEntries(IEnumerable<TermbaseEntry> entries) {
        try {
            var sourceTerms = TermbaseDocument.Group(entries);
            var json = JsonSerializer.Serialize(sourceTerms, TranslatorJson.File);

            var filePath = GetTermbaseFilePath();
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            File.WriteAllText(filePath, json);

            lock (Gate) {
                _cachedEntries = null;
            }

            return new TermbaseStoreResult(true, string.Empty);
        } catch (Exception ex) {
            Log.Error($"[Translator] Failed to save termbase: {ex}");
            return new TermbaseStoreResult(false, ex.Message);
        }
    }

    public static IReadOnlyDictionary<string, string> GetGlossaryForLanguage(string targetLanguageFolder) =>
        TermbaseDocument.BuildGlossary(LoadEntries(), targetLanguageFolder);

    private static string GetTermbaseFilePath() =>
        Path.Combine(GenFilePaths.SaveDataFolderPath, "Translator", "termbase.json");
}
