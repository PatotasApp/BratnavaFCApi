using BratnavaFC.Application.Services;
using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos.TeamBuilder;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;
using BratnavaFC.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BranavaFC.Tests;

// ── Helpers ───────────────────────────────────────────────────────────────────

file static class TBBuilders
{
    public static GroupEntity  MakeGroup()  => new("Patota", null, Guid.NewGuid());
    public static UserEntity   MakeUser(string tag = "u") =>
        new(tag, "Nome", "Sobrenome", $"{tag}@mail.com", "hash", null, null);
    public static PlayerEntity MakePlayer(string name, Guid groupId, Guid? userId = null) =>
        new(name, userId, groupId, 5m, false, false, Status.Active);

    /// <summary>
    /// Seeds a finalized match where <paramref name="teamAPlayers"/> play for Team A
    /// and <paramref name="teamBPlayers"/> for Team B.
    /// Returns the match and a dictionary: PlayerId → MatchPlayerId (needed to create GoalEntities).
    /// </summary>
    public static async Task<(MatchEntity match, Dictionary<Guid, Guid> playerToMpId)>
        SeedFinalizedMatchAsync(
            AppDbContext db,
            Guid groupId,
            List<PlayerEntity> teamAPlayers,
            List<PlayerEntity> teamBPlayers,
            int scoreA,
            int scoreB,
            DateTime? playedAt = null)
    {
        var all = teamAPlayers.Concat(teamBPlayers).ToList();

        var match = new MatchEntity(groupId, playedAt ?? DateTime.UtcNow, "Arena");
        db.Matches.Add(match);

        foreach (var p in all)
            match.AddPlayer(new MatchPlayerEntity(p.Id), p);

        await db.SaveChangesAsync();

        match.OpenAcceptation();
        foreach (var p in all) match.AcceptInvite(p.Id);

        match.GoToMatchMaking();
        match.AssignTeams(
            teamAPlayers.Select(p => p.Id).ToList(),
            teamBPlayers.Select(p => p.Id).ToList());

        match.Start();
        match.End();
        match.GoToPostGame();
        match.SetScore(scoreA, scoreB);
        match.FinalizeByVotes();

        await db.SaveChangesAsync();

        // Reload MatchPlayers so we have their DB-assigned IDs.
        var mps = await db.Set<MatchPlayerEntity>()
            .Where(mp => mp.MatchId == match.Id)
            .ToListAsync();

        var playerToMpId = mps.ToDictionary(mp => mp.PlayerId, mp => mp.Id);
        return (match, playerToMpId);
    }
}

// ── Tests ─────────────────────────────────────────────────────────────────────

public class TeamBuilderServiceTests
{
    private static TeamBuilderService Sut(AppDbContext db) => new(db);

    // ── Validation ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetStatsAsync_WithTooFewPlayers_ReturnsBadRequest()
    {
        await using var db = DbContextFactory.Create(nameof(GetStatsAsync_WithTooFewPlayers_ReturnsBadRequest));
        var sut = Sut(db);

        var result = await sut.GetStatsAsync(Guid.NewGuid(), [Guid.NewGuid()], CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.BadRequest);
        result.Error.Should().Contain("2");
    }

    [Fact]
    public async Task GetStatsAsync_WithTooManyPlayers_ReturnsBadRequest()
    {
        await using var db = DbContextFactory.Create(nameof(GetStatsAsync_WithTooManyPlayers_ReturnsBadRequest));
        var sut = Sut(db);

        var sixIds = Enumerable.Range(0, 6).Select(_ => Guid.NewGuid()).ToList();
        var result = await sut.GetStatsAsync(Guid.NewGuid(), sixIds, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.BadRequest);
    }

    [Fact]
    public async Task GetStatsAsync_WithPlayerNotInGroup_ReturnsBadRequest()
    {
        await using var db = DbContextFactory.Create(nameof(GetStatsAsync_WithPlayerNotInGroup_ReturnsBadRequest));

        var group = TBBuilders.MakeGroup();
        db.Groups.Add(group);
        var p1 = TBBuilders.MakePlayer("P1", group.Id);
        db.Players.Add(p1);
        await db.SaveChangesAsync();

        var sut = Sut(db);
        // p1 is in group, but p2 is a random Guid not in it
        var result = await sut.GetStatsAsync(group.Id, [p1.Id, Guid.NewGuid()], CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Status.Should().Be(ResultStatus.BadRequest);
        result.Error.Should().Contain("grupo");
    }

    [Fact]
    public async Task GetStatsAsync_DeduplicatesPlayerIds()
    {
        await using var db = DbContextFactory.Create(nameof(GetStatsAsync_DeduplicatesPlayerIds));

        var group = TBBuilders.MakeGroup();
        db.Groups.Add(group);
        var p1 = TBBuilders.MakePlayer("P1", group.Id);
        var p2 = TBBuilders.MakePlayer("P2", group.Id);
        db.Players.AddRange(p1, p2);
        await db.SaveChangesAsync();

        var sut = Sut(db);
        // Send p1 twice — after dedup it becomes [p1, p2] which is valid
        var result = await sut.GetStatsAsync(group.Id, [p1.Id, p1.Id, p2.Id], CancellationToken.None);

        // The request is valid after dedup; no "invalid player" error
        result.Status.Should().NotBe(ResultStatus.BadRequest);
    }

    // ── NeverPlayedTogether ───────────────────────────────────────────────────

    [Fact]
    public async Task GetStatsAsync_WhenNoMatchTogether_SetsNeverPlayedTogether()
    {
        await using var db = DbContextFactory.Create(nameof(GetStatsAsync_WhenNoMatchTogether_SetsNeverPlayedTogether));

        var group = TBBuilders.MakeGroup();
        db.Groups.Add(group);
        var p1 = TBBuilders.MakePlayer("A", group.Id);
        var p2 = TBBuilders.MakePlayer("B", group.Id);
        db.Players.AddRange(p1, p2);
        await db.SaveChangesAsync();

        var sut    = Sut(db);
        var result = await sut.GetStatsAsync(group.Id, [p1.Id, p2.Id], CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Data!.NeverPlayedTogether.Should().BeTrue();
        result.Data.Players.Should().HaveCount(2);
    }

    // ── Match stats ───────────────────────────────────────────────────────────

    [Fact]
    public async Task GetStatsAsync_WhenPlayedTogether_ReturnsTotalMatches()
    {
        await using var db = DbContextFactory.Create(nameof(GetStatsAsync_WhenPlayedTogether_ReturnsTotalMatches));

        var group = TBBuilders.MakeGroup();
        db.Groups.Add(group);
        var p1 = TBBuilders.MakePlayer("A", group.Id);
        var p2 = TBBuilders.MakePlayer("B", group.Id);
        var p3 = TBBuilders.MakePlayer("C", group.Id); // filler para time B
        db.Players.AddRange(p1, p2, p3);
        await db.SaveChangesAsync();

        // Dois jogos onde p1+p2 jogaram juntos (p3 faz número no time adversário)
        await TBBuilders.SeedFinalizedMatchAsync(db, group.Id, [p1, p2], [p3], 2, 1);
        await TBBuilders.SeedFinalizedMatchAsync(db, group.Id, [p1, p2], [p3], 0, 0);

        var sut    = Sut(db);
        var result = await sut.GetStatsAsync(group.Id, [p1.Id, p2.Id], CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Data!.NeverPlayedTogether.Should().BeFalse();
        result.Data.TotalMatches.Should().Be(2);
    }

    [Fact]
    public async Task GetStatsAsync_CountsWinDrawLossCorrectly()
    {
        await using var db = DbContextFactory.Create(nameof(GetStatsAsync_CountsWinDrawLossCorrectly));

        var group = TBBuilders.MakeGroup();
        db.Groups.Add(group);
        var p1 = TBBuilders.MakePlayer("A", group.Id);
        var p2 = TBBuilders.MakePlayer("B", group.Id);
        // Extra player to form the opposing team
        var p3 = TBBuilders.MakePlayer("C", group.Id);
        db.Players.AddRange(p1, p2, p3);
        await db.SaveChangesAsync();

        // p1+p2 on Team A in all matches; dominant=TeamA
        // Win:  TeamA 3 – TeamB 1
        // Draw: TeamA 2 – TeamB 2
        // Loss: TeamA 0 – TeamB 1
        await TBBuilders.SeedFinalizedMatchAsync(db, group.Id, [p1, p2], [p3], 3, 1);
        await TBBuilders.SeedFinalizedMatchAsync(db, group.Id, [p1, p2], [p3], 2, 2);
        await TBBuilders.SeedFinalizedMatchAsync(db, group.Id, [p1, p2], [p3], 0, 1);

        var sut    = Sut(db);
        var result = await sut.GetStatsAsync(group.Id, [p1.Id, p2.Id], CancellationToken.None);

        result.Data!.Wins.Should().Be(1);
        result.Data.Draws.Should().Be(1);
        result.Data.Losses.Should().Be(1);
        result.Data.GoalsScored.Should().Be(5);    // 3+2+0
        result.Data.GoalsConceded.Should().Be(4);  // 1+2+1
    }

    [Fact]
    public async Task GetStatsAsync_ExcludesMatchWhereOnlyOneOfTwoPlayed()
    {
        await using var db = DbContextFactory.Create(nameof(GetStatsAsync_ExcludesMatchWhereOnlyOneOfTwoPlayed));

        var group = TBBuilders.MakeGroup();
        db.Groups.Add(group);
        var p1 = TBBuilders.MakePlayer("A", group.Id);
        var p2 = TBBuilders.MakePlayer("B", group.Id);
        var p3 = TBBuilders.MakePlayer("C", group.Id);
        db.Players.AddRange(p1, p2, p3);
        await db.SaveChangesAsync();

        // Match with only p1+p3 (p2 absent) must NOT be counted
        await TBBuilders.SeedFinalizedMatchAsync(db, group.Id, [p1], [p3], 1, 0);
        // Match with both p1+p2 must be counted
        await TBBuilders.SeedFinalizedMatchAsync(db, group.Id, [p1, p2], [p3], 2, 1);

        var sut    = Sut(db);
        var result = await sut.GetStatsAsync(group.Id, [p1.Id, p2.Id], CancellationToken.None);

        result.Data!.TotalMatches.Should().Be(1, "só a partida onde ambos jogaram conta");
    }

    // ── Goals & assists ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetStatsAsync_CountsGoalsScoredBySelectedPlayers()
    {
        await using var db = DbContextFactory.Create(nameof(GetStatsAsync_CountsGoalsScoredBySelectedPlayers));

        var group = TBBuilders.MakeGroup();
        db.Groups.Add(group);
        var p1 = TBBuilders.MakePlayer("A", group.Id);
        var p2 = TBBuilders.MakePlayer("B", group.Id);
        var p3 = TBBuilders.MakePlayer("C", group.Id);
        db.Players.AddRange(p1, p2, p3);
        await db.SaveChangesAsync();

        var (match, mpIds) =
            await TBBuilders.SeedFinalizedMatchAsync(db, group.Id, [p1, p2], [p3], 3, 1);

        // 2 goals by p1, 1 goal by p2, 1 goal by p3 (not selected but in match)
        db.Goals.AddRange(
            new GoalEntity(match.Id, group.Id, mpIds[p1.Id], null,    null, false),
            new GoalEntity(match.Id, group.Id, mpIds[p1.Id], null,    null, false),
            new GoalEntity(match.Id, group.Id, mpIds[p2.Id], null,    null, false),
            new GoalEntity(match.Id, group.Id, mpIds[p3.Id], null,    null, false)); // p3 not selected
        await db.SaveChangesAsync();

        var sut    = Sut(db);
        var result = await sut.GetStatsAsync(group.Id, [p1.Id, p2.Id], CancellationToken.None);

        result.Data!.GoalsScoredByPlayers.Should().Be(3, "só os gols dos jogadores selecionados são contados");
    }

    [Fact]
    public async Task GetStatsAsync_ComputesAssistPairsCorrectly()
    {
        await using var db = DbContextFactory.Create(nameof(GetStatsAsync_ComputesAssistPairsCorrectly));

        var group = TBBuilders.MakeGroup();
        db.Groups.Add(group);
        var p1 = TBBuilders.MakePlayer("A", group.Id);
        var p2 = TBBuilders.MakePlayer("B", group.Id);
        var p3 = TBBuilders.MakePlayer("C", group.Id); // adversário
        db.Players.AddRange(p1, p2, p3);
        await db.SaveChangesAsync();

        var (match, mpIds) =
            await TBBuilders.SeedFinalizedMatchAsync(db, group.Id, [p1, p2], [p3], 3, 1);

        // p2 assists p1 twice; p1 assists p2 once
        db.Goals.AddRange(
            new GoalEntity(match.Id, group.Id, mpIds[p1.Id], mpIds[p2.Id], null, false),
            new GoalEntity(match.Id, group.Id, mpIds[p1.Id], mpIds[p2.Id], null, false),
            new GoalEntity(match.Id, group.Id, mpIds[p2.Id], mpIds[p1.Id], null, false));
        await db.SaveChangesAsync();

        var sut    = Sut(db);
        var result = await sut.GetStatsAsync(group.Id, [p1.Id, p2.Id], CancellationToken.None);

        result.Data!.AssistPairs.Should().HaveCount(2);

        var topPair = result.Data.AssistPairs[0];
        topPair.AssisterId.Should().Be(p2.Id, "p2 assistiu p1 duas vezes");
        topPair.ScorerId.Should().Be(p1.Id);
        topPair.Count.Should().Be(2);

        var secondPair = result.Data.AssistPairs[1];
        secondPair.AssisterId.Should().Be(p1.Id);
        secondPair.ScorerId.Should().Be(p2.Id);
        secondPair.Count.Should().Be(1);
    }

    [Fact]
    public async Task GetStatsAsync_IgnoresOwnGoals()
    {
        await using var db = DbContextFactory.Create(nameof(GetStatsAsync_IgnoresOwnGoals));

        var group = TBBuilders.MakeGroup();
        db.Groups.Add(group);
        var p1 = TBBuilders.MakePlayer("A", group.Id);
        var p2 = TBBuilders.MakePlayer("B", group.Id);
        var p3 = TBBuilders.MakePlayer("C", group.Id);
        db.Players.AddRange(p1, p2, p3);
        await db.SaveChangesAsync();

        var (match, mpIds) =
            await TBBuilders.SeedFinalizedMatchAsync(db, group.Id, [p1, p2], [p3], 0, 1);

        // Own goal by p1 must be ignored
        db.Goals.Add(new GoalEntity(match.Id, group.Id, mpIds[p1.Id], null, null, isOwnGoal: true));
        await db.SaveChangesAsync();

        var sut    = Sut(db);
        var result = await sut.GetStatsAsync(group.Id, [p1.Id, p2.Id], CancellationToken.None);

        result.Data!.NeverPlayedTogether.Should().BeFalse();
        result.Data.GoalsScoredByPlayers.Should().Be(0, "gols contra não são contados");
        result.Data.AssistPairs.Should().BeEmpty();
    }

    // ── Player info ───────────────────────────────────────────────────────────

    [Fact]
    public async Task GetStatsAsync_ReturnsGoalkeeperFlagCorrectly()
    {
        await using var db = DbContextFactory.Create(nameof(GetStatsAsync_ReturnsGoalkeeperFlagCorrectly));

        var group = TBBuilders.MakeGroup();
        db.Groups.Add(group);
        var gk = new PlayerEntity("GK", null, group.Id, 5m, isGoalkeeper: true,  isGuest: false, status: Status.Active);
        var lp = new PlayerEntity("LP", null, group.Id, 5m, isGoalkeeper: false, isGuest: false, status: Status.Active);
        db.Players.AddRange(gk, lp);
        await db.SaveChangesAsync();

        var sut    = Sut(db);
        var result = await sut.GetStatsAsync(group.Id, [gk.Id, lp.Id], CancellationToken.None);

        result.Data!.Players.Should().HaveCount(2);
        result.Data.Players.First(p => p.Id == gk.Id).IsGoalkeeper.Should().BeTrue();
        result.Data.Players.First(p => p.Id == lp.Id).IsGoalkeeper.Should().BeFalse();
    }

    // ── "Juntos" = mesmo time ─────────────────────────────────────────────────

    private static async Task<(AppDbContext db, GroupEntity group, PlayerEntity a, PlayerEntity b, PlayerEntity c, PlayerEntity d)>
        FourPlayersAsync(string name)
    {
        var db = DbContextFactory.Create(name);
        var group = TBBuilders.MakeGroup();
        db.Groups.Add(group);
        var a = TBBuilders.MakePlayer("Ana Souza", group.Id);
        var b = TBBuilders.MakePlayer("Beto", group.Id);
        var c = TBBuilders.MakePlayer("Caio", group.Id);
        var d = TBBuilders.MakePlayer("Duda", group.Id);
        db.Players.AddRange(a, b, c, d);
        await db.SaveChangesAsync();
        return (db, group, a, b, c, d);
    }

    [Fact]
    public async Task GetStatsAsync_AdversariosNaMesmaPartida_NaoContamComoJuntos()
    {
        var (db, group, a, b, c, d) = await FourPlayersAsync(nameof(GetStatsAsync_AdversariosNaMesmaPartida_NaoContamComoJuntos));
        await using var _ = db;

        // a e b em times opostos: não é "juntos".
        await TBBuilders.SeedFinalizedMatchAsync(db, group.Id, [a, c], [b, d], 3, 0);
        // a e b no mesmo time.
        await TBBuilders.SeedFinalizedMatchAsync(db, group.Id, [a, b], [c, d], 1, 2);

        var result = await Sut(db).GetStatsAsync(group.Id, [a.Id, b.Id], CancellationToken.None);

        result.Data!.TotalMatches.Should().Be(1);
        result.Data.OppositeTeamsMatches.Should().Be(1);
        result.Data.Losses.Should().Be(1);
        result.Data.Wins.Should().Be(0, "a vitória de a contra b não é do time deles");
        result.Data.Curiosities.Should().Contain(x => x.Contains("times opostos"));
    }

    [Fact]
    public async Task GetStatsAsync_SoEmTimesOpostos_NuncaJogaramJuntos()
    {
        var (db, group, a, b, c, d) = await FourPlayersAsync(nameof(GetStatsAsync_SoEmTimesOpostos_NuncaJogaramJuntos));
        await using var _ = db;

        await TBBuilders.SeedFinalizedMatchAsync(db, group.Id, [a, c], [b, d], 2, 2);

        var result = await Sut(db).GetStatsAsync(group.Id, [a.Id, b.Id], CancellationToken.None);

        result.Data!.NeverPlayedTogether.Should().BeTrue();
        result.Data.OppositeTeamsMatches.Should().Be(1);
        result.Data.PlayerBreakdown.Should().OnlyContain(p => p.OverallGames == 1 && p.OverallPointsPct == 33);
    }

    [Fact]
    public async Task GetStatsAsync_PartidaSemRespostaAoConvite_ContaPeloTimeEscalado()
    {
        var (db, group, a, b, c, _) = await FourPlayersAsync(nameof(GetStatsAsync_PartidaSemRespostaAoConvite_ContaPeloTimeEscalado));
        await using var __ = db;

        var (match, _) = await TBBuilders.SeedFinalizedMatchAsync(db, group.Id, [a, b], [c], 4, 1);

        // Partidas importadas (jan/2026) não têm resposta ao convite: só o time escalado.
        foreach (var mp in db.Set<MatchPlayerEntity>().Where(mp => mp.MatchId == match.Id))
            mp.RestoreInviteResponse(InviteResponse.None);
        await db.SaveChangesAsync();

        var result = await Sut(db).GetStatsAsync(group.Id, [a.Id, b.Id], CancellationToken.None);

        result.Data!.NeverPlayedTogether.Should().BeFalse();
        result.Data.TotalMatches.Should().Be(1);
        result.Data.Wins.Should().Be(1);
    }

    [Fact]
    public async Task GetStatsAsync_QuemNaoApareceu_NaoContaPresenca()
    {
        var (db, group, a, b, c, _) = await FourPlayersAsync(nameof(GetStatsAsync_QuemNaoApareceu_NaoContaPresenca));
        await using var __ = db;

        var (match, _) = await TBBuilders.SeedFinalizedMatchAsync(db, group.Id, [a, b], [c], 1, 0);
        db.Set<MatchPlayerEntity>().Single(mp => mp.MatchId == match.Id && mp.PlayerId == b.Id).SetDidNotPlay(true);
        await db.SaveChangesAsync();

        var result = await Sut(db).GetStatsAsync(group.Id, [a.Id, b.Id], CancellationToken.None);

        result.Data!.NeverPlayedTogether.Should().BeTrue();
        result.Data.OppositeTeamsMatches.Should().Be(0);
    }

    // ── Aproveitamento ────────────────────────────────────────────────────────

    [Fact]
    public async Task GetStatsAsync_AproveitamentoJuntosEMediaIndividual()
    {
        var (db, group, a, b, c, d) = await FourPlayersAsync(nameof(GetStatsAsync_AproveitamentoJuntosEMediaIndividual));
        await using var _ = db;
        var t0 = new DateTime(2026, 3, 1, 23, 0, 0, DateTimeKind.Utc);

        await TBBuilders.SeedFinalizedMatchAsync(db, group.Id, [a, b], [c], 3, 1, t0);              // V juntos
        await TBBuilders.SeedFinalizedMatchAsync(db, group.Id, [a, b], [c], 2, 2, t0.AddDays(7));    // E juntos
        await TBBuilders.SeedFinalizedMatchAsync(db, group.Id, [a], [b, c], 0, 1, t0.AddDays(14));   // a perde, b vence (opostos)
        await TBBuilders.SeedFinalizedMatchAsync(db, group.Id, [a, d], [c], 0, 2, t0.AddDays(21));   // a perde sem b

        var r = (await Sut(db).GetStatsAsync(group.Id, [a.Id, b.Id], CancellationToken.None)).Data!;

        r.TotalMatches.Should().Be(2);
        r.PointsPct.Should().Be(67, "V+E = 4 de 6 pontos");
        var pa = r.PlayerBreakdown.Single(p => p.Id == a.Id);
        var pb = r.PlayerBreakdown.Single(p => p.Id == b.Id);
        pa.OverallGames.Should().Be(4);
        pa.OverallPointsPct.Should().Be(33, "V, E, D, D = 4 de 12");
        pb.OverallGames.Should().Be(3);
        pb.OverallPointsPct.Should().Be(78, "V, E, V = 7 de 9");
        r.AverageIndividualPointsPct.Should().Be(56, "(33 + 78) / 2 = 55,5");
        r.GoalDifference.Should().Be(2);
        r.GoalsPerMatch.Should().Be(2.5);
        r.BiggestWin!.GoalsFor.Should().Be(3);
        r.WorstLoss.Should().BeNull();
        r.LastMatch!.Result.Should().Be(0);
        r.CurrentStreak!.Result.Should().Be(0);
        r.CurrentStreak.Count.Should().Be(1);
        r.Matches.First().PlayedAt.Should().BeAfter(r.Matches.Last().PlayedAt, "mais recente primeiro");
    }

    [Fact]
    public async Task GetStatsAsync_SequenciaEMaiorVitoria()
    {
        var (db, group, a, b, c, _) = await FourPlayersAsync(nameof(GetStatsAsync_SequenciaEMaiorVitoria));
        await using var __ = db;
        var t0 = new DateTime(2026, 4, 1, 23, 0, 0, DateTimeKind.Utc);

        await TBBuilders.SeedFinalizedMatchAsync(db, group.Id, [a, b], [c], 0, 3, t0);
        await TBBuilders.SeedFinalizedMatchAsync(db, group.Id, [a, b], [c], 5, 1, t0.AddDays(1));
        await TBBuilders.SeedFinalizedMatchAsync(db, group.Id, [a, b], [c], 2, 1, t0.AddDays(2));
        await TBBuilders.SeedFinalizedMatchAsync(db, group.Id, [a, b], [c], 4, 0, t0.AddDays(3));

        var r = (await Sut(db).GetStatsAsync(group.Id, [a.Id, b.Id], CancellationToken.None)).Data!;

        r.CurrentStreak!.Result.Should().Be(1);
        r.CurrentStreak.Count.Should().Be(3);
        r.BestWinStreak.Should().Be(3);
        r.BiggestWin!.GoalsFor.Should().Be(5, "5x1 e 4x0 empatam no saldo; vale o que fez mais gols");
        r.WorstLoss!.GoalsAgainst.Should().Be(3);
        r.Curiosities.Should().Contain(x => x.Contains("**3 vitórias seguidas**"));
    }

    // ── Filtros ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetStatsAsync_FiltrosDeTemporadaDataEUltimosJogos()
    {
        var (db, group, a, b, c, _) = await FourPlayersAsync(nameof(GetStatsAsync_FiltrosDeTemporadaDataEUltimosJogos));
        await using var __ = db;

        // 31/12/2025 21h em Brasília = 01/01/2026 00h UTC: ainda é temporada 2025.
        await TBBuilders.SeedFinalizedMatchAsync(db, group.Id, [a, b], [c], 1, 0, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        await TBBuilders.SeedFinalizedMatchAsync(db, group.Id, [a, b], [c], 2, 0, new DateTime(2026, 5, 10, 23, 0, 0, DateTimeKind.Utc));
        await TBBuilders.SeedFinalizedMatchAsync(db, group.Id, [a, b], [c], 0, 1, new DateTime(2026, 8, 20, 23, 0, 0, DateTimeKind.Utc));
        await TBBuilders.SeedFinalizedMatchAsync(db, group.Id, [a, b], [c], 1, 1, new DateTime(2026, 9, 20, 23, 0, 0, DateTimeKind.Utc));

        async Task<TeamBuilderStatsDto> Run(Action<TeamBuilderRequestDto> set)
        {
            var req = new TeamBuilderRequestDto { PlayerIds = [a.Id, b.Id] };
            set(req);
            return (await Sut(db).GetStatsAsync(group.Id, req, CancellationToken.None)).Data!;
        }

        (await Run(_ => { })).TotalMatches.Should().Be(4);
        (await Run(f => f.Season = 2026)).TotalMatches.Should().Be(3);
        (await Run(f => f.Season = 2025)).TotalMatches.Should().Be(1);
        (await Run(f => f.From = new DateOnly(2026, 8, 16))).TotalMatches.Should().Be(2);
        (await Run(f => f.To = new DateOnly(2026, 5, 10))).TotalMatches.Should().Be(2);

        var last2 = await Run(f => f.LastMatches = 2);
        last2.PeriodMatches.Should().Be(2);
        last2.Losses.Should().Be(1);
        last2.Draws.Should().Be(1);

        var players = (await Sut(db).GetPlayersAsync(group.Id, new TeamBuilderFilterDto { Season = 2026 }, CancellationToken.None)).Data!;
        players.Seasons.Should().Equal(2026, 2025);
        players.PeriodMatches.Should().Be(3);
        players.Players.Single(p => p.Id == a.Id).Games.Should().Be(3);
    }

    // ── Gols, passes, adversários ─────────────────────────────────────────────

    [Fact]
    public async Task GetStatsAsync_GolContraNaoContaAssistenciaMasEntraNosGolsDoTime()
    {
        var (db, group, a, b, c, _) = await FourPlayersAsync(nameof(GetStatsAsync_GolContraNaoContaAssistenciaMasEntraNosGolsDoTime));
        await using var __ = db;

        var (match, mp) = await TBBuilders.SeedFinalizedMatchAsync(db, group.Id, [a, b], [c], 3, 0);
        db.Goals.AddRange(
            new GoalEntity(match.Id, group.Id, mp[a.Id], mp[b.Id], null, false),            // b → a
            new GoalEntity(match.Id, group.Id, mp[c.Id], mp[b.Id], null, isOwnGoal: true),  // contra de c; a "assistência" de b não vale
            new GoalEntity(match.Id, group.Id, mp[b.Id], null,     null, false));
        await db.SaveChangesAsync();

        var r = (await Sut(db).GetStatsAsync(group.Id, [a.Id, b.Id], CancellationToken.None)).Data!;

        r.GoalsScoredByPlayers.Should().Be(2);
        r.TeamRegisteredGoals.Should().Be(3);
        r.PlayerBreakdown.Single(p => p.Id == b.Id).Assists.Should().Be(1);
        r.AssistPairs.Should().ContainSingle(p => p.AssisterId == b.Id && p.ScorerId == a.Id && p.Count == 1);
        r.Curiosities.Should().Contain(x => x.Contains("**67%** dos gols registrados"));
    }

    [Fact]
    public async Task GetStatsAsync_PedraNoSapatoEFregues_ComMinimoDeTresJogos()
    {
        var (db, group, a, b, c, d) = await FourPlayersAsync(nameof(GetStatsAsync_PedraNoSapatoEFregues_ComMinimoDeTresJogos));
        await using var _ = db;
        var t0 = new DateTime(2026, 6, 1, 23, 0, 0, DateTimeKind.Utc);

        for (var i = 0; i < 3; i++)
            await TBBuilders.SeedFinalizedMatchAsync(db, group.Id, [a, b], [c], 0, 1, t0.AddDays(i));   // c: 3 vitórias contra eles
        for (var i = 3; i < 5; i++)
            await TBBuilders.SeedFinalizedMatchAsync(db, group.Id, [a, b], [d], 2, 0, t0.AddDays(i));   // d: só 2 jogos

        var r = (await Sut(db).GetStatsAsync(group.Id, [a.Id, b.Id], CancellationToken.None)).Data!;

        r.Nemesis!.PlayerId.Should().Be(c.Id);
        r.Nemesis.Losses.Should().Be(3);
        r.FavoriteVictim.Should().BeNull("d só enfrentou eles 2 vezes");
    }

    [Fact]
    public async Task GetStatsAsync_NomeCurto_PrimeiroNomeSalvoDuplicado()
    {
        await using var db = DbContextFactory.Create(nameof(GetStatsAsync_NomeCurto_PrimeiroNomeSalvoDuplicado));
        var group = TBBuilders.MakeGroup();
        db.Groups.Add(group);
        var p1 = TBBuilders.MakePlayer("Pedro Roweder", group.Id);
        var p2 = TBBuilders.MakePlayer("Pedro Silva", group.Id);
        var p3 = TBBuilders.MakePlayer("Andrei Salvador", group.Id);
        db.Players.AddRange(p1, p2, p3);
        await db.SaveChangesAsync();

        var r = (await Sut(db).GetStatsAsync(group.Id, [p1.Id, p3.Id], CancellationToken.None)).Data!;

        r.Players.Single(p => p.Id == p1.Id).ShortName.Should().Be("Pedro Roweder");
        r.Players.Single(p => p.Id == p3.Id).ShortName.Should().Be("Andrei");
    }
}
