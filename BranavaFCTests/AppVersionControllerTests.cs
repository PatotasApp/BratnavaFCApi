using BratnavaFC.Api.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace BranavaFC.Tests;

public sealed class AppVersionControllerTests
{
    private static AppVersionController.AppVersionResponse Get(Dictionary<string, string?> values)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var result = new AppVersionController(config).GetVersion();
        return (AppVersionController.AppVersionResponse)result.Should().BeOfType<OkObjectResult>().Subject.Value!;
    }

    [Fact]
    public void SemConfiguracao_NaoBloqueiaNinguem()
    {
        var v = Get(new());

        v.Android.Should().Be(new AppVersionController.PlatformVersion(0, 0, null));
        v.Ios.Should().Be(new AppVersionController.PlatformVersion(0, 0, null));
    }

    [Fact]
    public void LeMinimaMaisRecenteELojaPorPlataforma()
    {
        var v = Get(new()
        {
            ["AppVersion:Android:MinBuild"] = "2026110301",
            ["AppVersion:Android:LatestBuild"] = "2026111501",
            ["AppVersion:Android:StoreUrl"] = "https://play.google.com/store/apps/details?id=br.com.patotasapp",
            ["AppVersion:Ios:MinBuild"] = "5",
        });

        v.Android.Should().Be(new AppVersionController.PlatformVersion(
            2026110301, 2026111501, "https://play.google.com/store/apps/details?id=br.com.patotasapp"));
        v.Ios.Should().Be(new AppVersionController.PlatformVersion(5, 5, null));
    }

    [Fact]
    public void MaisRecenteNuncaFicaAbaixoDaMinima()
    {
        var v = Get(new()
        {
            ["AppVersion:Android:MinBuild"] = "10",
            ["AppVersion:Android:LatestBuild"] = "3",
        });

        v.Android.LatestBuild.Should().Be(10);
    }
}
