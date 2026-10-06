using BratnavaFC.Application.Abstractions;
using BratnavaFC.Application.Services;
using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;
using BratnavaFC.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace BranavaFC.Tests;

/// <summary>
/// Exclusão de conta pelo próprio usuário.
///
/// A regra central é a RECUSA: ninguém sai deixando uma patota sem nenhum administrador,
/// porque aí ninguém mais consegue administrá-la — adicionar admin exige já ser admin. É o
/// mesmo bloqueio que GitHub, Slack e Discord aplicam ao dono único de uma organização,
/// workspace ou servidor.
///
/// Ser CRIADOR não bloqueia: Groups.CreatedByUserId é anulável de propósito, e a patota
/// sobrevive sem dono, administrada por quem ficou.
/// </summary>
public sealed class UserServiceDeleteAccountTests
{
    // ── A recusa, e a garantia de que ela não destrói nada ────────────────────
    //
    // A recusa só existe para proteger QUEM FICA. Por isso todo teste desta seção coloca
    // outra conta na patota: sem ela não há ninguém para desamparar, e a saída é liberada
    // (a patota some junto, coberto em GroupServiceAbandonedTests).

    [Fact]
    public async Task DeleteMyAccountAsync_WhenUserIsTheOnlyAdminOfAGroup_Fails()
    {
        var (sut, db, user, _) = await BuildAsync(nameof(DeleteMyAccountAsync_WhenUserIsTheOnlyAdminOfAGroup_Fails));
        var group = await SeedGroupAsync(db, "Pelada de Quinta", adminIds: new[] { user.Id });
        await SeedOtherMemberAsync(db, group, "mariana");

        var result = await sut.DeleteMyAccountAsync(user.Id, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.Conflict);
        result.Data.Should().ContainSingle(b => b.GroupName == "Pelada de Quinta");
    }

    /// <summary>
    /// O teste que mais importa. Se a validação tiver um furo e a exclusão rodar mesmo
    /// assim, o dado não volta — então a recusa precisa provar que NADA foi tocado.
    /// </summary>
    [Fact]
    public async Task DeleteMyAccountAsync_WhenRefused_ChangesNothing()
    {
        var (sut, db, user, _) = await BuildAsync(nameof(DeleteMyAccountAsync_WhenRefused_ChangesNothing));
        var group = await SeedGroupAsync(db, "Pelada de Quinta", adminIds: new[] { user.Id });
        await SeedOtherMemberAsync(db, group, "mariana");
        var player = await SeedPlayerAsync(db, group.Id, user);

        await sut.DeleteMyAccountAsync(user.Id, CancellationToken.None);

        (await db.Users.CountAsync()).Should().Be(2, "nem o usuário nem a outra conta podem ter sido apagados");

        var reloaded = await db.Players.AsNoTracking().FirstAsync(p => p.Id == player.Id);
        reloaded.UserId.Should().Be(user.Id, "o jogador não pode ter sido desvinculado");
        reloaded.IsGuest.Should().BeFalse("o jogador não pode ter virado convidado");
    }

    [Fact]
    public async Task DeleteMyAccountAsync_ListsEveryGroupWhereUserIsTheOnlyAdmin()
    {
        var (sut, db, user, _) = await BuildAsync(nameof(DeleteMyAccountAsync_ListsEveryGroupWhereUserIsTheOnlyAdmin));
        await SeedOtherMemberAsync(db, await SeedGroupAsync(db, "Pelada de Quinta", adminIds: new[] { user.Id }), "mariana");
        await SeedOtherMemberAsync(db, await SeedGroupAsync(db, "Bratnava FC", adminIds: new[] { user.Id }), "caio");

        var result = await sut.DeleteMyAccountAsync(user.Id, CancellationToken.None);

        result.Data!.Select(b => b.GroupName).Should().BeEquivalentTo("Pelada de Quinta", "Bratnava FC");
    }

    /// <summary>Havendo outro admin, a patota continua administrável e nada impede a saída.</summary>
    [Fact(Skip = "Exercita a exclusão de fato, que agora roda via ExecuteUpdate/ExecuteDelete — o provider InMemory não traduz nenhuma das duas. Reativar ao migrar esta família para Postgres real (Testcontainers). Verificado à mão contra o banco em 02/10/2026.")]
    public async Task DeleteMyAccountAsync_WhenGroupHasAnotherAdmin_Succeeds()
    {
        var (sut, db, user, _) = await BuildAsync(nameof(DeleteMyAccountAsync_WhenGroupHasAnotherAdmin_Succeeds));
        var other = await SeedUserAsync(db, "outro", "outro@test.com");
        await SeedGroupAsync(db, "Pelada de Quinta", adminIds: new[] { user.Id, other.Id });

        var result = await sut.DeleteMyAccountAsync(user.Id, CancellationToken.None);

        result.Success.Should().BeTrue();
    }

    /// <summary>
    /// Ser criador NÃO bloqueia: a coluna é anulável de propósito e a patota segue viva sem
    /// dono. Bloquear aqui só criaria fricção, que é o que a política da Play pune.
    /// </summary>
    [Fact(Skip = "Exercita a exclusão de fato, que agora roda via ExecuteUpdate/ExecuteDelete — o provider InMemory não traduz nenhuma das duas. Reativar ao migrar esta família para Postgres real (Testcontainers). Verificado à mão contra o banco em 02/10/2026.")]
    public async Task DeleteMyAccountAsync_WhenUserIsCreatorButNotTheOnlyAdmin_ClearsOwnership()
    {
        var (sut, db, user, _) = await BuildAsync(nameof(DeleteMyAccountAsync_WhenUserIsCreatorButNotTheOnlyAdmin_ClearsOwnership));
        var other = await SeedUserAsync(db, "outro", "outro@test.com");
        var group = await SeedGroupAsync(db, "Pelada de Quinta", adminIds: new[] { user.Id, other.Id }, creatorId: user.Id);

        var result = await sut.DeleteMyAccountAsync(user.Id, CancellationToken.None);

        result.Success.Should().BeTrue();
        (await db.Groups.AsNoTracking().FirstAsync(g => g.Id == group.Id))
            .CreatedByUserId.Should().BeNull("a patota sobrevive sem dono");
    }

    // ── O que a exclusão faz de fato ──────────────────────────────────────────

    [Fact(Skip = "Exercita a exclusão de fato, que agora roda via ExecuteUpdate/ExecuteDelete — o provider InMemory não traduz nenhuma das duas. Reativar ao migrar esta família para Postgres real (Testcontainers). Verificado à mão contra o banco em 02/10/2026.")]
    public async Task DeleteMyAccountAsync_TurnsPlayersIntoGuests()
    {
        var (sut, db, user, _) = await BuildAsync(nameof(DeleteMyAccountAsync_TurnsPlayersIntoGuests));
        var group = await SeedGroupAsync(db, "Pelada de Quinta");
        var player = await SeedPlayerAsync(db, group.Id, user);

        await sut.DeleteMyAccountAsync(user.Id, CancellationToken.None);

        var reloaded = await db.Players.AsNoTracking().FirstAsync(p => p.Id == player.Id);
        reloaded.UserId.Should().BeNull();
        reloaded.IsGuest.Should().BeTrue();
        reloaded.Name.Should().Be("Andrei", "o histórico da patota depende do nome do jogador");
    }

    [Fact(Skip = "Exercita a exclusão de fato, que agora roda via ExecuteUpdate/ExecuteDelete — o provider InMemory não traduz nenhuma das duas. Reativar ao migrar esta família para Postgres real (Testcontainers). Verificado à mão contra o banco em 02/10/2026.")]
    public async Task DeleteMyAccountAsync_RemovesTheUser()
    {
        var (sut, db, user, _) = await BuildAsync(nameof(DeleteMyAccountAsync_RemovesTheUser));

        await sut.DeleteMyAccountAsync(user.Id, CancellationToken.None);

        (await db.Users.AnyAsync(u => u.Id == user.Id)).Should().BeFalse();
    }

    [Fact(Skip = "Exercita a exclusão de fato, que agora roda via ExecuteUpdate/ExecuteDelete — o provider InMemory não traduz nenhuma das duas. Reativar ao migrar esta família para Postgres real (Testcontainers). Verificado à mão contra o banco em 02/10/2026.")]
    public async Task DeleteMyAccountAsync_DeletesTheAvatarFromStorage()
    {
        var (sut, db, user, storage) = await BuildAsync(nameof(DeleteMyAccountAsync_DeletesTheAvatarFromStorage));
        user.SetProfilePhoto("avatars/u/foto.jpg");
        await db.SaveChangesAsync();

        await sut.DeleteMyAccountAsync(user.Id, CancellationToken.None);

        storage.Verify(x => x.DeleteAsync("avatars/u/foto.jpg", It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Objeto órfão no bucket custa alguns KB. Derrubar a exclusão que a pessoa pediu custa
    /// a exclusão inteira — e a política exige que ela funcione.
    /// </summary>
    [Fact(Skip = "Exercita a exclusão de fato, que agora roda via ExecuteUpdate/ExecuteDelete — o provider InMemory não traduz nenhuma das duas. Reativar ao migrar esta família para Postgres real (Testcontainers). Verificado à mão contra o banco em 02/10/2026.")]
    public async Task DeleteMyAccountAsync_SucceedsEvenWhenStorageDeleteFails()
    {
        var (sut, db, user, storage) = await BuildAsync(nameof(DeleteMyAccountAsync_SucceedsEvenWhenStorageDeleteFails));
        user.SetProfilePhoto("avatars/u/foto.jpg");
        await db.SaveChangesAsync();
        storage.Setup(x => x.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .ThrowsAsync(new InvalidOperationException("R2 fora do ar"));

        var result = await sut.DeleteMyAccountAsync(user.Id, CancellationToken.None);

        result.Success.Should().BeTrue();
    }

    /// <summary>
    /// ExtraCharges.CreatedByAdminId era a única coluna de usuário que a exclusão não
    /// limpava: NOT NULL, sem chave estrangeira e sem ninguém lendo. Uma varredura do
    /// schema contra o banco real a flagrou apontando para um usuário inexistente — a
    /// suíte não pegava porque o provider InMemory não tem schema nem FK.
    /// </summary>
    [Fact(Skip = "Exercita a exclusão de fato, que agora roda via ExecuteUpdate/ExecuteDelete — o provider InMemory não traduz nenhuma das duas. Reativar ao migrar esta família para Postgres real (Testcontainers). Verificado à mão contra o banco em 02/10/2026.")]
    public async Task DeleteMyAccountAsync_ClearsAuthorshipOfExtraCharges()
    {
        var (sut, db, user, _) = await BuildAsync(nameof(DeleteMyAccountAsync_ClearsAuthorshipOfExtraCharges));
        var group = await SeedGroupAsync(db, "Pelada de Quinta");
        var charge = new ExtraChargeEntity(group.Id, "Rateio do juiz", null, 20m, null, user.Id);
        db.ExtraCharges.Add(charge);
        await db.SaveChangesAsync();

        await sut.DeleteMyAccountAsync(user.Id, CancellationToken.None);

        var reloaded = await db.ExtraCharges.AsNoTracking().FirstAsync(x => x.Id == charge.Id);
        reloaded.CreatedByAdminId.Should().BeNull();
        reloaded.Name.Should().Be("Rateio do juiz", "a cobrança continua valendo para a patota");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static async Task<(IUserService Sut, AppDbContext Db, UserEntity User, Mock<IImageStorageService> Storage)>
        BuildAsync(string dbName)
    {
        var db = DbContextFactory.Create(dbName);
        var user = await SeedUserAsync(db, "andrei", "andrei@test.com");

        var repo = new Mock<IRepositoryBase<UserEntity>>();
        repo.Setup(x => x.GetByIdIncludingInactiveAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        var storage = new Mock<IImageStorageService>();
        var sut = new UserService(db, repo.Object, Mock.Of<ILogger<UserService>>(), storage.Object, GroupServiceTestDoubles.GroupServiceStub());

        return (sut, db, user, storage);
    }

    private static async Task<UserEntity> SeedUserAsync(AppDbContext db, string userName, string email)
    {
        var user = new UserEntity(userName, "Andrei", "Salvador", email, "hash", null, null);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private static async Task<GroupEntity> SeedGroupAsync(
        AppDbContext db, string name, Guid[]? adminIds = null, Guid? creatorId = null)
    {
        var group = new GroupEntity(name, null, creatorId ?? Guid.NewGuid());
        if (adminIds is not null) group.SetAdmins(adminIds);

        db.Groups.Add(group);
        await db.SaveChangesAsync();
        return group;
    }

    /// <summary>
    /// Outra conta na patota, como jogador comum. É ela que torna a recusa necessária: sem
    /// ninguém para ficar desamparado, a saída é liberada.
    /// </summary>
    private static async Task SeedOtherMemberAsync(AppDbContext db, GroupEntity group, string userName)
    {
        var other = await SeedUserAsync(db, userName, $"{userName}@test.com");
        db.Players.Add(new PlayerEntity(userName, other.Id, group.Id, 0, false, false, Status.Active));
        await db.SaveChangesAsync();
    }

    private static async Task<PlayerEntity> SeedPlayerAsync(AppDbContext db, Guid groupId, UserEntity user)
    {
        var player = new PlayerEntity("Andrei", user.Id, groupId, 0, false, false, Status.Active);
        db.Players.Add(player);
        await db.SaveChangesAsync();
        return player;
    }
}
