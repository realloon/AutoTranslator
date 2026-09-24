using Translator.Helpers;
using Verse;

namespace Translator.Services;

internal sealed class DefTranslationStats {
    public int TranslatableInjectionItemCount;
    public int MissingDefInjectionCount;
}

internal sealed class StaticTranslateStats {
    public int UniqueLiteralKeyCount;
    public int MissingKeyCount;
}

internal sealed class StatsSnapshot {
    public DefTranslationStats DefStats = new();
    public StaticTranslateStats KeyStats = new();
}

internal static class StatsService {
    private static readonly Dictionary<string, StatsSnapshot> StatsByPackageId = [];
    private static readonly object Gate = new();
    private static string? _pendingPackageId;
    private static string? _statsActiveFolder;
    private static string? _statsDefaultFolder;

    /// <summary>
    /// Returns cached stats, or null while a background build is queued/running.
    /// </summary>
    public static StatsSnapshot? RequestStats(ModMetaData mod) {
        var activeLanguage = LanguageDatabase.activeLanguage;
        var defaultLanguage = LanguageDatabase.defaultLanguage;
        activeLanguage.LoadData();
        defaultLanguage.LoadData();
        RefreshStatsCacheByLanguage(activeLanguage, defaultLanguage);

        lock (Gate) {
            if (StatsByPackageId.TryGetValue(mod.PackageId, out var snapshot)) {
                return snapshot;
            }

            if (_pendingPackageId is not null) {
                return null;
            }

            _pendingPackageId = mod.PackageId;
        }

        var packageId = mod.PackageId;
        Task.Run(() => {
            StatsSnapshot snapshot;
            try {
                snapshot = BuildStatsSnapshot(mod, activeLanguage, defaultLanguage);
            } catch (Exception ex) {
                Log.Error($"[Translator] Failed to build stats for {packageId}: {ex}");
                snapshot = new StatsSnapshot();
            }

            lock (Gate) {
                if (_statsActiveFolder == activeLanguage.folderName
                    && _statsDefaultFolder == defaultLanguage.folderName) {
                    StatsByPackageId[packageId] = snapshot;
                }

                if (_pendingPackageId == packageId) {
                    _pendingPackageId = null;
                }
            }
        });

        return null;
    }

    private static StatsSnapshot BuildStatsSnapshot(ModMetaData mod, LoadedLanguage activeLanguage,
        LoadedLanguage defaultLanguage) {
        return new StatsSnapshot {
            DefStats = DefStatsHelper.BuildStats(mod, activeLanguage),
            KeyStats = KeyStatsHelper.BuildStats(mod, activeLanguage, defaultLanguage)
        };
    }

    private static void RefreshStatsCacheByLanguage(LoadedLanguage activeLanguage, LoadedLanguage defaultLanguage) {
        if (_statsActiveFolder == activeLanguage.folderName && _statsDefaultFolder == defaultLanguage.folderName) {
            return;
        }

        lock (Gate) {
            StatsByPackageId.Clear();
            _pendingPackageId = null;
            _statsActiveFolder = activeLanguage.folderName;
            _statsDefaultFolder = defaultLanguage.folderName;
        }
    }
}
