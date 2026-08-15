using System.Text.Json;
using System.Text.Json.Serialization;

namespace DosBoxxer.Core.Infrastructure.ScreenScraper.Dto;

/// <summary>
/// ScreenScraper is not perfectly consistent: a field that is normally an array is sometimes
/// serialised as a single object (and occasionally as an empty string). This converter accepts
/// all three shapes and always yields a list, so a slightly odd response never aborts parsing.
/// </summary>
public sealed class FlexibleListConverter<T> : JsonConverter<List<T>>
{
    public override List<T>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return null;

            case JsonTokenType.StartArray:
            {
                var list = new List<T>();
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                {
                    var item = JsonSerializer.Deserialize<T>(ref reader, options);
                    if (item is not null)
                    {
                        list.Add(item);
                    }
                }

                return list;
            }

            case JsonTokenType.StartObject:
            {
                var single = JsonSerializer.Deserialize<T>(ref reader, options);
                return single is null ? new List<T>() : new List<T> { single };
            }

            default:
                reader.Skip();
                return new List<T>();
        }
    }

    public override void Write(Utf8JsonWriter writer, List<T> value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, value, options);
}

/// <summary>
/// Numeric fields are sometimes strings and sometimes numbers in ScreenScraper responses.
/// </summary>
public sealed class FlexibleStringConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Number => reader.TryGetInt64(out var l)
                ? l.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : reader.GetDouble().ToString(System.Globalization.CultureInfo.InvariantCulture),
            JsonTokenType.True => "true",
            JsonTokenType.False => "false",
            JsonTokenType.Null => null,
            _ => SkipAndReturnNull(ref reader),
        };

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value);

    private static string? SkipAndReturnNull(ref Utf8JsonReader reader)
    {
        reader.Skip();
        return null;
    }
}
