using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using JetBrains.Annotations;
using Verse;

namespace Translator.Services;

internal sealed class LlmTranslateResult {
    public bool Success;
    public string Message = string.Empty;
    public int UpdatedCount;
    public int PendingCount;
}

internal sealed class LlmConfigValidationResult {
    public bool Success;
    public string Message = string.Empty;
}

internal static class LlmTranslateService {
    private const int MaxEstimatedCharsPerBatch = 18000;
    private const int RequestTimeoutSeconds = 600;

    private static readonly JsonSerializerOptions JsonOptions = new() {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly HttpClient HttpClient = CreateHttpClient();

    public static LlmConfigValidationResult ValidateCurrentConfig(bool testConnection) {
        try {
            if (!TryGetActiveConfig(out var apiUrl, out var apiKey, out var model, out _, out _, out _,
                    out var configError)) {
                return new LlmConfigValidationResult {
                    Success = false,
                    Message = configError
                };
            }

            if (testConnection) {
                ProbeConnection(apiUrl, apiKey, model);
            }

            return new LlmConfigValidationResult {
                Success = true,
                Message = "OK"
            };
        } catch (Exception ex) {
            return new LlmConfigValidationResult {
                Success = false,
                Message = ex.Message
            };
        }
    }

    public static LlmTranslateResult TranslateWorkset(LanguageWorksetFile workset,
        string targetLanguageFolder,
        string targetLanguageDisplayName) {
        var pendingCount = 0;
        try {
            if (!TryGetActiveConfig(out var apiUrl, out var apiKey, out var model, out var batchSize,
                    out var concurrency, out var retryCount,
                    out var configError)) {
                return new LlmTranslateResult {
                    Success = false,
                    Message = configError,
                    UpdatedCount = 0,
                    PendingCount = 0
                };
            }

            var pending = CollectPendingEntries(workset);
            pendingCount = pending.Count;
            if (pending.Count == 0) {
                return new LlmTranslateResult {
                    Success = true,
                    Message = "No pending entries.",
                    UpdatedCount = 0,
                    PendingCount = 0
                };
            }

            var translatedById = new Dictionary<string, string>(StringComparer.Ordinal);
            var termbaseGlossary = TermbaseService.GetGlossaryForLanguage(targetLanguageFolder);
            var batches = BuildBatches(pending, batchSize, MaxEstimatedCharsPerBatch);

            var failedBatches = new List<string>();
            for (var i = 0; i < batches.Count; i += concurrency) {
                var wave = batches.Skip(i).Take(concurrency).ToList();
                var waveTasks = wave
                    .Select(batch => Task.Run(() =>
                        RequestBatchTranslationsWithRetry(apiUrl,
                            apiKey,
                            model,
                            targetLanguageDisplayName,
                            batch,
                            retryCount,
                            termbaseGlossary)))
                    .ToArray();
                Task.WhenAll(waveTasks).GetAwaiter().GetResult();
                foreach (var task in waveTasks) {
                    var batchResult = task.Result;
                    if (!batchResult.Success) {
                        failedBatches.Add($"batch#{batchResult.BatchNo}: {batchResult.ErrorMessage}");
                        continue;
                    }

                    foreach (var (id, translation) in batchResult.Translations) {
                        translatedById[id] = translation;
                    }
                }
            }

            if (failedBatches.Count > 0) {
                return new LlmTranslateResult {
                    Success = false,
                    Message = BuildBatchFailureMessage(failedBatches),
                    UpdatedCount = 0,
                    PendingCount = pending.Count
                };
            }

            var updatedCount = 0;
            foreach (var item in pending) {
                if (!translatedById.TryGetValue(item.Id, out var translated) || string.IsNullOrEmpty(translated)) {
                    continue;
                }

                item.ApplyTranslation(translated);
                updatedCount += 1;
            }

            return new LlmTranslateResult {
                Success = true,
                Message = "OK",
                UpdatedCount = updatedCount,
                PendingCount = pending.Count
            };
        } catch (Exception ex) {
            return new LlmTranslateResult {
                Success = false,
                Message = ex.Message,
                UpdatedCount = 0,
                PendingCount = pendingCount
            };
        }
    }

    private static List<PendingTranslationItem> CollectPendingEntries(LanguageWorksetFile workset) {
        var result = new List<PendingTranslationItem>();

        for (var i = 0; i < workset.Keyed.Count; i++) {
            var item = workset.Keyed[i];
            if (!string.IsNullOrEmpty(item.Translation)) {
                continue;
            }

            var index = i;
            result.Add(new PendingTranslationItem {
                Id = $"K:{index}",
                Tag = item.Tag,
                Original = item.Original,
                DefType = null,
                IsCollectionItem = null,
                ApplyTranslation = translation => workset.Keyed[index].Translation = translation
            });
        }

        for (var i = 0; i < workset.DefInjected.Count; i++) {
            var item = workset.DefInjected[i];
            if (!string.IsNullOrEmpty(item.Translation)) {
                continue;
            }

            var index = i;
            result.Add(new PendingTranslationItem {
                Id = $"D:{index}",
                Tag = item.Tag,
                Original = item.Original,
                DefType = item.DefType,
                IsCollectionItem = item.IsCollectionItem,
                ApplyTranslation = translation => workset.DefInjected[index].Translation = translation
            });
        }

        return result;
    }

    private static bool TryGetActiveConfig(out string apiUrl,
        out string apiKey,
        out string model,
        out int batchSize,
        out int concurrency,
        out int retryCount,
        out string errorMessage) {
        var settings = TranslatorMod.Settings;
        apiUrl = settings.ApiUrl.Trim();
        apiKey = settings.ApiKey.Trim();
        model = settings.Model.Trim();
        batchSize = settings.BatchSize;
        concurrency = settings.Concurrency;
        retryCount = settings.RetryCount;

        if (string.IsNullOrWhiteSpace(apiUrl)) {
            errorMessage = "Translator API URL is empty. Configure it in Mod Settings.";
            return false;
        }

        if (!Uri.TryCreate(apiUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)) {
            errorMessage = "Translator API URL is invalid. Configure a valid http/https endpoint in Mod Settings.";
            return false;
        }

        if (!TryNormalizeApiUrl(uri, out apiUrl, out errorMessage)) {
            return false;
        }

        if (string.IsNullOrWhiteSpace(apiKey)) {
            errorMessage = "Translator API key is empty. Configure it in Mod Settings.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(model)) {
            errorMessage = "Translator model is empty. Configure it in Mod Settings.";
            return false;
        }

        if (batchSize < TranslatorSettings.MinBatchSize || batchSize > TranslatorSettings.MaxBatchSize) {
            errorMessage =
                $"Translator batch size is invalid. Configure a value between {TranslatorSettings.MinBatchSize} and {TranslatorSettings.MaxBatchSize} in Mod Settings.";
            return false;
        }

        if (concurrency < TranslatorSettings.MinConcurrency || concurrency > TranslatorSettings.MaxConcurrency) {
            errorMessage =
                $"Translator concurrency is invalid. Configure a value between {TranslatorSettings.MinConcurrency} and {TranslatorSettings.MaxConcurrency} in Mod Settings.";
            return false;
        }

        if (retryCount < TranslatorSettings.MinRetryCount || retryCount > TranslatorSettings.MaxRetryCount) {
            errorMessage =
                $"Translator retry count is invalid. Configure a value between {TranslatorSettings.MinRetryCount} and {TranslatorSettings.MaxRetryCount} in Mod Settings.";
            return false;
        }

        errorMessage = string.Empty;
        return true;
    }

    private static BatchExecutionResult RequestBatchTranslationsWithRetry(string apiUrl,
        string apiKey,
        string model,
        string targetLanguage,
        BatchRequest batch,
        int retryCount,
        IReadOnlyDictionary<string, string> termbaseGlossary) {
        Exception? lastException = null;
        for (var attempt = 0; attempt <= retryCount; attempt++) {
            try {
                var translations =
                    RequestBatchTranslations(apiUrl, apiKey, model, targetLanguage, batch.Items, termbaseGlossary);
                return new BatchExecutionResult {
                    BatchNo = batch.BatchNo,
                    Success = true,
                    ErrorMessage = string.Empty,
                    Translations = translations
                };
            } catch (TaskCanceledException ex) {
                lastException = new TimeoutException(
                    $"Request timed out or was canceled (timeout={RequestTimeoutSeconds}s, batchNo={batch.BatchNo}, entries={batch.Items.Count}, attempt={attempt + 1}/{retryCount + 1}). Consider lowering Batch Size/Concurrency in Mod Settings.",
                    ex);
            } catch (Exception ex) {
                lastException = ex;
            }
        }

        return new BatchExecutionResult {
            BatchNo = batch.BatchNo,
            Success = false,
            ErrorMessage = lastException?.Message ?? "Unknown batch translation error.",
            Translations = new Dictionary<string, string>(StringComparer.Ordinal)
        };
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

    private static bool TryNormalizeApiUrl(Uri uri, out string normalizedApiUrl, out string errorMessage) {
        var builder = new UriBuilder(uri) {
            Query = string.Empty,
            Fragment = string.Empty
        };
        var path = builder.Path.TrimEnd('/');

        if (string.IsNullOrEmpty(path)) {
            builder.Path = "/v1/chat/completions";
        } else if (path.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)) {
            builder.Path = path;
        } else if (path.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)) {
            builder.Path = $"{path}/chat/completions";
        } else {
            builder.Path = $"{path}/v1/chat/completions";
        }

        normalizedApiUrl = builder.Uri.AbsoluteUri;
        errorMessage = string.Empty;
        return true;
    }

    private static void ProbeConnection(string apiUrl, string apiKey, string model) {
        var requestPayload = new {
            model,
            temperature = 0.0,
            max_tokens = 1,
            messages = new object[] {
                new {
                    role = "system",
                    content = "Health check."
                },
                new {
                    role = "user",
                    content = "Reply with OK."
                }
            }
        };

        var requestJson = JsonSerializer.Serialize(requestPayload);
        using var request = new HttpRequestMessage(HttpMethod.Post, apiUrl);
        request.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var response = HttpClient.SendAsync(request).GetAwaiter().GetResult();
        var responseJson = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        if (!response.IsSuccessStatusCode) {
            throw new InvalidOperationException(
                $"Config validation request failed ({(int)response.StatusCode}): {response.ReasonPhrase}; {responseJson}");
        }
    }

    private static Dictionary<string, string> RequestBatchTranslations(string apiUrl,
        string apiKey,
        string model,
        string targetLanguage,
        IReadOnlyList<PendingTranslationItem> batch,
        IReadOnlyDictionary<string, string> termbaseGlossary) {
        var payloadItems = batch.Select(item => new {
            id = item.Id,
            tag = item.Tag,
            original = item.Original,
            defType = item.DefType,
            isCollectionItem = item.IsCollectionItem
        }).ToList();

        const int maxGlossaryItems = 200;
        var glossaryItems = termbaseGlossary
            .Take(maxGlossaryItems)
            .Select(pair => new {
                source = pair.Key,
                target = pair.Value
            })
            .ToList();
        var hasGlossary = glossaryItems.Count > 0;

        var systemPrompt =
            "You are a professional RimWorld localization translator. " +
            "Translate each entry's original text into the target language. " +
            "Preserve placeholders like {0}, {1}, {name}, escaped newline markers (\\n), and punctuation. " +
            (hasGlossary
                ? "When termbase rules are provided, follow them exactly for the matching source terms. "
                : string.Empty) +
            "Return a JSON object with shape {\"translations\":[{\"id\":\"...\",\"translation\":\"...\"}]}, and provide each id exactly once.";

        var userPrompt =
            $"Target language: {targetLanguage}\n" +
            (hasGlossary
                ? $"Termbase rules (source -> target, mandatory):\n{JsonSerializer.Serialize(glossaryItems, JsonOptions)}\n"
                : string.Empty
            ) +
            "Entries:\n" +
            JsonSerializer.Serialize(payloadItems, JsonOptions);

        var requestPayload = new {
            model,
            temperature = 0.0,
            max_tokens = EstimateMaxTokensForBatch(batch),
            response_format = new {
                type = "json_object"
            },
            messages = new object[] {
                new {
                    role = "system",
                    content = systemPrompt
                },
                new {
                    role = "user",
                    content = userPrompt
                }
            }
        };

        var requestJson = JsonSerializer.Serialize(requestPayload);
        using var request = new HttpRequestMessage(HttpMethod.Post, apiUrl);
        request.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var response = HttpClient.SendAsync(request).GetAwaiter().GetResult();
        var responseJson = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        if (!response.IsSuccessStatusCode) {
            throw new InvalidOperationException(
                $"LLM request failed ({(int)response.StatusCode}): {response.ReasonPhrase}; {responseJson}");
        }

        var completion = JsonSerializer.Deserialize<ChatCompletionResponse>(responseJson, JsonOptions);
        var content = completion?.Choices.FirstOrDefault()?.Message.Content ?? string.Empty;
        if (string.IsNullOrEmpty(content)) {
            throw new InvalidOperationException("LLM response content is empty.");
        }

        var normalizedJson = ExtractJsonObject(content);
        if (!TryDeserializeBatchResponse(normalizedJson, out var translatedPayload, out var deserializeError)) {
            var repairedJson = TryRepairBatchJsonWithLlm(apiUrl, apiKey, model, normalizedJson);
            var repaired = !repairedJson.NullOrEmpty() &&
                           TryDeserializeBatchResponse(repairedJson, out translatedPayload, out _);
            if (!repaired) {
                throw new InvalidOperationException(
                    $"Invalid LLM translation payload: {deserializeError}");
            }
        }

        if (translatedPayload?.Translations is null) {
            throw new InvalidOperationException("Invalid LLM translation payload: missing translations array.");
        }

        var expectedIds = batch
            .Select(item => item.Id)
            .ToHashSet(StringComparer.Ordinal);
        var matchedTranslations = new Dictionary<string, string>(StringComparer.Ordinal);
        var duplicateIdCount = 0;
        var unknownIdCount = 0;

        foreach (var item in translatedPayload.Translations) {
            if (string.IsNullOrEmpty(item.Id)) {
                continue;
            }

            if (!expectedIds.Contains(item.Id)) {
                unknownIdCount += 1;
                continue;
            }

            if (!matchedTranslations.TryAdd(item.Id, item.Translation ?? string.Empty)) {
                duplicateIdCount += 1;
                matchedTranslations[item.Id] = item.Translation ?? string.Empty;
            }
        }

        var emptyTranslationCount = matchedTranslations.Count(pair => string.IsNullOrWhiteSpace(pair.Value));
        var missingCount = expectedIds.Count(id => !matchedTranslations.ContainsKey(id));
        if (missingCount > 0 || emptyTranslationCount > 0 || unknownIdCount > 0 || duplicateIdCount > 0) {
            var missingSample = expectedIds
                .Where(id => !matchedTranslations.ContainsKey(id))
                .Take(3)
                .ToList();
            var sampleText = missingSample.Count == 0 ? "none" : string.Join(", ", missingSample);
            throw new InvalidOperationException(
                $"LLM batch response is incomplete. expected={expectedIds.Count}, matched={matchedTranslations.Count}, missing={missingCount}, empty={emptyTranslationCount}, unknown={unknownIdCount}, duplicate={duplicateIdCount}, missingSample=[{sampleText}]");
        }

        return matchedTranslations;
    }

    private static string ExtractJsonObject(string content) {
        var first = content.IndexOf('{');
        var last = content.LastIndexOf('}');
        if (first < 0 || last < first) {
            throw new InvalidOperationException("LLM response does not contain JSON object.");
        }

        return content[first..(last + 1)];
    }

    private static HttpClient CreateHttpClient() {
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
        var client = new HttpClient {
            Timeout = TimeSpan.FromSeconds(RequestTimeoutSeconds)
        };
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    private static List<BatchRequest> BuildBatches(IReadOnlyList<PendingTranslationItem> pending,
        int maxEntriesPerBatch, int maxEstimatedCharsPerBatch) {
        var batches = new List<BatchRequest>();
        var currentItems = new List<PendingTranslationItem>();
        var currentChars = 0;

        foreach (var item in pending) {
            var estimatedChars = EstimateChars(item);
            var exceedCount = currentItems.Count >= maxEntriesPerBatch;
            var exceedChars = currentItems.Count > 0 && currentChars + estimatedChars > maxEstimatedCharsPerBatch;
            if (exceedCount || exceedChars) {
                batches.Add(new BatchRequest {
                    BatchNo = batches.Count + 1,
                    Items = currentItems.ToList()
                });
                currentItems.Clear();
                currentChars = 0;
            }

            currentItems.Add(item);
            currentChars += estimatedChars;
        }

        if (currentItems.Count > 0) {
            batches.Add(new BatchRequest {
                BatchNo = batches.Count + 1,
                Items = currentItems
            });
        }

        return batches;
    }

    private static int EstimateChars(PendingTranslationItem item) {
        var tagLength = string.IsNullOrEmpty(item.Tag) ? 0 : item.Tag.Length;
        var originalLength = string.IsNullOrEmpty(item.Original) ? 0 : item.Original.Length;
        var defTypeLength = item.DefType?.Length ?? 0;
        return Math.Max(64, tagLength + originalLength + defTypeLength + 32);
    }

    private static int EstimateMaxTokensForBatch(IReadOnlyList<PendingTranslationItem> batch) {
        var estimatedChars = batch.Sum(EstimateChars);
        var estimatedTokens = estimatedChars / 3;
        return Math.Clamp(estimatedTokens + 2000, 3000, 16000);
    }

    private static bool TryDeserializeBatchResponse(string json, out BatchTranslationResponse? response,
        out string error) {
        try {
            response = JsonSerializer.Deserialize<BatchTranslationResponse>(json, JsonOptions);
            if (response?.Translations is null) {
                error = "translations array is missing.";
                return false;
            }

            error = string.Empty;
            return true;
        } catch (Exception ex) {
            response = null;
            error = ex.Message;
            return false;
        }
    }

    private static string TryRepairBatchJsonWithLlm(string apiUrl, string apiKey, string model, string brokenJson) {
        try {
            var requestPayload = new {
                model,
                temperature = 0.0,
                max_tokens = 4000,
                response_format = new {
                    type = "json_object"
                },
                messages = new object[] {
                    new {
                        role = "system",
                        content =
                            "You are a JSON repair tool. Return a valid JSON object and keep all ids and translations unchanged."
                    },
                    new {
                        role = "user",
                        content =
                            "Please produce a JSON object with shape {\"translations\":[{\"id\":\"...\",\"translation\":\"...\"}]}. Include all original translation items and keep values unchanged.\n" +
                            brokenJson
                    }
                }
            };

            var requestJson = JsonSerializer.Serialize(requestPayload);
            using var request = new HttpRequestMessage(HttpMethod.Post, apiUrl);
            request.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            using var response = HttpClient.SendAsync(request).GetAwaiter().GetResult();
            var responseJson = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode) {
                return string.Empty;
            }

            var completion = JsonSerializer.Deserialize<ChatCompletionResponse>(responseJson, JsonOptions);
            var content = completion?.Choices.FirstOrDefault()?.Message.Content ?? string.Empty;
            if (content.NullOrEmpty()) {
                return string.Empty;
            }

            return ExtractJsonObject(content);
        } catch {
            return string.Empty;
        }
    }

    private sealed class PendingTranslationItem {
        public string Id = string.Empty;
        public string Tag = string.Empty;
        public string Original = string.Empty;
        public string? DefType;
        public bool? IsCollectionItem;
        public Action<string> ApplyTranslation = _ => { };
    }

    private sealed class BatchRequest {
        public int BatchNo;
        public List<PendingTranslationItem> Items { get; set; } = [];
    }

    private sealed class BatchExecutionResult {
        public int BatchNo;
        public bool Success;
        public string ErrorMessage = string.Empty;
        public Dictionary<string, string> Translations { get; set; } = new(StringComparer.Ordinal);
    }

    private sealed class ChatCompletionResponse {
        [JsonPropertyName("choices")]
        public List<ChatChoice> Choices { get; set; } = [];
    }

    private sealed class ChatChoice {
        [UsedImplicitly]
        [JsonPropertyName("message")]
        public ChatMessage Message { get; set; } = new();
    }

    private sealed class ChatMessage {
        [UsedImplicitly]
        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;
    }

    private sealed class BatchTranslationResponse {
        [JsonPropertyName("translations")]
        public List<TranslatedEntry> Translations { get; set; } = [];
    }

    private sealed class TranslatedEntry {
        [UsedImplicitly]
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [UsedImplicitly]
        [JsonPropertyName("translation")]
        public string? Translation { get; set; }
    }
}
