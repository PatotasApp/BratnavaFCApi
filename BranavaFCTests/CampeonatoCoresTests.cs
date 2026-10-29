using BratnavaFC.Application.Services;
using BratnavaFC.Application.TeamGeneration.MultiTeam;
using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos.Championships;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BranavaFC.Tests;

/// <summary>Cores e nomes dos times do campeonato: sorteio, falta de cores e troca no meio do campeonato.</summary>
[Trait("Category", "Postgres")]
public sealed class CampeonatoCoresTests : IClassFixture<CampeonatoPostgresFixture>
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private readonly CampeonatoPostgresFixture _fixture;

    public CampeonatoCoresTests(CampeonatoPostgresFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task SorteioDeCores_UsaSoCoresAtivasDaPatota_SemRepetir()
    {
        await using var db = await _fixture.CreateDatabaseAsync(nameof(SorteioDeCores_UsaSoCoresAtivasDaPatota_SemRepetir));
        var (groupId, id, sut) = await SetupAsync(db, activeColors: 4);

        for (var round = 0; round < 5; round++)
        {
            var result = await sut.DrawTeamColorsAsync(groupId, id, Ct);
            result.Success.Should().BeTrue(result.Error);
            result.Message.Should().Be("Cores sorteadas.");

            var details = await DetailsAsync(sut, groupId, id);
            var colorIds = details.Teams.Select(t => t.ColorId).ToList();
            colorIds.Should().OnlyContain(c => c.HasValue).And.OnlyHaveUniqueItems();

            var allowed = await db.TeamColors.Where(c => c.GroupId == groupId && c.IsActive).Select(c => c.Id).ToListAsync();
            colorIds.Select(c => c!.Value).Should().BeSubsetOf(allowed, "nunca cor inativa nem de outra patota");
            details.Teams.Should().OnlyContain(t => t.ColorHex != null);
        }
    }

    [Fact]
    public async Task PoucasCores_AvisaEDeixaTimesSemCor()
    {
        await using var db = await _fixture.CreateDatabaseAsync(nameof(PoucasCores_AvisaEDeixaTimesSemCor));
        var (groupId, id, sut) = await SetupAsync(db, activeColors: 2, defineTeams: false);

        var draw = await sut.DrawTeamsAsync(groupId, id, 3, Ct);
        draw.Success.Should().BeTrue(draw.Error);
        draw.Message.Should().Contain("so 2 cor(es) ativa(s) para 3 times");
        draw.Data!.Teams.Count(t => t.ColorId.HasValue).Should().Be(2);
        draw.Data.Teams.Single(t => !t.ColorId.HasValue).Name.Should().Be("Time 3");

        Ok(await sut.DefineTeamsAsync(groupId, id, draw.Data, Ct));
        var colors = await sut.DrawTeamColorsAsync(groupId, id, Ct);
        colors.Message.Should().Contain("1 time(s) ficaram sem cor");
    }

    [Fact]
    public async Task TrocarNomeECor_DuranteOSorteio_ComValidacoes()
    {
        await using var db = await _fixture.CreateDatabaseAsync(nameof(TrocarNomeECor_DuranteOSorteio_ComValidacoes));
        var (groupId, id, sut) = await SetupAsync(db, activeColors: 5);

        var details = await DetailsAsync(sut, groupId, id);
        var (a, b) = (details.Teams[0], details.Teams[1]);
        var used = details.Teams.Select(t => t.ColorId).ToHashSet();
        var free = await db.TeamColors.Where(c => c.GroupId == groupId && c.IsActive && !used.Contains(c.Id)).Select(c => c.Id).FirstAsync();

        Ok(await sut.UpdateTeamAsync(groupId, id, a.Id, new UpdateChampionshipTeamDto("Azul Royal", free), Ct));
        var updated = (await DetailsAsync(sut, groupId, id)).Teams.Single(t => t.Id == a.Id);
        (updated.Name, updated.ColorId).Should().Be(("Azul Royal", free));

        // Validações.
        (await sut.UpdateTeamAsync(groupId, id, a.Id, new UpdateChampionshipTeamDto("X", b.ColorId), Ct))
            .Error.Should().Contain("mesma cor");
        var inactive = await db.TeamColors.Where(c => c.GroupId == groupId && !c.IsActive).Select(c => c.Id).FirstAsync();
        (await sut.UpdateTeamAsync(groupId, id, a.Id, new UpdateChampionshipTeamDto("X", inactive), Ct))
            .Error.Should().Contain("cor ativa");
        var otherGroup = await db.TeamColors.Where(c => c.GroupId != groupId).Select(c => c.Id).FirstAsync();
        (await sut.UpdateTeamAsync(groupId, id, a.Id, new UpdateChampionshipTeamDto("X", otherGroup), Ct))
            .Error.Should().Contain("cor ativa");
        (await sut.UpdateTeamAsync(groupId, id, a.Id, new UpdateChampionshipTeamDto(" ", null), Ct))
            .Success.Should().BeFalse();

        // Sem cor também é permitido.
        Ok(await sut.UpdateTeamAsync(groupId, id, a.Id, new UpdateChampionshipTeamDto("Azul Royal", null), Ct));
    }

    [Fact]
    public async Task DepoisDeIniciado_TimesFicamTravados()
    {
        await using var db = await _fixture.CreateDatabaseAsync(nameof(DepoisDeIniciado_TimesFicamTravados));
        var (groupId, id, sut) = await SetupAsync(db, activeColors: 5);
        var details = await DetailsAsync(sut, groupId, id);
        var (a, b) = (details.Teams[0], details.Teams[1]);
        Ok(await sut.StartAsync(groupId, id, Ct));

        var mover = a.PlayerIds.First(p => p != a.PlayerIds.Last());
        var attempts = new[]
        {
            await sut.UpdateTeamAsync(groupId, id, a.Id, new UpdateChampionshipTeamDto("Outro nome", null), Ct),
            await sut.DrawTeamColorsAsync(groupId, id, Ct),
            await sut.MovePlayerAsync(groupId, id, mover, b.Id, Ct),
            await sut.SetTeamGoalkeeperAsync(groupId, id, new SetGameGoalkeeperDto(a.Id, mover), Ct),
        };
        attempts.Should().OnlyContain(r => !r.Success && r.Error!.Contains("ficam travados"));

        var after = await DetailsAsync(sut, groupId, id);
        after.Teams.Select(t => (t.Name, t.ColorId, string.Join(",", t.PlayerIds.OrderBy(x => x))))
            .Should().Equal(details.Teams.Select(t => (t.Name, t.ColorId, string.Join(",", t.PlayerIds.OrderBy(x => x)))));
    }

    [Fact]
    public async Task CampeonatoFinalizado_NaoPermiteMudarTimes()
    {
        await using var db = await _fixture.CreateDatabaseAsync(nameof(CampeonatoFinalizado_NaoPermiteMudarTimes));
        var (groupId, id, sut) = await SetupAsync(db, activeColors: 3);
        Ok(await sut.StartAsync(groupId, id, Ct));
        var teams = (await DetailsAsync(sut, groupId, id)).Teams;
        var game = Ok(await sut.CreateGameAsync(groupId, id, new CreateChampionshipGameDto(teams[0].Id, teams[1].Id), Ct)).GameId;
        Ok(await sut.EndGameAsync(groupId, id, game, new EndChampionshipGameDto(null), Ct));
        Ok(await sut.FinishAsync(groupId, id, Ct));
        Ok(await sut.FinalizeAsync(groupId, id, new FinalizeChampionshipDto(null), Ct));

        (await sut.DrawTeamColorsAsync(groupId, id, Ct)).Success.Should().BeFalse();
        (await sut.UpdateTeamAsync(groupId, id, teams[0].Id, new UpdateChampionshipTeamDto("X", null), Ct)).Success.Should().BeFalse();
    }

    // ── Cenário: patota com 9 jogadores, cores ativas, 1 inativa e 1 de outra patota ──

    private static async Task<(Guid groupId, Guid id, ChampionshipService sut)> SetupAsync(
        AppDbContext db, int activeColors, bool defineTeams = true)
    {
        var group = new GroupEntity("Bratnava FC", null, Guid.NewGuid());
        var other = new GroupEntity("Outra patota", null, Guid.NewGuid());
        db.Groups.AddRange(group, other);

        string[] hex = ["#0000FF", "#FF0000", "#00FF00", "#FFFF00", "#000000", "#FFFFFF"];
        for (var i = 0; i < activeColors; i++)
            db.TeamColors.Add(new TeamColorEntity(group.Id, $"Cor {i}", hex[i]));
        var inactive = new TeamColorEntity(group.Id, "Inativa", "#123456");
        inactive.Inactivate();
        db.TeamColors.AddRange(inactive, new TeamColorEntity(other.Id, "De outra patota", "#654321"));

        var players = Enumerable.Range(0, 9)
            .Select(i => new PlayerEntity($"Jogador {i}", null, group.Id, 3m, isGoalkeeper: i < 3))
            .ToList();
        db.Players.AddRange(players);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var sut = new ChampionshipService(db, new MultiTeamGenerationService(new FakeStatsService([])));
        var id = Ok(await sut.CreateAsync(group.Id, new CreateChampionshipDto("Torneio", new DateTime(2026, 12, 20, 13, 0, 0, DateTimeKind.Utc), "Arena", null), Ct)).Id;
        Ok(await sut.OpenAcceptationAsync(group.Id, id, Ct));
        foreach (var p in players)
            Ok(await sut.RespondInviteAsync(group.Id, id, p.Id, accept: true, Ct));

        if (defineTeams)
            Ok(await sut.DefineTeamsAsync(group.Id, id, Ok(await sut.DrawTeamsAsync(group.Id, id, 3, Ct)), Ct));

        return (group.Id, id, sut);
    }

    private static async Task<ChampionshipDetailsDto> DetailsAsync(ChampionshipService sut, Guid groupId, Guid id) =>
        Ok(await sut.GetAsync(groupId, id, null, Ct));

    private static T Ok<T>(Result<T> result)
    {
        result.Success.Should().BeTrue(result.Error);
        return result.Data!;
    }

    private static void Ok(Result result) => result.Success.Should().BeTrue(result.Error);
}
