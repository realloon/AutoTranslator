using System.Text;
using System.Xml.Linq;
using RimWorld;
using Verse;

namespace Translator.Services;

internal sealed class TranslatorIrExportResult {
    public bool Success;
    public string Message = string.Empty;
    public string? FilePath;
    public List<LanguageWorksetFile> Worksets = [];
}

internal static class IrExportService {
    public static TranslatorIrExportResult Export(ModMetaData mod, IReadOnlyCollection<LoadedLanguage> targetLanguages,
        LoadedLanguage defaultLanguage) {
        try {
            if (targetLanguages.Count == 0) {
                return new TranslatorIrExportResult {
                    Success = false,
                    Message = "No language selected."
                };
            }

            defaultLanguage.LoadData();
            var exportedLanguageFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var worksets = new List<LanguageWorksetFile>();
            foreach (var language in targetLanguages) {
                if (language is null || language.folderName.NullOrEmpty() ||
                    !exportedLanguageFolders.Add(language.folderName)) {
                    continue;
                }

                language.LoadData();
                worksets.Add(new LanguageWorksetFile {
                    LanguageFolderName = language.folderName,
                    Keyed = BuildKeyedEntries(mod, language, defaultLanguage),
                    DefInjected = BuildDefInjectedEntries(mod, language)
                });
            }

            if (worksets.Count == 0) {
                return new TranslatorIrExportResult {
                    Success = false,
                    Message = "No valid language selected."
                };
            }

            var exportToken = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            var outputModDir = ResolveExportModDirectory(mod, exportToken);
            EnsureExportStructure(outputModDir);
            BuildLanguageDirectoryStructure(outputModDir, worksets);
            WriteAboutFile(mod, outputModDir, exportToken);

            return new TranslatorIrExportResult {
                Success = true,
                FilePath = outputModDir,
                Message = "OK",
                Worksets = worksets
            };
        } catch (Exception ex) {
            Log.Error($"[Translator] Failed to export translation mod for {mod.PackageId}: {ex}");
            return new TranslatorIrExportResult {
                Success = false,
                Message = ex.Message
            };
        }
    }

    private static List<LanguageWorksetKeyedItem> BuildKeyedEntries(ModMetaData mod, LoadedLanguage activeLanguage,
        LoadedLanguage defaultLanguage) {
        var modRoot = NormalizePath(mod.RootDir.FullName);
        var entries = new List<LanguageWorksetKeyedItem>();
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var keyedReplacement in defaultLanguage.keyedReplacements) {
            var key = keyedReplacement.Key;
            if (!seenKeys.Add(key)) {
                continue;
            }

            var sourcePath = keyedReplacement.Value.fileSourceFullPath;
            if (sourcePath.NullOrEmpty() || !IsPathUnderRoot(sourcePath, modRoot)) {
                continue;
            }

            var defaultText = defaultLanguage.TryGetTextFromKey(key, out var text) ? text.ToString() : string.Empty;
            var currentText = TryGetExistingKeyedTranslation(activeLanguage, key);
            entries.Add(new LanguageWorksetKeyedItem {
                Tag = key,
                Original = defaultText,
                Translation = currentText
            });
        }

        entries.Sort((left, right) => string.Compare(left.Tag, right.Tag, StringComparison.Ordinal));
        return entries;
    }

    private static List<LanguageWorksetDefInjectedItem> BuildDefInjectedEntries(ModMetaData mod,
        LoadedLanguage activeLanguage) {
        var injectionsByType = BuildDefInjectionLookup(activeLanguage, mod);
        var entries = new List<LanguageWorksetDefInjectedItem>();

        foreach (var defType in GenDefDatabase.AllDefTypesWithDatabases()) {
            injectionsByType.TryGetValue(defType, out var injectionsByNormalizedPath);
            injectionsByNormalizedPath ??= new Dictionary<string, DefInjectionPackage.DefInjection>();

            try {
                DefInjectionUtility.ForEachPossibleDefInjection(
                    defType,
                    (suggestedPath, normalizedPath, isCollection, currentValue, currentValueCollection,
                        translationAllowed,
                        _, fieldInfo, def) => {
                        if (!translationAllowed) {
                            return;
                        }

                        if (!isCollection) {
                            var requiresTranslation =
                                DefInjectionUtility.ShouldCheckMissingInjection(currentValue, fieldInfo, def);
                            injectionsByNormalizedPath.TryGetValue(normalizedPath, out var singleInjection);
                            var hasTranslation = singleInjection is
                                { IsFullListInjection: false, isPlaceholder: false };
                            var defaultText = singleInjection?.injected == true
                                ? singleInjection.replacedString ?? currentValue ?? string.Empty
                                : currentValue ?? string.Empty;
                            var currentText =
                                hasTranslation ? singleInjection!.injection ?? string.Empty : string.Empty;
                            if (!requiresTranslation && !hasTranslation) {
                                return;
                            }

                            entries.Add(new LanguageWorksetDefInjectedItem {
                                Tag = suggestedPath,
                                Original = defaultText,
                                Translation = currentText,
                                DefType = defType.Name,
                                IsCollectionItem = false
                            });
                            return;
                        }

                        var listValues = currentValueCollection?.ToList() ?? [];
                        if (!injectionsByNormalizedPath.TryGetValue(normalizedPath, out var listInjection) ||
                            !listInjection.IsFullListInjection) {
                            listInjection = null;
                        }

                        for (var i = 0; i < listValues.Count; i++) {
                            var itemValue = listValues[i] ?? string.Empty;
                            var requiresTranslation =
                                DefInjectionUtility.ShouldCheckMissingInjection(itemValue, fieldInfo, def);
                            var itemNormalizedPath = normalizedPath + "." + i;
                            var itemSuggestedPath = suggestedPath + "." + i;
                            if (TKeySystem.TrySuggestTKeyPath(itemNormalizedPath, out var tKeyPath)) {
                                itemSuggestedPath = tKeyPath;
                            }

                            injectionsByNormalizedPath.TryGetValue(itemNormalizedPath, out var itemInjection);
                            if (itemInjection is { IsFullListInjection: true }) {
                                itemInjection = null;
                            }

                            var defaultText = itemValue;
                            if (itemInjection?.injected == true && !itemInjection.replacedString.NullOrEmpty()) {
                                defaultText = itemInjection.replacedString;
                            } else if (listInjection is { injected: true, replacedList: not null }) {
                                defaultText = listInjection.replacedList.ElementAtOrDefault(i) ?? itemValue;
                            }

                            var hasItemTranslation = itemInjection is { isPlaceholder: false };
                            var hasListTranslation = listInjection is
                                                         { isPlaceholder: false, fullListInjection: not null } &&
                                                     i < listInjection.fullListInjection.Count;
                            var hasTranslation = hasItemTranslation || hasListTranslation;
                            var currentText = string.Empty;
                            if (hasItemTranslation) {
                                currentText = itemInjection!.injection ?? string.Empty;
                            } else if (hasListTranslation && listInjection?.fullListInjection is not null) {
                                currentText = listInjection.fullListInjection[i] ?? string.Empty;
                            }

                            if (!requiresTranslation && !hasTranslation) {
                                continue;
                            }

                            entries.Add(new LanguageWorksetDefInjectedItem {
                                Tag = itemSuggestedPath,
                                Original = defaultText,
                                Translation = currentText,
                                DefType = defType.Name,
                                IsCollectionItem = true
                            });
                        }
                    },
                    mod);
            } catch (Exception ex) {
                Log.Warning($"[Translator] Def traversal failed for {defType.Name}: {ex}");
            }
        }

        entries.Sort((left, right) => {
            var defTypeCompare = string.Compare(left.DefType, right.DefType, StringComparison.Ordinal);
            return defTypeCompare != 0
                ? defTypeCompare
                : string.Compare(left.Tag, right.Tag, StringComparison.Ordinal);
        });
        return entries;
    }

    private static Dictionary<Type, Dictionary<string, DefInjectionPackage.DefInjection>> BuildDefInjectionLookup(
        LoadedLanguage activeLanguage, ModMetaData mod) {
        var result = new Dictionary<Type, Dictionary<string, DefInjectionPackage.DefInjection>>();
        foreach (var package in activeLanguage.defInjections) {
            var defType = package.defType;
            if (defType is null || result.ContainsKey(defType)) continue;

            var lookup = new Dictionary<string, DefInjectionPackage.DefInjection>(StringComparer.Ordinal);
            foreach (var (key, injection) in package.injections) {
                if (injection is null || !injection.ModifiesDefFromModOrNullCore(mod, defType)) {
                    continue;
                }

                var normalizedPath = injection.normalizedPath;
                if (normalizedPath.NullOrEmpty()) {
                    normalizedPath = key;
                }

                if (normalizedPath.NullOrEmpty() || !lookup.TryAdd(normalizedPath, injection)) { }
            }

            result.Add(defType, lookup);
        }

        return result;
    }

    private static string TryGetExistingKeyedTranslation(LoadedLanguage activeLanguage, string key) {
        if (!activeLanguage.keyedReplacements.TryGetValue(key, out var replacement) || replacement.isPlaceholder) {
            return string.Empty;
        }

        return replacement.value ?? string.Empty;
    }

    private static bool IsPathUnderRoot(string path, string root) {
        var normalizedPath = NormalizePath(path);
        return normalizedPath.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string path) {
        try {
            return Path.GetFullPath(path)
                .Replace('\\', '/')
                .TrimEnd('/');
        } catch {
            return path.Replace('\\', '/').TrimEnd('/');
        }
    }

    private static void EnsureExportStructure(string outputModDir) {
        Directory.CreateDirectory(outputModDir);
        Directory.CreateDirectory(Path.Combine(outputModDir, "About"));
        Directory.CreateDirectory(Path.Combine(outputModDir, "Languages"));
    }

    private static void BuildLanguageDirectoryStructure(string outputModDir,
        IReadOnlyList<LanguageWorksetFile> worksets) {
        foreach (var workset in worksets) {
            if (workset.LanguageFolderName.NullOrEmpty()) {
                continue;
            }

            var languageRoot = Path.Combine(outputModDir, "Languages", workset.LanguageFolderName);
            Directory.CreateDirectory(languageRoot);
            Directory.CreateDirectory(Path.Combine(languageRoot, LoadedLanguage.KeyedTranslationsFolderName));
            Directory.CreateDirectory(Path.Combine(languageRoot, LoadedLanguage.DefInjectionsFolderName));
        }
    }

    private static void WriteAboutFile(ModMetaData mod, string outputModDir, string exportToken) {
        var packageId = BuildGeneratedPackageId(mod, exportToken);
        var aboutDoc = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement("ModMetaData",
                new XElement("packageId", packageId),
                new XElement("name", $"{mod.Name} Translation"),
                new XElement("description", $"Generated by Auto Translator for {mod.Name}."),
                new XElement("author", "Translator"),
                new XElement("supportedVersions",
                    new XElement("li", $"{VersionControl.CurrentMajor}.{VersionControl.CurrentMinor}")
                ),
                new XElement("loadAfter",
                    new XElement("li", mod.PackageIdPlayerFacing)
                )));

        var aboutPath = Path.Combine(outputModDir, "About", "About.xml");
        File.WriteAllText(aboutPath, aboutDoc.ToString(), Encoding.UTF8);
    }

    private static string BuildExportFolderName(ModMetaData mod, string exportToken) {
        var safePackage = SanitizeFileNamePart(mod.PackageIdPlayerFacing);
        return $"TranslatorExport_{safePackage}_{exportToken}";
    }

    private static string BuildGeneratedPackageId(ModMetaData mod, string exportToken) {
        var safePackage = SanitizePackageIdPart(mod.PackageIdPlayerFacing);
        var compactToken = exportToken.Replace("_", string.Empty);
        return $"translator.{safePackage}.{compactToken}";
    }

    private static string ResolveExportModDirectory(ModMetaData mod, string exportToken) {
        var folderName = BuildExportFolderName(mod, exportToken);
        try {
            var modsFolderPath = GenFilePaths.ModsFolderPath;
            if (!modsFolderPath.NullOrEmpty()) {
                return Path.Combine(modsFolderPath, folderName);
            }
        } catch (Exception ex) {
            Log.Warning($"[Translator] Could not resolve mods export path, fallback to save data folder: {ex}");
        }

        return Path.Combine(GenFilePaths.SaveDataFolderPath, "Translator", "Exports", folderName);
    }

    private static string SanitizeFileNamePart(string value) {
        if (value.NullOrEmpty()) return "unknown";

        var invalidChars = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);

        foreach (var ch in value) {
            builder.Append(invalidChars.Contains(ch) ? '_' : ch);
        }

        return builder.ToString();
    }

    private static string SanitizePackageIdPart(string value) {
        if (value.NullOrEmpty()) return "unknown";

        var builder = new StringBuilder(value.Length);
        foreach (var ch in value.ToLowerInvariant()) {
            if (char.IsLetterOrDigit(ch) || ch is '.' or '_') {
                builder.Append(ch);
                continue;
            }

            builder.Append('_');
        }

        var result = builder.ToString().Trim('_');
        return result.NullOrEmpty() ? "unknown" : result;
    }

}
