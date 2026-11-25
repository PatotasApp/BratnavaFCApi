using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Dtos;
using System.Text.Json;

namespace BranavaFCTests;

public sealed class StructuredLocationEntityTests
{
    [Fact]
    public void GroupSettings_ShouldStoreStructuredDefaultLocation()
    {
        var settings = new GroupSettingsEntity(
            Guid.NewGuid(), 5, 14, "Arena", DayOfWeek.Saturday, TimeSpan.FromHours(10));

        settings.SetDefaultLocation(
            " Rua das Flores, 10 ", -23.550520, -46.633308);

        Assert.Equal("Rua das Flores, 10", settings.DefaultPlaceAddress);
        Assert.Equal(-23.550520, settings.DefaultPlaceLatitude);
        Assert.Equal(-46.633308, settings.DefaultPlaceLongitude);
    }

    [Fact]
    public void Match_ShouldKeepLocationSnapshot()
    {
        var match = new MatchEntity(
            Guid.NewGuid(), DateTime.UtcNow, "Arena",
            "Rua das Flores, 10", -23.550520, -46.633308);

        Assert.Equal("Rua das Flores, 10", match.PlaceAddress);
        Assert.Equal(-23.550520, match.PlaceLatitude);
        Assert.Equal(-46.633308, match.PlaceLongitude);
    }

    [Fact]
    public void Location_ShouldRequireLatitudeAndLongitudeTogether()
    {
        var settings = new GroupSettingsEntity(
            Guid.NewGuid(), 5, 14, "Arena", null, null);

        var error = Assert.Throws<InvalidOperationException>(() =>
            settings.SetDefaultLocation("Rua das Flores", -23.5, null));

        Assert.Contains("juntas", error.Message);
    }

    [Fact]
    public void LegacySettingsPayload_ShouldNotClearStructuredLocation()
    {
        var legacy = JsonSerializer.Deserialize<UpsertGroupSettingsDto>(
            "{\"minPlayers\":5,\"maxPlayers\":14}",
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        var explicitClear = JsonSerializer.Deserialize<UpsertGroupSettingsDto>(
            "{\"defaultPlaceAddress\":null,\"defaultPlaceLatitude\":null,\"defaultPlaceLongitude\":null}",
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.False(legacy!.HasStructuredLocation);
        Assert.True(explicitClear!.HasStructuredLocation);
    }
}
