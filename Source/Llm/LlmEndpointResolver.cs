namespace Translator.Llm;

public sealed record LlmEndpoints(Uri ChatCompletions, Uri Responses, Uri Models) {
    public Uri For(LlmApiProtocol protocol) =>
        protocol == LlmApiProtocol.Responses ? Responses : ChatCompletions;
}

/// <summary>Turns provider presets or user-entered URLs into concrete endpoints.</summary>
public static class LlmEndpointResolver {
    private const string ChatCompletionsSuffix = "/chat/completions";
    private const string V1Suffix = "/v1";

    public static bool TryResolve(
        LlmProviderPreset? preset,
        string customApiUrl,
        out LlmEndpoints endpoints,
        out string error) {
        endpoints = null!;
        error = string.Empty;

        if (preset is not null) {
            var baseUrl = preset.BaseUrl.TrimEnd('/');
            endpoints = new LlmEndpoints(
                NormalizeChatCompletionsUri(new Uri(baseUrl)),
                new Uri(baseUrl + "/responses"),
                new Uri(baseUrl + "/models"));
            return true;
        }

        var apiUrl = customApiUrl.Trim();
        if (apiUrl.Length == 0) {
            error = "Translator API URL is empty. Configure it in Mod Settings.";
            return false;
        }

        if (!Uri.TryCreate(apiUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)) {
            error = "Translator API URL is invalid. Configure a valid http/https endpoint in Mod Settings.";
            return false;
        }

        var chatCompletions = NormalizeChatCompletionsUri(uri);
        endpoints = new LlmEndpoints(
            chatCompletions,
            ReplaceEndpoint(chatCompletions, "/responses"),
            ReplaceEndpoint(chatCompletions, "/models"));
        return true;
    }

    public static Uri NormalizeChatCompletionsUri(Uri uri) {
        var builder = new UriBuilder(uri) {
            Query = string.Empty,
            Fragment = string.Empty
        };

        var path = builder.Path.TrimEnd('/');
        builder.Path = path switch {
            "" => "/v1/chat/completions",
            _ when path.EndsWith(ChatCompletionsSuffix, StringComparison.OrdinalIgnoreCase) => path,
            _ when path.EndsWith(V1Suffix, StringComparison.OrdinalIgnoreCase) => path + ChatCompletionsSuffix,
            _ => path + V1Suffix + ChatCompletionsSuffix
        };

        return builder.Uri;
    }

    public static Uri ReplaceEndpoint(Uri chatCompletionsUri, string endpoint) {
        var currentPath = chatCompletionsUri.AbsolutePath;
        var path = currentPath.EndsWith(ChatCompletionsSuffix, StringComparison.OrdinalIgnoreCase)
            ? currentPath[..^ChatCompletionsSuffix.Length] + endpoint
            : currentPath.TrimEnd('/') + endpoint;

        var builder = new UriBuilder(chatCompletionsUri) {
            Path = path,
            Query = string.Empty,
            Fragment = string.Empty
        };

        return builder.Uri;
    }
}
