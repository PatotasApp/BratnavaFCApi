using System.Security.Claims;
using BratnavaFC.Application.Services;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;
using BratnavaFC.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace BranavaFC.Tests;

/// <summary>
/// Cobre a tradução entre a identidade EXTERNA (UID do Firebase) e a INTERNA (GUID da tabela
/// Users), incluindo a guarda que impede alguém de herdar a linha — e o histórico — de outra
/// pessoa apenas por saber o e-mail dela.
///
/// O serviço chama FirebaseAuth.DefaultInstance para gravar custom claims, mas a chamada é
/// best-effort dentro de um try/catch: sem FirebaseApp inicializado ela lança e é engolida
/// com um warning. Por isso o serviço é testável sem Firebase — e é justamente esse contrato
/// ("falhar ao gravar claim não derruba o provisionamento") que os testes exercitam de graça.
/// </summary>
public class UserProvisioningServiceTests
{
    private static UserProvisioningService Sut(AppDbContext db)
        => new(db, Mock.Of<ILogger<UserProvisioningService>>());

    private static ClaimsPrincipal Principal(string? email, bool emailVerified = true, string? name = null)
    {
        var claims = new List<Claim>
        {
            // O Firebase envia booleano JSON; o handler do JWT materializa como "true"/"false".
            new("email_verified", emailVerified ? "true" : "false"),
        };

        if (email is not null) claims.Add(new Claim("email", email));
        if (name is not null) claims.Add(new Claim("name", name));

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
    }

    private static UserEntity User(
        string userName,
        string email,
        string? firebaseUid = null,
        Status status = Status.Active,
        UserRole role = UserRole.User)
    {
        var user = new UserEntity(userName, "Primeiro", "Ultimo", email, "hash", null, null, role);

        if (firebaseUid is not null) user.SetFirebaseUid(firebaseUid);
        if (status == Status.Inactive) user.Inactivate();

        return user;
    }

    // ─── Resolução por FirebaseUid ───────────────────────────────────────────

    [Fact]
    public async Task ResolveOrCreateAsync_WhenFirebaseUidKnown_ReturnsInternalIdentity()
    {
        await using var db = DbContextFactory.Create(nameof(ResolveOrCreateAsync_WhenFirebaseUidKnown_ReturnsInternalIdentity));

        var user = User("luis", "luis@test.com", firebaseUid: "uid-externo", role: UserRole.Admin);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var result = await Sut(db).ResolveOrCreateAsync("uid-externo", Principal("luis@test.com"), CancellationToken.None);

        result.Should().NotBeNull();
        result!.Id.Should().Be(user.Id, "a identidade interna é o GUID da linha, não o UID do Firebase");
        result.Role.Should().Be(UserRole.Admin);
        result.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task ResolveOrCreateAsync_WhenUserInactive_ReportsInactive()
    {
        await using var db = DbContextFactory.Create(nameof(ResolveOrCreateAsync_WhenUserInactive_ReportsInactive));

        db.Users.Add(User("luis", "luis@test.com", firebaseUid: "uid-externo", status: Status.Inactive));
        await db.SaveChangesAsync();

        var result = await Sut(db).ResolveOrCreateAsync("uid-externo", Principal("luis@test.com"), CancellationToken.None);

        // O middleware usa isto para NÃO injetar a role — é o que substitui a checagem de
        // status que existia no login antigo, já que não há mais login no backend.
        result!.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task ResolveOrCreateAsync_WhenTokenEmailChanged_SyncsToDatabase()
    {
        await using var db = DbContextFactory.Create(nameof(ResolveOrCreateAsync_WhenTokenEmailChanged_SyncsToDatabase));

        var user = User("luis", "antigo@test.com", firebaseUid: "uid-externo");
        db.Users.Add(user);
        await db.SaveChangesAsync();

        await Sut(db).ResolveOrCreateAsync("uid-externo", Principal("novo@test.com"), CancellationToken.None);

        user.Email.Should().Be("novo@test.com", "o Firebase é a fonte da verdade do e-mail");
    }

    [Fact]
    public async Task ResolveOrCreateAsync_WhenTokenEmailBelongsToAnotherRow_KeepsCurrentEmail()
    {
        await using var db = DbContextFactory.Create(nameof(ResolveOrCreateAsync_WhenTokenEmailBelongsToAnotherRow_KeepsCurrentEmail));

        var user = User("luis", "antigo@test.com", firebaseUid: "uid-externo");
        db.Users.AddRange(user, User("outro", "ocupado@test.com"));
        await db.SaveChangesAsync();

        await Sut(db).ResolveOrCreateAsync("uid-externo", Principal("ocupado@test.com"), CancellationToken.None);

        // Sincronizar violaria o índice único de Email e derrubaria a request inteira.
        user.Email.Should().Be("antigo@test.com");
    }

    // ─── Vínculo por e-mail (caminho da migração) ────────────────────────────

    [Fact]
    public async Task ResolveOrCreateAsync_WhenEmailMatchesUnlinkedRow_AndVerified_LinksExistingRow()
    {
        await using var db = DbContextFactory.Create(nameof(ResolveOrCreateAsync_WhenEmailMatchesUnlinkedRow_AndVerified_LinksExistingRow));

        var legado = User("luis", "luis@test.com");   // FirebaseUid nulo: ainda não migrou
        db.Users.Add(legado);
        await db.SaveChangesAsync();

        var result = await Sut(db).ResolveOrCreateAsync("uid-novo", Principal("luis@test.com", emailVerified: true), CancellationToken.None);

        result!.Id.Should().Be(legado.Id, "vincular preserva o histórico em vez de criar linha nova");
        legado.FirebaseUid.Should().Be("uid-novo");
        (await db.Users.CountAsync()).Should().Be(1, "nenhuma linha duplicada deve nascer");
    }

    [Fact]
    public async Task ResolveOrCreateAsync_WhenEmailMatchesUnlinkedRow_AndNotVerified_RefusesToLink()
    {
        await using var db = DbContextFactory.Create(nameof(ResolveOrCreateAsync_WhenEmailMatchesUnlinkedRow_AndNotVerified_RefusesToLink));

        var legado = User("luis", "luis@test.com");
        db.Users.Add(legado);
        await db.SaveChangesAsync();

        var result = await Sut(db).ResolveOrCreateAsync("uid-do-impostor", Principal("luis@test.com", emailVerified: false), CancellationToken.None);

        // Sem esta guarda, qualquer pessoa criaria conta com o e-mail de um usuário ainda não
        // migrado — cadastro por senha não exige verificação — e assumiria a linha dele,
        // com partidas, apostas e financeiro, na primeira request.
        result.Should().BeNull("herdar histórico exige provar posse do e-mail");
        legado.FirebaseUid.Should().BeNull("a linha da vítima não pode ser tocada");
    }

    [Fact]
    public async Task ResolveOrCreateAsync_WhenEmailAlreadyLinkedToAnotherUid_ReturnsNull()
    {
        await using var db = DbContextFactory.Create(nameof(ResolveOrCreateAsync_WhenEmailAlreadyLinkedToAnotherUid_ReturnsNull));

        db.Users.Add(User("luis", "luis@test.com", firebaseUid: "uid-do-dono"));
        await db.SaveChangesAsync();

        var result = await Sut(db).ResolveOrCreateAsync("uid-diferente", Principal("luis@test.com"), CancellationToken.None);

        result.Should().BeNull("vincular sobrescreveria o vínculo de outra conta");
    }

    // ─── Criação (usuário novo) ──────────────────────────────────────────────

    [Fact]
    public async Task ResolveOrCreateAsync_WhenNoRowExists_CreatesUser_EvenWithoutVerifiedEmail()
    {
        await using var db = DbContextFactory.Create(nameof(ResolveOrCreateAsync_WhenNoRowExists_CreatesUser_EvenWithoutVerifiedEmail));

        var result = await Sut(db).ResolveOrCreateAsync("uid-novo", Principal("novo@test.com", emailVerified: false), CancellationToken.None);

        // A guarda vale só para HERDAR linha existente. Quem não herda nada não precisa
        // provar nada — é o que mantém o cadastro novo sem fricção.
        result.Should().NotBeNull();

        var criado = await db.Users.SingleAsync();
        criado.FirebaseUid.Should().Be("uid-novo");
        criado.Email.Should().Be("novo@test.com");
        criado.Role.Should().Be(UserRole.User);
    }

    [Fact]
    public async Task ResolveOrCreateAsync_WhenTokenHasNoEmail_ReturnsNull()
    {
        await using var db = DbContextFactory.Create(nameof(ResolveOrCreateAsync_WhenTokenHasNoEmail_ReturnsNull));

        // Acontece em login por telefone ou anônimo: sem e-mail não há como vincular nem
        // criar, porque a coluna é obrigatória e é o único identificador natural.
        var result = await Sut(db).ResolveOrCreateAsync("uid-sem-email", Principal(email: null), CancellationToken.None);

        result.Should().BeNull();
        (await db.Users.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ResolveOrCreateAsync_WhenCreating_UsesNameClaimForProfile()
    {
        await using var db = DbContextFactory.Create(nameof(ResolveOrCreateAsync_WhenCreating_UsesNameClaimForProfile));

        await Sut(db).ResolveOrCreateAsync("uid-novo", Principal("novo@test.com", name: "Luis Carlos Mello"), CancellationToken.None);

        var criado = await db.Users.SingleAsync();
        criado.FirstName.Should().Be("Luis");
        criado.LastName.Should().Be("Carlos Mello", "o resto do nome vira sobrenome inteiro");
    }

    [Fact]
    public async Task ResolveOrCreateAsync_WhenCreatingWithoutNameClaim_FallsBackToEmail()
    {
        await using var db = DbContextFactory.Create(nameof(ResolveOrCreateAsync_WhenCreatingWithoutNameClaim_FallsBackToEmail));

        await Sut(db).ResolveOrCreateAsync("uid-novo", Principal("pedro@test.com", name: null), CancellationToken.None);

        // Documenta o fallback: é o que acontece quando o provedor não manda `name`. O
        // cadastro por e-mail e senha corrige isso com um PUT /me logo depois.
        var criado = await db.Users.SingleAsync();
        criado.FirstName.Should().Be("pedro");
        criado.LastName.Should().Be("-");
    }

    [Fact]
    public async Task ResolveOrCreateAsync_WhenCreating_DerivesUserNameFromEmail()
    {
        await using var db = DbContextFactory.Create(nameof(ResolveOrCreateAsync_WhenCreating_DerivesUserNameFromEmail));

        await Sut(db).ResolveOrCreateAsync("uid-novo", Principal("luis.mello@test.com"), CancellationToken.None);

        // Nenhum provedor social fornece username, e a coluna é obrigatória e única.
        (await db.Users.SingleAsync()).UserName.Should().Be("luis.mello");
    }

    [Fact]
    public async Task ResolveOrCreateAsync_WhenDerivedUserNameTaken_AppendsSuffix()
    {
        await using var db = DbContextFactory.Create(nameof(ResolveOrCreateAsync_WhenDerivedUserNameTaken_AppendsSuffix));

        db.Users.Add(User("luis", "outro-luis@test.com", firebaseUid: "uid-do-outro"));
        await db.SaveChangesAsync();

        await Sut(db).ResolveOrCreateAsync("uid-novo", Principal("luis@test.com"), CancellationToken.None);

        var criado = await db.Users.SingleAsync(u => u.FirebaseUid == "uid-novo");
        criado.UserName.Should().Be("luis2");
    }

    [Fact]
    public async Task ResolveOrCreateAsync_NormalizesEmailCasing()
    {
        await using var db = DbContextFactory.Create(nameof(ResolveOrCreateAsync_NormalizesEmailCasing));

        var legado = User("luis", "luis@test.com");
        db.Users.Add(legado);
        await db.SaveChangesAsync();

        var result = await Sut(db).ResolveOrCreateAsync("uid-novo", Principal("  LUIS@TEST.COM  "), CancellationToken.None);

        // Caixa e espaço não podem impedir o reencontro da linha na migração.
        result!.Id.Should().Be(legado.Id);
    }

    [Fact]
    public async Task ResolveOrCreateAsync_WhenFirebaseUidEmpty_ReturnsNull()
    {
        await using var db = DbContextFactory.Create(nameof(ResolveOrCreateAsync_WhenFirebaseUidEmpty_ReturnsNull));

        var result = await Sut(db).ResolveOrCreateAsync("  ", Principal("luis@test.com"), CancellationToken.None);

        result.Should().BeNull();
        (await db.Users.CountAsync()).Should().Be(0);
    }
}
