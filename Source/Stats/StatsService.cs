using Verse;

namespace Translator.Stats;

internal sealed record TranslationSectionStats(int TranslatableCount, int MissingCount);

internal sealed record StatsSnapshot(TranslationSectionStats DefStats, TranslationSectionStats KeyStats);

/// <summary>
/// Computes per-mod translation coverage in the background and caches the last completed snapshot.
/// The lock only guards the caches; the expensive traversal runs outside it.
/// </summary>
internal static class StatsService {
    private static readonly Lock Gate = new();
    private static readonly Dictionary<string, StatsSnapshot> Cache = new(StringComparer.Ordinal);
    private static string? _pendingPackageId;
    private static string _activeFolder = string.Empty;
    private static string _defaultFolder = string.Empty;

    public static StatsSnapshot? Request(ModMetaData mod) {
        var activeLanguage = LanguageDatabase.activeLanguage;
        var defaultLanguage = LanguageDatabase.defaultLanguage;

        activeLanguage.LoadData();
        defaultLanguage.LoadData();
        RefreshLanguageKey(activeLanguage, defaultLanguage);

        var packageId = mod.PackageId;
        lock (Gate) {
            if (Cache.TryGetValue(packageId, out var cached)) {
                return cached;
            }

            // Def/GenType traversal touches non-thread-safe static caches, so only one build at a time.
            if (_pendingPackageId is not null) {
                return null;
            }

            _pendingPackageId = packageId;
        }

        _ = Task.Run(() => Build(packageId, mod, activeLanguage, defaultLanguage));
        return null;
    }

    private static void Build(
        string packageId,
        ModMetaData mod,
        LoadedLanguage activeLanguage,
        LoadedLanguage defaultLanguage) {
        StatsSnapshot snapshot;

        try {
            snapshot = new StatsSnapshot(
                DefStatsCounter.Count(mod, activeLanguage),
                KeyStatsCounter.Count(mod, activeLanguage, defaultLanguage));
        } catch (Exception ex) {
            Log.Error($"[Translator] Failed to build stats for {packageId}: {ex}");
            snapshot = new StatsSnapshot(new TranslationSectionStats(0, 0), new TranslationSectionStats(0, 0));
        }

        lock (Gate) {
            if (_activeFolder == activeLanguage.folderName && _defaultFolder == defaultLanguage.folderName) {
                Cache[packageId] = snapshot;
            }

            if (_pendingPackageId == packageId) {
                _pendingPackageId = null;
            }
        }
    }

    private static void RefreshLanguageKey(LoadedLanguage activeLanguage, LoadedLanguage defaultLanguage) {
        lock (Gate) {
            if (_activeFolder == activeLanguage.folderName && _defaultFolder == defaultLanguage.folderName) {
                return;
            }

            Cache.Clear();
            _activeFolder = activeLanguage.folderName;
            _defaultFolder = defaultLanguage.folderName;
        }
    }
}
