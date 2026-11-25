using System.Text.Json;
using Translator.Utilities;

namespace Translator.Llm;

/// <summary>
/// Validates a model translation payload against the batch that was sent.
/// Kept separate from the HTTP layer so the strictness rules can be unit-tested.
/// </summary>
public static class TranslationResponseParser {
    public static bool TryParse(
        string content,
        IReadOnlyList<TranslationItem> batch,
        out Dictionary<string, string> translations,
        out string error) {
        translations = new Dictionary<string, string>(StringComparer.Ordinal);
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(content)) {
            error = "LLM response content is empty.";
            return false;
        }

        var payload = DeserializePayload(content, out error);
        if (payload is null) {
            error = error.Length > 0 ? error : "Invalid LLM translation payload.";
            return false;
        }

        var expectedIds = new HashSet<string>(batch.Count, StringComparer.Ordinal);
        foreach (var item in batch) {
            expectedIds.Add(item.Id);
        }

        var matched = new Dictionary<string, string>(StringComparer.Ordinal);
        var duplicateCount = 0;
        var unknownIdCount = 0;

        foreach (var entry in payload.Translations) {
            if (string.IsNullOrEmpty(entry.Id)) {
                continue;
            }

            if (!expectedIds.Contains(entry.Id)) {
                unknownIdCount++;
                continue;
            }

            var value = entry.Translation;
            if (!matched.TryAdd(entry.Id, value)) {
                duplicateCount++;
                matched[entry.Id] = value;
            }
        }

        var emptyTranslationCount = matched.Count(pair => string.IsNullOrWhiteSpace(pair.Value));
        var missingCount = expectedIds.Count(id => !matched.ContainsKey(id));
        if (missingCount == 0 && emptyTranslationCount == 0 && unknownIdCount == 0 && duplicateCount == 0) {
            translations = matched;
            return true;
        }

        var missingSample = expectedIds
            .Where(id => !matched.ContainsKey(id))
            .Take(3)
            .ToList();
        var sampleText = missingSample.Count == 0 ? "none" : string.Join(", ", missingSample);

        error = "LLM batch response is incomplete. " +
                $"expected={expectedIds.Count}, matched={matched.Count}, missing={missingCount}, " +
                $"empty={emptyTranslationCount}, unknown={unknownIdCount}, duplicate={duplicateCount}, " +
                $"missingSample=[{sampleText}]";
        return false;
    }

    private static TranslationBatchResponse? DeserializePayload(string content, out string error) {
        error = string.Empty;

        try {
            var direct = JsonSerializer.Deserialize<TranslationBatchResponse>(content, TranslatorJson.Api);
            if (direct is not null) {
                return direct;
            }
        } catch (JsonException) {
            // Fall through to the tolerant extraction path below.
        }

        if (!TryExtractObject(content, out var json)) {
            error = "LLM response does not contain JSON object.";
            return null;
        }

        try {
            return JsonSerializer.Deserialize<TranslationBatchResponse>(json, TranslatorJson.Api);
        } catch (JsonException ex) {
            error = $"Invalid LLM translation payload: {ex.Message}";
            return null;
        }
    }

    private static bool TryExtractObject(string content, out string json) {
        var first = content.IndexOf('{');
        var last = content.LastIndexOf('}');
        if (first < 0 || last <= first) {
            json = string.Empty;
            return false;
        }

        json = content[first..(last + 1)];
        return true;
    }
}
