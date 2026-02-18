using Translator.Services;
using Verse;

namespace Translator.Helpers;

internal static class DefStatsHelper {
    private static readonly IReadOnlyDictionary<string, DefInjectionPackage.DefInjection> EmptyInjectionLookup =
        new Dictionary<string, DefInjectionPackage.DefInjection>();

    public static DefTranslationStats BuildStats(ModMetaData mod, LoadedLanguage activeLanguage) {
        var stats = new DefTranslationStats();
        var packagesByDefType = activeLanguage.defInjections
            .GroupBy(p => p.defType)
            .ToDictionary(g => g.Key, g => g.First());

        foreach (var defType in GenDefDatabase.AllDefTypesWithDatabases()) {
            try {
                packagesByDefType.TryGetValue(defType, out var package);
                var injectionsByNormalizedPath = BuildNormalizedInjectionLookup(package);
                DefInjectionUtility.ForEachPossibleDefInjection(
                    defType,
                    (_, normalizedPath, isCollection, currentValue, currentValueCollection, translationAllowed,
                        fullListTranslationAllowed, fieldInfo, def) => {
                        if (!translationAllowed) {
                            return;
                        }

                        if (!isCollection) {
                            if (!DefInjectionUtility.ShouldCheckMissingInjection(currentValue, fieldInfo, def)) {
                                return;
                            }

                            stats.TranslatableInjectionItemCount += 1;
                            if (IsMissingSingleInjection(
                                normalizedPath,
                                injectionsByNormalizedPath)) {
                                stats.MissingDefInjectionCount += 1;
                            }

                            return;
                        }

                        var values = currentValueCollection?.ToList() ?? [];
                        var translatableIndexes = new List<int>(values.Count);
                        for (var i = 0; i < values.Count; i++) {
                            if (DefInjectionUtility.ShouldCheckMissingInjection(values[i], fieldInfo, def)) {
                                translatableIndexes.Add(i);
                            }
                        }

                        if (translatableIndexes.Count == 0) {
                            return;
                        }

                        stats.TranslatableInjectionItemCount += translatableIndexes.Count;
                        stats.MissingDefInjectionCount += CountMissingCollectionInjections(
                            normalizedPath,
                            translatableIndexes,
                            fullListTranslationAllowed,
                            def,
                            injectionsByNormalizedPath);
                    },
                    mod);
            } catch (Exception ex) {
                Log.Warning($"[Translator] Def stats traversal failed for {defType.Name}: {ex}");
            }
        }

        return stats;
    }

    private static IReadOnlyDictionary<string, DefInjectionPackage.DefInjection> BuildNormalizedInjectionLookup(
        DefInjectionPackage? package) {
        if (package is null || package.injections.Count == 0) {
            return EmptyInjectionLookup;
        }

        var lookup = new Dictionary<string, DefInjectionPackage.DefInjection>();
        foreach (var injection in package.injections) {
            var normalizedPath = injection.Value.normalizedPath;
            if (normalizedPath.NullOrEmpty() || lookup.ContainsKey(normalizedPath)) {
                continue;
            }

            lookup.Add(normalizedPath, injection.Value);
        }

        return lookup;
    }

    private static bool IsMissingSingleInjection(string normalizedPath,
        IReadOnlyDictionary<string, DefInjectionPackage.DefInjection> injectionsByNormalizedPath) {
        if (!injectionsByNormalizedPath.TryGetValue(normalizedPath, out var existingInjection)) {
            return true;
        }

        if (existingInjection.IsFullListInjection) {
            return true;
        }

        return existingInjection.isPlaceholder;
    }

    private static int CountMissingCollectionInjections(string normalizedPath, IReadOnlyList<int> translatableIndexes,
        bool fullListTranslationAllowed, Def def,
        IReadOnlyDictionary<string, DefInjectionPackage.DefInjection> injectionsByNormalizedPath) {
        if (!injectionsByNormalizedPath.TryGetValue(normalizedPath, out var listInjection) ||
            !listInjection.IsFullListInjection) {
            var count = 0;
            foreach (var index in translatableIndexes) {
                var itemPath = normalizedPath + "." + index;
                if (!injectionsByNormalizedPath.TryGetValue(itemPath, out var itemInjection) ||
                    itemInjection.IsFullListInjection ||
                    itemInjection.isPlaceholder) {
                    count += 1;
                }
            }

            return count;
        }

        if (!fullListTranslationAllowed) {
            return 0;
        }

        if (listInjection.isPlaceholder && !def.generated) {
            return translatableIndexes.Count;
        }

        return 0;
    }
}
