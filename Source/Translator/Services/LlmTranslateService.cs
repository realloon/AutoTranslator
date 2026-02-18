using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

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
    private const int BatchSize = 40;

    private static readonly JsonSerializerOptions JsonOptions = new() {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly HttpClient HttpClient = CreateHttpClient();

    public static LlmConfigValidationResult ValidateCurrentConfig(bool testConnection) {
        try {
            if (!TryGetActiveConfig(out var apiUrl, out var apiKey, out var model, out var configError)) {
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
            if (!TryGetActiveConfig(out var apiUrl, out var apiKey, out var model, out var configError)) {
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
            for (var i = 0; i < pending.Count; i += BatchSize) {
                var batch = pending.Skip(i).Take(BatchSize).ToList();
                var batchResult = RequestBatchTranslations(apiUrl, apiKey, model, targetLanguage, batch);
                foreach (var (id, translation) in batchResult) {
                    translatedById[id] = translation;
                }
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
        out string errorMessage) {
        var settings = TranslatorMod.Settings;
        apiUrl = settings?.ApiUrl ?? TranslatorSettings.DefaultApiUrl;
        apiKey = settings?.ApiKey ?? string.Empty;
        model = settings?.Model ?? TranslatorSettings.DefaultModel;

        apiUrl = apiUrl.Trim();
        apiKey = apiKey.Trim();
        model = model.Trim();

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

        errorMessage = string.Empty;
        return true;
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

    private sealed class ChatCompletionResponse {
        [JsonPropertyName("choices")]
        public List<ChatChoice> Choices { get; set; } = [];
    }

    private sealed class ChatChoice {
        [JsonPropertyName("message")]
        public ChatMessage Message { get; set; } = new();
    }

    private sealed class ChatMessage {
        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;
    }

    private sealed class BatchTranslationResponse {
        [JsonPropertyName("translations")]
        public List<TranslatedEntry> Translations { get; set; } = [];
    }

    private sealed class TranslatedEntry {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("translation")]
        public string? Translation { get; set; }
    }
}
