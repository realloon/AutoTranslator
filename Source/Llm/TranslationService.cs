using Translator.Export;
using Translator.Termbase;

namespace Translator.Llm;

/// <summary>
/// Orchestrates batches for one language workset. Batches run in waves of
/// <see cref="LlmSessionConfig.Concurrency"/>; translations are applied and persisted serially
/// between waves, matching the previous progress reporting semantics.
/// </summary>
internal static class TranslationService {
    public static async Task<TranslationOutcome> TranslateAsync(
        LanguageWorksetFile workset,
        string targetLanguageFolder,
        string targetLanguageDisplayName,
        Action? saveProgress = null,
        CancellationToken cancellationToken = default) {
        var pendingCount = 0;

        try {
            if (!LlmConfigResolver.TryCreate(TranslatorMod.Settings, out var config, out var error)) {
                return new(false, error, 0, 0);
            }

            var pending = workset.CollectPending();
            pendingCount = pending.Count;
            if (pendingCount == 0) {
                return new(true, "No pending entries.", 0, 0);
            }

            var pendingById = pending.ToDictionary(entry => entry.Id, StringComparer.Ordinal);
            var items = pending.Select(entry => entry.ToTranslationItem()).ToArray();

            var glossary = TermbaseService.GetGlossaryForLanguage(targetLanguageFolder);
            var batches = BatchPlanner.Plan(items, config.BatchSize);
            var failures = new List<string>();

            foreach (var wave in batches.Chunk(config.Concurrency)) {
                var tasks = new Task<BatchTranslationResult>[wave.Length];
                for (var i = 0; i < wave.Length; i++) {
                    tasks[i] = LlmClient.TranslateBatchAsync(
                        config, wave[i], targetLanguageDisplayName, glossary, cancellationToken);
                }

                var results = await Task.WhenAll(tasks).ConfigureAwait(false);
                foreach (var result in results) {
                    if (!result.Success) {
                        failures.Add($"batch#{result.BatchNumber}: {result.ErrorMessage}");
                        continue;
                    }

                    foreach (var (id, translation) in result.Translations) {
                        pendingById[id].Apply(translation);
                    }

                    saveProgress?.Invoke();
                }
            }

            return failures.Count > 0
                ? new(false, BuildBatchFailureMessage(failures), 0, pendingCount)
                : new(true, "OK", pendingCount, pendingCount);
        } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            return new(false, "Translation cancelled.", 0, pendingCount);
        } catch (Exception ex) {
            return new(false, ex.Message, 0, pendingCount);
        }
    }

    private static string BuildBatchFailureMessage(IReadOnlyList<string> failedBatches) {
        const int maxErrors = 3;
        var shown = failedBatches
            .Take(maxErrors)
            .Select(error => error.Length > 180 ? $"{error[..180]}..." : error);
        var message = string.Join(" | ", shown);
        if (failedBatches.Count > maxErrors) {
            message += $" (+{failedBatches.Count - maxErrors} more)";
        }

        return $"Some translation batches failed after retries: {message}";
    }
}
