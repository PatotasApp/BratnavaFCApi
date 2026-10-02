using BratnavaFC.Application.Abstractions;
using BratnavaFC.Application.Services;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;
using BratnavaFC.Infrastructure.Data;
using BratnavaFC.Infrastructure.Repositories;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace BranavaFC.Tests;

/// <summary>
/// Quando uma patota fica sem ninguém.
///
/// Dois caminhos levam a isso — sair da patota e excluir a conta — e os dois perguntam o
/// mesmo aqui. A regra olha para CONTAS, não para jogadores: convidado é nome no histórico,
/// não tem login e não administra nada. Uma patota só de convidados está tão abandonada
/// quanto uma vazia, porque adicionar admin exige já ser admin e ninguém mais consegue.
///
/// Esta família roda no InMemory porque é só consulta. A exclusão em si (DeleteManyAsync)
/// depende da ordem das FKs RESTRICT e só é verificável contra Postgres de verdade.
/// </summary>
public class GroupServiceAbandonedTests
{
    [Fact]
    public async Task FindAbandonedByAsync_WhenUserIsTheOnlyAccount_ReturnsTheGroup()
    {
        var (sut, db) = Build(nameof(FindAbandonedByAsync_WhenUserIsTheOnlyAccount_ReturnsTheGroup));
        var dono = await SeedUserAsync(db, "andrei");
        var grupo = await SeedGroupAsync(db, "Pelada de Quinta", dono);

        var result = await sut.FindAbandonedByAsync(dono.Id, CancellationToken.None);

        result.Should().ContainSingle().Which.Should().Be(grupo.Id);
    }

    /// <summary>
    /// O caso que motivou tudo: a pessoa cria a patota, fica sozinha nela e sai. Antes a
    /// patota continuava no banco sem admin e sem conta, e no app seguia aparecendo para
    /// quem acabou de sair.
    /// </summary>
    [Fact]
    public async Task FindAbandonedByAsync_WhenOnlyGuestsRemain_ReturnsTheGroup()
    {
        var (sut, db) = Build(nameof(FindAbandonedByAsync_WhenOnlyGuestsRemain_ReturnsTheGroup));
        var dono = await SeedUserAsync(db, "andrei");
        var grupo = await SeedGroupAsync(db, "Pelada de Quinta", dono);

        db.Players.Add(new PlayerEntity("Primo do Caio", null, grupo.Id, 0, false, true, Status.Active));
        await db.SaveChangesAsync();

        var result = await sut.FindAbandonedByAsync(dono.Id, CancellationToken.None);

        result.Should().ContainSingle("convidado não administra nada: a patota continua abandonada");
    }

    /// <summary>
    /// A trava que não pode cair. Se outra CONTA está na patota, apagá-la destruiria dado de
    /// gente real — e é por isso que a exclusão de conta recusa em vez de seguir em frente.
    /// </summary>
    [Fact]
    public async Task FindAbandonedByAsync_WhenAnotherAccountIsAPlayer_ReturnsNothing()
    {
        var (sut, db) = Build(nameof(FindAbandonedByAsync_WhenAnotherAccountIsAPlayer_ReturnsNothing));
        var dono = await SeedUserAsync(db, "andrei");
        var grupo = await SeedGroupAsync(db, "Pelada de Quinta", dono);
        var outra = await SeedUserAsync(db, "mariana");

        db.Players.Add(new PlayerEntity("Mariana", outra.Id, grupo.Id, 0, false, false, Status.Active));
        await db.SaveChangesAsync();

        var result = await sut.FindAbandonedByAsync(dono.Id, CancellationToken.None);

        result.Should().BeEmpty("a patota segue viva para quem ficou");
    }

    [Fact]
    public async Task FindAbandonedByAsync_WhenThereIsAnotherAdmin_ReturnsNothing()
    {
        var (sut, db) = Build(nameof(FindAbandonedByAsync_WhenThereIsAnotherAdmin_ReturnsNothing));
        var dono = await SeedUserAsync(db, "andrei");
        var outro = await SeedUserAsync(db, "mariana");
        await SeedGroupAsync(db, "Pelada de Quinta", dono, outro);

        var result = await sut.FindAbandonedByAsync(dono.Id, CancellationToken.None);

        result.Should().BeEmpty();
    }

    /// <summary>Patota de outra gente não pode entrar na lista de ninguém.</summary>
    [Fact]
    public async Task FindAbandonedByAsync_IgnoresGroupsTheUserHasNothingToDoWith()
    {
        var (sut, db) = Build(nameof(FindAbandonedByAsync_IgnoresGroupsTheUserHasNothingToDoWith));
        var estranho = await SeedUserAsync(db, "andrei");
        var dona = await SeedUserAsync(db, "mariana");
        await SeedGroupAsync(db, "Patota da Mariana", dona);

        var result = await sut.FindAbandonedByAsync(estranho.Id, CancellationToken.None);

        result.Should().BeEmpty();
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

    private static async Task<GroupEntity> SeedGroupAsync(AppDbContext db, string name, params UserEntity[] admins)
    {
        var group = new GroupEntity(name, null, admins[0].Id);
        group.SetAdmins(admins.Select(a => a.Id).ToArray());
        db.Groups.Add(group);
        await db.SaveChangesAsync();

        // O criador também joga: é o que o CreateAsync faz, e sem isso o cenário não bate
        // com a realidade — a regra olha para Players, não só para GroupAdmins.
        foreach (var admin in admins)
            db.Players.Add(new PlayerEntity(admin.UserName, admin.Id, group.Id, 0, false, false, Status.Active));

        await db.SaveChangesAsync();
        return group;
    }
}
