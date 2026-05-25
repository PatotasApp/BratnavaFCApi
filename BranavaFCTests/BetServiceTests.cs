using BratnavaFC.Application.Services;
using BratnavaFC.Domain.Dtos;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BranavaFC.Tests;

// ── Helpers ───────────────────────────────────────────────────────────────────

file static class BetBuilders
{
    public static GroupEntity  MakeGroup()  => new("Patota FC", null, Guid.NewGuid());
    public static UserEntity   MakeUser(string name = "u1") =>
        new(name, "Nome", "Sobrenome", $"{name}@mail.com", "hash", null, null);
    public static PlayerEntity MakePlayer(Guid groupId, Guid? userId = null) =>
        new("Jogador", userId, groupId, 5m, false, userId == null, Status.Active);

    public static MatchEntity MakeMatchInMatchMaking(Guid groupId)
    {
        var m = new MatchEntity(groupId, DateTime.UtcNow.AddHours(2), "Arena");
        // Bypass entity state machine — tests only need the DB row in MatchMaking status.
        typeof(MatchEntity).GetProperty(nameof(MatchEntity.Status))!
            .SetValue(m, MatchStatus.MatchMaking);
        return m;
    }

    public static void SetMatchScore(MatchEntity match, int a, int b)
    {
        typeof(MatchEntity).GetProperty(nameof(MatchEntity.TeamAGoals))!.SetValue(match, (int?)a);
        typeof(MatchEntity).GetProperty(nameof(MatchEntity.TeamBGoals))!.SetValue(match, (int?)b);
    }

    public static BetService MakeSut(BratnavaFC.Infrastructure.Data.AppDbContext db) => new(db);

    /// <summary>
    /// Seeds one player in TeamA and one in TeamB for the given match so that the
    /// "teams must be set" gate in PlaceOrUpdateBetAsync / DeleteBetAsync is satisfied.
    /// </summary>
    public static async Task SetTeamsOnMatchAsync(
        BratnavaFC.Infrastructure.Data.AppDbContext db,
        MatchEntity match,
        Guid groupId)
    {
        var p1 = MakePlayer(groupId);
        var p2 = MakePlayer(groupId);
        db.Players.Add(p1);
        db.Players.Add(p2);
        await db.SaveChangesAsync();

        var mp1 = new MatchPlayerEntity(p1.Id);
        typeof(MatchPlayerEntity).GetProperty(nameof(MatchPlayerEntity.MatchId))!.SetValue(mp1, match.Id);
        typeof(MatchPlayerEntity).GetProperty(nameof(MatchPlayerEntity.GroupId))!.SetValue(mp1, groupId);
        typeof(MatchPlayerEntity).GetProperty(nameof(MatchPlayerEntity.Team))!  .SetValue(mp1, (short)1);

        var mp2 = new MatchPlayerEntity(p2.Id);
        typeof(MatchPlayerEntity).GetProperty(nameof(MatchPlayerEntity.MatchId))!.SetValue(mp2, match.Id);
        typeof(MatchPlayerEntity).GetProperty(nameof(MatchPlayerEntity.GroupId))!.SetValue(mp2, groupId);
        typeof(MatchPlayerEntity).GetProperty(nameof(MatchPlayerEntity.Team))!  .SetValue(mp2, (short)2);

        db.Set<MatchPlayerEntity>().Add(mp1);
        db.Set<MatchPlayerEntity>().Add(mp2);
        await db.SaveChangesAsync();
    }

    public static PlaceMatchBetDto SimpleDto(string winner = "TeamA", int wager = 50) =>
        new(new List<BetSelectionRequestDto>
        {
            new("WinningTeam", winner, wager),
        });
}

// ── PlaceOrUpdateBetAsync ─────────────────────────────────────────────────────

public class BetService_PlaceOrUpdateTests
{
    [Fact]
    public async Task HappyPath_ShouldPersistBet()
    {
        await using var db = DbContextFactory.Create(nameof(HappyPath_ShouldPersistBet));
        var group  = BetBuilders.MakeGroup();
        var user   = BetBuilders.MakeUser();
        var match  = BetBuilders.MakeMatchInMatchMaking(group.Id);
        db.Groups.Add(group); db.Users.Add(user); db.Matches.Add(match);
        await db.SaveChangesAsync();
        await BetBuilders.SetTeamsOnMatchAsync(db, match, group.Id);

        var result = await BetBuilders.MakeSut(db).PlaceOrUpdateBetAsync(
            group.Id, match.Id, user.Id, BetBuilders.SimpleDto(), CancellationToken.None);

        result.Success.Should().BeTrue();
        var bet = await db.Set<MatchBetEntity>()
            .Include(b => b.Selections)
            .FirstOrDefaultAsync(b => b.MatchId == match.Id && b.UserId == user.Id);
        bet.Should().NotBeNull();
        bet!.Selections.Should().HaveCount(1);
    }

    [Fact]
    public async Task Update_ShouldReplaceExistingSelections()
    {
        await using var db = DbContextFactory.Create(nameof(Update_ShouldReplaceExistingSelections));
        var group = BetBuilders.MakeGroup();
        var user  = BetBuilders.MakeUser();
        var match = BetBuilders.MakeMatchInMatchMaking(group.Id);
        db.Groups.Add(group); db.Users.Add(user); db.Matches.Add(match);
        await db.SaveChangesAsync();
        await BetBuilders.SetTeamsOnMatchAsync(db, match, group.Id);

        var sut = BetBuilders.MakeSut(db);
        await sut.PlaceOrUpdateBetAsync(group.Id, match.Id, user.Id, BetBuilders.SimpleDto("TeamA", 50), CancellationToken.None);

        // Update: troca o vencedor e aumenta a cota
        var updated = new PlaceMatchBetDto(new List<BetSelectionRequestDto>
        {
            new("WinningTeam", "TeamB", 80),
        });
        var result = await sut.PlaceOrUpdateBetAsync(group.Id, match.Id, user.Id, updated, CancellationToken.None);

        result.Success.Should().BeTrue();
        var bet = await db.Set<MatchBetEntity>()
            .Include(b => b.Selections)
            .FirstOrDefaultAsync(b => b.MatchId == match.Id && b.UserId == user.Id);
        bet!.Selections.Should().HaveCount(1);
        bet.Selections[0].PredictedValue.Should().Be("TeamB");
        bet.Selections[0].FichasWagered.Should().Be(80);
    }

    [Fact]
    public async Task WhenMatchNotFound_ShouldFail()
    {
        await using var db = DbContextFactory.Create(nameof(WhenMatchNotFound_ShouldFail));
        var group = BetBuilders.MakeGroup();
        db.Groups.Add(group);
        await db.SaveChangesAsync();

        var result = await BetBuilders.MakeSut(db).PlaceOrUpdateBetAsync(
            group.Id, Guid.NewGuid(), Guid.NewGuid(), BetBuilders.SimpleDto(), CancellationToken.None);

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task WhenMatchNotInMatchMaking_ShouldFail()
    {
        await using var db = DbContextFactory.Create(nameof(WhenMatchNotInMatchMaking_ShouldFail));
        var group = BetBuilders.MakeGroup();
        // Partida sem avançar de status (Scheduled)
        var match = new MatchEntity(group.Id, DateTime.UtcNow.AddHours(2), "Arena");
        db.Groups.Add(group); db.Matches.Add(match);
        await db.SaveChangesAsync();

        var result = await BetBuilders.MakeSut(db).PlaceOrUpdateBetAsync(
            group.Id, match.Id, Guid.NewGuid(), BetBuilders.SimpleDto(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("matchmaking");
    }

    [Fact]
    public async Task WhenMissingWinningTeam_ShouldFail()
    {
        await using var db = DbContextFactory.Create(nameof(WhenMissingWinningTeam_ShouldFail));
        var group = BetBuilders.MakeGroup();
        var match = BetBuilders.MakeMatchInMatchMaking(group.Id);
        db.Groups.Add(group); db.Matches.Add(match);
        await db.SaveChangesAsync();

        var dto = new PlaceMatchBetDto(new List<BetSelectionRequestDto>
        {
            new("FinalScore", "2:1", 50),
        });

        var result = await BetBuilders.MakeSut(db).PlaceOrUpdateBetAsync(
            group.Id, match.Id, Guid.NewGuid(), dto, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("obrigat");
    }

    [Fact]
    public async Task WhenWagerBelowMinimum_ShouldFail()
    {
        await using var db = DbContextFactory.Create(nameof(WhenWagerBelowMinimum_ShouldFail));
        var group = BetBuilders.MakeGroup();
        var match = BetBuilders.MakeMatchInMatchMaking(group.Id);
        db.Groups.Add(group); db.Matches.Add(match);
        await db.SaveChangesAsync();

        var result = await BetBuilders.MakeSut(db).PlaceOrUpdateBetAsync(
            group.Id, match.Id, Guid.NewGuid(), BetBuilders.SimpleDto(wager: 10), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("30");
    }

    [Fact]
    public async Task WhenTotalExceedsMax_ShouldFail()
    {
        await using var db = DbContextFactory.Create(nameof(WhenTotalExceedsMax_ShouldFail));
        var group = BetBuilders.MakeGroup();
        var match = BetBuilders.MakeMatchInMatchMaking(group.Id);
        db.Groups.Add(group); db.Matches.Add(match);
        await db.SaveChangesAsync();

        var dto = new PlaceMatchBetDto(new List<BetSelectionRequestDto>
        {
            new("WinningTeam", "TeamA", 150),
            new("FinalScore",  "2:1",   100), // total = 250 > 200
        });

        var result = await BetBuilders.MakeSut(db).PlaceOrUpdateBetAsync(
            group.Id, match.Id, Guid.NewGuid(), dto, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("200");
    }

    [Fact]
    public async Task WhenScoreInconsistentWithWinner_ShouldFail()
    {
        await using var db = DbContextFactory.Create(nameof(WhenScoreInconsistentWithWinner_ShouldFail));
        var group = BetBuilders.MakeGroup();
        var match = BetBuilders.MakeMatchInMatchMaking(group.Id);
        db.Groups.Add(group); db.Matches.Add(match);
        await db.SaveChangesAsync();

        // Diz TeamA venceu mas placar é empate
        var dto = new PlaceMatchBetDto(new List<BetSelectionRequestDto>
        {
            new("WinningTeam", "TeamA", 100),
            new("FinalScore",  "1:1",   100),
        });

        var result = await BetBuilders.MakeSut(db).PlaceOrUpdateBetAsync(
            group.Id, match.Id, Guid.NewGuid(), dto, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("incompatível");
    }

    [Fact]
    public async Task WhenBettingOnSelf_ShouldFail()
    {
        await using var db = DbContextFactory.Create(nameof(WhenBettingOnSelf_ShouldFail));
        var group  = BetBuilders.MakeGroup();
        var user   = BetBuilders.MakeUser();
        var player = BetBuilders.MakePlayer(group.Id, user.Id);
        var match  = BetBuilders.MakeMatchInMatchMaking(group.Id);
        db.Groups.Add(group); db.Users.Add(user); db.Players.Add(player); db.Matches.Add(match);
        await db.SaveChangesAsync();

        // Adiciona o player à partida via reflection (contorna setters privados)
        var mp = new MatchPlayerEntity(player.Id);
        typeof(MatchPlayerEntity).GetProperty(nameof(MatchPlayerEntity.MatchId))!.SetValue(mp, match.Id);
        typeof(MatchPlayerEntity).GetProperty(nameof(MatchPlayerEntity.GroupId))!.SetValue(mp, group.Id);
        db.Set<MatchPlayerEntity>().Add(mp);
        await db.SaveChangesAsync();

        var dto = new PlaceMatchBetDto(new List<BetSelectionRequestDto>
        {
            new("WinningTeam", "TeamA", 100),
            new("PlayerGoals", $"{mp.Id}|2", 100),
        });

        var result = await BetBuilders.MakeSut(db).PlaceOrUpdateBetAsync(
            group.Id, match.Id, user.Id, dto, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("si mesmo");
    }

    [Fact]
    public async Task WhenSelectionsCountExceedsMax_ShouldFail()
    {
        await using var db = DbContextFactory.Create(nameof(WhenSelectionsCountExceedsMax_ShouldFail));
        var group = BetBuilders.MakeGroup();
        var match = BetBuilders.MakeMatchInMatchMaking(group.Id);
        db.Groups.Add(group); db.Matches.Add(match);
        await db.SaveChangesAsync();

        // 6 seleções (max é 5)
        var sels = Enumerable.Range(0, 6)
            .Select(_ => new BetSelectionRequestDto("WinningTeam", "TeamA", 30))
            .ToList();
        var dto = new PlaceMatchBetDto(sels);

        var result = await BetBuilders.MakeSut(db).PlaceOrUpdateBetAsync(
            group.Id, match.Id, Guid.NewGuid(), dto, CancellationToken.None);

        result.Success.Should().BeFalse();
    }
}

// ── DeleteBetAsync ────────────────────────────────────────────────────────────

public class BetService_DeleteTests
{
    [Fact]
    public async Task HappyPath_ShouldRemoveBet()
    {
        await using var db = DbContextFactory.Create(nameof(HappyPath_ShouldRemoveBet));
        var group = BetBuilders.MakeGroup();
        var user  = BetBuilders.MakeUser();
        var match = BetBuilders.MakeMatchInMatchMaking(group.Id);
        db.Groups.Add(group); db.Users.Add(user); db.Matches.Add(match);
        await db.SaveChangesAsync();
        await BetBuilders.SetTeamsOnMatchAsync(db, match, group.Id);

        var sut = BetBuilders.MakeSut(db);
        await sut.PlaceOrUpdateBetAsync(group.Id, match.Id, user.Id, BetBuilders.SimpleDto(), CancellationToken.None);

        var result = await sut.DeleteBetAsync(group.Id, match.Id, user.Id, CancellationToken.None);

        result.Success.Should().BeTrue();
        var remaining = await db.Set<MatchBetEntity>()
            .FirstOrDefaultAsync(b => b.MatchId == match.Id && b.UserId == user.Id);
        remaining.Should().BeNull();
    }

    [Fact]
    public async Task WhenBetNotFound_ShouldFail()
    {
        await using var db = DbContextFactory.Create(nameof(WhenBetNotFound_ShouldFail));
        var group = BetBuilders.MakeGroup();
        var match = BetBuilders.MakeMatchInMatchMaking(group.Id);
        db.Groups.Add(group); db.Matches.Add(match);
        await db.SaveChangesAsync();

        var result = await BetBuilders.MakeSut(db).DeleteBetAsync(
            group.Id, match.Id, Guid.NewGuid(), CancellationToken.None);

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task WhenMatchLocked_ShouldFail()
    {
        await using var db = DbContextFactory.Create(nameof(WhenMatchLocked_ShouldFail));
        var group = BetBuilders.MakeGroup();
        var user  = BetBuilders.MakeUser();
        var match = BetBuilders.MakeMatchInMatchMaking(group.Id);
        db.Groups.Add(group); db.Users.Add(user); db.Matches.Add(match);
        await db.SaveChangesAsync();

        var sut = BetBuilders.MakeSut(db);
        await sut.PlaceOrUpdateBetAsync(group.Id, match.Id, user.Id, BetBuilders.SimpleDto(), CancellationToken.None);

        // Avança a partida para além do matchmaking (qualquer status > MatchMaking fecha apostas)
        typeof(MatchEntity).GetProperty(nameof(MatchEntity.Status))!
            .SetValue(match, MatchStatus.Started);
        await db.SaveChangesAsync();

        var result = await sut.DeleteBetAsync(group.Id, match.Id, user.Id, CancellationToken.None);

        result.Success.Should().BeFalse();
    }
}

// ── GetMyBalanceAsync ─────────────────────────────────────────────────────────

public class BetService_BalanceTests
{
    [Fact]
    public async Task WhenNoBalance_ShouldReturnZero()
    {
        await using var db = DbContextFactory.Create(nameof(WhenNoBalance_ShouldReturnZero));
        var group = BetBuilders.MakeGroup();
        db.Groups.Add(group);
        await db.SaveChangesAsync();

        var balance = await BetBuilders.MakeSut(db).GetMyBalanceAsync(
            group.Id, Guid.NewGuid(), CancellationToken.None);

        balance.Should().Be(0);
    }

    [Fact]
    public async Task WhenBalanceExists_ShouldReturnCorrectValue()
    {
        await using var db = DbContextFactory.Create(nameof(WhenBalanceExists_ShouldReturnCorrectValue));
        var group = BetBuilders.MakeGroup();
        var user  = BetBuilders.MakeUser();
        db.Groups.Add(group); db.Users.Add(user);
        var bal = new UserBetBalanceEntity(group.Id, user.Id);
        bal.ApplyDelta(275);
        db.Set<UserBetBalanceEntity>().Add(bal);
        await db.SaveChangesAsync();

        var result = await BetBuilders.MakeSut(db).GetMyBalanceAsync(
            group.Id, user.Id, CancellationToken.None);

        result.Should().Be(275);
    }
}

// ── GetLeaderboardAsync ───────────────────────────────────────────────────────

public class BetService_LeaderboardTests
{
    [Fact]
    public async Task ShouldReturnSortedByBalanceDesc()
    {
        await using var db = DbContextFactory.Create(nameof(ShouldReturnSortedByBalanceDesc));
        var group  = BetBuilders.MakeGroup();
        var user1  = BetBuilders.MakeUser("u1");
        var user2  = BetBuilders.MakeUser("u2");
        var user3  = BetBuilders.MakeUser("u3");
        db.Groups.Add(group);
        db.Users.AddRange(user1, user2, user3);
        var bal1 = new UserBetBalanceEntity(group.Id, user1.Id); bal1.ApplyDelta(100);
        var bal2 = new UserBetBalanceEntity(group.Id, user2.Id); bal2.ApplyDelta(300);
        var bal3 = new UserBetBalanceEntity(group.Id, user3.Id); bal3.ApplyDelta(200);
        db.Set<UserBetBalanceEntity>().AddRange(bal1, bal2, bal3);
        await db.SaveChangesAsync();

        var result = await BetBuilders.MakeSut(db).GetLeaderboardAsync(group.Id, CancellationToken.None);

        result.Should().HaveCount(3);
        result[0].Balance.Should().Be(300);
        result[1].Balance.Should().Be(200);
        result[2].Balance.Should().Be(100);
    }

    [Fact]
    public async Task ShouldAssignRanks()
    {
        await using var db = DbContextFactory.Create(nameof(ShouldAssignRanks));
        var group = BetBuilders.MakeGroup();
        var user1 = BetBuilders.MakeUser("u1");
        var user2 = BetBuilders.MakeUser("u2");
        db.Groups.Add(group);
        db.Users.AddRange(user1, user2);
        var bal1 = new UserBetBalanceEntity(group.Id, user1.Id); bal1.ApplyDelta(500);
        var bal2 = new UserBetBalanceEntity(group.Id, user2.Id); bal2.ApplyDelta(100);
        db.Set<UserBetBalanceEntity>().AddRange(bal1, bal2);
        await db.SaveChangesAsync();

        var result = await BetBuilders.MakeSut(db).GetLeaderboardAsync(group.Id, CancellationToken.None);

        result[0].Rank.Should().Be(1);
        result[1].Rank.Should().Be(2);
    }

    [Fact]
    public async Task WhenEmpty_ShouldReturnEmptyList()
    {
        await using var db = DbContextFactory.Create(nameof(WhenEmpty_ShouldReturnEmptyList));
        var group = BetBuilders.MakeGroup();
        db.Groups.Add(group);
        await db.SaveChangesAsync();

        var result = await BetBuilders.MakeSut(db).GetLeaderboardAsync(group.Id, CancellationToken.None);

        result.Should().BeEmpty();
    }
}

// ── ResolveMatchBetsAsync — cálculo de fichas ─────────────────────────────────

public class BetService_ResolveTests
{
    private static async Task<(GroupEntity group, UserEntity user, MatchEntity match)> SeedAsync(
        BratnavaFC.Infrastructure.Data.AppDbContext db)
    {
        var group = BetBuilders.MakeGroup();
        var user  = BetBuilders.MakeUser();
        var match = BetBuilders.MakeMatchInMatchMaking(group.Id);
        db.Groups.Add(group);
        db.Users.Add(user);
        db.Matches.Add(match);
        await db.SaveChangesAsync();
        await BetBuilders.SetTeamsOnMatchAsync(db, match, group.Id);
        return (group, user, match);
    }

    private static async Task SetScore(BratnavaFC.Infrastructure.Data.AppDbContext db, MatchEntity match, int a, int b)
    {
        BetBuilders.SetMatchScore(match, a, b);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task CorrectWinner_ShouldEarnWagerX1()
    {
        await using var db = DbContextFactory.Create(nameof(CorrectWinner_ShouldEarnWagerX1));
        var (group, user, match) = await SeedAsync(db);
        var sut = BetBuilders.MakeSut(db);

        await sut.PlaceOrUpdateBetAsync(group.Id, match.Id, user.Id,
            BetBuilders.SimpleDto("TeamA", 100), CancellationToken.None);

        await SetScore(db, match, 2, 1); // TeamA vence

        await sut.ResolveMatchBetsAsync(match.Id, CancellationToken.None);

        var bal = await db.Set<UserBetBalanceEntity>()
            .FirstAsync(b => b.GroupId == group.Id && b.UserId == user.Id);
        // +50 bônus + +100 (100 × 1.0)
        bal.Balance.Should().Be(150);
        bal.TotalBets.Should().Be(1);
        bal.TotalCorrect.Should().Be(1);
    }

    [Fact]
    public async Task CorrectDraw_ShouldEarnWagerX2point5()
    {
        await using var db = DbContextFactory.Create(nameof(CorrectDraw_ShouldEarnWagerX2point5));
        var (group, user, match) = await SeedAsync(db);
        var sut = BetBuilders.MakeSut(db);

        await sut.PlaceOrUpdateBetAsync(group.Id, match.Id, user.Id,
            BetBuilders.SimpleDto("Draw", 100), CancellationToken.None);

        await SetScore(db, match, 1, 1); // Empate

        await sut.ResolveMatchBetsAsync(match.Id, CancellationToken.None);

        var bal = await db.Set<UserBetBalanceEntity>()
            .FirstAsync(b => b.GroupId == group.Id && b.UserId == user.Id);
        // +50 bônus + +250 (100 × 2.5)
        bal.Balance.Should().Be(300);
    }

    [Fact]
    public async Task WrongWinner_ShouldLoseHalfWager()
    {
        await using var db = DbContextFactory.Create(nameof(WrongWinner_ShouldLoseHalfWager));
        var (group, user, match) = await SeedAsync(db);
        var sut = BetBuilders.MakeSut(db);

        await sut.PlaceOrUpdateBetAsync(group.Id, match.Id, user.Id,
            BetBuilders.SimpleDto("TeamB", 100), CancellationToken.None);

        await SetScore(db, match, 2, 1); // TeamA vence (apostou TeamB)
        await db.SaveChangesAsync();

        await sut.ResolveMatchBetsAsync(match.Id, CancellationToken.None);

        var bal = await db.Set<UserBetBalanceEntity>()
            .FirstAsync(b => b.GroupId == group.Id && b.UserId == user.Id);
        // +50 bônus − 50 (100 × 0.5)
        bal.Balance.Should().Be(0);
        bal.TotalCorrect.Should().Be(0);
    }

    [Fact]
    public async Task CorrectFinalScore_ShouldEarnWagerX4()
    {
        await using var db = DbContextFactory.Create(nameof(CorrectFinalScore_ShouldEarnWagerX4));
        var (group, user, match) = await SeedAsync(db);
        var sut = BetBuilders.MakeSut(db);

        var dto = new PlaceMatchBetDto(new List<BetSelectionRequestDto>
        {
            new("WinningTeam", "TeamA", 50),
            new("FinalScore",  "2:1",   50),
        });
        await sut.PlaceOrUpdateBetAsync(group.Id, match.Id, user.Id, dto, CancellationToken.None);

        await SetScore(db, match, 2, 1);

        await sut.ResolveMatchBetsAsync(match.Id, CancellationToken.None);

        var bal = await db.Set<UserBetBalanceEntity>()
            .FirstAsync(b => b.GroupId == group.Id && b.UserId == user.Id);
        // +50 bônus + 50 (WinningTeam ×1) + 200 (FinalScore ×4)
        bal.Balance.Should().Be(300);
        bal.TotalCorrect.Should().Be(2);
    }

    [Fact]
    public async Task PartialFinalScore_OffByOneTotalGoals_ShouldEarnZero()
    {
        await using var db = DbContextFactory.Create(nameof(PartialFinalScore_OffByOneTotalGoals_ShouldEarnZero));
        var (group, user, match) = await SeedAsync(db);
        var sut = BetBuilders.MakeSut(db);

        var dto = new PlaceMatchBetDto(new List<BetSelectionRequestDto>
        {
            new("WinningTeam", "TeamA", 100),
            new("FinalScore",  "2:1",   100), // total apostado = 3 gols
        });
        await sut.PlaceOrUpdateBetAsync(group.Id, match.Id, user.Id, dto, CancellationToken.None);

        await SetScore(db, match, 2, 0); // real = 2:0, total = 2 (diferença 1)

        await sut.ResolveMatchBetsAsync(match.Id, CancellationToken.None);

        var bal = await db.Set<UserBetBalanceEntity>()
            .FirstAsync(b => b.GroupId == group.Id && b.UserId == user.Id);
        // +50 bônus + 100 (WinningTeam correto ×1) + 0 (FinalScore parcial)
        bal.Balance.Should().Be(150);
    }

    [Fact]
    public async Task WrongFinalScore_ShouldLoseHalfWager()
    {
        await using var db = DbContextFactory.Create(nameof(WrongFinalScore_ShouldLoseHalfWager));
        var (group, user, match) = await SeedAsync(db);
        var sut = BetBuilders.MakeSut(db);

        var dto = new PlaceMatchBetDto(new List<BetSelectionRequestDto>
        {
            new("WinningTeam", "TeamA", 100),
            new("FinalScore",  "3:0",   100), // real será 2:1 (total diferença = 0 mas placar diferente e total igual)
        });
        await sut.PlaceOrUpdateBetAsync(group.Id, match.Id, user.Id, dto, CancellationToken.None);

        await SetScore(db, match, 5, 0); // total = 5, apostou = 3, diferença = 2

        await sut.ResolveMatchBetsAsync(match.Id, CancellationToken.None);

        var sel = await db.Set<MatchBetSelectionEntity>()
            .Where(s => s.Category == BetCategory.FinalScore)
            .FirstAsync();
        sel.FichasEarned.Should().Be(-50); // 100 × 0.5
        sel.IsCorrect.Should().BeFalse();
        sel.IsPartialCredit.Should().BeFalse();
    }

    [Fact]
    public async Task ParticipationBonus_ShouldAlwaysBeAdded()
    {
        // Mesmo errando tudo, o bônus de participação é creditado
        await using var db = DbContextFactory.Create(nameof(ParticipationBonus_ShouldAlwaysBeAdded));
        var (group, user, match) = await SeedAsync(db);
        var sut = BetBuilders.MakeSut(db);

        await sut.PlaceOrUpdateBetAsync(group.Id, match.Id, user.Id,
            BetBuilders.SimpleDto("TeamB", 100), CancellationToken.None);

        await SetScore(db, match, 3, 0); // TeamA vence, apostou TeamB

        await sut.ResolveMatchBetsAsync(match.Id, CancellationToken.None);

        var bal = await db.Set<UserBetBalanceEntity>()
            .FirstAsync(b => b.GroupId == group.Id && b.UserId == user.Id);
        // +50 bônus − 50 (100 × 0.5) = 0
        bal.Balance.Should().Be(0);
    }

    [Fact]
    public async Task AlreadyResolved_ShouldNotResolveAgain()
    {
        await using var db = DbContextFactory.Create(nameof(AlreadyResolved_ShouldNotResolveAgain));
        var (group, user, match) = await SeedAsync(db);
        var sut = BetBuilders.MakeSut(db);

        await sut.PlaceOrUpdateBetAsync(group.Id, match.Id, user.Id,
            BetBuilders.SimpleDto("TeamA", 100), CancellationToken.None);

        await SetScore(db, match, 2, 0);

        await sut.ResolveMatchBetsAsync(match.Id, CancellationToken.None);
        await sut.ResolveMatchBetsAsync(match.Id, CancellationToken.None); // segunda chamada não deve duplicar

        var bal = await db.Set<UserBetBalanceEntity>()
            .FirstAsync(b => b.GroupId == group.Id && b.UserId == user.Id);
        // Deve ter sido resolvido apenas uma vez
        bal.TotalBets.Should().Be(1);
    }

    [Fact]
    public async Task MultipleUsers_ShouldResolveIndependently()
    {
        await using var db = DbContextFactory.Create(nameof(MultipleUsers_ShouldResolveIndependently));
        var group  = BetBuilders.MakeGroup();
        var user1  = BetBuilders.MakeUser("u1");
        var user2  = BetBuilders.MakeUser("u2");
        var match  = BetBuilders.MakeMatchInMatchMaking(group.Id);
        db.Groups.Add(group); db.Users.AddRange(user1, user2); db.Matches.Add(match);
        await db.SaveChangesAsync();
        await BetBuilders.SetTeamsOnMatchAsync(db, match, group.Id);

        var sut = BetBuilders.MakeSut(db);
        await sut.PlaceOrUpdateBetAsync(group.Id, match.Id, user1.Id,
            BetBuilders.SimpleDto("TeamA", 100), CancellationToken.None); // vai acertar
        await sut.PlaceOrUpdateBetAsync(group.Id, match.Id, user2.Id,
            BetBuilders.SimpleDto("TeamB", 100), CancellationToken.None); // vai errar

        BetBuilders.SetMatchScore(match, 2, 0);
        await db.SaveChangesAsync();

        await sut.ResolveMatchBetsAsync(match.Id, CancellationToken.None);

        var bal1 = await db.Set<UserBetBalanceEntity>().FirstAsync(b => b.UserId == user1.Id);
        var bal2 = await db.Set<UserBetBalanceEntity>().FirstAsync(b => b.UserId == user2.Id);

        bal1.Balance.Should().Be(150);  // +50 bônus + 100
        bal2.Balance.Should().Be(0);    // +50 bônus − 50
    }
}

// ── ReResolveMatchBetsAsync ───────────────────────────────────────────────────

public class BetService_ReResolveTests
{
    [Fact]
    public async Task ShouldReverseAndRecalculate()
    {
        await using var db = DbContextFactory.Create(nameof(ShouldReverseAndRecalculate));
        var group = BetBuilders.MakeGroup();
        var user  = BetBuilders.MakeUser();
        var match = BetBuilders.MakeMatchInMatchMaking(group.Id);
        db.Groups.Add(group); db.Users.Add(user); db.Matches.Add(match);
        await db.SaveChangesAsync();
        await BetBuilders.SetTeamsOnMatchAsync(db, match, group.Id);

        var sut = BetBuilders.MakeSut(db);
        await sut.PlaceOrUpdateBetAsync(group.Id, match.Id, user.Id,
            BetBuilders.SimpleDto("TeamA", 100), CancellationToken.None);

        // Resolve com placar TeamA vencendo
        BetBuilders.SetMatchScore(match, 2, 0);
        await db.SaveChangesAsync();
        await sut.ResolveMatchBetsAsync(match.Id, CancellationToken.None);

        var balAfterFirst = (await db.Set<UserBetBalanceEntity>()
            .FirstAsync(b => b.UserId == user.Id)).Balance;
        balAfterFirst.Should().Be(150); // +50 + 100

        // Corrige o placar: TeamB venceu
        BetBuilders.SetMatchScore(match, 0, 2);
        await db.SaveChangesAsync();
        await sut.ReResolveMatchBetsAsync(match.Id, CancellationToken.None);

        var balAfterReResolve = (await db.Set<UserBetBalanceEntity>()
            .FirstAsync(b => b.UserId == user.Id)).Balance;
        balAfterReResolve.Should().Be(0); // +50 bônus − 50 (errou)
    }
}

// ── RecalculateAllBalancesAsync ───────────────────────────────────────────────

public class BetService_RecalculateTests
{
    [Fact]
    public async Task ShouldRecalculateBalancesFromScratch()
    {
        await using var db = DbContextFactory.Create(nameof(ShouldRecalculateBalancesFromScratch));
        var group = BetBuilders.MakeGroup();
        var user  = BetBuilders.MakeUser();
        db.Groups.Add(group); db.Users.Add(user);

        // Saldo inflado manualmente (como estava antes da mudança de regra)
        var bal = new UserBetBalanceEntity(group.Id, user.Id);
        bal.ForceSetBalance(999); // valor incorreto
        db.Set<UserBetBalanceEntity>().Add(bal);

        // Aposta resolvida com seleções conhecidas
        var match = new MatchEntity(group.Id, DateTime.UtcNow, "Arena");
        db.Matches.Add(match);
        await db.SaveChangesAsync();

        var bet = new MatchBetEntity(group.Id, match.Id, user.Id);
        bet.ReplaceSelections(new List<MatchBetSelectionEntity>
        {
            new MatchBetSelectionEntity(bet.Id, BetCategory.WinningTeam, "TeamA", 100),
        });
        // Resolve as seleções manualmente
        bet.Selections[0].Resolve(100, true, false, "TeamA");
        bet.MarkResolved();
        db.Set<MatchBetEntity>().Add(bet);
        await db.SaveChangesAsync();

        var updated = await BetBuilders.MakeSut(db).RecalculateAllBalancesAsync(CancellationToken.None);

        updated.Should().Be(1);
        var newBal = await db.Set<UserBetBalanceEntity>().FirstAsync(b => b.UserId == user.Id);
        // 1 aposta resolvida: +50 bônus + 100 = 150
        newBal.Balance.Should().Be(150);
    }

    [Fact]
    public async Task WhenNoResolvedBets_ShouldSetBalanceToZero()
    {
        await using var db = DbContextFactory.Create(nameof(WhenNoResolvedBets_ShouldSetBalanceToZero));
        var group = BetBuilders.MakeGroup();
        var user  = BetBuilders.MakeUser();
        db.Groups.Add(group); db.Users.Add(user);

        var bal = new UserBetBalanceEntity(group.Id, user.Id);
        bal.ForceSetBalance(500);
        db.Set<UserBetBalanceEntity>().Add(bal);
        await db.SaveChangesAsync();

        await BetBuilders.MakeSut(db).RecalculateAllBalancesAsync(CancellationToken.None);

        var newBal = await db.Set<UserBetBalanceEntity>().FirstAsync(b => b.UserId == user.Id);
        newBal.Balance.Should().Be(0);
    }
}
