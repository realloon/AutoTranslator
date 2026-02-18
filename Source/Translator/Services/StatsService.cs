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
    private static readonly Dictionary<string, Lazy<StatsSnapshot>> StatsByPackageId = [];
    private static string? _statsLanguageCacheKey;

    public static (DefTranslationStats DefStats, StaticTranslateStats KeyStats) GetOrBuildStats(ModMetaData mod) {
        var activeLanguage = LanguageDatabase.activeLanguage;
        var defaultLanguage = LanguageDatabase.defaultLanguage;
        activeLanguage.LoadData();
        defaultLanguage.LoadData();
        RefreshStatsCacheByLanguage(activeLanguage, defaultLanguage);

        if (!StatsByPackageId.TryGetValue(mod.PackageId, out var lazyStats)) {
            lazyStats = new Lazy<StatsSnapshot>(() => BuildStatsSnapshot(mod, activeLanguage, defaultLanguage),
                LazyThreadSafetyMode.None);
            StatsByPackageId[mod.PackageId] = lazyStats;
        }

        try {
            var snapshot = lazyStats.Value;
            return (snapshot.DefStats, snapshot.KeyStats);
        } catch (Exception ex) {
            StatsByPackageId.Remove(mod.PackageId);
            Log.Error($"[Translator] Failed to build stats for {mod.PackageId}: {ex}");
            return (new DefTranslationStats(), new StaticTranslateStats());
        }
    }

    private static StatsSnapshot BuildStatsSnapshot(ModMetaData mod, LoadedLanguage activeLanguage,
        LoadedLanguage defaultLanguage) {
        return new StatsSnapshot {
            DefStats = DefStatsHelper.BuildStats(mod, activeLanguage),
            KeyStats = KeyStatsHelper.BuildStats(mod, activeLanguage, defaultLanguage)
        };
    }

    private static void RefreshStatsCacheByLanguage(LoadedLanguage activeLanguage, LoadedLanguage defaultLanguage) {
        var cacheKey = $"{activeLanguage.folderName}|{defaultLanguage.folderName}";
        if (_statsLanguageCacheKey == cacheKey) {
            return;
        }

        StatsByPackageId.Clear();
        _statsLanguageCacheKey = cacheKey;
    }
}
