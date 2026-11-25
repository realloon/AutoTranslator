using Translator.Llm;

namespace Translator.Export;

/// <summary>One language's in-memory translation unit produced by the exporter.</summary>
public sealed class LanguageWorksetFile {
    public required string LanguageFolderName { get; init; }
    public List<LanguageWorksetDefInjectedItem> DefInjected { get; init; } = [];
    public List<LanguageWorksetKeyedItem> Keyed { get; init; } = [];
}

public abstract class WorksetTextItem {
    public required string Tag { get; init; }
    public required string Original { get; init; }
    public string Translation { get; set; } = string.Empty;
}

public sealed class LanguageWorksetKeyedItem : WorksetTextItem { }

public sealed class LanguageWorksetDefInjectedItem : WorksetTextItem {
    public required string DefType { get; init; }
    public bool IsCollectionItem { get; init; }
}

/// <summary>A not-yet-translated entry plus the mutation that stores its translation.</summary>
public readonly record struct PendingTranslation(
    string Id,
    string Tag,
    string Original,
    string? DefType,
    bool? IsCollectionItem,
    Action<string> Apply) {
    public TranslationItem ToTranslationItem() => new(Id, Tag, Original, DefType, IsCollectionItem);
}

public static class LanguageWorksetExtensions {
    /// <summary>IDs are stable within one workset: "K:{index}" for keyed, "D:{index}" for def-injected.</summary>
    public static List<PendingTranslation> CollectPending(this LanguageWorksetFile workset) {
        var pending = new List<PendingTranslation>(workset.Keyed.Count + workset.DefInjected.Count);

        for (var i = 0; i < workset.Keyed.Count; i++) {
            var item = workset.Keyed[i];
            if (!string.IsNullOrEmpty(item.Translation)) continue;

            var index = i;
            pending.Add(new PendingTranslation(
                $"K:{index}", item.Tag, item.Original, DefType: null, IsCollectionItem: null,
                translation => workset.Keyed[index].Translation = translation));
        }

        for (var i = 0; i < workset.DefInjected.Count; i++) {
            var item = workset.DefInjected[i];
            if (!string.IsNullOrEmpty(item.Translation)) continue;

            var index = i;
            pending.Add(new PendingTranslation(
                $"D:{index}", item.Tag, item.Original, item.DefType, item.IsCollectionItem,
                translation => workset.DefInjected[index].Translation = translation));
        }

        return pending;
    }
}