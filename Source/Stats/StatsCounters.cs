using Translator.Utilities;
using Verse;

namespace Translator.Stats;

internal static class DefStatsCounter {
    public static TranslationSectionStats Count(ModMetaData mod, LoadedLanguage activeLanguage) {
        var translatableCount = 0;
        var missingCount = 0;

        var packagesByDefType = new Dictionary<Type, DefInjectionPackage>();
        foreach (var package in activeLanguage.defInjections) {
            packagesByDefType.TryAdd(package.defType, package);
        }

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

                            translatableCount++;
                            if (IsMissingSingleInjection(normalizedPath, injectionsByNormalizedPath)) {
                                missingCount++;
                            }

                            return;
                        }

                        var values = currentValueCollection.ToList();
                        var translatableIndexes = new List<int>(values.Count);
                        for (var i = 0; i < values.Count; i++) {
                            if (DefInjectionUtility.ShouldCheckMissingInjection(values[i], fieldInfo, def)) {
                                translatableIndexes.Add(i);
                            }
                        }

                        if (translatableIndexes.Count == 0) {
                            return;
                        }

                        translatableCount += translatableIndexes.Count;
                        missingCount += CountMissingCollectionInjections(
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

        return new(translatableCount, missingCount);
    }

    private static IReadOnlyDictionary<string, DefInjectionPackage.DefInjection> BuildNormalizedInjectionLookup(
        DefInjectionPackage? package) {
        var lookup = new Dictionary<string, DefInjectionPackage.DefInjection>(StringComparer.Ordinal);
        if (package is null) {
            return lookup;
        }

        foreach (var (_, injection) in package.injections) {
            var normalizedPath = injection.normalizedPath;
            if (normalizedPath.NullOrEmpty() || lookup.ContainsKey(normalizedPath)) {
                continue;
            }

            lookup.Add(normalizedPath, injection);
        }

        return lookup;
    }

    private static bool IsMissingSingleInjection(
        string normalizedPath,
        IReadOnlyDictionary<string, DefInjectionPackage.DefInjection> injectionsByNormalizedPath) {
        if (!injectionsByNormalizedPath.TryGetValue(normalizedPath, out var existingInjection)) {
            return true;
        }

        return existingInjection.IsFullListInjection || existingInjection.isPlaceholder;
    }

    private static int CountMissingCollectionInjections(
        string normalizedPath,
        IReadOnlyList<int> translatableIndexes,
        bool fullListTranslationAllowed,
        Def def,
        IReadOnlyDictionary<string, DefInjectionPackage.DefInjection> injectionsByNormalizedPath) {
        if (!injectionsByNormalizedPath.TryGetValue(normalizedPath, out var listInjection) ||
            !listInjection.IsFullListInjection) {
            var count = 0;
            foreach (var index in translatableIndexes) {
                var itemPath = normalizedPath + "." + index;
                if (!injectionsByNormalizedPath.TryGetValue(itemPath, out var itemInjection) ||
                    itemInjection.IsFullListInjection ||
                    itemInjection.isPlaceholder) {
                    count++;
                }
            }

            return count;
        }

        if (!fullListTranslationAllowed) {
            return 0;
        }

        return listInjection.isPlaceholder && !def.generated ? translatableIndexes.Count : 0;
    }
}

internal static class KeyStatsCounter {
    public static TranslationSectionStats Count(
        ModMetaData mod,
        LoadedLanguage activeLanguage,
        LoadedLanguage defaultLanguage) {
        var defaultKeys = CollectDefaultKeyedKeysForMod(mod, defaultLanguage);
        var missingCount = activeLanguage != defaultLanguage
            ? defaultKeys.Count(key => !activeLanguage.HaveTextForKey(key))
            : 0;

        return new(defaultKeys.Count, missingCount);
    }

    private static HashSet<string> CollectDefaultKeyedKeysForMod(
        ModMetaData mod,
        LoadedLanguage defaultLanguage) =>
        ModKeyedTexts.Of(mod, defaultLanguage).Select(entry => entry.Key).ToHashSet(StringComparer.Ordinal);
}
