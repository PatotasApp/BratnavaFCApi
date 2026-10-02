using BratnavaFC.Application.Abstractions;
using BratnavaFC.Application.Services;
using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos.Users;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;
using BratnavaFC.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace BranavaFC.Tests;

/// <summary>
/// Cobre GET/PUT /api/users/me — o único caminho pelo qual o usuário comum lê e edita o
/// próprio perfil depois da migração para o Firebase.
///
/// Nos testes de sucesso o usuário fica com FirebaseUid nulo de propósito: é essa condição
/// que faz o serviço pular a chamada ao Admin SDK (indisponível aqui). Um usuário COM
/// FirebaseUid exercita o caminho oposto — a falha do Firebase e o rollback do SQL.
/// </summary>
public class UserServiceMeTests
{
    private static UserService Sut(AppDbContext db)
        => new(db, Mock.Of<IRepositoryBase<UserEntity>>(), Mock.Of<ILogger<UserService>>(), TestImageStorage.Create(), GroupServiceTestDoubles.GroupServiceStub());

    /// <summary>
    /// A topbar renderiza o avatar do usuário logado a partir do /me. Antes ela lia de
    /// MyPlayerDto, cuja query tem a tabela Players como raiz — usuário sem patota não tinha
    /// linha nenhuma, então a foto nunca chegava à tela mesmo estando gravada.
    /// </summary>
    [Fact]
    public async Task GetMeAsync_ReturnsThePhotoUrlComposedFromTheStoredKey()
    {
        await using var db = DbContextFactory.Create(nameof(GetMeAsync_ReturnsThePhotoUrlComposedFromTheStoredKey));
        var user = User("andrei", "andrei@test.com");
        user.SetProfilePhoto("avatars/u/foto.jpg");
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var result = await Sut(db).GetMeAsync(user.Id, null, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Data!.PhotoUrl.Should().Be($"{TestImageStorage.BaseUrl}/avatars/u/foto.jpg");
    }

    /// <summary>Usuário sem foto: nulo, não string vazia — o client decide cair nas iniciais.</summary>
    [Fact]
    public async Task GetMeAsync_WithoutPhoto_ReturnsNullPhotoUrl()
    {
        await using var db = DbContextFactory.Create(nameof(GetMeAsync_WithoutPhoto_ReturnsNullPhotoUrl));
        var user = User("semfoto", "semfoto@test.com");
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var result = await Sut(db).GetMeAsync(user.Id, null, CancellationToken.None);

        result.Data!.PhotoUrl.Should().BeNull();
    }

    private static UserEntity User(
        string userName,
        string email,
        string? firebaseUid = null,
        UserRole role = UserRole.User)
    {
        var user = new UserEntity(userName, "Primeiro", "Ultimo", email, "hash", "11999999999", null, role);

        if (firebaseUid is not null) user.SetFirebaseUid(firebaseUid);

        return user;
    }

    // ─── GetMeAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetMeAsync_WhenUserNotFound_ReturnsNotFound()
    {
        await using var db = DbContextFactory.Create(nameof(GetMeAsync_WhenUserNotFound_ReturnsNotFound));

        var result = await Sut(db).GetMeAsync(Guid.NewGuid(), "qualquer@test.com", CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task GetMeAsync_ReturnsInternalProfile()
    {
        await using var db = DbContextFactory.Create(nameof(GetMeAsync_ReturnsInternalProfile));

        var user = User("luis", "luis@test.com", role: UserRole.Admin);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var result = await Sut(db).GetMeAsync(user.Id, "luis@test.com", CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Data!.Id.Should().Be(user.Id, "é este GUID que o front usa como identidade, não o sub do token");
        result.Data.Email.Should().Be("luis@test.com");
        result.Data.UserName.Should().Be("luis");
        result.Data.Role.Should().Be(UserRole.Admin);
    }

    [Fact]
    public async Task GetMeAsync_WhenTokenEmailDiffers_SyncsEmail()
    {
        await using var db = DbContextFactory.Create(nameof(GetMeAsync_WhenTokenEmailDiffers_SyncsEmail));

        var user = User("luis", "antigo@test.com");
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var result = await Sut(db).GetMeAsync(user.Id, "novo@test.com", CancellationToken.None);

        // Precisa acontecer aqui: o caminho rápido do middleware não chama o provisionamento
        // depois que o token carrega as custom claims, então a sincronização de lá para de
        // rodar. Sem isto, trocar de e-mail no Firebase nunca refletiria no banco.
        result.Data!.Email.Should().Be("novo@test.com");
        (await db.Users.SingleAsync()).Email.Should().Be("novo@test.com");
    }

    [Fact]
    public async Task GetMeAsync_WhenTokenEmailBelongsToAnotherUser_KeepsCurrentEmail()
    {
        await using var db = DbContextFactory.Create(nameof(GetMeAsync_WhenTokenEmailBelongsToAnotherUser_KeepsCurrentEmail));

        var user = User("luis", "antigo@test.com");
        db.Users.AddRange(user, User("outro", "ocupado@test.com"));
        await db.SaveChangesAsync();

        var result = await Sut(db).GetMeAsync(user.Id, "ocupado@test.com", CancellationToken.None);

        // Sincronizar violaria o índice único e derrubaria o /me inteiro — melhor devolver o
        // perfil com o e-mail antigo e registrar o conflito no log.
        result.Success.Should().BeTrue();
        result.Data!.Email.Should().Be("antigo@test.com");
    }

    [Fact]
    public async Task GetMeAsync_WhenTokenEmailAbsent_KeepsCurrentEmail()
    {
        await using var db = DbContextFactory.Create(nameof(GetMeAsync_WhenTokenEmailAbsent_KeepsCurrentEmail));

        var user = User("luis", "luis@test.com");
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var result = await Sut(db).GetMeAsync(user.Id, null, CancellationToken.None);

        result.Data!.Email.Should().Be("luis@test.com");
    }

    [Fact]
    public async Task GetMeAsync_WhenTokenEmailDiffersOnlyByCasing_DoesNotRewrite()
    {
        await using var db = DbContextFactory.Create(nameof(GetMeAsync_WhenTokenEmailDiffersOnlyByCasing_DoesNotRewrite));

        var user = User("luis", "luis@test.com");
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var result = await Sut(db).GetMeAsync(user.Id, "LUIS@TEST.COM", CancellationToken.None);

        result.Data!.Email.Should().Be("luis@test.com");
    }

    // ─── UpdateMeAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateMeAsync_WhenUserNotFound_ReturnsNotFound()
    {
        await using var db = DbContextFactory.Create(nameof(UpdateMeAsync_WhenUserNotFound_ReturnsNotFound));

        var result = await Sut(db).UpdateMeAsync(Guid.NewGuid(), new UpdateMeDto { FirstName = "Novo" }, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.NotFound);
    }

    [Fact]
    public async Task UpdateMeAsync_UpdatesProfileFields()
    {
        await using var db = DbContextFactory.Create(nameof(UpdateMeAsync_UpdatesProfileFields));

        var user = User("luis", "luis@test.com");
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var result = await Sut(db).UpdateMeAsync(
            user.Id,
            new UpdateMeDto { UserName = "luisinho", FirstName = "Luis", LastName = "Mello", Phone = "11888888888" },
            CancellationToken.None);

        result.Success.Should().BeTrue();

        var salvo = await db.Users.SingleAsync();
        salvo.UserName.Should().Be("luisinho");
        salvo.FirstName.Should().Be("Luis");
        salvo.LastName.Should().Be("Mello");
        salvo.Phone.Should().Be("11888888888");
    }

    [Fact]
    public async Task UpdateMeAsync_WhenFieldsOmitted_KeepsCurrentValues()
    {
        await using var db = DbContextFactory.Create(nameof(UpdateMeAsync_WhenFieldsOmitted_KeepsCurrentValues));

        var user = User("luis", "luis@test.com");
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var result = await Sut(db).UpdateMeAsync(user.Id, new UpdateMeDto { FirstName = "Luis" }, CancellationToken.None);

        result.Success.Should().BeTrue();

        var salvo = await db.Users.SingleAsync();
        salvo.FirstName.Should().Be("Luis");
        salvo.LastName.Should().Be("Ultimo", "campo ausente não deve apagar o valor existente");
        salvo.UserName.Should().Be("luis");
    }

    [Fact]
    public async Task UpdateMeAsync_UpdatesBirthDate()
    {
        await using var db = DbContextFactory.Create(nameof(UpdateMeAsync_UpdatesBirthDate));

        var user = User("luis", "luis@test.com");
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var birthDate = new DateTimeOffset(1990, 5, 14, 0, 0, 0, TimeSpan.Zero);

        var result = await Sut(db).UpdateMeAsync(
            user.Id,
            new UpdateMeDto { BirthDate = birthDate },
            CancellationToken.None);

        result.Success.Should().BeTrue();

        (await db.Users.SingleAsync()).BirthDate.Should().Be(birthDate);
    }

    [Fact]
    public async Task UpdateMeAsync_WhenBirthDateOmitted_KeepsCurrentValue()
    {
        await using var db = DbContextFactory.Create(nameof(UpdateMeAsync_WhenBirthDateOmitted_KeepsCurrentValue));

        var birthDate = new DateTimeOffset(1990, 5, 14, 0, 0, 0, TimeSpan.Zero);

        var user = User("luis", "luis@test.com");
        user.UpdateProfile("Primeiro", "Ultimo", birthDate, "11999999999");
        db.Users.Add(user);
        await db.SaveChangesAsync();

        // Clientes editam o próprio perfil sem mandar a data. Se ausente significasse "limpar",
        // cada salvamento apagaria o aniversário — que é o que alimenta a tela de
        // aniversariantes.
        var result = await Sut(db).UpdateMeAsync(
            user.Id,
            new UpdateMeDto { FirstName = "Luis" },
            CancellationToken.None);

        result.Success.Should().BeTrue();

        (await db.Users.SingleAsync()).BirthDate.Should().Be(birthDate);
    }

    [Fact]
    public async Task UpdateMeAsync_NeverChangesEmail()
    {
        await using var db = DbContextFactory.Create(nameof(UpdateMeAsync_NeverChangesEmail));

        var user = User("luis", "luis@test.com");
        db.Users.Add(user);
        await db.SaveChangesAsync();

        // O DTO não tem campo de e-mail de propósito: trocá-lo por aqui abriria caminho para
        // account takeover, e o valor é espelho do Firebase.
        await Sut(db).UpdateMeAsync(user.Id, new UpdateMeDto { FirstName = "Luis" }, CancellationToken.None);

        (await db.Users.SingleAsync()).Email.Should().Be("luis@test.com");
    }

    [Fact]
    public async Task UpdateMeAsync_WhenUserNameTakenByAnother_ReturnsFailure()
    {
        await using var db = DbContextFactory.Create(nameof(UpdateMeAsync_WhenUserNameTakenByAnother_ReturnsFailure));

        var user = User("luis", "luis@test.com");
        db.Users.AddRange(user, User("ocupado", "outro@test.com"));
        await db.SaveChangesAsync();

        var result = await Sut(db).UpdateMeAsync(user.Id, new UpdateMeDto { UserName = "  OCUPADO  " }, CancellationToken.None);

        // A unicidade do UserName é garantida na aplicação: a coluna não tem índice único.
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("OCUPADO");
        (await db.Users.SingleAsync(u => u.Id == user.Id)).UserName.Should().Be("luis");
    }

    [Fact]
    public async Task UpdateMeAsync_WhenKeepingOwnUserName_Succeeds()
    {
        await using var db = DbContextFactory.Create(nameof(UpdateMeAsync_WhenKeepingOwnUserName_Succeeds));

        var user = User("luis", "luis@test.com");
        db.Users.Add(user);
        await db.SaveChangesAsync();

        // A checagem de duplicidade precisa ignorar a própria linha, senão ninguém consegue
        // salvar o perfil sem trocar de username.
        var result = await Sut(db).UpdateMeAsync(user.Id, new UpdateMeDto { UserName = "luis", FirstName = "Luis" }, CancellationToken.None);

        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateMeAsync_WhenFirebaseSyncFails_RollsBackSql()
    {
        await using var db = DbContextFactory.Create(nameof(UpdateMeAsync_WhenFirebaseSyncFails_RollsBackSql));

        // Com FirebaseUid preenchido o serviço tenta sincronizar o DisplayName no Admin SDK.
        // Sem FirebaseApp inicializado a chamada lança, exercitando o rollback.
        var user = User("luis", "luis@test.com", firebaseUid: "uid-externo");
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var result = await Sut(db).UpdateMeAsync(
            user.Id,
            new UpdateMeDto { UserName = "luisinho", FirstName = "Novo", LastName = "Nome" },
            CancellationToken.None);

        result.Success.Should().BeFalse("sem sincronizar o Firebase, o SQL não pode ficar adiantado");

        var salvo = await db.Users.SingleAsync();
        salvo.UserName.Should().Be("luis");
        salvo.FirstName.Should().Be("Primeiro");
        salvo.LastName.Should().Be("Ultimo");
    }
}
