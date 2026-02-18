namespace Translator.Services;

internal sealed class LanguageWorksetFile {
    public string LanguageFolderName { get; set; } = string.Empty;
    public List<LanguageWorksetDefInjectedItem> DefInjected { get; set; } = [];
    public List<LanguageWorksetKeyedItem> Keyed { get; set; } = [];
}

internal sealed class LanguageWorksetDefInjectedItem {
    public string Tag { get; set; } = string.Empty;
    public string Original { get; set; } = string.Empty;
    public string Translation { get; set; } = string.Empty;
    public string DefType { get; set; } = string.Empty;
    public bool IsCollectionItem { get; set; }
}

internal sealed class LanguageWorksetKeyedItem {
    public string Tag { get; set; } = string.Empty;
    public string Original { get; set; } = string.Empty;
    public string Translation { get; set; } = string.Empty;
}
