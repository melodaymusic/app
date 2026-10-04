using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HumeMusic.Core.Plex.Json;

/// <summary>
/// Plex responses are inconsistent about primitive types (ids as numbers or strings,
/// booleans as true/false, 0/1 or "1"). These converters accept any reasonable
/// representation so one unexpected field never fails a whole response.
/// </summary>
public static class PlexJson
{
    public static readonly JsonSerializerOptions Options = Create();

    private static JsonSerializerOptions Create()
    {
        var o = new JsonSerializerOptions
        {
            // Plex uses camelCase for attributes and PascalCase for child collections, and the same
            // word can appear in both forms ("guid" string vs "Guid" array), so matching is exact.
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false,
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };
        o.Converters.Add(new LenientStringConverter());
        o.Converters.Add(new LenientBoolConverter());
        o.Converters.Add(new LenientNullableBoolConverter());
        o.Converters.Add(new LenientIntConverter());
        o.Converters.Add(new LenientNullableIntConverter());
        o.Converters.Add(new LenientLongConverter());
        o.Converters.Add(new LenientNullableLongConverter());
        o.Converters.Add(new LenientDoubleConverter());
        o.Converters.Add(new LenientNullableDoubleConverter());
        return o;
    }

    internal static string? ReadAsString(ref Utf8JsonReader reader)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String: return reader.GetString();
            case JsonTokenType.Number:
                return reader.TryGetInt64(out var l)
                    ? l.ToString(CultureInfo.InvariantCulture)
                    : reader.GetDouble().ToString(CultureInfo.InvariantCulture);
            case JsonTokenType.True: return "1";
            case JsonTokenType.False: return "0";
            case JsonTokenType.Null: return null;
            default:
                reader.Skip();
                return null;
        }
    }

    internal static double? ReadAsDouble(ref Utf8JsonReader reader)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Number: return reader.GetDouble();
            case JsonTokenType.String:
                return double.TryParse(reader.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;
            case JsonTokenType.True: return 1;
            case JsonTokenType.False: return 0;
            case JsonTokenType.Null: return null;
            default:
                reader.Skip();
                return null;
        }
    }

    internal static bool? ReadAsBool(ref Utf8JsonReader reader)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.True: return true;
            case JsonTokenType.False: return false;
            case JsonTokenType.Number: return reader.TryGetInt64(out var l) ? l != 0 : reader.GetDouble() != 0;
            case JsonTokenType.String:
                var s = reader.GetString();
                if (string.IsNullOrEmpty(s)) return null;
                if (bool.TryParse(s, out var b)) return b;
                return s != "0";
            case JsonTokenType.Null: return null;
            default:
                reader.Skip();
                return null;
        }
    }
}

internal sealed class LenientStringConverter : JsonConverter<string>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => PlexJson.ReadAsString(ref reader);
    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) => writer.WriteStringValue(value);
}

internal sealed class LenientBoolConverter : JsonConverter<bool>
{
    public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => PlexJson.ReadAsBool(ref reader) ?? false;
    public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options) => writer.WriteBooleanValue(value);
}

internal sealed class LenientNullableBoolConverter : JsonConverter<bool?>
{
    public override bool HandleNull => true;
    public override bool? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => PlexJson.ReadAsBool(ref reader);
    public override void Write(Utf8JsonWriter writer, bool? value, JsonSerializerOptions options)
    {
        if (value is null) writer.WriteNullValue(); else writer.WriteBooleanValue(value.Value);
    }
}

internal sealed class LenientIntConverter : JsonConverter<int>
{
    public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var d = PlexJson.ReadAsDouble(ref reader);
        return d is null ? 0 : (int)Math.Clamp(d.Value, int.MinValue, int.MaxValue);
    }
    public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options) => writer.WriteNumberValue(value);
}

internal sealed class LenientNullableIntConverter : JsonConverter<int?>
{
    public override bool HandleNull => true;
    public override int? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var d = PlexJson.ReadAsDouble(ref reader);
        return d is null ? null : (int)Math.Clamp(d.Value, int.MinValue, int.MaxValue);
    }
    public override void Write(Utf8JsonWriter writer, int? value, JsonSerializerOptions options)
    {
        if (value is null) writer.WriteNullValue(); else writer.WriteNumberValue(value.Value);
    }
}

internal sealed class LenientLongConverter : JsonConverter<long>
{
    public override long Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt64(out var l)) return l;
        var d = PlexJson.ReadAsDouble(ref reader);
        return d is null ? 0 : (long)d.Value;
    }
    public override void Write(Utf8JsonWriter writer, long value, JsonSerializerOptions options) => writer.WriteNumberValue(value);
}

internal sealed class LenientNullableLongConverter : JsonConverter<long?>
{
    public override bool HandleNull => true;
    public override long? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt64(out var l)) return l;
        var d = PlexJson.ReadAsDouble(ref reader);
        return d is null ? null : (long)d.Value;
    }
    public override void Write(Utf8JsonWriter writer, long? value, JsonSerializerOptions options)
    {
        if (value is null) writer.WriteNullValue(); else writer.WriteNumberValue(value.Value);
    }
}

internal sealed class LenientDoubleConverter : JsonConverter<double>
{
    public override double Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => PlexJson.ReadAsDouble(ref reader) ?? 0;
    public override void Write(Utf8JsonWriter writer, double value, JsonSerializerOptions options) => writer.WriteNumberValue(value);
}

internal sealed class LenientNullableDoubleConverter : JsonConverter<double?>
{
    public override bool HandleNull => true;
    public override double? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => PlexJson.ReadAsDouble(ref reader);
    public override void Write(Utf8JsonWriter writer, double? value, JsonSerializerOptions options)
    {
        if (value is null) writer.WriteNullValue(); else writer.WriteNumberValue(value.Value);
    }
}
