using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentLog.Core;

/// <summary>
/// Maps `exit` (int OR "interrupted" / "error") to <see cref="ExitStatus"/> and back.
/// Any other string or an out-of-range number is read as an error, so one odd value does not drop the whole line.
/// </summary>
public sealed class ExitStatusJsonConverter : JsonConverter<ExitStatus>
{
    private const string Interrupted = "interrupted";
    private const string Error = "error";

    public override ExitStatus Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Number when reader.TryGetInt32(out var code):
                return new ExitStatus(ExitKind.Code, code);
            case JsonTokenType.Number:
                // Out of int range (e.g. Windows 0xC0000005): still a failure, keep the line.
                return new ExitStatus(ExitKind.Error);
            case JsonTokenType.String:
                return reader.GetString() switch
                {
                    Interrupted => new ExitStatus(ExitKind.Interrupted),
                    Error => new ExitStatus(ExitKind.Error),
                    _ => new ExitStatus(ExitKind.Error),
                };
            default:
                throw new JsonException($"Unexpected token {reader.TokenType} for exit.");
        }
    }

    public override void Write(Utf8JsonWriter writer, ExitStatus value, JsonSerializerOptions options)
    {
        switch (value.Kind)
        {
            case ExitKind.Code:
                writer.WriteNumberValue(value.Code ?? throw new JsonException("Exit code is missing."));
                break;
            case ExitKind.Interrupted:
                writer.WriteStringValue(Interrupted);
                break;
            case ExitKind.Error:
                writer.WriteStringValue(Error);
                break;
            default:
                throw new JsonException($"Unknown exit kind {value.Kind}.");
        }
    }
}
