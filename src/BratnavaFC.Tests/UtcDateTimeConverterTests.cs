using System.Text.Json;
using BratnavaFC.Api;
using Xunit;

namespace BratnavaFC.Tests;

public class UtcDateTimeConverterTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new UtcDateTimeConverter() }
    };

    private sealed record MatchClockDto(DateTime PlayedAt);

    [Theory]
    [InlineData("2026-06-15T21:00:00")]
    [InlineData("2026-06-15T21:00:00Z")]
    [InlineData("2026-06-15T21:00:00-03:00")]
    [InlineData("2026-06-15T21:00:00+02:00")]
    public void Read_KeepsLiteralClockTime(string playedAt)
    {
        var dto = JsonSerializer.Deserialize<MatchClockDto>(
            $$"""{"PlayedAt":"{{playedAt}}"}""",
            Options);

        Assert.NotNull(dto);
        Assert.Equal(new DateTime(2026, 6, 15, 21, 0, 0, DateTimeKind.Utc), dto!.PlayedAt);
    }

    [Fact]
    public void Write_DoesNotEmitTimezoneSuffix()
    {
        var dto = new MatchClockDto(new DateTime(2026, 6, 15, 21, 0, 0, DateTimeKind.Utc));

        var json = JsonSerializer.Serialize(dto, Options);

        Assert.Contains(@"""PlayedAt"":""2026-06-15T21:00:00.000""", json);
        Assert.DoesNotContain("Z", json);
    }
}
