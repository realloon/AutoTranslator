using System.Text.Encodings.Web;
using System.Text.Json;

namespace Translator.Utilities;

/// <summary>
/// Shared serializer settings. Kept explicit and immutable because <see cref="JsonSerializerOptions"/>
/// is only safe to share once it is frozen by first use.
/// </summary>
public static class TranslatorJson {
    private static readonly JavaScriptEncoder Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;

    /// <summary>Reading responses from remote APIs. Property names are matched case-insensitively.</summary>
    public static JsonSerializerOptions Api { get; } = new() {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    /// <summary>Prompts embedded in request payloads. Mirrors the previous Newtonsoft output.</summary>
    public static JsonSerializerOptions Prompt { get; } = new() {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = Encoder
    };

    /// <summary>Human-editable files such as the termbase.</summary>
    public static JsonSerializerOptions File { get; } = new() {
        WriteIndented = true,
        Encoder = Encoder,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    /// <summary>Compact request payloads sent over the wire.</summary>
    public static JsonSerializerOptions Wire { get; } = new() {
        Encoder = Encoder
    };
}
