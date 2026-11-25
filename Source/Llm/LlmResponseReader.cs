using System.Text;
using System.Text.Json;
using Translator.Utilities;

namespace Translator.Llm;

public static class LlmResponseReader {
    public static string ExtractContent(string responseJson, LlmApiProtocol protocol) {
        if (protocol == LlmApiProtocol.Responses) {
            var response = JsonSerializer.Deserialize<ResponsesApiResponse>(responseJson, TranslatorJson.Api);
            if (response is null) {
                return string.Empty;
            }

            var content = new StringBuilder();
            foreach (var item in response.Output) {
                if (item.Type != "message") {
                    continue;
                }

                foreach (var part in item.Content) {
                    if (part.Type == "output_text") {
                        content.Append(part.Text);
                    }
                }
            }

            return content.ToString();
        }

        var completion = JsonSerializer.Deserialize<ChatCompletionResponse>(responseJson, TranslatorJson.Api);
        return completion?.Choices is { Count: > 0 } choices
            ? choices[0].Message.Content
            : string.Empty;
    }

    public static IReadOnlyList<string> ParseModelIds(string responseJson) {
        var response = JsonSerializer.Deserialize<ModelListResponse>(responseJson, TranslatorJson.Api);
        return response?.Data
            .Select(entry => entry.Id)
            .Where(id => !string.IsNullOrEmpty(id))
            .ToList() ?? [];
    }
}
