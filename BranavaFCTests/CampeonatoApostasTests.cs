using BratnavaFC.Application.Services;
using BratnavaFC.Application.TeamGeneration.MultiTeam;
using BratnavaFC.Domain.Championships;
using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos.Championships;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;
using BratnavaFC.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BranavaFC.Tests;

/// <summary>
/// Apostas do campeonato. Campeonato fixo, todos contra todos, 3 times de 3 (A, B, C; o 3º de cada é goleiro):
///   Jogo 1: A 2x0 B (a1, a1 com assistência de a2) · Jogo 2: A 1x1 C (a1; c1) · Jogo 3: B 3x0 C (b1 x3)
/// Campeão A · artilharia empatada a1/b1 (3) · assistente a2 · menos vazado: goleiro de A (média 0,5) · MVP a1.
/// </summary>
[Trait("Category", "Postgres")]
public sealed class CampeonatoApostasTests : IClassFixture<CampeonatoPostgresFixture>
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private readonly CampeonatoPostgresFixture _fixture;

    public CampeonatoApostasTests(CampeonatoPostgresFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task FluxoCompleto_JanelaValidacoesLiquidacaoEstornoERecalculo()
    {
        await using var db = await _fixture.CreateDatabaseAsync(nameof(FluxoCompleto_JanelaValidacoesLiquidacaoEstornoERecalculo));
        var s = await SetupAsync(db);
        var bets = new ChampionshipBetService(db);

        // Antes dos times: fechado.
        (await bets.PlaceOrUpdateAsync(s.GroupId, s.Id, s.Bettor1, Bet((ChampionshipBetCategory.Champion, s.TeamA, 50)), Ct))
            .Error.Should().Contain("abrem quando os times");

        await DefineTeamsAsync(s);
        (await bets.GetBettableAsync(s.GroupId, Ct)).Select(b => b.ChampionshipId).Should().Equal(s.Id);
        s = s with { TeamA = await TeamIdAsync(s, 0), TeamB = await TeamIdAsync(s, 1), TeamC = await TeamIdAsync(s, 2) };

        // Validações.
        (await bets.PlaceOrUpdateAsync(s.GroupId, s.Id, s.Bettor1, Bet((ChampionshipBetCategory.TopScorer, s.B1, 50)), Ct))
            .Error.Should().Contain("Time campeao' e obrigatoria");
        (await bets.PlaceOrUpdateAsync(s.GroupId, s.Id, s.Bettor1, Bet((ChampionshipBetCategory.Champion, s.TeamA, 150), (ChampionshipBetCategory.TopScorer, s.B1, 60)), Ct))
            .Error.Should().Contain("nao pode ultrapassar 200");
        (await bets.PlaceOrUpdateAsync(s.GroupId, s.Id, s.Bettor1, Bet((ChampionshipBetCategory.Champion, s.TeamA, 20)), Ct))
            .Error.Should().Contain("Minimo de 30");
        (await bets.PlaceOrUpdateAsync(s.GroupId, s.Id, s.Bettor1, Bet((ChampionshipBetCategory.Champion, s.TeamA, 50), (ChampionshipBetCategory.Champion, s.TeamB, 50)), Ct))
            .Error.Should().Contain("uma vez");
        (await bets.PlaceOrUpdateAsync(s.GroupId, s.Id, s.Bettor1, Bet((ChampionshipBetCategory.Champion, Guid.NewGuid(), 50)), Ct))
            .Error.Should().Contain("Time invalido");
        (await bets.PlaceOrUpdateAsync(s.GroupId, s.Id, s.Bettor1, Bet((ChampionshipBetCategory.Champion, s.TeamA, 50), (ChampionshipBetCategory.LeastConcededGoalkeeper, s.A1, 50)), Ct))
            .Error.Should().Contain("Jogador invalido", "a1 não é goleiro");
        (await bets.PlaceOrUpdateAsync(s.GroupId, s.Id, s.UserOfA1, Bet((ChampionshipBetCategory.Champion, s.TeamA, 50), (ChampionshipBetCategory.TopScorer, s.A1, 50)), Ct))
            .Error.Should().Contain("si mesmo");

        // Apostas válidas (a 2ª é refeita: vale a última).
        Ok(await bets.PlaceOrUpdateAsync(s.GroupId, s.Id, s.Bettor1, Bet((ChampionshipBetCategory.Champion, s.TeamB, 30)), Ct));
        Ok(await bets.PlaceOrUpdateAsync(s.GroupId, s.Id, s.Bettor1, Bet(
            (ChampionshipBetCategory.Champion, s.TeamA, 100),
            (ChampionshipBetCategory.TopScorer, s.B1, 50),
            (ChampionshipBetCategory.LeastConcededGoalkeeper, s.GkA, 50)), Ct));
        Ok(await bets.PlaceOrUpdateAsync(s.GroupId, s.Id, s.Bettor2, Bet(
            (ChampionshipBetCategory.Champion, s.TeamC, 60),
            (ChampionshipBetCategory.TopAssister, s.A1, 40),
            (ChampionshipBetCategory.Mvp, s.B1, 30)), Ct));

        var open = Ok(await bets.GetContextAsync(s.GroupId, s.Id, s.Bettor1, Ct));
        open.IsOpen.Should().BeTrue();
        open.AllBets.Should().BeEmpty("com as apostas abertas ninguém vê a dos outros");
        open.MyBet!.Selections.Should().HaveCount(3);
        open.Categories.Single(c => c.Category == ChampionshipBetCategory.Champion).Multiplier.Should().Be(2, "3 times: 2x");
        open.Categories.Select(c => c.Category).Should().Contain(ChampionshipBetCategory.Mvp);

        // Iniciado: fecha.
        Ok(await s.Service.StartAsync(s.GroupId, s.Id, Ct));
        (await bets.PlaceOrUpdateAsync(s.GroupId, s.Id, s.Bettor1, Bet((ChampionshipBetCategory.Champion, s.TeamA, 30)), Ct))
            .Error.Should().Contain("fecharam");
        (await bets.DeleteAsync(s.GroupId, s.Id, s.Bettor1, Ct)).Success.Should().BeFalse();
        (await bets.GetBettableAsync(s.GroupId, Ct)).Should().BeEmpty();

        await PlayAndFinalizeAsync(s);

        // Liquidação: Bettor1 = 50 bônus + 200 (campeão 2x) + 150 (artilheiro empatado 3x) + 150 (goleiro 3x).
        // Bettor2 = 50 bônus − 30 − 20 − 15.
        db.ChangeTracker.Clear();
        (await BalanceAsync(db, s, s.Bettor1)).Should().Be(550);
        (await BalanceAsync(db, s, s.Bettor2)).Should().Be(-15);

        var resolved = Ok(await bets.GetContextAsync(s.GroupId, s.Id, s.Bettor1, Ct));
        resolved.IsResolved.Should().BeTrue();
        resolved.AllBets.Should().HaveCount(2);
        resolved.AllBets[0].TotalForChampionship.Should().Be(550);
        Winners(resolved, ChampionshipBetCategory.Champion).Should().Equal(s.TeamA);
        Winners(resolved, ChampionshipBetCategory.TopScorer).Should().BeEquivalentTo([s.A1, s.B1]);
        Winners(resolved, ChampionshipBetCategory.TopAssister).Should().Equal(s.A2);
        Winners(resolved, ChampionshipBetCategory.LeastConcededGoalkeeper).Should().Equal(s.GkA);
        Winners(resolved, ChampionshipBetCategory.Mvp).Should().Equal(s.A1);

        var leaderboard = await new BetService(db).GetLeaderboardAsync(s.GroupId, Ct);
        leaderboard.First().UserId.Should().Be(s.Bettor1);
        leaderboard.First().TotalCorrect.Should().Be(3);

        (await bets.GetHistoryAsync(s.GroupId, 1, 10, Ct)).Items.Single().UserBets.Should().HaveCount(2);

        // Recalcular saldos do zero não pode apagar as apostas do campeonato.
        await new BetService(db).RecalculateAllBalancesAsync(Ct);
        db.ChangeTracker.Clear();
        (await BalanceAsync(db, s, s.Bettor1)).Should().Be(550);

        // Voltar de Finalizado estorna; finalizar de novo liquida uma vez só.
        Ok(await s.Service.StepBackAsync(s.GroupId, s.Id, Ct));
        db.ChangeTracker.Clear();
        (await BalanceAsync(db, s, s.Bettor1)).Should().Be(0);
        (await BalanceAsync(db, s, s.Bettor2)).Should().Be(0);
        (await db.ChampionshipBets.CountAsync(b => b.ChampionshipId == s.Id && !b.IsResolved)).Should().Be(2);

        Ok(await s.Service.FinalizeAsync(s.GroupId, s.Id, new FinalizeChampionshipDto(null), Ct));
        db.ChangeTracker.Clear();
        (await BalanceAsync(db, s, s.Bettor1)).Should().Be(550);
        (await db.Set<UserBetBalanceEntity>().SingleAsync(b => b.UserId == s.Bettor1)).TotalBets.Should().Be(1);
    }

    [Fact]
    public async Task TimesRedefinidosOuCampeonatoCancelado_DescartamApostasAbertas()
    {
        await using var db = await _fixture.CreateDatabaseAsync(nameof(TimesRedefinidosOuCampeonatoCancelado_DescartamApostasAbertas));
        var s = await SetupAsync(db);
        var bets = new ChampionshipBetService(db);

        await DefineTeamsAsync(s);
        Ok(await bets.PlaceOrUpdateAsync(s.GroupId, s.Id, s.Bettor1, Bet((ChampionshipBetCategory.Champion, await TeamIdAsync(s, 0), 50)), Ct));

        await DefineTeamsAsync(s);
        (await db.ChampionshipBets.CountAsync(b => b.ChampionshipId == s.Id)).Should().Be(0, "a aposta apontava para um time que não existe mais");

        Ok(await bets.PlaceOrUpdateAsync(s.GroupId, s.Id, s.Bettor1, Bet((ChampionshipBetCategory.Champion, await TeamIdAsync(s, 0), 50)), Ct));
        Ok(await s.Service.CancelAsync(s.GroupId, s.Id, Ct));
        (await db.ChampionshipBets.CountAsync(b => b.ChampionshipId == s.Id)).Should().Be(0);
    }

    [Fact]
    public async Task SemMvp_CategoriaMvpNaoExiste()
    {
        await using var db = await _fixture.CreateDatabaseAsync(nameof(SemMvp_CategoriaMvpNaoExiste));
        var s = await SetupAsync(db, MvpMode.None);
        var bets = new ChampionshipBetService(db);
        await DefineTeamsAsync(s);

        var context = Ok(await bets.GetContextAsync(s.GroupId, s.Id, s.Bettor1, Ct));
        context.Categories.Select(c => c.Category).Should().NotContain(ChampionshipBetCategory.Mvp);
        (await bets.PlaceOrUpdateAsync(s.GroupId, s.Id, s.Bettor1, Bet(
                (ChampionshipBetCategory.Champion, await TeamIdAsync(s, 0), 50), (ChampionshipBetCategory.Mvp, s.A1, 50)), Ct))
            .Error.Should().Contain("nao tem MVP");
    }

    // ── Cenário ──────────────────────────────────────────────────────────────

    private sealed record Scenario(
        AppDbContext Db, ChampionshipService Service, Guid GroupId, Guid Id, List<List<Guid>> Lineups,
        Guid Bettor1, Guid Bettor2, Guid UserOfA1, List<PlayerEntity> Players,
        Guid TeamA = default, Guid TeamB = default, Guid TeamC = default)
    {
        public Guid A1 => Lineups[0][0];
        public Guid A2 => Lineups[0][1];
        public Guid GkA => Lineups[0][2];
        public Guid B1 => Lineups[1][0];
        public Guid C1 => Lineups[2][0];
    }

    private static async Task<Scenario> SetupAsync(AppDbContext db, MvpMode mvp = MvpMode.PerChampionship)
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
        var bettor1 = new UserEntity("apostador1", "Ana", "A", "a1@t.com", "hash", null, null);
        var bettor2 = new UserEntity("apostador2", "Beto", "B", "b2@t.com", "hash", null, null);
        db.Users.AddRange(bettor1, bettor2);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var rules = ChampionshipEntity.DefaultRules with { Format = ChampionshipFormat.RoundRobin, WinnerStays = null, MvpMode = mvp };
        var sut = new ChampionshipService(db, new MultiTeamGenerationService(new FakeStatsService([])), new ConquistaProjectionService(db));
        var id = Ok(await sut.CreateAsync(group.Id, new CreateChampionshipDto("Torneio", new DateTime(2026, 12, 20, 13, 0, 0, DateTimeKind.Utc), "Arena", rules), Ct)).Id;
        Ok(await sut.OpenAcceptationAsync(group.Id, id, Ct));
        foreach (var p in players)
            Ok(await sut.RespondInviteAsync(group.Id, id, p.Id, accept: true, Ct));

        var lineups = Enumerable.Range(0, 3).Select(t => players.Skip(t * 3).Take(3).Select(p => p.Id).ToList()).ToList();
        return new Scenario(db, sut, group.Id, id, lineups, bettor1.Id, bettor2.Id, players[0].UserId!.Value, players);
    }

    private static async Task DefineTeamsAsync(Scenario s) =>
        Ok(await s.Service.DefineTeamsAsync(s.GroupId, s.Id, new DefineTeamsDto(s.Lineups
            .Select((ids, t) => new DefineTeamDto($"Time {t}", null, ids, ids[2])).ToList()), Ct));

    private static async Task<Guid> TeamIdAsync(Scenario s, int index) =>
        Ok(await s.Service.GetAsync(s.GroupId, s.Id, null, Ct)).Teams.Single(t => t.Name == $"Time {index}").Id;

    private static async Task PlayAndFinalizeAsync(Scenario s)
    {
        await GameAsync(s, s.TeamA, s.TeamB, [(s.A1, null), (s.A1, s.A2)]);
        await GameAsync(s, s.TeamA, s.TeamC, [(s.A1, null), (s.C1, null)]);
        await GameAsync(s, s.TeamB, s.TeamC, [(s.B1, null), (s.B1, null), (s.B1, null)]);
        Ok(await s.Service.FinishAsync(s.GroupId, s.Id, Ct));

        foreach (var voter in s.Players.Where(p => p.Id != s.A1))
            await s.Service.VoteAsync(s.GroupId, s.Id, voter.UserId!.Value, new ChampionshipVoteDto(s.A1, null), Ct);

        Ok(await s.Service.FinalizeAsync(s.GroupId, s.Id, new FinalizeChampionshipDto(null), Ct));
    }

    private static async Task GameAsync(Scenario s, Guid teamA, Guid teamB, (Guid Scorer, Guid? Assist)[] goals)
    {
        var gameId = Ok(await s.Service.CreateGameAsync(s.GroupId, s.Id, new CreateChampionshipGameDto(teamA, teamB), Ct)).GameId;
        foreach (var (scorer, assist) in goals)
            Ok(await s.Service.AddGoalAsync(s.GroupId, s.Id, gameId, new ChampionshipGoalDto(scorer, assist, false, null), Ct));
        Ok(await s.Service.EndGameAsync(s.GroupId, s.Id, gameId, new EndChampionshipGameDto(null), Ct));
    }

    private static PlaceChampionshipBetDto Bet(params (ChampionshipBetCategory Category, Guid Id, int Wager)[] selections) =>
        new(selections.Select(x => new PlaceChampionshipBetSelectionDto(x.Category, x.Id, x.Wager)).ToList());

    private static IReadOnlyList<Guid> Winners(ChampionshipBetContextDto context, ChampionshipBetCategory category) =>
        context.Categories.Single(c => c.Category == category).WinnerIds!;

    private static async Task<int> BalanceAsync(AppDbContext db, Scenario s, Guid userId) =>
        await db.Set<UserBetBalanceEntity>().Where(b => b.GroupId == s.GroupId && b.UserId == userId)
            .Select(b => (int?)b.Balance).FirstOrDefaultAsync() ?? 0;

    private static T Ok<T>(Result<T> result)
    {
        result.Success.Should().BeTrue(result.Error);
        return result.Data!;
    }

    private static void Ok(Result result) => result.Success.Should().BeTrue(result.Error);
}
