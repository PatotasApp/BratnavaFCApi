using System.Text.Json;
using BratnavaFC.Api;
using BratnavaFC.Domain.Entities;

namespace BranavaFC.Tests;

public sealed class UtcDateTimeConverterTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new UtcDateTimeConverter() }
    };

    private sealed record DateDto(DateTime PlayedAt);

    [Fact]
    public void Read_WhenValueHasNoTimezone_ShouldKeepLocalWallTimeAsUnspecified()
    {
        var dto = JsonSerializer.Deserialize<DateDto>(
            """{"playedAt":"2026-07-14T21:00:00"}""",
            Options);

        Assert.NotNull(dto);
        Assert.Equal(DateTimeKind.Unspecified, dto!.PlayedAt.Kind);
        Assert.Equal(new DateTime(2026, 7, 14, 21, 0, 0), dto.PlayedAt);
    }

    [Fact]
    public void MatchEntity_WhenCreatedFromLocalWallTime_ShouldStoreUtcInstant()
    {
        var dto = JsonSerializer.Deserialize<DateDto>(
            """{"playedAt":"2026-07-14T21:00:00"}""",
            Options)!;

        var match = new MatchEntity(Guid.NewGuid(), dto.PlayedAt, "Boca Jrs");

        Assert.Equal(DateTimeKind.Utc, match.PlayedAt.Kind);
        Assert.Equal(new DateTime(2026, 7, 15, 0, 0, 0, DateTimeKind.Utc), match.PlayedAt);
    }

    [Fact]
    public void Write_WhenValueIsUtc_ShouldSerializeAsSaoPauloWallTime()
    {
        var dto = new DateDto(new DateTime(2026, 7, 15, 0, 0, 0, DateTimeKind.Utc));

        var json = JsonSerializer.Serialize(dto, Options);

        Assert.Contains("\"PlayedAt\":\"2026-07-14T21:00:00.000\"", json);
    }
}
