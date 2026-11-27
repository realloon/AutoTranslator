using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Translator.Utilities;

namespace Translator.Llm;

internal sealed class LlmRequestException(string message, bool isTransient, TimeSpan? retryAfter = null,
    HttpStatusCode? statusCode = null) : Exception(message) {
    public bool IsTransient { get; } = isTransient;
    public TimeSpan? RetryAfter { get; } = retryAfter;
    public HttpStatusCode? StatusCode { get; } = statusCode;
}

/// <summary>Every call uses a per-request timeout so a slow endpoint cannot stall the game indefinitely.</summary>
internal static class LlmClient {
    private const int RequestTimeoutSeconds = 600;
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(15);

    private static readonly HttpClient Http = CreateHttpClient();

    public static async Task<LlmConfigValidationResult> ValidateAsync(CancellationToken cancellationToken = default) {
        if (!LlmConfigResolver.TryCreate(TranslatorMod.Settings, out var config, out var error)) {
            return new LlmConfigValidationResult(false, new LlmError(string.Empty, error));
        }

        try {
            var payload = LlmRequestBuilder.CreateValidationRequest(
                config.Model, config.Protocol, config.DisableThinking);
            using var request = CreateRequest(HttpMethod.Post, config.Endpoints.For(config.Protocol), config.ApiKey,
                payload);
            await SendAsync(request, "Config validation", cancellationToken).ConfigureAwait(false);
            return new LlmConfigValidationResult(true);
        } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            return new LlmConfigValidationResult(false, new LlmError(string.Empty, "Cancelled."));
        } catch (Exception ex) {
            return new LlmConfigValidationResult(false, DescribeError(ex));
        }
    }

    public static async Task<LlmModelListResult> FetchModelsAsync(CancellationToken cancellationToken = default) {
        if (!LlmConfigResolver.TryCreate(TranslatorMod.Settings, out var config, out var error, requireModel: false)) {
            return LlmModelListResult.Failure(new LlmError(string.Empty, error));
        }

        try {
            using var request = CreateRequest(HttpMethod.Get, config.Endpoints.Models, config.ApiKey);
            var body = await SendAsync(request, "Model discovery", cancellationToken).ConfigureAwait(false);
            return new LlmModelListResult(true, null, LlmResponseReader.ParseModelIds(body));
        } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            return LlmModelListResult.Failure(new LlmError(string.Empty, "Cancelled."));
        } catch (Exception ex) {
            return LlmModelListResult.Failure(DescribeError(ex));
        }
    }

    public static async Task<BatchTranslationResult> TranslateBatchAsync(LlmSessionConfig config,
        TranslationBatch batch, string targetLanguage, IReadOnlyDictionary<string, string> glossary,
        CancellationToken cancellationToken = default) {
        var prompt = PromptBuilder.Build(targetLanguage, batch.Items, glossary);
        var payload = LlmRequestBuilder.CreateTranslationRequest(
            config.Model, config.Protocol, config.DisableThinking, prompt,
            BatchPlanner.EstimateMaxTokens(batch.Items));

        for (var attempt = 0;; attempt++) {
            try {
                using var request = CreateRequest(
                    HttpMethod.Post, config.Endpoints.For(config.Protocol), config.ApiKey, payload);
                var body = await SendAsync(request, "LLM", cancellationToken).ConfigureAwait(false);
                var content = LlmResponseReader.ExtractContent(body, config.Protocol);
                if (content.Length == 0) {
                    throw new LlmRequestException("LLM response content is empty.", isTransient: true);
                }

                return !TranslationResponseParser.TryParse(content, batch.Items, out var translations,
                    out var parseError)
                    ? throw new LlmRequestException(parseError, isTransient: true)
                    : BatchTranslationResult.Ok(batch.Number, translations);
            } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                // Let the orchestrator turn this into a cancellation outcome instead of a batch failure.
                throw;
            } catch (LlmRequestException ex) when (ex.IsTransient && attempt < config.RetryCount) {
                await Task.Delay(GetRetryDelay(attempt, ex.RetryAfter), cancellationToken)
                    .ConfigureAwait(false);
            } catch (Exception ex) {
                return BatchTranslationResult.Failed(batch.Number, DescribeError(ex));
            }
        }
    }

    private static LlmError DescribeError(Exception exception) {
        if (exception is LlmRequestException { StatusCode: { } statusCode }) {
            var key = statusCode switch {
                HttpStatusCode.Unauthorized => "Translator_ApiKeyInvalid",
                HttpStatusCode.Forbidden => "Translator_ApiKeyForbidden",
                HttpStatusCode.NotFound => "Translator_EndpointNotFound",
                HttpStatusCode.TooManyRequests => "Translator_RateLimited",
                _ when (int)statusCode >= 500 => "Translator_ProviderUnavailable",
                _ => null
            };

            if (key is not null) {
                return new LlmError(key, exception.Message);
            }
        }

        return new LlmError(string.Empty, exception.Message);
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, Uri uri, string apiKey,
        JsonObject? payload = null) {
        var request = new HttpRequestMessage(method, uri);
        if (payload is not null) {
            request.Content = new StringContent(
                payload.ToJsonString(TranslatorJson.Wire), Encoding.UTF8, "application/json");
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        return request;
    }

    private static async Task<string> SendAsync(HttpRequestMessage request, string operation,
        CancellationToken cancellationToken) {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(RequestTimeoutSeconds));

        try {
            using var response = await Http
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode) {
                throw new LlmRequestException(
                    $"{operation} request failed ({(int)response.StatusCode}): {response.ReasonPhrase}; {body}",
                    IsTransient(response.StatusCode),
                    GetRetryAfter(response),
                    response.StatusCode);
            }

            return body;
        } catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
            throw new LlmRequestException(
                $"{operation} request timed out after {RequestTimeoutSeconds} seconds.",
                isTransient: true);
        } catch (HttpRequestException ex) {
            throw new LlmRequestException(ex.Message, isTransient: true);
        }
    }

    private static bool IsTransient(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests ||
        (int)statusCode >= 500;

    private static TimeSpan? GetRetryAfter(HttpResponseMessage response) {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta) {
            return delta;
        }

        return retryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : null;
    }

    private static TimeSpan GetRetryDelay(int attempt, TimeSpan? retryAfter) {
        if (retryAfter is { } serverDelay && serverDelay > TimeSpan.Zero) {
            return serverDelay > MaxRetryDelay ? MaxRetryDelay : serverDelay;
        }

        var exponential = TimeSpan.FromMilliseconds(500 * Math.Pow(2, attempt));
        var jitter = TimeSpan.FromMilliseconds(Random.Shared.Next(0, 250));
        var delay = exponential + jitter;
        return delay > MaxRetryDelay ? MaxRetryDelay : delay;
    }

    private static HttpClient CreateHttpClient() {
        var handler = new SocketsHttpHandler {
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            ConnectTimeout = TimeSpan.FromSeconds(15),
            AutomaticDecompression = DecompressionMethods.All
        };

        var client = new HttpClient(handler) {
            Timeout = Timeout.InfiniteTimeSpan
        };
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }
}
