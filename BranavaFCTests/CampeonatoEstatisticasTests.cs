using BratnavaFC.Application.Abstractions;
using BratnavaFC.Application.Services;
using BratnavaFC.Domain.Championships;
using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos;
using BratnavaFC.Domain.Dtos.Championships;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace BranavaFC.Tests;

/// <summary>
/// Fase 3: as estatísticas gerais respeitam as regras do campeonato, e todas as telas
/// (histórico, relatório visual, conquistas) contam igual para cada jogador.
///
/// Campeonato fixo, todos contra todos, 3 times de 3 (A, B, C):
///   Jogo 1: A 2x0 B  (a1, a1 com assistência de a2)
///   Jogo 2: A 1x1 C  (a1; c1)
///   Jogo 3: B 3x0 C  (b1 hat-trick)
/// Classificação: A 1º (V+E), B 2º (D+V), C 3º (E+D).
/// a1 faz 3 gols, mas em jogos diferentes: nunca pode virar hat-trick.
/// </summary>
[Trait("Category", "Postgres")]
public sealed class CampeonatoEstatisticasTests : IClassFixture<CampeonatoPostgresFixture>
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private readonly CampeonatoPostgresFixture _fixture;

    public CampeonatoEstatisticasTests(CampeonatoPostgresFixture fixture) => _fixture = fixture;

    public sealed record Caso(
        string Nome, HistoryMode History, ResultMode Result, MvpMode Mvp, bool GoalsCount, bool MvpCount,
        // Esperado por time [A, B, C]: (jogos, vitórias, empates, derrotas) de cada jogador do time.
        (int G, int W, int D, int L)[] PorTime,
        int UnidadesNoHistorico, int GolsA1, int HatTricksB1, int MvpsA1, int HatTricksA1 = 0)
    {
        public override string ToString() => Nome;
    }

    public static TheoryData<Caso> Casos => new()
    {
        new("Consolidado, colocação, MVP do campeonato, conta tudo",
            HistoryMode.Consolidated, ResultMode.Placement, MvpMode.PerChampionship, true, true,
            [(1, 1, 0, 0), (1, 0, 1, 0), (1, 0, 0, 1)], 1, GolsA1: 3, HatTricksB1: 1, MvpsA1: 1),
        new("Consolidado, Vitória para todos, sem gols",
            HistoryMode.Consolidated, ResultMode.WinForAll, MvpMode.PerChampionship, false, true,
            [(1, 1, 0, 0), (1, 1, 0, 0), (1, 1, 0, 0)], 1, GolsA1: 0, HatTricksB1: 0, MvpsA1: 1),
        new("Consolidado, só presença, MVP não conta",
            HistoryMode.Consolidated, ResultMode.PresenceOnly, MvpMode.PerChampionship, true, false,
            [(1, 0, 0, 0), (1, 0, 0, 0), (1, 0, 0, 0)], 1, GolsA1: 3, HatTricksB1: 1, MvpsA1: 0),
        new("Consolidado, MVP por jogo conta no máximo 1",
            HistoryMode.Consolidated, ResultMode.Placement, MvpMode.PerGame, true, true,
            [(1, 1, 0, 0), (1, 0, 1, 0), (1, 0, 0, 1)], 1, GolsA1: 3, HatTricksB1: 1, MvpsA1: 1),
        new("Jogos separados, V/E/D por jogo",
            HistoryMode.SeparateGames, ResultMode.PerGame, MvpMode.PerGame, true, true,
            [(2, 1, 1, 0), (2, 1, 0, 1), (2, 0, 1, 1)], 3, GolsA1: 3, HatTricksB1: 1, MvpsA1: 1),
        new("Jogos separados, colocação em cada jogo",
            HistoryMode.SeparateGames, ResultMode.Placement, MvpMode.PerChampionship, true, true,
            [(2, 2, 0, 0), (2, 0, 2, 0), (2, 0, 0, 2)], 3, GolsA1: 3, HatTricksB1: 1, MvpsA1: 1),
        new("Jogos separados, Vitória para todos, nada de gols nem MVP",
            HistoryMode.SeparateGames, ResultMode.WinForAll, MvpMode.PerGame, false, false,
            [(2, 2, 0, 0), (2, 2, 0, 0), (2, 2, 0, 0)], 3, GolsA1: 0, HatTricksB1: 0, MvpsA1: 0),
        new("Só no campeonato: não aparece em lugar nenhum",
            HistoryMode.ChampionshipOnly, ResultMode.Placement, MvpMode.PerChampionship, true, true,
            [(0, 0, 0, 0), (0, 0, 0, 0), (0, 0, 0, 0)], 0, GolsA1: 0, HatTricksB1: 0, MvpsA1: 0),
    };

    [Theory]
    [MemberData(nameof(Casos))]
    public async Task EstatisticasRespeitamAsRegras_ETodasAsTelasContamIgual(Caso caso)
    {
        await using var db = await _fixture.CreateDatabaseAsync("est_" + Math.Abs(caso.Nome.GetHashCode()));
        var (groupId, teams) = await PlayChampionshipAsync(db, caso);
        db.ChangeTracker.Clear();

        var visual = (await new PlayerStatsService(db).GetVisualReportAsync(groupId)).Players.ToDictionary(p => p.PlayerId);
        var projections = await db.PlayerStatProjections.AsNoTracking()
            .Where(p => p.GroupId == groupId && p.Season == 0)
            .ToDictionaryAsync(p => p.PlayerId);
        var matches = CreateMatchService(db);

        var history = Ok(await matches.GetHistoryAsync(groupId, 100, 0, Ct, includeChampionships: true));
        history.Total.Should().Be(caso.UnidadesNoHistorico);
        Ok(await matches.GetHistoryAsync(groupId, 100, 0, Ct)).Total
            .Should().Be(0, "sem includeChampionships, clientes antigos não veem campeonato");

        for (var t = 0; t < 3; t++)
        {
            var (games, wins, draws, losses) = caso.PorTime[t];
            foreach (var playerId in teams[t])
            {
                var v = visual[playerId];
                (v.GamesPlayed, v.Wins, v.Ties, v.Losses).Should().Be((games, wins, draws, losses), $"time {t}, relatório visual");

                var playerHistory = Ok(await matches.GetPlayerHistoryAsync(groupId, playerId, null, Ct, includeChampionships: true));
                playerHistory.Should().HaveCount(games, "histórico do jogador = jogos do relatório");
                Ok(await matches.GetHistoryAsync(groupId, 100, 0, Ct, playerId, includeChampionships: true)).Total.Should().Be(games);

                if (games == 0)
                {
                    projections.Should().NotContainKey(playerId);
                    continue;
                }

                var proj = projections[playerId];
                (proj.Games, proj.Wins).Should().Be((games, wins), "conquistas contam igual ao relatório");
                proj.Goals.Should().Be(v.Goals);
                proj.Mvps.Should().Be(v.Mvps);
            }
        }

        var (a1, b1) = (teams[0][0], teams[1][0]);
        visual[a1].Goals.Should().Be(caso.GolsA1);
        visual[a1].Mvps.Should().Be(caso.MvpsA1);
        if (caso.UnidadesNoHistorico > 0)
        {
            projections[b1].HatTricks.Should().Be(caso.HatTricksB1);
            projections[a1].HatTricks.Should().Be(caso.HatTricksA1, "3 gols em jogos diferentes não são hat-trick");
        }
    }

    [Fact]
    public async Task CampeonatoNaoFinalizado_NaoContaEmNada()
    {
        await using var db = await _fixture.CreateDatabaseAsync(nameof(CampeonatoNaoFinalizado_NaoContaEmNada));
        var caso = Casos.Cast<object[]>().Select(x => (Caso)x[0]).First();
        var (groupId, teams) = await PlayChampionshipAsync(db, caso, finalize: false);
        db.ChangeTracker.Clear();

        var visual = await new PlayerStatsService(db).GetVisualReportAsync(groupId);
        visual.Players.Should().OnlyContain(p => p.GamesPlayed == 0);
        Ok(await CreateMatchService(db).GetHistoryAsync(groupId, 100, 0, Ct, includeChampionships: true)).Total.Should().Be(0);
    }

    // ── Cenário ──────────────────────────────────────────────────────────────

    private static async Task<(Guid groupId, List<List<Guid>> teams)> PlayChampionshipAsync(
        AppDbContext db, Caso caso, bool finalize = true)
    {
        var group = new GroupEntity("Bratnava FC", null, Guid.NewGuid());
        db.Groups.Add(group);
        var players = new List<PlayerEntity>();
        for (var i = 0; i < 9; i++)
        {
            var user = new UserEntity($"j{i}", $"J{i}", "T", $"j{i}@t.com", "hash", null, null);
            db.Users.Add(user);
            players.Add(new PlayerEntity($"Jogador {i}", user.Id, group.Id, 3m, isGoalkeeper: i % 3 == 2));
        }
        db.Players.AddRange(players);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var rules = ChampionshipEntity.DefaultRules with
        {
            HistoryMode = caso.History,
            ResultMode = caso.Result,
            MvpMode = caso.Mvp,
            GoalsCountInGeneralStats = caso.GoalsCount,
            MvpCountsInGeneralStats = caso.MvpCount,
            Format = ChampionshipFormat.RoundRobin,
            WinnerStays = null
        };

        var sut = new ChampionshipService(db, new ConquistaProjectionService(db));
        var id = Ok(await sut.CreateAsync(group.Id, new CreateChampionshipDto("Torneio", new DateTime(2026, 12, 20, 13, 0, 0, DateTimeKind.Utc), "Arena", rules), Ct)).Id;
        Ok(await sut.OpenAcceptationAsync(group.Id, id, Ct));
        foreach (var p in players)
            Ok(await sut.RespondInviteAsync(group.Id, id, p.Id, accept: true, Ct));

        var lineups = Enumerable.Range(0, 3).Select(t => players.Skip(t * 3).Take(3).Select(p => p.Id).ToList()).ToList();
        Ok(await sut.DefineTeamsAsync(group.Id, id, new DefineTeamsDto(lineups
            .Select((ids, t) => new DefineTeamDto($"Time {t}", null, ids, ids[2])).ToList()), Ct));
        Ok(await sut.StartAsync(group.Id, id, Ct));

        var details = Ok(await sut.GetAsync(group.Id, id, null, Ct));
        var teamIds = details.Teams.OrderBy(t => t.Name).Select(t => t.Id).ToList();
        var (a1, a2, b1, c1) = (lineups[0][0], lineups[0][1], lineups[1][0], lineups[2][0]);

        var g1 = await GameAsync(sut, group.Id, id, teamIds[0], teamIds[1], [(a1, null), (a1, a2)]);
        var g2 = await GameAsync(sut, group.Id, id, teamIds[0], teamIds[2], [(a1, null), (c1, null)]);
        var g3 = await GameAsync(sut, group.Id, id, teamIds[1], teamIds[2], [(b1, null), (b1, null), (b1, null)]);

        if (!finalize) return (group.Id, lineups);

        Ok(await sut.FinishAsync(group.Id, id, Ct));

        // Votos: MVP do campeonato = a1; por jogo = a1, c1, b1.
        var voters = players.Where(p => p.Id != a1 && p.Id != b1 && p.Id != c1).ToList();
        foreach (var voter in voters)
        {
            if (caso.Mvp == MvpMode.PerChampionship)
                Ok(await sut.VoteAsync(group.Id, id, voter.UserId!.Value, new ChampionshipVoteDto(a1, null), Ct));
            else
            {
                Ok(await sut.VoteAsync(group.Id, id, voter.UserId!.Value, new ChampionshipVoteDto(a1, g1), Ct));
                Ok(await sut.VoteAsync(group.Id, id, voter.UserId!.Value, new ChampionshipVoteDto(c1, g2), Ct));
                Ok(await sut.VoteAsync(group.Id, id, voter.UserId!.Value, new ChampionshipVoteDto(b1, g3), Ct));
            }
        }

        Ok(await sut.FinalizeAsync(group.Id, id, new FinalizeChampionshipDto(null), Ct));
        return (group.Id, lineups);
    }

    private static async Task<Guid> GameAsync(
        ChampionshipService sut, Guid groupId, Guid id, Guid teamA, Guid teamB, (Guid Scorer, Guid? Assist)[] goals)
    {
        var gameId = Ok(await sut.CreateGameAsync(groupId, id, new CreateChampionshipGameDto(teamA, teamB), Ct)).GameId;
        foreach (var (scorer, assist) in goals)
            Ok(await sut.AddGoalAsync(groupId, id, gameId, new ChampionshipGoalDto(scorer, assist, false, null), Ct));
        Ok(await sut.EndGameAsync(groupId, id, gameId, new EndChampionshipGameDto(null), Ct));
        return gameId;
    }

    private static MatchService CreateMatchService(AppDbContext db)
        => new(db, new Mock<IRepositoryBase<MatchEntity>>().Object, Mock.Of<IPushService>(), Mock.Of<IReplayUrlService>(),
            Mock.Of<IBetService>(), Mock.Of<INotificationScheduler>(), TestImageStorage.Create());

    private static T Ok<T>(Result<T> result)
    {
        result.Success.Should().BeTrue(result.Error);
        return result.Data!;
    }

    private static void Ok(Result result) => result.Success.Should().BeTrue(result.Error);
}
