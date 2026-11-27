using Translator.Export;
using Translator.Termbase;

namespace Translator.Llm;

/// <summary>
/// Runs batches in waves of <see cref="LlmSessionConfig.Concurrency"/>; translations are applied
/// and persisted serially between waves.
/// </summary>
internal static class TranslationService {
    public static async Task<TranslationOutcome> TranslateAsync(
        LanguageWorksetFile workset,
        string targetLanguageFolder,
        string targetLanguageDisplayName,
        Action? saveProgress = null,
        CancellationToken cancellationToken = default) {
        try {
            if (!LlmConfigResolver.TryCreate(TranslatorMod.Settings, out var config, out var error)) {
                return new TranslationOutcome(false, error, []);
            }

            var pending = workset.CollectPending();
            if (pending.Count == 0) {
                return new TranslationOutcome(true, "No pending entries.", []);
            }

            var pendingById = pending.ToDictionary(entry => entry.Id, StringComparer.Ordinal);
            var items = pending.Select(entry => entry.ToTranslationItem()).ToArray();

            var glossary = TermbaseService.GetGlossaryForLanguage(targetLanguageFolder);
            var batches = BatchPlanner.Plan(items, config.BatchSize);
            var failures = new List<BatchFailure>();

            foreach (var wave in batches.Chunk(config.Concurrency)) {
                var tasks = new Task<BatchTranslationResult>[wave.Length];
                for (var i = 0; i < wave.Length; i++) {
                    tasks[i] = LlmClient.TranslateBatchAsync(
                        config, wave[i], targetLanguageDisplayName, glossary, cancellationToken);
                }

                var results = await Task.WhenAll(tasks).ConfigureAwait(false);
                foreach (var result in results) {
                    if (!result.Success) {
                        failures.Add(new BatchFailure(result.BatchNumber, result.Error!));
                        continue;
                    }

                    foreach (var (id, translation) in result.Translations) {
                        pendingById[id].Apply(translation);
                    }

                    saveProgress?.Invoke();
                }
            }

            return failures.Count > 0
                ? new TranslationOutcome(false, string.Empty, failures)
                : new TranslationOutcome(true, "OK", []);
        } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            return new TranslationOutcome(false, "Translation cancelled.", []);
        } catch (Exception ex) {
            return new TranslationOutcome(false, ex.Message, []);
        }
    }
}
