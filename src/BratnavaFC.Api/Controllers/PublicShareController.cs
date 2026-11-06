using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Dtos.Profile;
using BratnavaFC.Domain.Dtos.Share;
using BratnavaFC.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BratnavaFC.Api.Controllers;

/// <summary>
/// Links compartilhados (perfil, patota e conteúdos internos). Rotas anônimas: quem manda o
/// token é identificado (o FirebaseIdentityMiddleware roda também aqui), quem não manda é
/// tratado como visitante.
///
/// Perfil e patota têm vitrine pública. Partida, votação de MVP, campeonato e enquete são
/// dados internos: aqui só sai se quem abriu é integrante (para o cliente abrir a tela
/// interna, que também checa integrante na API) e, para os demais, o mínimo para o aviso.
/// </summary>
[ApiController]
[Route("api/public")]
[AllowAnonymous]
[EnableRateLimiting("PerUser")]
public sealed class PublicShareController : GroupAuthorizedController
{
    private readonly AppDbContext _db;
    private readonly IProfileService _profiles;
    private readonly IPublicShareService _share;

    public PublicShareController(AppDbContext db, IProfileService profiles, IPublicShareService share)
    {
        _db = db;
        _profiles = profiles;
        _share = share;
    }

    // GET /api/public/profiles/{userId}
    [HttpGet("profiles/{userId:guid}")]
    public async Task<IActionResult> GetProfile(Guid userId, CancellationToken ct)
    {
        var result = await _profiles.GetShowcaseAsync(userId, ViewerId(), IsPlatformAdmin(), ct);
        return result.Access switch
        {
            ProfileShowcaseAccess.Visible => Ok(new { data = result.Profile }),
            ProfileShowcaseAccess.LoginRequired => StatusCode(StatusCodes.Status401Unauthorized,
                new { reason = "login_required", error = "Entre na sua conta para ver este perfil." }),
            ProfileShowcaseAccess.SharedPatotasOnly => StatusCode(StatusCodes.Status403Forbidden,
                new { reason = "shared_patotas", error = "Este perfil é visível apenas para integrantes das mesmas patotas." }),
            ProfileShowcaseAccess.Private => StatusCode(StatusCodes.Status403Forbidden,
                new { reason = "private", error = "Este perfil é privado." }),
            _ => NotFound(new { reason = "not_found", error = "Perfil não encontrado." }),
        };
    }

    // GET /api/public/groups/{groupId}
    [HttpGet("groups/{groupId:guid}")]
    public async Task<IActionResult> GetGroup(Guid groupId, CancellationToken ct)
    {
        var isMember = ViewerId() is not null && await IsGroupMemberAsync(groupId, _db, ct);
        var result = await _share.GetGroupAsync(groupId, isMember, ct);
        return result is null
            ? NotFound(new { reason = "not_found", error = "Patota não encontrada." })
            : Ok(new { data = result });
    }

    // GET /api/public/share/{kind}/{id}   kind = match | mvp | championship | poll
    [HttpGet("share/{kind}/{id:guid}")]
    public async Task<IActionResult> Resolve(string kind, Guid id, CancellationToken ct)
    {
        if (!ShareKinds.Internal.Contains(kind))
            return NotFound(new { reason = "not_found", error = "Link inválido." });

        var groupId = await _share.FindOwnerGroupAsync(kind, id, ct);
        if (groupId is null)
            return NotFound(new { reason = "not_found", error = "Conteúdo não encontrado." });

        var access = ViewerId() is null
            ? ShareAccess.LoginRequired
            : await IsGroupMemberAsync(groupId.Value, _db, ct) ? ShareAccess.Member : ShareAccess.NotMember;

        return Ok(new { data = await _share.BuildTargetAsync(kind, id, groupId.Value, access, ct) });
    }

    /// <summary>
    /// Usuário logado e ativo. Conta inativa chega sem role (FirebaseIdentityMiddleware) e é
    /// tratada como visitante, igual ao que as rotas internas fazem com o [Authorize(Roles)].
    /// </summary>
    private Guid? ViewerId() =>
        User.IsInRole("User") || User.IsInRole("Admin") || User.IsInRole("GodMode")
            ? GetCurrentUserId()
            : null;

    private bool IsPlatformAdmin() => User.IsInRole("Admin") || User.IsInRole("GodMode");
}
