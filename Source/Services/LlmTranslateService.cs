using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using Newtonsoft.Json;

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

internal sealed class LlmModelListResult {
    public bool Success;
    public string Message = string.Empty;
    public List<string> Models = [];
}

internal sealed class LlmActiveConfig {
    public string ApiUrl = string.Empty;
    public string ModelsUrl = string.Empty;
    public string ApiKey = string.Empty;
    public string Model = string.Empty;
    public int BatchSize;
    public int Concurrency;
    public int RetryCount;
    public LlmApiProtocol Protocol;
    public LlmProviderPreset? Preset;
}

internal static class LlmTranslateService {
    private const int MaxEstimatedCharsPerBatch = 18000;
    private const int RequestTimeoutSeconds = 600;

    private static readonly JsonSerializerSettings IndentedJsonSettings = new() {
        Formatting = Formatting.Indented
    };

    private static readonly HttpClient HttpClient = CreateHttpClient();

    public static LlmConfigValidationResult ValidateCurrentConfig(bool testConnection) {
        try {
            if (!TryGetActiveConfig(out var config, out var configError)) {
                return new LlmConfigValidationResult {
                    Success = false,
                    Message = configError
                };
            }

            if (testConnection) {
                ProbeConnection(config);
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

    public static LlmModelListResult FetchModels() {
        try {
            if (!TryGetActiveConfig(out var config, out var configError, requireModel: false)) {
                return new LlmModelListResult {
                    Success = false,
                    Message = configError
                };
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, config.ModelsUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey);
            using var response = HttpClient.SendAsync(request).GetAwaiter().GetResult();
            var responseJson = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode) {
                return new LlmModelListResult {
                    Success = false,
                    Message = $"{(int)response.StatusCode}: {response.ReasonPhrase}"
                };
            }

            var parsed = JsonConvert.DeserializeObject<ModelListResponse>(responseJson);
            var models = parsed?.Data
                .Select(entry => entry.Id)
                .Where(id => !string.IsNullOrEmpty(id))
                .ToList() ?? [];
            return new LlmModelListResult {
                Success = true,
                Models = models
            };
        } catch (Exception ex) {
            return new LlmModelListResult {
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
            if (!TryGetActiveConfig(out var config, out var configError)) {
                return new LlmTranslateResult {
                    Success = false,
                    Message = configError
                };
            }

            var pending = CollectPendingEntries(workset);
            pendingCount = pending.Count;
            if (pending.Count == 0) {
                return new LlmTranslateResult {
                    Success = true,
                    Message = "No pending entries."
                };
            }

            var translatedById = new Dictionary<string, string>(StringComparer.Ordinal);
            var termbaseGlossary = TermbaseService.GetGlossaryForLanguage(targetLanguageFolder);
            var batches = BuildBatches(pending, config.BatchSize, MaxEstimatedCharsPerBatch);

            var failedBatches = new List<string>();
            for (var start = 0; start < batches.Count; start += config.Concurrency) {
                var end = Math.Min(start + config.Concurrency, batches.Count);
                var waveTasks = new Task<BatchExecutionResult>[end - start];
                for (var j = start; j < end; j++) {
                    var batch = batches[j];
                    waveTasks[j - start] = Task.Run(() =>
                        RequestBatchTranslationsWithRetry(config,
                            targetLanguageDisplayName,
                            batch,
                            termbaseGlossary));
                }

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
                    PendingCount = pending.Count
                };
            }

            foreach (var item in pending) {
                item.ApplyTranslation(translatedById[item.Id]);
            }

            return new LlmTranslateResult {
                Success = true,
                Message = "OK",
                UpdatedCount = pending.Count,
                PendingCount = pending.Count
            };
        } catch (Exception ex) {
            return new LlmTranslateResult {
                Success = false,
                Message = ex.Message,
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

    private static bool TryGetActiveConfig(out LlmActiveConfig config, out string errorMessage,
        bool requireModel = true) {
        var settings = TranslatorMod.Settings;
        var preset = LlmProviderPresets.Find(settings.ProviderId);
        var protocol = preset?.Protocol ?? settings.CustomProtocol;
        var apiKey = settings.GetApiKey(settings.ProviderId).Trim();
        var model = settings.GetModel(settings.ProviderId).Trim();

        string apiUrl;
        string modelsUrl;
        if (preset is not null) {
            modelsUrl = preset.BaseUrl.TrimEnd('/') + "/models";
            apiUrl = protocol == LlmApiProtocol.Responses
                ? preset.BaseUrl.TrimEnd('/') + "/responses"
                : NormalizeApiUrl(new Uri(preset.BaseUrl));
        } else {
            apiUrl = settings.ApiUrl.Trim();
            if (string.IsNullOrWhiteSpace(apiUrl)) {
                errorMessage = "Translator API URL is empty. Configure it in Mod Settings.";
                config = new LlmActiveConfig();
                return false;
            }

            if (!Uri.TryCreate(apiUrl, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)) {
                errorMessage = "Translator API URL is invalid. Configure a valid http/https endpoint in Mod Settings.";
                config = new LlmActiveConfig();
                return false;
            }

            var chatUrl = NormalizeApiUrl(uri);
            modelsUrl = ReplaceEndpoint(chatUrl, "/models");
            apiUrl = protocol == LlmApiProtocol.Responses ? ReplaceEndpoint(chatUrl, "/responses") : chatUrl;
        }

        if (string.IsNullOrWhiteSpace(apiKey)) {
            errorMessage = "Translator API key is empty. Configure it in Mod Settings.";
            config = new LlmActiveConfig();
            return false;
        }

        if (requireModel && string.IsNullOrWhiteSpace(model)) {
            errorMessage = "Translator model is empty. Configure it in Mod Settings.";
            config = new LlmActiveConfig();
            return false;
        }

        config = new LlmActiveConfig {
            ApiUrl = apiUrl,
            ModelsUrl = modelsUrl,
            ApiKey = apiKey,
            Model = model,
            BatchSize = settings.BatchSize,
            Concurrency = settings.Concurrency,
            RetryCount = settings.RetryCount,
            Protocol = protocol,
            Preset = preset
        };
        errorMessage = string.Empty;
        return true;
    }

    private static BatchExecutionResult RequestBatchTranslationsWithRetry(LlmActiveConfig config,
        string targetLanguage,
        BatchRequest batch,
        IReadOnlyDictionary<string, string> termbaseGlossary) {
        Exception? lastException = null;
        for (var attempt = 0; attempt <= config.RetryCount; attempt++) {
            try {
                var translations =
                    RequestBatchTranslations(config, targetLanguage, batch.Items, termbaseGlossary);
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

    private static string ReplaceEndpoint(string chatCompletionsUrl, string endpoint) {
        const string suffix = "/chat/completions";
        return chatCompletionsUrl.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            ? chatCompletionsUrl[..^suffix.Length] + endpoint
            : chatCompletionsUrl.TrimEnd('/') + endpoint;
    }

    private static string NormalizeApiUrl(Uri uri) {
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

        return builder.Uri.AbsoluteUri;
    }

    private static void ProbeConnection(LlmActiveConfig config) {
        var requestPayload = config.Protocol == LlmApiProtocol.Responses
            ? new Dictionary<string, object> {
                ["model"] = config.Model,
                ["instructions"] = "Health check.",
                ["input"] = new object[] {
                    new {
                        role = "user",
                        content = "Reply with OK."
                    }
                },
                ["max_output_tokens"] = 1,
                ["store"] = false
            }
            : new Dictionary<string, object> {
                ["model"] = config.Model,
                ["temperature"] = 0.0,
                ["max_tokens"] = 1,
                ["messages"] = new object[] {
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
        ApplyProviderOptions(requestPayload, config);

        var requestJson = JsonConvert.SerializeObject(requestPayload);
        using var request = new HttpRequestMessage(HttpMethod.Post, config.ApiUrl);
        request.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey);

        using var response = HttpClient.SendAsync(request).GetAwaiter().GetResult();
        var responseJson = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        if (!response.IsSuccessStatusCode) {
            throw new InvalidOperationException(
                $"Config validation request failed ({(int)response.StatusCode}): {response.ReasonPhrase}; {responseJson}");
        }
    }

    private static void ApplyProviderOptions(Dictionary<string, object> requestPayload, LlmActiveConfig config) {
        if (config.Preset is not { DisableThinking: true }) return;

        if (config.Protocol == LlmApiProtocol.Responses) {
            requestPayload["reasoning"] = new { effort = "none" };
        } else {
            requestPayload["thinking"] = new { type = "disabled" };
        }
    }

    private static Dictionary<string, object> BuildRequestPayload(LlmActiveConfig config, string systemPrompt,
        string userPrompt, IReadOnlyList<PendingTranslationItem> batch) {
        if (config.Protocol == LlmApiProtocol.Responses) {
            return new Dictionary<string, object> {
                ["model"] = config.Model,
                ["instructions"] = systemPrompt,
                ["input"] = new object[] {
                    new {
                        role = "user",
                        content = userPrompt
                    }
                },
                ["temperature"] = 0.0,
                ["max_output_tokens"] = EstimateMaxTokensForBatch(batch),
                ["text"] = new {
                    format = new {
                        type = "json_object"
                    }
                },
                ["store"] = false
            };
        }

        return new Dictionary<string, object> {
            ["model"] = config.Model,
            ["temperature"] = 0.0,
            ["max_tokens"] = EstimateMaxTokensForBatch(batch),
            ["response_format"] = new {
                type = "json_object"
            },
            ["messages"] = new object[] {
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
    }

    private static Dictionary<string, string> RequestBatchTranslations(LlmActiveConfig config,
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
                ? $"Termbase rules (source -> target, mandatory):\n{JsonConvert.SerializeObject(glossaryItems, IndentedJsonSettings)}\n"
                : string.Empty
            ) +
            "Entries:\n" +
            JsonConvert.SerializeObject(payloadItems, IndentedJsonSettings);

        var requestPayload = BuildRequestPayload(config, systemPrompt, userPrompt, batch);
        ApplyProviderOptions(requestPayload, config);

        var requestJson = JsonConvert.SerializeObject(requestPayload);
        using var request = new HttpRequestMessage(HttpMethod.Post, config.ApiUrl);
        request.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey);

        using var response = HttpClient.SendAsync(request).GetAwaiter().GetResult();
        var responseJson = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        if (!response.IsSuccessStatusCode) {
            throw new InvalidOperationException(
                $"LLM request failed ({(int)response.StatusCode}): {response.ReasonPhrase}; {responseJson}");
        }

        var content = ExtractResponseContent(responseJson, config.Protocol);
        if (string.IsNullOrEmpty(content)) {
            throw new InvalidOperationException("LLM response content is empty.");
        }

        BatchTranslationResponse translatedPayload;
        try {
            translatedPayload = JsonConvert.DeserializeObject<BatchTranslationResponse>(ExtractJsonObject(content))!;
        } catch (Exception ex) {
            throw new InvalidOperationException($"Invalid LLM translation payload: {ex.Message}");
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

            if (!matchedTranslations.TryAdd(item.Id, item.Translation)) {
                duplicateIdCount += 1;
                matchedTranslations[item.Id] = item.Translation;
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

    private static string ExtractResponseContent(string responseJson, LlmApiProtocol protocol) {
        if (protocol == LlmApiProtocol.Responses) {
            var response = JsonConvert.DeserializeObject<ResponsesResponse>(responseJson);
            if (response is null) return string.Empty;

            return string.Concat(response.Output
                .Where(item => item.Type == "message")
                .SelectMany(item => item.Content)
                .Where(part => part.Type == "output_text")
                .Select(part => part.Text));
        }

        var completion = JsonConvert.DeserializeObject<ChatCompletionResponse>(responseJson);
        return completion?.Choices.FirstOrDefault()?.Message.Content ?? string.Empty;
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
                    Items = [.. currentItems]
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
        public List<PendingTranslationItem> Items = [];
    }

    private sealed class BatchExecutionResult {
        public int BatchNo;
        public bool Success;
        public string ErrorMessage = string.Empty;
        public Dictionary<string, string> Translations = new(StringComparer.Ordinal);
    }

    private sealed class ResponsesResponse {
        [JsonProperty("output")]
        public List<ResponsesOutputItem> Output = [];
    }

    private sealed class ResponsesOutputItem {
        [JsonProperty("type")]
        public string Type = string.Empty;

        [JsonProperty("content")]
        public List<ResponsesContentPart> Content = [];
    }

    private sealed class ResponsesContentPart {
        [JsonProperty("type")]
        public string Type = string.Empty;

        [JsonProperty("text")]
        public string Text = string.Empty;
    }

    private sealed class ModelListResponse {
        [JsonProperty("data")]
        public List<ModelEntry> Data = [];
    }

    private sealed class ModelEntry {
        [JsonProperty("id")]
        public string Id = string.Empty;
    }

    private sealed class ChatCompletionResponse {
        [JsonProperty("choices")]
        public List<ChatChoice> Choices = [];
    }

    private sealed class ChatChoice {
        [JsonProperty("message")]
        public ChatMessage Message = new();
    }

    private sealed class ChatMessage {
        [JsonProperty("content")]
        public string Content = string.Empty;
    }

    private sealed class BatchTranslationResponse {
        [JsonProperty("translations")]
        public List<TranslatedEntry> Translations = [];
    }

    private sealed class TranslatedEntry {
        [JsonProperty("id")]
        public string Id = string.Empty;

        [JsonProperty("translation")]
        public string Translation = string.Empty;
    }
}