namespace Translator.Llm;

public sealed record TranslationItem(
    string Id,
    string Tag,
    string Original,
    string? DefType,
    bool? IsCollectionItem);

public sealed record TranslationBatch(int Number, IReadOnlyList<TranslationItem> Items);

public static class BatchPlanner {
    private const int DefaultMaxEstimatedCharsPerBatch = 18_000;
    private const int MinEstimatedCharsPerItem = 64;
    private const int MinMaxTokens = 3_000;
    private const int MaxMaxTokens = 16_000;

    public static List<TranslationBatch> Plan(IReadOnlyList<TranslationItem> items, int maxItemsPerBatch,
        int maxEstimatedCharsPerBatch = DefaultMaxEstimatedCharsPerBatch) {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxItemsPerBatch, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxEstimatedCharsPerBatch, 1);

        var batches = new List<TranslationBatch>();
        var current = new List<TranslationItem>();
        var currentChars = 0;

        foreach (var item in items) {
            var estimatedChars = EstimateChars(item);
            var exceedsCount = current.Count >= maxItemsPerBatch;
            var exceedsChars = current.Count > 0 && currentChars + estimatedChars > maxEstimatedCharsPerBatch;

            if (exceedsCount || exceedsChars) {
                batches.Add(new TranslationBatch(batches.Count + 1, current.ToArray()));
                current.Clear();
                currentChars = 0;
            }

            current.Add(item);
            currentChars += estimatedChars;
        }

        if (current.Count > 0) {
            batches.Add(new TranslationBatch(batches.Count + 1, current.ToArray()));
        }

        return batches;
    }

    public static int EstimateMaxTokens(IReadOnlyList<TranslationItem> batch) =>
        Math.Clamp(batch.Sum(EstimateChars) / 3 + 2_000, MinMaxTokens, MaxMaxTokens);

    private static int EstimateChars(TranslationItem item) => Math.Max(
        MinEstimatedCharsPerItem,
        item.Tag.Length + item.Original.Length + (item.DefType?.Length ?? 0) + 32);
}