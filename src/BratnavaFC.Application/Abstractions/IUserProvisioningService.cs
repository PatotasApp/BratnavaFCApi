using System.Security.Claims;
using BratnavaFC.Domain.Entities;

namespace BratnavaFC.Application.Abstractions;

/// <summary>
/// Traduz a identidade EXTERNA (UID do Firebase, vindo do token) para a identidade INTERNA
/// (GUID da tabela Users, usado como FK em todo o resto). Criar o usuário no primeiro acesso
/// é regra de negócio e mora aqui — o middleware só orquestra.
/// </summary>
public interface IUserProvisioningService
{
    Task<ProvisionedUser?> ResolveOrCreateAsync(
        string firebaseUid,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken);
}

/// <summary>
/// Dados que o middleware precisa injetar nas claims da request. A Role vem daqui, e não do
/// token, porque a custom claim do Firebase só chega ao cliente depois de um refresh — sem
/// isso o usuário recém-provisionado tomaria 403 em todo endpoint autorizado.
/// </summary>
/// <param name="Id">GUID interno.</param>
/// <param name="Role">Role gravada no banco.</param>
/// <param name="IsActive">Falso bloqueia a injeção da role, o que resulta em 403.</param>
public sealed record ProvisionedUser(Guid Id, UserRole Role, bool IsActive);
