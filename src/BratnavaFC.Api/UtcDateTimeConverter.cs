using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BratnavaFC.Api;

/// <summary>
/// Serializa DateTime como horário de parede, sem sufixo de timezone.
/// Para dados do domínio como horário de partida, 21:00 deve ser exibido como
/// 21:00 em todos os clientes, independente do fuso do dispositivo/navegador.
/// </summary>
public sealed class UtcDateTimeConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var raw = StripTimezone(reader.GetString()!);
        var parsed = DateTime.Parse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None);
        return DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture));
    }

    private static string StripTimezone(string value)
    {
        if (value.EndsWith('Z') || value.EndsWith('z'))
            return value[..^1];

        if (value.Length >= 6)
        {
            var offset = value[^6..];
            if ((offset[0] == '+' || offset[0] == '-') &&
                char.IsDigit(offset[1]) &&
                char.IsDigit(offset[2]) &&
                offset[3] == ':' &&
                char.IsDigit(offset[4]) &&
                char.IsDigit(offset[5]))
            {
                return value[..^6];
            }
        }

        return value;
    }
}
