using BratnavaFC.Application.Abstractions;
using BratnavaFC.Application.Services;
using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos.Groups;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;
using BratnavaFC.Infrastructure.Data;
using BratnavaFC.Infrastructure.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace BranavaFC.Tests;

/// <summary>
/// Quem pode destruir uma patota, e por qual porta.
///
/// Sair e encerrar são atos diferentes. Misturá-los era o risco real: o fluxo de saída
/// oferecia "Excluir a patota" ao lado de "Promover alguém", e quando não havia ninguém
/// elegível para promover a exclusão virava a única saída oferecida — alguém que só queria
/// sair ficava a dois cliques de apagar o histórico de todo mundo.
///
/// A correção segue GitHub, Slack e Discord: sair exige transferir; encerrar é ação própria,
/// em outro lugar, protegida por fricção na interface e não por proibição — proibir criaria
/// patota que ninguém consegue encerrar.
/// </summary>
public class GroupServiceDeleteGuardTests
{
    // ── Pela porta da saída ───────────────────────────────────────────────────

    [Fact]
    public async Task CreatorLeave_WhenOtherAccountsRemain_RefusesToDeleteTheGroup()
    {
        var (sut, db) = Build(nameof(CreatorLeave_WhenOtherAccountsRemain_RefusesToDeleteTheGroup));
        var admin = await SeedUserAsync(db, "andrei");
        var grupo = await SeedGroupAsync(db, admin);
        await SeedMemberAsync(db, grupo, "mariana");

        var result = await sut.CreatorLeaveGroupAsync(
            grupo.Id, admin.Id, new CreatorLeaveGroupDto(DeleteGroup: true), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.Conflict);
        (await db.Groups.CountAsync(x => x.Id == grupo.Id)).Should().Be(1, "a patota não pode sumir");
    }

    /// <summary>Sem mais ninguém, encerrar é o desfecho natural — não há história alheia a destruir.</summary>
    [Fact]
    public async Task CreatorLeave_WhenNobodyElseRemains_EndsTheGroup()
    {
        var (sut, db) = Build(nameof(CreatorLeave_WhenNobodyElseRemains_EndsTheGroup));
        var admin = await SeedUserAsync(db, "andrei");
        var grupo = await SeedGroupAsync(db, admin);

        var result = await sut.CreatorLeaveGroupAsync(
            grupo.Id, admin.Id, new CreatorLeaveGroupDto(DeleteGroup: true), CancellationToken.None);

        result.Success.Should().BeTrue(result.Error);
        (await db.Groups.CountAsync(x => x.Id == grupo.Id)).Should().Be(0);
    }

    /// <summary>
    /// CreatedByUserId é anulável desde a exclusão de conta. Amarrar a permissão a ele deixava
    /// a patota de um criador excluído sem NINGUÉM capaz de transferir ou encerrar.
    /// </summary>
    [Fact]
    public async Task CreatorLeave_WhenGroupHasNoCreator_StillAllowsItsAdmin()
    {
        var (sut, db) = Build(nameof(CreatorLeave_WhenGroupHasNoCreator_StillAllowsItsAdmin));
        var admin = await SeedUserAsync(db, "andrei");
        var grupo = await SeedGroupAsync(db, admin, semCriador: true);

        var result = await sut.CreatorLeaveGroupAsync(
            grupo.Id, admin.Id, new CreatorLeaveGroupDto(DeleteGroup: true), CancellationToken.None);

        result.Success.Should().BeTrue(result.Error);
    }

    [Fact]
    public async Task CreatorLeave_WhenRequesterIsNotAnAdmin_IsForbidden()
    {
        var (sut, db) = Build(nameof(CreatorLeave_WhenRequesterIsNotAnAdmin_IsForbidden));
        var admin = await SeedUserAsync(db, "andrei");
        var grupo = await SeedGroupAsync(db, admin);
        var estranho = await SeedUserAsync(db, "mariana");

        var result = await sut.CreatorLeaveGroupAsync(
            grupo.Id, estranho.Id, new CreatorLeaveGroupDto(DeleteGroup: true), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.Forbidden);
    }

    // ── Pela porta própria ────────────────────────────────────────────────────

    /// <summary>
    /// Aqui a quantidade de gente NÃO trava: é ação deliberada, e proibir criaria patota-zumbi.
    /// Quem protege contra o acidente é a interface, exigindo digitar o nome.
    /// </summary>
    [Fact]
    public async Task DeleteByAdmin_EvenWithOtherMembers_EndsTheGroup()
    {
        var (sut, db) = Build(nameof(DeleteByAdmin_EvenWithOtherMembers_EndsTheGroup));
        var admin = await SeedUserAsync(db, "andrei");
        var grupo = await SeedGroupAsync(db, admin);
        await SeedMemberAsync(db, grupo, "mariana");

        var result = await sut.DeleteByAdminAsync(grupo.Id, admin.Id, CancellationToken.None);

        result.Success.Should().BeTrue(result.Error);
        (await db.Groups.CountAsync(x => x.Id == grupo.Id)).Should().Be(0);
    }

    [Fact]
    public async Task DeleteByAdmin_WhenRequesterOnlyPlays_IsForbidden()
    {
        var (sut, db) = Build(nameof(DeleteByAdmin_WhenRequesterOnlyPlays_IsForbidden));
        var admin = await SeedUserAsync(db, "andrei");
        var grupo = await SeedGroupAsync(db, admin);
        var jogadora = await SeedMemberAsync(db, grupo, "mariana");

        var result = await sut.DeleteByAdminAsync(grupo.Id, jogadora.Id, CancellationToken.None);

        result.Status.Should().Be(ResultStatus.Forbidden);
        (await db.Groups.CountAsync(x => x.Id == grupo.Id)).Should().Be(1);
    }

    /// <summary>Quem não administra a patota não precisa descobrir se ela existe.</summary>
    [Fact]
    public async Task DeleteByAdmin_WhenGroupDoesNotExist_IsNotFound()
    {
        var (sut, _) = Build(nameof(DeleteByAdmin_WhenGroupDoesNotExist_IsNotFound));

        var result = await sut.DeleteByAdminAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        result.Status.Should().Be(ResultStatus.NotFound);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static (IGroupService Sut, AppDbContext Db) Build(string dbName)
    {
        var db = DbContextFactory.Create(dbName);

        var sut = new GroupService(
            db,
            Mock.Of<ILogger<GroupService>>(),
            new RepositoryBase<GroupEntity>(db),
            Mock.Of<IPushService>(),
            TestImageStorage.Create());

        return (sut, db);
    }

    private static async Task<UserEntity> SeedUserAsync(AppDbContext db, string userName)
    {
        var user = new UserEntity(userName, "Nome", "Sobrenome", $"{userName}@test.com", "hash", null, null);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private static async Task<GroupEntity> SeedGroupAsync(AppDbContext db, UserEntity admin, bool semCriador = false)
    {
        var group = new GroupEntity("Pelada de Quinta", null, admin.Id);
        group.SetAdmins(new[] { admin.Id });

        db.Groups.Add(group);

        // Pelo change tracker: a propriedade tem setter privado e não existe método de
        // domínio para anulá-la — a exclusão de conta faz isso por UPDATE direto no banco.
        if (semCriador)
            db.Entry(group).Property(x => x.CreatedByUserId).CurrentValue = null;

        db.Players.Add(new PlayerEntity(admin.UserName, admin.Id, group.Id, 0, false, false, Status.Active));
        await db.SaveChangesAsync();
        return group;
    }

    private static async Task<UserEntity> SeedMemberAsync(AppDbContext db, GroupEntity group, string userName)
    {
        var user = await SeedUserAsync(db, userName);
        db.Players.Add(new PlayerEntity(userName, user.Id, group.Id, 0, false, false, Status.Active));
        await db.SaveChangesAsync();
        return user;
    }
}
