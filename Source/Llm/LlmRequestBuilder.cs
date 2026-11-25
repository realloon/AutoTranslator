using System.Text.Json.Nodes;

namespace Translator.Llm;

/// <summary><see cref="JsonObject"/> is used instead of DTOs: the payload is data, not a type.</summary>
public static class LlmRequestBuilder {
    public static JsonObject CreateTranslationRequest(string model, LlmApiProtocol protocol, bool disableThinking,
        TranslationPrompt prompt, int maxOutputTokens) {
        var payload = protocol == LlmApiProtocol.Responses
            ? CreateResponsesPayload(model, prompt, maxOutputTokens, store: false, jsonResponse: true,
                includeTemperature: true)
            : CreateChatPayload(model, prompt, maxOutputTokens, jsonResponse: true);

        ApplyProviderOptions(payload, protocol, disableThinking);
        return payload;
    }

    /// <summary>Minimal request used to verify credentials/endpoint reachability.</summary>
    public static JsonObject CreateValidationRequest(string model, LlmApiProtocol protocol, bool disableThinking) {
        var prompt = new TranslationPrompt("Health check.", "Reply with OK.");
        var payload = protocol == LlmApiProtocol.Responses
            ? CreateResponsesPayload(model, prompt, maxOutputTokens: 1, store: false, jsonResponse: false,
                includeTemperature: false)
            : CreateChatPayload(model, prompt, maxOutputTokens: 1, jsonResponse: false);

        ApplyProviderOptions(payload, protocol, disableThinking);
        return payload;
    }

    private static JsonObject CreateChatPayload(string model, TranslationPrompt prompt, int maxOutputTokens,
        bool jsonResponse) {
        var payload = new JsonObject {
            ["model"] = model,
            ["temperature"] = 0.0,
            ["max_tokens"] = maxOutputTokens
        };

        if (jsonResponse) {
            payload["response_format"] = new JsonObject {
                ["type"] = "json_object"
            };
        }

        payload["messages"] = new JsonArray {
            new JsonObject {
                ["role"] = "system",
                ["content"] = prompt.SystemPrompt
            },
            new JsonObject {
                ["role"] = "user",
                ["content"] = prompt.UserPrompt
            }
        };

        return payload;
    }

    private static JsonObject CreateResponsesPayload(string model, TranslationPrompt prompt, int maxOutputTokens,
        bool store, bool jsonResponse, bool includeTemperature) {
        var payload = new JsonObject {
            ["model"] = model,
            ["instructions"] = prompt.SystemPrompt,
            ["input"] = new JsonArray {
                new JsonObject {
                    ["role"] = "user",
                    ["content"] = prompt.UserPrompt
                }
            },
            ["max_output_tokens"] = maxOutputTokens
        };

        if (includeTemperature) {
            payload["temperature"] = 0.0;
        }

        if (jsonResponse) {
            payload["text"] = new JsonObject {
                ["format"] = new JsonObject {
                    ["type"] = "json_object"
                }
            };
        }

        payload["store"] = store;
        return payload;
    }

    private static void ApplyProviderOptions(JsonObject payload, LlmApiProtocol protocol, bool disableThinking) {
        if (!disableThinking) return;

        if (protocol == LlmApiProtocol.Responses) {
            payload["reasoning"] = new JsonObject {
                ["effort"] = "none"
            };
        } else {
            payload["thinking"] = new JsonObject {
                ["type"] = "disabled"
            };
        }
    }
}