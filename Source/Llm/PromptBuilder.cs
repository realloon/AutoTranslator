using System.Text;
using System.Text.Json;
using Translator.Utilities;

namespace Translator.Llm;

public sealed record TranslationPrompt(string SystemPrompt, string UserPrompt);

/// <summary>
/// Builds the system/user prompts. The request item DTO is separate from
/// <see cref="TranslationItem"/> so the wire shape stays stable.
/// </summary>
public static class PromptBuilder {
    public const int MaxGlossaryItems = 200;

    private const string SystemPromptPrefix =
        "You are a professional RimWorld localization translator. " +
        "Translate each entry's original text into the target language. " +
        "Preserve placeholders like {0}, {1}, {name}, escaped newline markers (\\n), and punctuation. ";

    private const string SystemPromptGlossaryHint =
        "When termbase rules are provided, follow them exactly for the matching source terms. ";

    private const string SystemPromptSuffix =
        "Return a JSON object with shape {\"translations\":[{\"id\":\"...\",\"translation\":\"...\"}]}, " +
        "and provide each id exactly once.";

    public static TranslationPrompt Build(
        string targetLanguage,
        IReadOnlyList<TranslationItem> batch,
        IReadOnlyDictionary<string, string> glossary) {
        var hasGlossary = glossary.Count > 0;
        var systemPrompt = SystemPromptPrefix +
                           (hasGlossary ? SystemPromptGlossaryHint : string.Empty) +
                           SystemPromptSuffix;

        var userPrompt = new StringBuilder()
            .Append("Target language: ").Append(targetLanguage).Append('\n');

        if (hasGlossary) {
            var glossaryItems = BuildGlossaryItems(glossary);
            userPrompt
                .Append("Termbase rules (source -> target, mandatory):\n")
                .Append(JsonSerializer.Serialize(glossaryItems, TranslatorJson.Prompt))
                .Append('\n');
        }

        userPrompt
            .Append("Entries:\n")
            .Append(JsonSerializer.Serialize(BuildRequestItems(batch), TranslatorJson.Prompt));

        return new TranslationPrompt(systemPrompt, userPrompt.ToString());
    }

    private static List<PromptGlossaryItem> BuildGlossaryItems(IReadOnlyDictionary<string, string> glossary) =>
        glossary.Take(MaxGlossaryItems)
            .Select(pair => new PromptGlossaryItem(pair.Key, pair.Value))
            .ToList();

    private static PromptItem[] BuildRequestItems(IReadOnlyList<TranslationItem> batch) =>
        batch.Select(item => new PromptItem(item.Id, item.Tag, item.Original, item.DefType,
            item.IsCollectionItem)).ToArray();

    private sealed record PromptItem(string Id, string Tag, string Original, string? DefType, bool? IsCollectionItem);

    private sealed record PromptGlossaryItem(string Source, string Target);
}
