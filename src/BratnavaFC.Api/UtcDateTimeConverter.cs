using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BratnavaFC.Api;

/// <summary>
/// Serializa DateTime sempre com sufixo "Z" (UTC ISO-8601).
/// Datas armazenadas como UTC no banco chegam com Kind=Unspecified via Npgsql;
/// este converter as trata como UTC, garantindo que o cliente receba o fuso correto.
/// </summary>
public sealed class UtcDateTimeConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => DateTime.Parse(reader.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        var utc = value.Kind switch
        {
            DateTimeKind.Utc     => value,
            DateTimeKind.Local   => value.ToUniversalTime(),
            _                    => DateTime.SpecifyKind(value, DateTimeKind.Utc) // Unspecified → UTC
        };
        writer.WriteStringValue(utc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture));
    }
}
