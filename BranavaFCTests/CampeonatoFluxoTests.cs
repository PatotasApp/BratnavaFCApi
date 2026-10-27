using BratnavaFC.Application.Abstractions;
using BratnavaFC.Application.Services;
using BratnavaFC.Domain.Championships;
using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos.Championships;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;
using BratnavaFC.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace BranavaFC.Tests;

/// <summary>Fases 1 e 2 do Modo Campeonato: fluxo completo pelo serviço, contra Postgres real.</summary>
[Trait("Category", "Postgres")]
public sealed class CampeonatoFluxoTests : IClassFixture<CampeonatoPostgresFixture>
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private readonly CampeonatoPostgresFixture _fixture;

    public CampeonatoFluxoTests(CampeonatoPostgresFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task FicaQuemGanha_FluxoCompleto_ClassificaVotaEFinaliza()
    {
        await using var db = await _fixture.CreateDatabaseAsync(nameof(FicaQuemGanha_FluxoCompleto_ClassificaVotaEFinaliza));
        var (groupId, players) = await SeedGroupAsync(db, 9);
        var sut = new ChampionshipService(db);

        var id = Ok(await sut.CreateAsync(groupId, new CreateChampionshipDto("Torneio de Natal", Day(), "Arena", null), Ct)).Id;
        Ok(await sut.OpenAcceptationAsync(groupId, id, Ct));
        foreach (var p in players.Take(8))
            Ok(await sut.RespondMyInviteAsync(groupId, id, p.UserId!.Value, accept: true, Ct));
        Ok(await sut.RespondMyInviteAsync(groupId, id, players[8].UserId!.Value, accept: false, Ct));

        var draw = Ok(await sut.DrawTeamsAsync(groupId, id, 3, Ct));
        draw.Teams.Should().HaveCount(3);
        draw.Teams.SelectMany(t => t.PlayerIds).Should().HaveCount(8).And.NotContain(players[8].Id);
        Ok(await sut.DefineTeamsAsync(groupId, id, draw, Ct));

        // Regras travam ao iniciar.
        Ok(await sut.StartAsync(groupId, id, Ct));
        (await sut.UpdateAsync(groupId, id, new UpdateChampionshipDto("X", Day(), "Arena", ChampionshipEntity.DefaultRules), Ct))
            .Success.Should().BeFalse();

        var teams = (await Details(sut, groupId, id)).Teams.Select(t => t.Id).ToList();
        var (a, b, c) = (teams[0], teams[1], teams[2]);

        // Jogo 1: A 2x0 B → fica A, entra C.
        var next = Ok(await sut.GetNextGameAsync(groupId, id, Ct))!;
        (next.TeamAId, next.TeamBId).Should().Be((a, b));
        next.WaitingTeamIds.Should().Equal(c);
        await PlayAsync(db, sut, groupId, id, a, b, goalsA: 2, goalsB: 0);

        next = Ok(await sut.GetNextGameAsync(groupId, id, Ct))!;
        new[] { next.TeamAId, next.TeamBId }.Should().BeEquivalentTo(new[] { a, c });

        // Jogo 2: C 1x0 A → fica C, entra B.
        await PlayAsync(db, sut, groupId, id, next.TeamAId, next.TeamBId,
            goalsA: next.TeamAId == c ? 1 : 0, goalsB: next.TeamBId == c ? 1 : 0);

        // Só um jogo por vez.
        next = Ok(await sut.GetNextGameAsync(groupId, id, Ct))!;
        Ok(await sut.CreateGameAsync(groupId, id, new CreateChampionshipGameDto(next.TeamAId, next.TeamBId), Ct));
        (await sut.CreateGameAsync(groupId, id, new CreateChampionshipGameDto(next.TeamAId, next.TeamBId), Ct))
            .Success.Should().BeFalse();
        var current = (await Details(sut, groupId, id)).CurrentGameId!.Value;
        Ok(await sut.EndGameAsync(groupId, id, current, new EndChampionshipGameDto(null), Ct)); // 0x0

        Ok(await sut.FinishAsync(groupId, id, Ct));

        var details = await Details(sut, groupId, id);
        details.Games.Should().HaveCount(3).And.OnlyContain(g => g.Status == ChampionshipGameStatus.Finished);
        details.Standings.Should().HaveCount(3);
        details.TopScorers.Sum(s => s.Goals).Should().Be(3);

        // MVP do campeonato: todos que jogaram votam no artilheiro do time A.
        var teamAPlayers = details.Teams.First(t => t.Id == a).PlayerIds;
        var mvp = details.TopScorers.First().PlayerId;
        foreach (var voter in players.Take(8).Where(p => p.Id != mvp))
            Ok(await sut.VoteAsync(groupId, id, voter.UserId!.Value, new ChampionshipVoteDto(mvp, null), Ct));
        (await sut.VoteAsync(groupId, id, players[8].UserId!.Value, new ChampionshipVoteDto(mvp, null), Ct))
            .Success.Should().BeFalse("quem não jogou não vota");

        Ok(await sut.FinalizeAsync(groupId, id, new FinalizeChampionshipDto(null), Ct));

        details = await Details(sut, groupId, id);
        details.Status.Should().Be(ChampionshipStatus.Finalized);
        details.MvpPlayerIds.Should().Equal(mvp);
        details.Teams.Should().OnlyContain(t => t.FinalPosition.HasValue);
        teamAPlayers.Should().NotBeEmpty();

        var summary = Ok(await sut.ListAsync(groupId, Ct)).Single();
        summary.GameCount.Should().Be(3);
        summary.ChampionTeamName.Should().NotBeNullOrEmpty();
        summary.RuleBadges.Should().Contain("Conta como 1 partida").And.Contain("MVP do campeonato");

        // Fase 0 continua valendo: nada disso aparece no histórico de partidas.
        db.ChangeTracker.Clear();
        (await db.Matches.CountAsync()).Should().Be(0);
        (await db.Matches.IgnoreQueryFilters().CountAsync()).Should().Be(3);
    }

    [Fact]
    public async Task TodosContraTodosMaisFinal_EmpateNaFinalExigePenaltis_EColocacao()
    {
        await using var db = await _fixture.CreateDatabaseAsync(nameof(TodosContraTodosMaisFinal_EmpateNaFinalExigePenaltis_EColocacao));
        var (groupId, players) = await SeedGroupAsync(db, 8);
        var sut = new ChampionshipService(db);

        var rules = ChampionshipEntity.DefaultRules with
        {
            Format = ChampionshipFormat.RoundRobinWithFinal, WinnerStays = null, MvpMode = MvpMode.PerGame,
            Standings = ChampionshipEntity.DefaultRules.Standings with { Mode = StandingMode.TotalPoints }
        };
        var id = Ok(await sut.CreateAsync(groupId, new CreateChampionshipDto("Com final", Day(), "Arena", rules), Ct)).Id;
        await SetupTeamsAsync(sut, groupId, id, players, teamCount: 4);

        // Fase de grupos: 6 jogos; o "time A" de cada jogo sugerido vence por 1x0.
        for (var i = 0; i < 6; i++)
        {
            var next = Ok(await sut.GetNextGameAsync(groupId, id, Ct))!;
            next.Reason.Should().StartWith("Proximo jogo da tabela");
            await PlayAsync(db, sut, groupId, id, next.TeamAId, next.TeamBId, goalsA: 1, goalsB: 0);
        }

        var standings = (await Details(sut, groupId, id)).Standings;
        var final = Ok(await sut.GetNextGameAsync(groupId, id, Ct))!;
        final.Reason.Should().StartWith("Final");
        new[] { final.TeamAId, final.TeamBId }.Should().BeEquivalentTo(standings.Take(2).Select(s => s.TeamId));

        // Final empatada: precisa dos pênaltis.
        var finalGame = Ok(await sut.CreateGameAsync(groupId, id, new CreateChampionshipGameDto(final.TeamAId, final.TeamBId), Ct)).GameId;
        (await sut.EndGameAsync(groupId, id, finalGame, new EndChampionshipGameDto(null), Ct)).Success
            .Should().BeFalse("empate na final precisa de vencedor");
        Ok(await sut.EndGameAsync(groupId, id, finalGame, new EndChampionshipGameDto(final.TeamBId), Ct));
        Ok(await sut.GetNextGameAsync(groupId, id, Ct)).Should().BeNull("tabela e final jogadas");

        Ok(await sut.FinishAsync(groupId, id, Ct));
        Ok(await sut.FinalizeAsync(groupId, id, new FinalizeChampionshipDto(null), Ct));

        var position = (await Details(sut, groupId, id)).Teams.ToDictionary(t => t.Id, t => t.FinalPosition);
        position[final.TeamBId].Should().Be(1, "venceu a final nos pênaltis");
        position[final.TeamAId].Should().Be(2);
        foreach (var other in standings.Skip(2))
            position[other.TeamId].Should().Be(other.Position, "os demais ficam pela classificação");
    }

    [Fact]
    public async Task RegrasInvalidas_SaoRecusadas_ECancelarApagaJogosAbertos()
    {
        await using var db = await _fixture.CreateDatabaseAsync(nameof(RegrasInvalidas_SaoRecusadas_ECancelarApagaJogosAbertos));
        var (groupId, players) = await SeedGroupAsync(db, 6);
        var sut = new ChampionshipService(db);

        var invalid = ChampionshipEntity.DefaultRules with { ResultMode = ResultMode.PerGame };
        var refused = await sut.CreateAsync(groupId, new CreateChampionshipDto("X", Day(), "Arena", invalid), Ct);
        refused.Success.Should().BeFalse();
        refused.Error.Should().Contain("Resultado por jogo");

        var pointsNotFirst = ChampionshipEntity.DefaultRules with
        {
            Standings = ChampionshipEntity.DefaultRules.Standings with
            {
                Tiebreakers = [TiebreakerCriteria.GoalDifference, TiebreakerCriteria.Points]
            }
        };
        (await sut.CreateAsync(groupId, new CreateChampionshipDto("X", Day(), "Arena", pointsNotFirst), Ct))
            .Error.Should().Contain("Pontos tem de ser o primeiro");

        var knockout = ChampionshipEntity.DefaultRules with { Format = (ChampionshipFormat)3 };
        (await sut.CreateAsync(groupId, new CreateChampionshipDto("X", Day(), "Arena", knockout), Ct))
            .Error.Should().Be("Formato invalido.", "o mata-mata foi descartado");

        var id = Ok(await sut.CreateAsync(groupId, new CreateChampionshipDto("Y", Day(), "Arena", null), Ct)).Id;
        var teams = await SetupTeamsAsync(sut, groupId, id, players, teamCount: 3);
        Ok(await sut.CreateGameAsync(groupId, id, new CreateChampionshipGameDto(teams[0], teams[1]), Ct));

        Ok(await sut.CancelAsync(groupId, id, Ct));

        db.ChangeTracker.Clear();
        (await db.Matches.IgnoreQueryFilters().CountAsync()).Should().Be(0);
        (await db.Championships.SingleAsync()).Status.Should().Be(ChampionshipStatus.Cancelled);
    }

    // ── Auxiliares ───────────────────────────────────────────────────────────

    private static DateTime Day() => new(2026, 12, 20, 13, 0, 0, DateTimeKind.Utc);

    private static T Ok<T>(Result<T> result)
    {
        result.Success.Should().BeTrue(result.Error);
        return result.Data!;
    }

    private static void Ok(Result result) => result.Success.Should().BeTrue(result.Error);

    private static async Task<ChampionshipDetailsDto> Details(ChampionshipService sut, Guid groupId, Guid id) =>
        Ok(await sut.GetAsync(groupId, id, null, Ct));

    private static async Task<List<Guid>> SetupTeamsAsync(
        ChampionshipService sut, Guid groupId, Guid id, IReadOnlyList<PlayerEntity> players, int teamCount)
    {
        Ok(await sut.OpenAcceptationAsync(groupId, id, Ct));
        foreach (var p in players)
            Ok(await sut.RespondInviteAsync(groupId, id, p.Id, accept: true, Ct));
        Ok(await sut.DefineTeamsAsync(groupId, id, Ok(await sut.DrawTeamsAsync(groupId, id, teamCount, Ct)), Ct));
        Ok(await sut.StartAsync(groupId, id, Ct));
        return (await Details(sut, groupId, id)).Teams.Select(t => t.Id).ToList();
    }

    /// <summary>Cria o jogo, marca os gols com o primeiro jogador de cada time e encerra.</summary>
    private static async Task PlayAsync(
        AppDbContext db, ChampionshipService sut, Guid groupId, Guid id, Guid teamA, Guid teamB, int goalsA, int goalsB)
    {
        var gameId = Ok(await sut.CreateGameAsync(groupId, id, new CreateChampionshipGameDto(teamA, teamB), Ct)).GameId;
        var details = await Details(sut, groupId, id);
        var scorerA = details.Teams.Single(t => t.Id == teamA).PlayerIds.First();
        var scorerB = details.Teams.Single(t => t.Id == teamB).PlayerIds.First();

        for (var i = 0; i < goalsA; i++)
            Ok(await sut.AddGoalAsync(groupId, id, gameId, new ChampionshipGoalDto(scorerA, null, false, null), Ct));
        for (var i = 0; i < goalsB; i++)
            Ok(await sut.AddGoalAsync(groupId, id, gameId, new ChampionshipGoalDto(scorerB, null, false, null), Ct));

        Ok(await sut.EndGameAsync(groupId, id, gameId, new EndChampionshipGameDto(null), Ct));
        db.ChangeTracker.Clear();
    }

    private static async Task<(Guid groupId, List<PlayerEntity> players)> SeedGroupAsync(AppDbContext db, int count)
    {
        var group = new GroupEntity("Bratnava FC", null, Guid.NewGuid());
        db.Groups.Add(group);

        var players = new List<PlayerEntity>();
        for (var i = 0; i < count; i++)
        {
            var user = new UserEntity($"jogador{i}", $"Jogador{i}", "Teste", $"jogador{i}@t.com", "hash", null, null);
            db.Users.Add(user);
            players.Add(new PlayerEntity($"Jogador {i}", user.Id, group.Id, 3m + i % 3, isGoalkeeper: i < 3));
        }

        db.Players.AddRange(players);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return (group.Id, players);
    }
}
