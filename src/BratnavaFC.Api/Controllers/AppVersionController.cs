using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BratnavaFC.Api.Controllers;

/// <summary>
/// Versão mínima e mais recente do app por plataforma, para o app decidir se
/// bloqueia (abaixo da mínima) ou só avisa (abaixo da mais recente).
///
/// Os valores vêm da configuração <c>AppVersion</c>; no Fly, por secret:
/// <c>AppVersion__Android__MinBuild</c>, <c>AppVersion__Android__LatestBuild</c>,
/// <c>AppVersion__Android__StoreUrl</c> e o mesmo para <c>Ios</c>. O número é o
/// build (o <c>+N</c> do pubspec / <c>--build-number</c>). Sem configuração,
/// mínima e mais recente ficam 0 e nenhum app é bloqueado.
///
/// Anônimo de propósito: o app consulta antes do login, e uma versão antiga
/// precisa conseguir ler isto mesmo que o resto da API tenha mudado.
/// </summary>
[ApiController]
[Route("api/app")]
[AllowAnonymous]
public sealed class AppVersionController : ControllerBase
{
    private readonly IConfiguration _configuration;

    public AppVersionController(IConfiguration configuration) => _configuration = configuration;

    // GET /api/app/version
    [HttpGet("version")]
    public IActionResult GetVersion() => Ok(new AppVersionResponse(
        Platform("Android"),
        Platform("Ios")));

    private PlatformVersion Platform(string name)
    {
        var section = _configuration.GetSection($"AppVersion:{name}");
        var min = Math.Max(0, section.GetValue<long?>("MinBuild") ?? 0);
        var latest = Math.Max(min, section.GetValue<long?>("LatestBuild") ?? 0);
        var storeUrl = section["StoreUrl"];
        return new PlatformVersion(min, latest, string.IsNullOrWhiteSpace(storeUrl) ? null : storeUrl);
    }

    public sealed record AppVersionResponse(PlatformVersion Android, PlatformVersion Ios);

    public sealed record PlatformVersion(long MinBuild, long LatestBuild, string? StoreUrl);
}
