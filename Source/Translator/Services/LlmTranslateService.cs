using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using JetBrains.Annotations;

namespace Translator.Services;

internal sealed class LlmTranslateResult {
    public bool Success;
    public string Message = string.Empty;
    public int UpdatedCount;
}

internal sealed class LlmConfigValidationResult {
    public bool Success;
    public string Message = string.Empty;
}

internal static class LlmTranslateService {
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

    public static LlmTranslateResult TranslateWorkset(LanguageWorksetFile workset, string targetLanguage) {
        try {
            if (!TryGetActiveConfig(out var apiUrl, out var apiKey, out var model, out var batchSize,
                    out var concurrency, out var retryCount,
                    out var configError)) {
                return new LlmTranslateResult {
                    Success = false,
                    Message = configError,
                    UpdatedCount = 0
                };
            }

            var pending = CollectPendingEntries(workset);
            if (pending.Count == 0) {
                return new LlmTranslateResult {
                    Success = true,
                    Message = "No pending entries.",
                    UpdatedCount = 0
                };
            }

            var translatedById = new Dictionary<string, string>(StringComparer.Ordinal);
            var batches = new List<BatchRequest>();
            for (var i = 0; i < pending.Count; i += batchSize) {
                batches.Add(new BatchRequest {
                    BatchNo = batches.Count + 1,
                    Items = pending.Skip(i).Take(batchSize).ToList()
                });
            }

            var failedBatches = new List<string>();
            for (var i = 0; i < batches.Count; i += concurrency) {
                var wave = batches.Skip(i).Take(concurrency).ToList();
                var waveTasks = wave
                    .Select(batch => Task.Run(() =>
                        RequestBatchTranslationsWithRetry(apiUrl, apiKey, model, targetLanguage, batch, retryCount)))
                    .ToArray();
                Task.WaitAll(waveTasks);
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
                    UpdatedCount = 0
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
                UpdatedCount = updatedCount
            };
        } catch (Exception ex) {
            return new LlmTranslateResult {
                Success = false,
                Message = ex.Message,
                UpdatedCount = 0
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
        int retryCount) {
        Exception? lastException = null;
        for (var attempt = 0; attempt <= retryCount; attempt++) {
            try {
                var translations = RequestBatchTranslations(apiUrl, apiKey, model, targetLanguage, batch.Items);
                return new BatchExecutionResult {
                    BatchNo = batch.BatchNo,
                    Success = true,
                    ErrorMessage = string.Empty,
                    Translations = translations
                };
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
        IReadOnlyList<PendingTranslationItem> batch) {
        var payloadItems = batch.Select(item => new {
            id = item.Id,
            tag = item.Tag,
            original = item.Original,
            defType = item.DefType,
            isCollectionItem = item.IsCollectionItem
        }).ToList();

        var systemPrompt =
            "You are a professional RimWorld localization translator. " +
            "Translate each entry's original text into the target language. " +
            "Preserve placeholders like {0}, {1}, {name}, escaped newline markers (\\n), and punctuation. " +
            "Output JSON only with shape: {\"translations\":[{\"id\":\"...\",\"translation\":\"...\"}]} and include every id exactly once.";

        var userPrompt =
            $"Target language: {targetLanguage}\n" +
            "Entries:\n" +
            JsonSerializer.Serialize(payloadItems, JsonOptions);

        var requestPayload = new {
            model,
            temperature = 0.2,
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
        var translatedPayload = JsonSerializer.Deserialize<BatchTranslationResponse>(normalizedJson, JsonOptions);
        if (translatedPayload?.Translations is null) {
            throw new InvalidOperationException("Invalid LLM translation payload.");
        }

        return translatedPayload.Translations
            .Where(item => !string.IsNullOrEmpty(item.Id) && item.Translation is not null)
            .ToDictionary(item => item.Id, item => item.Translation!, StringComparer.Ordinal);
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
            Timeout = TimeSpan.FromSeconds(90)
        };
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
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
