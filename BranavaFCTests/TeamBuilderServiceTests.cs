using BratnavaFC.Application.Services;
using BratnavaFC.Domain.Common;
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
            int scoreB)
    {
        var all = teamAPlayers.Concat(teamBPlayers).ToList();

        var match = new MatchEntity(groupId, DateTime.UtcNow, "Arena");
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
        db.Players.AddRange(p1, p2);
        await db.SaveChangesAsync();

        var (match, mpIds) =
            await TBBuilders.SeedFinalizedMatchAsync(db, group.Id, [p1], [p2], 2, 1);

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
        db.Players.AddRange(p1, p2);
        await db.SaveChangesAsync();

        var (match, mpIds) =
            await TBBuilders.SeedFinalizedMatchAsync(db, group.Id, [p1], [p2], 1, 0);

        // Own goal by p1 must be ignored
        db.Goals.Add(new GoalEntity(match.Id, group.Id, mpIds[p1.Id], null, null, isOwnGoal: true));
        await db.SaveChangesAsync();

        var sut    = Sut(db);
        var result = await sut.GetStatsAsync(group.Id, [p1.Id, p2.Id], CancellationToken.None);

        result.Data!.GoalsScoredByPlayers.Should().Be(0, "gols contra não são contados");
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
}
