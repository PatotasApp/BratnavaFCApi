using System.Security.Claims;
using BratnavaFC.Application.Abstractions;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;
using BratnavaFC.Infrastructure.Data;
using FirebaseAdmin.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BratnavaFC.Application.Services;

public sealed class UserProvisioningService : IUserProvisioningService
{
    public const string InternalIdClaim = "internal_id";
    public const string RoleClaim = "role";

    private readonly AppDbContext _db;
    private readonly ILogger<UserProvisioningService> _logger;

    public UserProvisioningService(AppDbContext db, ILogger<UserProvisioningService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ProvisionedUser?> ResolveOrCreateAsync(
        string firebaseUid,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(firebaseUid))
            return null;

        var email = Normalize(principal.FindFirst(ClaimTypes.Email)?.Value
                              ?? principal.FindFirst("email")?.Value);

        // Caminho normal: o usuário já foi resolvido alguma vez.
        var user = await _db.Users
            .FirstOrDefaultAsync(x => x.FirebaseUid == firebaseUid, cancellationToken);

        if (user is not null)
        {
            await SyncEmailAsync(user, email, cancellationToken);

            // Chegar até aqui significa que o token NÃO trazia internal_id + role — senão o
            // middleware teria resolvido pelo caminho rápido. Reescrever as claims é o que
            // torna isto autocurável: se a escrita falhou numa tentativa anterior, ela é
            // refeita agora, e o usuário sai do caminho lento no próximo refresh de token.
            await TryWriteCustomClaimsAsync(firebaseUid, user, cancellationToken);

            return new ProvisionedUser(user.Id, user.Role, user.Status != Status.Inactive);
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            // Sem e-mail não há como vincular nem criar: a coluna é obrigatória e é o único
            // identificador natural que temos. Acontece com login por telefone ou anônimo.
            _logger.LogWarning(
                "[Provisioning] Token do Firebase UID {FirebaseUid} chegou sem e-mail.",
                firebaseUid);

            return null;
        }

        // Vínculo: existe linha com este e-mail que ainda não tem FirebaseUid — usuário
        // anterior à migração que entrou pelo SDK antes do script em lote rodar. Vincular
        // aqui é o que evita conta duplicada e órfã de FKs.
        var existingByEmail = await _db.Users
            .FirstOrDefaultAsync(x => x.Email.ToLower() == email, cancellationToken);

        if (existingByEmail is not null)
        {
            if (existingByEmail.FirebaseUid is not null)
            {
                // Mesmo e-mail apontando para outro UID. Vincular sobrescreveria o vínculo de
                // outra conta, então recusamos e deixamos rastro.
                _logger.LogError(
                    "[Provisioning] E-mail {Email} já está vinculado ao UID {Existing}; " +
                    "recusando o UID {Incoming}.",
                    email,
                    existingByEmail.FirebaseUid,
                    firebaseUid);

                return null;
            }

            // Herdar uma linha existente significa herdar TODO o histórico dela — partidas,
            // apostas, financeiro. Só liberamos isso para quem provou controlar o e-mail.
            //
            // Sem esta guarda, qualquer pessoa criaria uma conta com o e-mail de um usuário
            // que ainda não migrou (cadastro por senha não exige verificação) e, na primeira
            // request, assumiria a conta dele.
            //
            // Google e Apple sempre trazem email_verified = true, então o caminho de migração
            // por login social não é afetado. O ramo de CRIAR usuário novo, mais abaixo, também
            // não tem esta exigência: quem não herda nada não precisa provar nada.
            if (!IsEmailVerified(principal))
            {
                _logger.LogWarning(
                    "[Provisioning] UID {FirebaseUid} tentou vincular-se ao usuário {UserId} " +
                    "pelo e-mail {Email} sem tê-lo verificado. Vínculo recusado.",
                    firebaseUid,
                    existingByEmail.Id,
                    email);

                return null;
            }

            existingByEmail.SetFirebaseUid(firebaseUid);

            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "[Provisioning] Usuário {UserId} vinculado ao UID {FirebaseUid} do Firebase.",
                existingByEmail.Id,
                firebaseUid);

            await TryWriteCustomClaimsAsync(firebaseUid, existingByEmail, cancellationToken);

            return new ProvisionedUser(
                existingByEmail.Id,
                existingByEmail.Role,
                existingByEmail.Status != Status.Inactive);
        }

        return await CreateAsync(firebaseUid, principal, email, cancellationToken);
    }

    private async Task<ProvisionedUser> CreateAsync(
        string firebaseUid,
        ClaimsPrincipal principal,
        string email,
        CancellationToken cancellationToken)
    {
        var (firstName, lastName) = SplitName(
            principal.FindFirst("name")?.Value ?? principal.FindFirst(ClaimTypes.Name)?.Value,
            email);

        var user = new UserEntity(
            userName: await GenerateUniqueUserNameAsync(email, cancellationToken),
            firstName: firstName,
            lastName: lastName,
            email: email,
            passwordHashed: UserEntity.FirebaseManagedPassword,
            phone: null,
            birthDate: null,
            role: UserRole.User);

        user.SetFirebaseUid(firebaseUid);

        _db.Users.Add(user);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "[Provisioning] Usuário {UserId} criado no primeiro acesso a partir do UID {FirebaseUid}.",
            user.Id,
            firebaseUid);

        await TryWriteCustomClaimsAsync(firebaseUid, user, cancellationToken);

        return new ProvisionedUser(user.Id, user.Role, user.Status != Status.Inactive);
    }

    private async Task SyncEmailAsync(UserEntity user, string email, CancellationToken cancellationToken)
    {
        // Sincronização reativa: o e-mail é gerenciado no Firebase, então o token é a fonte
        // da verdade. Trocar de e-mail lá reflete aqui no acesso seguinte.
        if (string.IsNullOrWhiteSpace(email) || string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase))
            return;

        var emailTaken = await _db.Users
            .AnyAsync(x => x.Id != user.Id && x.Email.ToLower() == email, cancellationToken);

        if (emailTaken)
        {
            _logger.LogError(
                "[Provisioning] Não foi possível sincronizar o e-mail do usuário {UserId}: " +
                "{Email} já pertence a outra conta.",
                user.Id,
                email);

            return;
        }

        var previous = user.Email;
        user.SetEmail(email);

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "[Provisioning] E-mail do usuário {UserId} sincronizado de {Previous} para {Current}.",
            user.Id,
            previous,
            email);
    }

    /// <summary>
    /// Grava o GUID interno e a role como custom claims, para que as requests seguintes
    /// resolvam a identidade sem tocar o banco. Só passa a valer depois de o cliente renovar
    /// o token, então é otimização — o middleware nunca depende disso.
    /// </summary>
    private async Task TryWriteCustomClaimsAsync(
        string firebaseUid,
        UserEntity user,
        CancellationToken cancellationToken)
    {
        try
        {
            await FirebaseAuth.DefaultInstance.SetCustomUserClaimsAsync(
                firebaseUid,
                new Dictionary<string, object>
                {
                    [InternalIdClaim] = user.Id.ToString(),
                    [RoleClaim] = user.Role.ToString()
                },
                cancellationToken);
        }
        catch (Exception ex)
        {
            // Falhar aqui só custa um lookup por request; o caminho lento continua correto.
            _logger.LogWarning(
                ex,
                "[Provisioning] Não foi possível gravar as custom claims do usuário {UserId}.",
                user.Id);
        }
    }

    /// <summary>
    /// Lê a claim <c>email_verified</c> do ID token. O Firebase a envia como booleano JSON, que
    /// o handler materializa como a string "true"/"false" — daí o parse em vez de comparação
    /// direta. Ausência da claim é tratada como não verificado.
    /// </summary>
    private static bool IsEmailVerified(ClaimsPrincipal principal)
        => bool.TryParse(principal.FindFirst("email_verified")?.Value, out var verified) && verified;

    /// <summary>
    /// O UserName é obrigatório e único, e nenhum provedor social fornece um. Derivamos da
    /// parte local do e-mail e desempatamos com sufixo numérico; o usuário troca depois em
    /// PUT /api/users/me.
    /// </summary>
    private async Task<string> GenerateUniqueUserNameAsync(string email, CancellationToken cancellationToken)
    {
        var candidate = new string(email
            .Split('@')[0]
            .Where(c => char.IsLetterOrDigit(c) || c is '.' or '_' or '-')
            .ToArray());

        if (string.IsNullOrWhiteSpace(candidate))
            candidate = "usuario";

        var taken = await _db.Users
            .Where(x => x.UserName.ToLower().StartsWith(candidate.ToLower()))
            .Select(x => x.UserName.ToLower())
            .ToListAsync(cancellationToken);

        if (!taken.Contains(candidate.ToLower()))
            return candidate;

        for (var suffix = 2; ; suffix++)
        {
            var withSuffix = $"{candidate}{suffix}";

            if (!taken.Contains(withSuffix.ToLower()))
                return withSuffix;
        }
    }

    private static (string FirstName, string LastName) SplitName(string? name, string email)
    {
        // UserEntity exige FirstName e LastName. Provedor que não manda "name" cai no e-mail.
        var parts = (string.IsNullOrWhiteSpace(name) ? email.Split('@')[0] : name)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return parts.Length switch
        {
            0 => ("Usuário", "-"),
            1 => (parts[0], "-"),
            _ => (parts[0], string.Join(' ', parts.Skip(1)))
        };
    }

    private static string Normalize(string? value) => value?.Trim().ToLowerInvariant() ?? string.Empty;
}
