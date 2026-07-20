using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using BratnavaFC.Domain.Time;

namespace BratnavaFC.Api;

/// <summary>
/// Contrato JSON para DateTime:
/// - Entrada com timezone/offset representa um instante e e convertida para UTC.
/// - Entrada sem timezone representa horario local de Sao Paulo e segue sem Kind.
/// - Saida de instantes UTC e convertida para horario local de Sao Paulo, sem sufixo.
/// Assim, um jogo salvo como 21:00 continua aparecendo como 21:00 nos clientes.
/// </summary>
public sealed class UtcDateTimeConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var raw = reader.GetString()!;
        if (HasTimezone(raw))
            return DateTimeOffset.Parse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
                .UtcDateTime;

        var parsed = DateTime.Parse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None);
        return DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified);
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        var output = value.Kind == DateTimeKind.Utc
            ? BratnavaDateTime.UtcToSaoPauloLocal(value)
            : value;

        writer.WriteStringValue(output.ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture));
    }

    private static bool HasTimezone(string value)
        => value.EndsWith('Z') ||
           value.EndsWith('z') ||
           (value.Length >= 6 &&
            (value[^6] == '+' || value[^6] == '-') &&
            char.IsDigit(value[^5]) &&
            char.IsDigit(value[^4]) &&
            value[^3] == ':' &&
            char.IsDigit(value[^2]) &&
            char.IsDigit(value[^1]));
}
