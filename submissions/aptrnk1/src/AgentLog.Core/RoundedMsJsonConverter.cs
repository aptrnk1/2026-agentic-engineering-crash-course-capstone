using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentLog.Core;

/// <summary>Reads `ms` as any JSON number and rounds it, so a fractional duration does not drop the line.</summary>
public sealed class RoundedMsJsonConverter : JsonConverter<int?>
{
    public override int? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.Null => null,
            JsonTokenType.Number => (int)Math.Round(reader.GetDouble(), MidpointRounding.AwayFromZero),
            _ => throw new JsonException($"Unexpected token {reader.TokenType} for ms."),
        };

    public override void Write(Utf8JsonWriter writer, int? value, JsonSerializerOptions options)
    {
        if (value is { } ms)
            writer.WriteNumberValue(ms);
        else
            writer.WriteNullValue();
    }
}
