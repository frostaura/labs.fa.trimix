using System.Globalization;
using System.Text.Json;

namespace Trimix.Core.Connectors.Infra;

/// <summary>
/// Reads Firestore's typed-value wire format out of a document's <c>fields</c> object. Every scalar is
/// wrapped by type — <c>{"stringValue":"x"}</c>, <c>{"doubleValue":1.2}</c>, <c>{"integerValue":"3"}</c>
/// (integers arrive as STRINGS), <c>{"booleanValue":true}</c>, <c>{"nullValue":null}</c>,
/// <c>{"mapValue":{"fields":{…}}}</c> — so a plain property read never works; these helpers unwrap it.
///
/// <para>LogTRAK stores each per-sample channel as a <c>stringValue</c> holding a JSON array of numbers
/// (e.g. depth as <c>"[0,2.6,3.7,…]"</c>), which <see cref="NumberArray"/> parses. Read by key and tolerate
/// absence: every field is optional and a low-fidelity dive simply omits channels.</para>
/// </summary>
internal static class FirestoreValue
{
    /// <summary>The <c>fields</c> object of a nested <c>mapValue</c>, or a null <see cref="JsonElement"/> if absent.</summary>
    public static JsonElement Map(JsonElement fields, string key) =>
        fields.ValueKind == JsonValueKind.Object
        && fields.TryGetProperty(key, out var wrapped)
        && wrapped.ValueKind == JsonValueKind.Object
        && wrapped.TryGetProperty("mapValue", out var map)
        && map.TryGetProperty("fields", out var inner)
            ? inner
            : default;

    public static string? String(JsonElement fields, string key) =>
        Unwrap(fields, key, "stringValue") is { ValueKind: JsonValueKind.String } el ? el.GetString() : null;

    public static bool? Bool(JsonElement fields, string key) =>
        Unwrap(fields, key, "booleanValue") is { ValueKind: JsonValueKind.True or JsonValueKind.False } el
            ? el.GetBoolean()
            : null;

    /// <summary>A <c>doubleValue</c>, an <c>integerValue</c> (string-encoded), or a numeric <c>stringValue</c>
    /// (LogTRAK stores several numbers — <c>o2Mixture</c>, <c>airTempCelsius</c> — as strings).</summary>
    public static double? Double(JsonElement fields, string key)
    {
        if (fields.ValueKind != JsonValueKind.Object || !fields.TryGetProperty(key, out var wrapped)
            || wrapped.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (wrapped.TryGetProperty("doubleValue", out var d) && d.ValueKind == JsonValueKind.Number)
        {
            return d.GetDouble();
        }

        // integerValue and numeric stringValue both arrive as JSON strings.
        foreach (var type in (ReadOnlySpan<string>)["integerValue", "stringValue"])
        {
            if (wrapped.TryGetProperty(type, out var el) && el.ValueKind == JsonValueKind.String
                && double.TryParse(el.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                return value;
            }
        }

        return null;
    }

    public static int? Int(JsonElement fields, string key) =>
        Double(fields, key) is { } value ? (int)Math.Round(value) : null;

    /// <summary>Parses a <c>stringValue</c> holding a JSON array of numbers (LogTRAK's per-sample channel
    /// encoding). Returns an empty array for the common <c>"[]"</c> empty channel, and null when the field is
    /// absent — so a caller can tell "no such channel" from "channel present but empty".</summary>
    public static double[]? NumberArray(JsonElement fields, string key)
    {
        var raw = String(fields, key);
        if (raw is null)
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var values = new double[doc.RootElement.GetArrayLength()];
            var i = 0;
            foreach (var element in doc.RootElement.EnumerateArray())
            {
                values[i++] = element.ValueKind == JsonValueKind.Number ? element.GetDouble() : double.NaN;
            }

            return values;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static JsonElement? Unwrap(JsonElement fields, string key, string type) =>
        fields.ValueKind == JsonValueKind.Object
        && fields.TryGetProperty(key, out var wrapped)
        && wrapped.ValueKind == JsonValueKind.Object
        && wrapped.TryGetProperty(type, out var value)
            ? value
            : null;
}
