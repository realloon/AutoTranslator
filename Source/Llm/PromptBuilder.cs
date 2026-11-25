using System.Text;
using System.Text.Json;
using Translator.Utilities;

namespace Translator.Llm;

public sealed record TranslationPrompt(string SystemPrompt, string UserPrompt);

/// <summary>Request and glossary entries are anonymous payloads: they are serialized, not read.</summary>
public static class PromptBuilder {
    private const int MaxGlossaryItems = 200;

    private const string SystemPromptPrefix =
        "You are a professional RimWorld localization translator. " +
        "Translate each entry's original text into the target language. " +
        "Preserve placeholders like {0}, {1}, {name}, escaped newline markers (\\n), and punctuation. ";

    private const string SystemPromptGlossaryHint =
        "When termbase rules are provided, follow them exactly for the matching source terms. ";

    private const string SystemPromptSuffix =
        "Return a JSON object with shape {\"translations\":[{\"id\":\"...\",\"translation\":\"...\"}]}, " +
        "and provide each id exactly once.";

    public static TranslationPrompt Build(string targetLanguage, IReadOnlyList<TranslationItem> batch,
        IReadOnlyDictionary<string, string> glossary) {
        var hasGlossary = glossary.Count > 0;
        var systemPrompt = SystemPromptPrefix + (hasGlossary ? SystemPromptGlossaryHint : string.Empty) +
                           SystemPromptSuffix;

        var userPrompt = new StringBuilder().Append("Target language: ").Append(targetLanguage).Append('\n');

        if (hasGlossary) {
            userPrompt
                .Append("Termbase rules (source -> target, mandatory):\n")
                .Append(JsonSerializer.Serialize(
                    glossary.Take(MaxGlossaryItems).Select(pair => new { source = pair.Key, target = pair.Value }),
                    TranslatorJson.Prompt))
                .Append('\n');
        }

        userPrompt
            .Append("Entries:\n")
            .Append(JsonSerializer.Serialize(
                batch.Select(item => new {
                    id = item.Id,
                    tag = item.Tag,
                    original = item.Original,
                    defType = item.DefType,
                    isCollectionItem = item.IsCollectionItem
                }),
                TranslatorJson.Prompt));

        return new TranslationPrompt(systemPrompt, userPrompt.ToString());
    }
}