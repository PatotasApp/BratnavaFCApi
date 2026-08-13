using System.Security.Claims;
using BratnavaFC.Application.Abstractions;
using BratnavaFC.Application.Services;

namespace BratnavaFC.Api.Middleware;

/// <summary>
/// Traduz a identidade externa do token do Firebase para a identidade interna da aplicação e
/// injeta o resultado nas claims da request.
///
/// Roda entre <c>UseAuthentication</c> e <c>UseAuthorization</c> por dois motivos: precisa do
/// principal já montado, e precisa injetar a claim de role ANTES de os
/// <c>[Authorize(Roles = ...)]</c> serem avaliados.
///
/// A regra de negócio (resolver ou criar o usuário) vive no
/// <see cref="IUserProvisioningService"/>. Aqui só há orquestração.
///
/// Custo por request:
///   - anônima: uma checagem de flag, sai antes de tudo;
///   - token com internal_id + role: só leitura de claims, nenhum I/O e nenhum serviço
///     scoped resolvido;
///   - sem essas claims: um SELECT de uma linha por índice único.
///
/// O terceiro caso é um evento POR USUÁRIO, não um custo recorrente. As custom claims são
/// permanentes no registro do Firebase, e o SDK renova o ID token a cada ~55 min — a partir da
/// primeira renovação, todo token seguinte já as carrega. O front-end deve chamar
/// <c>getIdToken(true)</c> logo após o primeiro sign-in para encurtar essa janela a uma request.
/// </summary>
public sealed class FirebaseIdentityMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<FirebaseIdentityMiddleware> _logger;

    public FirebaseIdentityMiddleware(RequestDelegate next, ILogger<FirebaseIdentityMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.User?.Identity is not ClaimsIdentity identity || identity.IsAuthenticated is false)
        {
            await _next(context);
            return;
        }

        // "user_id" e "sub" carregam o mesmo valor no ID token do Firebase; o SDK do cliente
        // usa o primeiro, a especificação do JWT o segundo.
        var firebaseUid = identity.FindFirst("user_id")?.Value
                          ?? identity.FindFirst("sub")?.Value;

        if (string.IsNullOrWhiteSpace(firebaseUid))
        {
            await _next(context);
            return;
        }

        // CAMINHO RÁPIDO: o token já traz as custom claims gravadas no provisionamento, então
        // não há lookup nem resolução de serviço scoped. Só vale quando role também está
        // presente — sem ela a autorização falharia adiante.
        var internalIdClaim = identity.FindFirst(UserProvisioningService.InternalIdClaim)?.Value;
        var hasRole = identity.FindFirst(UserProvisioningService.RoleClaim) is not null;

        if (hasRole && Guid.TryParse(internalIdClaim, out var claimId))
        {
            ApplyInternalId(identity, claimId);

            await _next(context);
            return;
        }

        // CAMINHO LENTO: resolve no banco e cria o usuário se for o primeiro acesso. O serviço
        // é resolvido aqui, e não por parâmetro do InvokeAsync, porque resolvê-lo sempre
        // instanciaria um AppDbContext scoped inclusive nas requests que não consultam o banco.
        var provisioning = context.RequestServices.GetRequiredService<IUserProvisioningService>();

        var provisioned = await provisioning.ResolveOrCreateAsync(
            firebaseUid,
            context.User,
            context.RequestAborted);

        if (provisioned is null)
        {
            // Token válido sem identidade interna resolvível. Segue sem claims internas: os
            // endpoints com [Authorize(Roles = ...)] respondem 403.
            _logger.LogWarning(
                "[Identity] Não foi possível resolver a identidade interna do UID {FirebaseUid}.",
                firebaseUid);

            await _next(context);
            return;
        }

        ApplyInternalId(identity, provisioned.Id);

        // Usuário inativo não recebe role, e portanto não passa por nenhum
        // [Authorize(Roles = ...)]. É o que substitui a checagem de status que existia no
        // login antigo, já que agora não há login no backend.
        if (provisioned.IsActive && !hasRole)
        {
            identity.AddClaim(new Claim(
                UserProvisioningService.RoleClaim,
                provisioned.Role.ToString()));
        }

        await _next(context);
    }

    /// <summary>
    /// Grava o GUID interno em NameIdentifier e em "internal_id". O NameIdentifier é o que os
    /// controllers já leem; a claim nomeada existe para quem precisa ser explícito de que
    /// quer a identidade INTERNA, não o UID do Firebase que continua em "sub".
    /// </summary>
    private static void ApplyInternalId(ClaimsIdentity identity, Guid internalId)
    {
        var value = internalId.ToString();

        foreach (var stale in identity.FindAll(ClaimTypes.NameIdentifier).ToList())
            identity.RemoveClaim(stale);

        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, value));

        if (identity.FindFirst(UserProvisioningService.InternalIdClaim) is null)
            identity.AddClaim(new Claim(UserProvisioningService.InternalIdClaim, value));
    }
}
