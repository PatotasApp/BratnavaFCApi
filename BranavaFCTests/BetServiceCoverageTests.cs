using BratnavaFC.Application.Services;
using BratnavaFC.Domain.Dtos;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BranavaFC.Tests;

// ── Helpers (espelham BetBuilders de BetServiceTests.cs — file-scoped, não reutilizável) ──

file static class BetCoverageBuilders
{
    public static GroupEntity MakeGroup() => new("Patota FC", null, Guid.NewGuid());

    public static UserEntity MakeUser(string name = "u1") =>
        new(name, "Nome", "Sobrenome", $"{name}@mail.com", "hash", null, null);

    public static PlayerEntity MakePlayer(Guid groupId, Guid? userId = null) =>
        new("Jogador", userId, groupId, 5m, false, userId == null, Status.Active);

    public static MatchEntity MakeMatch(Guid groupId, MatchStatus status = MatchStatus.MatchMaking)
    {
        var m = new MatchEntity(groupId, DateTime.UtcNow.AddHours(2), "Arena");
        typeof(MatchEntity).GetProperty(nameof(MatchEntity.Status))!.SetValue(m, status);
        return m;
    }

    public static void SetStatus(MatchEntity match, MatchStatus status) =>
        typeof(MatchEntity).GetProperty(nameof(MatchEntity.Status))!.SetValue(match, status);

    public static void SetMatchScore(MatchEntity match, int a, int b)
    {
        typeof(MatchEntity).GetProperty(nameof(MatchEntity.TeamAGoals))!.SetValue(match, (int?)a);
        typeof(MatchEntity).GetProperty(nameof(MatchEntity.TeamBGoals))!.SetValue(match, (int?)b);
    }

    public static BetService MakeSut(BratnavaFC.Infrastructure.Data.AppDbContext db) => new(db);

    public static MatchPlayerEntity AddMatchPlayer(
        BratnavaFC.Infrastructure.Data.AppDbContext db,
        MatchEntity match, Guid groupId, PlayerEntity player, short team)
    {
        var mp = new MatchPlayerEntity(player.Id);
        typeof(MatchPlayerEntity).GetProperty(nameof(MatchPlayerEntity.MatchId))!.SetValue(mp, match.Id);
        typeof(MatchPlayerEntity).GetProperty(nameof(MatchPlayerEntity.GroupId))!.SetValue(mp, groupId);
        typeof(MatchPlayerEntity).GetProperty(nameof(MatchPlayerEntity.Team))!.SetValue(mp, team);
        db.Set<MatchPlayerEntity>().Add(mp);
        return mp;
    }

    /// <summary>Seeds one player per team; returns both MatchPlayerEntities.</summary>
    public static async Task<(MatchPlayerEntity mpA, MatchPlayerEntity mpB)> SetTeamsAsync(
        BratnavaFC.Infrastructure.Data.AppDbContext db, MatchEntity match, Guid groupId)
    {
        var p1 = MakePlayer(groupId);
        var p2 = MakePlayer(groupId);
        db.Players.AddRange(p1, p2);
        await db.SaveChangesAsync();

        var mpA = AddMatchPlayer(db, match, groupId, p1, 1);
        var mpB = AddMatchPlayer(db, match, groupId, p2, 2);
        await db.SaveChangesAsync();
        return (mpA, mpB);
    }

    public static PlaceMatchBetDto SimpleDto(string winner = "TeamA", int wager = 50) =>
        new(new List<BetSelectionRequestDto> { new("WinningTeam", winner, wager) });
}

// ── GetCurrentContextAsync / GetContextForMatchAsync ─────────────────────────

public class BetService_ContextCoverageTests
{
    [Fact]
    public async Task GetCurrentContext_WhenNoEligibleMatch_ShouldReturnNull()
    {
        await using var db = DbContextFactory.Create(nameof(GetCurrentContext_WhenNoEligibleMatch_ShouldReturnNull));
        var group = BetCoverageBuilders.MakeGroup();
        // Created e Finalized não são elegíveis
        var created   = BetCoverageBuilders.MakeMatch(group.Id, MatchStatus.Created);
        var finalized = BetCoverageBuilders.MakeMatch(group.Id, MatchStatus.Finalized);
        db.Groups.Add(group); db.Matches.AddRange(created, finalized);
        await db.SaveChangesAsync();

        var ctx = await BetCoverageBuilders.MakeSut(db).GetCurrentContextAsync(
            group.Id, Guid.NewGuid(), CancellationToken.None);

        ctx.Should().BeNull();
    }

    [Fact]
    public async Task GetCurrentContext_MatchMakingWithTeams_ShouldOpenBetWindow()
    {
        await using var db = DbContextFactory.Create(nameof(GetCurrentContext_MatchMakingWithTeams_ShouldOpenBetWindow));
        var group = BetCoverageBuilders.MakeGroup();
        var match = BetCoverageBuilders.MakeMatch(group.Id);
        db.Groups.Add(group); db.Matches.Add(match);
        await db.SaveChangesAsync();
        await BetCoverageBuilders.SetTeamsAsync(db, match, group.Id);

        var ctx = await BetCoverageBuilders.MakeSut(db).GetCurrentContextAsync(
            group.Id, Guid.NewGuid(), CancellationToken.None);

        ctx.Should().NotBeNull();
        ctx!.MatchId.Should().Be(match.Id);
        ctx.BetWindowOpen.Should().BeTrue();
        ctx.Players.Should().HaveCount(2);
        ctx.MyBet.Should().BeNull();
    }

    [Fact]
    public async Task GetCurrentContext_MatchMakingWithoutTeams_BetWindowShouldBeClosed()
    {
        await using var db = DbContextFactory.Create(nameof(GetCurrentContext_MatchMakingWithoutTeams_BetWindowShouldBeClosed));
        var group = BetCoverageBuilders.MakeGroup();
        var match = BetCoverageBuilders.MakeMatch(group.Id);
        db.Groups.Add(group); db.Matches.Add(match);
        await db.SaveChangesAsync();

        var ctx = await BetCoverageBuilders.MakeSut(db).GetCurrentContextAsync(
            group.Id, Guid.NewGuid(), CancellationToken.None);

        ctx.Should().NotBeNull();
        ctx!.BetWindowOpen.Should().BeFalse("sem times definidos a janela de apostas fica fechada.");
    }

    [Fact]
    public async Task GetCurrentContext_WithMyBet_ShouldFlagBettorAndReturnMyBet()
    {
        await using var db = DbContextFactory.Create(nameof(GetCurrentContext_WithMyBet_ShouldFlagBettorAndReturnMyBet));
        var group = BetCoverageBuilders.MakeGroup();
        var user  = BetCoverageBuilders.MakeUser();
        var match = BetCoverageBuilders.MakeMatch(group.Id);
        db.Groups.Add(group); db.Users.Add(user); db.Matches.Add(match);
        await db.SaveChangesAsync();
        await BetCoverageBuilders.SetTeamsAsync(db, match, group.Id);

        // Player do usuário escalado no time A
        var myPlayer = BetCoverageBuilders.MakePlayer(group.Id, user.Id);
        db.Players.Add(myPlayer);
        await db.SaveChangesAsync();
        BetCoverageBuilders.AddMatchPlayer(db, match, group.Id, myPlayer, 1);
        await db.SaveChangesAsync();

        var sut = BetCoverageBuilders.MakeSut(db);
        (await sut.PlaceOrUpdateBetAsync(group.Id, match.Id, user.Id,
            BetCoverageBuilders.SimpleDto("TeamA", 70), CancellationToken.None)).Success.Should().BeTrue();

        var ctx = await sut.GetCurrentContextAsync(group.Id, user.Id, CancellationToken.None);

        ctx.Should().NotBeNull();
        ctx!.MyBet.Should().NotBeNull();
        ctx.MyBet!.IsResolved.Should().BeFalse();
        ctx.MyBet.IsLocked.Should().BeFalse();
        ctx.MyBet.Selections.Should().ContainSingle().Which.PredictedValue.Should().Be("TeamA");
        var me = ctx.Players.Single(p => p.PlayerId == myPlayer.Id);
        me.HasBet.Should().BeTrue();
        me.TotalFichasWagered.Should().Be(70);
    }

    [Fact]
    public async Task GetCurrentContext_LateGroupMembers_ShouldAppearWithTeamZero()
    {
        await using var db = DbContextFactory.Create(nameof(GetCurrentContext_LateGroupMembers_ShouldAppearWithTeamZero));
        var group = BetCoverageBuilders.MakeGroup();
        var user  = BetCoverageBuilders.MakeUser();
        var match = BetCoverageBuilders.MakeMatch(group.Id);
        db.Groups.Add(group); db.Users.Add(user); db.Matches.Add(match);
        await db.SaveChangesAsync();
        await BetCoverageBuilders.SetTeamsAsync(db, match, group.Id);

        // Membro do grupo (não-guest, ativo) que não está na partida
        var lateMember = BetCoverageBuilders.MakePlayer(group.Id, user.Id);
        db.Players.Add(lateMember);
        await db.SaveChangesAsync();

        var ctx = await BetCoverageBuilders.MakeSut(db).GetCurrentContextAsync(
            group.Id, Guid.NewGuid(), CancellationToken.None);

        ctx.Should().NotBeNull();
        ctx!.Players.Should().HaveCount(3);
        var late = ctx.Players.Single(p => p.PlayerId == lateMember.Id);
        late.Team.Should().Be(0);
        late.HasBet.Should().BeFalse();
    }

    [Fact]
    public async Task GetContextForMatch_WhenNotFound_ShouldReturnNull()
    {
        await using var db = DbContextFactory.Create(nameof(GetContextForMatch_WhenNotFound_ShouldReturnNull));
        var group = BetCoverageBuilders.MakeGroup();
        db.Groups.Add(group);
        await db.SaveChangesAsync();

        var ctx = await BetCoverageBuilders.MakeSut(db).GetContextForMatchAsync(
            group.Id, Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        ctx.Should().BeNull();
    }

    [Fact]
    public async Task GetContextForMatch_StartedMatch_ShouldLockBetAndCloseWindow()
    {
        await using var db = DbContextFactory.Create(nameof(GetContextForMatch_StartedMatch_ShouldLockBetAndCloseWindow));
        var group = BetCoverageBuilders.MakeGroup();
        var user  = BetCoverageBuilders.MakeUser();
        var match = BetCoverageBuilders.MakeMatch(group.Id);
        db.Groups.Add(group); db.Users.Add(user); db.Matches.Add(match);
        await db.SaveChangesAsync();
        await BetCoverageBuilders.SetTeamsAsync(db, match, group.Id);

        var sut = BetCoverageBuilders.MakeSut(db);
        await sut.PlaceOrUpdateBetAsync(group.Id, match.Id, user.Id,
            BetCoverageBuilders.SimpleDto(), CancellationToken.None);

        BetCoverageBuilders.SetStatus(match, MatchStatus.Started);
        await db.SaveChangesAsync();

        var ctx = await sut.GetContextForMatchAsync(group.Id, match.Id, user.Id, CancellationToken.None);

        ctx.Should().NotBeNull();
        ctx!.BetWindowOpen.Should().BeFalse();
        ctx.StatusName.Should().Be("Started");
        ctx.MyBet!.IsLocked.Should().BeTrue();
    }
}

// ── GetBettableMatchesAsync ───────────────────────────────────────────────────

public class BetService_BettableMatchesTests
{
    [Fact]
    public async Task WhenNoMatchMakingMatches_ShouldReturnEmpty()
    {
        await using var db = DbContextFactory.Create(nameof(WhenNoMatchMakingMatches_ShouldReturnEmpty));
        var group = BetCoverageBuilders.MakeGroup();
        db.Groups.Add(group);
        db.Matches.Add(BetCoverageBuilders.MakeMatch(group.Id, MatchStatus.Started));
        await db.SaveChangesAsync();

        var result = await BetCoverageBuilders.MakeSut(db).GetBettableMatchesAsync(
            group.Id, CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ShouldReturnOnlyMatchesWithBothTeamsAssigned()
    {
        await using var db = DbContextFactory.Create(nameof(ShouldReturnOnlyMatchesWithBothTeamsAssigned));
        var group     = BetCoverageBuilders.MakeGroup();
        var withTeams = BetCoverageBuilders.MakeMatch(group.Id);
        var noTeams   = BetCoverageBuilders.MakeMatch(group.Id);
        var oneTeam   = BetCoverageBuilders.MakeMatch(group.Id);
        db.Groups.Add(group);
        db.Matches.AddRange(withTeams, noTeams, oneTeam);
        await db.SaveChangesAsync();

        await BetCoverageBuilders.SetTeamsAsync(db, withTeams, group.Id);

        var solo = BetCoverageBuilders.MakePlayer(group.Id);
        db.Players.Add(solo);
        await db.SaveChangesAsync();
        BetCoverageBuilders.AddMatchPlayer(db, oneTeam, group.Id, solo, 1);
        await db.SaveChangesAsync();

        var result = await BetCoverageBuilders.MakeSut(db).GetBettableMatchesAsync(
            group.Id, CancellationToken.None);

        result.Should().ContainSingle().Which.MatchId.Should().Be(withTeams.Id);
    }
}

// ── PlaceOrUpdateBetAsync — validações extras ─────────────────────────────────

public class BetService_PlaceValidationCoverageTests
{
    private static async Task<(GroupEntity group, MatchEntity match)> SeedAsync(
        BratnavaFC.Infrastructure.Data.AppDbContext db)
    {
        var group = BetCoverageBuilders.MakeGroup();
        var match = BetCoverageBuilders.MakeMatch(group.Id);
        db.Groups.Add(group); db.Matches.Add(match);
        await db.SaveChangesAsync();
        return (group, match);
    }

    [Fact]
    public async Task WhenZeroSelections_ShouldFail()
    {
        await using var db = DbContextFactory.Create(nameof(WhenZeroSelections_ShouldFail));
        var (group, match) = await SeedAsync(db);

        var result = await BetCoverageBuilders.MakeSut(db).PlaceOrUpdateBetAsync(
            group.Id, match.Id, Guid.NewGuid(),
            new PlaceMatchBetDto(new List<BetSelectionRequestDto>()), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("1 e 5");
    }

    [Fact]
    public async Task WhenWinningTeamValueInvalid_ShouldFail()
    {
        await using var db = DbContextFactory.Create(nameof(WhenWinningTeamValueInvalid_ShouldFail));
        var (group, match) = await SeedAsync(db);

        var result = await BetCoverageBuilders.MakeSut(db).PlaceOrUpdateBetAsync(
            group.Id, match.Id, Guid.NewGuid(),
            BetCoverageBuilders.SimpleDto("TeamC"), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("WinningTeam");
    }

    [Fact]
    public async Task WhenPredictedValueEmpty_ShouldFail()
    {
        await using var db = DbContextFactory.Create(nameof(WhenPredictedValueEmpty_ShouldFail));
        var (group, match) = await SeedAsync(db);

        var result = await BetCoverageBuilders.MakeSut(db).PlaceOrUpdateBetAsync(
            group.Id, match.Id, Guid.NewGuid(),
            BetCoverageBuilders.SimpleDto("  "), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("vazio");
    }

    [Fact]
    public async Task WhenFinalScoreFormatInvalid_ShouldFail()
    {
        await using var db = DbContextFactory.Create(nameof(WhenFinalScoreFormatInvalid_ShouldFail));
        var (group, match) = await SeedAsync(db);

        var dto = new PlaceMatchBetDto(new List<BetSelectionRequestDto>
        {
            new("WinningTeam", "TeamA", 50),
            new("FinalScore",  "2x1",   50), // sem ':'
        });
        var result = await BetCoverageBuilders.MakeSut(db).PlaceOrUpdateBetAsync(
            group.Id, match.Id, Guid.NewGuid(), dto, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("FinalScore");
    }

    [Fact]
    public async Task WhenPlayerGoalsFormatInvalid_ShouldFail()
    {
        await using var db = DbContextFactory.Create(nameof(WhenPlayerGoalsFormatInvalid_ShouldFail));
        var (group, match) = await SeedAsync(db);

        var dto = new PlaceMatchBetDto(new List<BetSelectionRequestDto>
        {
            new("WinningTeam", "TeamA",  50),
            new("PlayerGoals", "semPipe", 50),
        });
        var result = await BetCoverageBuilders.MakeSut(db).PlaceOrUpdateBetAsync(
            group.Id, match.Id, Guid.NewGuid(), dto, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("PlayerGoals");
    }

    [Fact]
    public async Task WhenTeamsNotDefined_ShouldFail()
    {
        await using var db = DbContextFactory.Create(nameof(WhenTeamsNotDefined_ShouldFail));
        var (group, match) = await SeedAsync(db);

        var result = await BetCoverageBuilders.MakeSut(db).PlaceOrUpdateBetAsync(
            group.Id, match.Id, Guid.NewGuid(),
            BetCoverageBuilders.SimpleDto(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("times");
    }
}

// ── DeleteBetAsync — branches restantes ───────────────────────────────────────

public class BetService_DeleteCoverageTests
{
    [Fact]
    public async Task WhenMatchNotFound_ShouldFail()
    {
        await using var db = DbContextFactory.Create(nameof(BetService_DeleteCoverageTests) + nameof(WhenMatchNotFound_ShouldFail));
        var group = BetCoverageBuilders.MakeGroup();
        db.Groups.Add(group);
        await db.SaveChangesAsync();

        var result = await BetCoverageBuilders.MakeSut(db).DeleteBetAsync(
            group.Id, Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("não encontrada");
    }

    [Fact]
    public async Task WhenTeamsNotDefined_ShouldFail()
    {
        await using var db = DbContextFactory.Create(nameof(BetService_DeleteCoverageTests) + nameof(WhenTeamsNotDefined_ShouldFail));
        var group = BetCoverageBuilders.MakeGroup();
        var match = BetCoverageBuilders.MakeMatch(group.Id);
        db.Groups.Add(group); db.Matches.Add(match);
        await db.SaveChangesAsync();

        var result = await BetCoverageBuilders.MakeSut(db).DeleteBetAsync(
            group.Id, match.Id, Guid.NewGuid(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("times");
    }
}

// ── GetMatchResultsAsync ──────────────────────────────────────────────────────

public class BetService_MatchResultsTests
{
    [Fact]
    public async Task WhenMatchNotFound_ShouldReturnNull()
    {
        await using var db = DbContextFactory.Create(nameof(BetService_MatchResultsTests) + nameof(WhenMatchNotFound_ShouldReturnNull));
        var group = BetCoverageBuilders.MakeGroup();
        db.Groups.Add(group);
        await db.SaveChangesAsync();

        var result = await BetCoverageBuilders.MakeSut(db).GetMatchResultsAsync(
            group.Id, Guid.NewGuid(), CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task WhenNoBets_ShouldReturnEmptyUserBets()
    {
        await using var db = DbContextFactory.Create(nameof(WhenNoBets_ShouldReturnEmptyUserBets));
        var group = BetCoverageBuilders.MakeGroup();
        var match = BetCoverageBuilders.MakeMatch(group.Id, MatchStatus.Finalized);
        db.Groups.Add(group); db.Matches.Add(match);
        await db.SaveChangesAsync();

        var result = await BetCoverageBuilders.MakeSut(db).GetMatchResultsAsync(
            group.Id, match.Id, CancellationToken.None);

        result.Should().NotBeNull();
        result!.IsResolved.Should().BeTrue();
        result.UserBets.Should().BeEmpty();
    }

    [Fact]
    public async Task WhenNotFinalizedWithBet_ShouldReturnUnresolvedWithZeroTotal()
    {
        await using var db = DbContextFactory.Create(nameof(WhenNotFinalizedWithBet_ShouldReturnUnresolvedWithZeroTotal));
        var group = BetCoverageBuilders.MakeGroup();
        var user  = BetCoverageBuilders.MakeUser();
        var match = BetCoverageBuilders.MakeMatch(group.Id);
        db.Groups.Add(group); db.Users.Add(user); db.Matches.Add(match);
        await db.SaveChangesAsync();
        await BetCoverageBuilders.SetTeamsAsync(db, match, group.Id);

        var sut = BetCoverageBuilders.MakeSut(db);
        await sut.PlaceOrUpdateBetAsync(group.Id, match.Id, user.Id,
            BetCoverageBuilders.SimpleDto(), CancellationToken.None);

        var result = await sut.GetMatchResultsAsync(group.Id, match.Id, CancellationToken.None);

        result.Should().NotBeNull();
        result!.IsResolved.Should().BeFalse();
        var userBet = result.UserBets.Single();
        userBet.TotalFichasEarned.Should().Be(0, "aposta ainda não resolvida não soma fichas.");
        userBet.UserName.Should().Contain("Nome");
    }

    [Fact]
    public async Task WhenFinalizedWithUnresolvedBets_ShouldLazilyResolveAndReturnTotals()
    {
        await using var db = DbContextFactory.Create(nameof(WhenFinalizedWithUnresolvedBets_ShouldLazilyResolveAndReturnTotals));
        var group = BetCoverageBuilders.MakeGroup();
        var user  = BetCoverageBuilders.MakeUser();
        var match = BetCoverageBuilders.MakeMatch(group.Id);
        db.Groups.Add(group); db.Users.Add(user); db.Matches.Add(match);
        await db.SaveChangesAsync();
        await BetCoverageBuilders.SetTeamsAsync(db, match, group.Id);

        var sut = BetCoverageBuilders.MakeSut(db);
        await sut.PlaceOrUpdateBetAsync(group.Id, match.Id, user.Id,
            BetCoverageBuilders.SimpleDto("TeamA", 100), CancellationToken.None);

        BetCoverageBuilders.SetMatchScore(match, 2, 0);
        BetCoverageBuilders.SetStatus(match, MatchStatus.Finalized);
        await db.SaveChangesAsync();

        var result = await sut.GetMatchResultsAsync(group.Id, match.Id, CancellationToken.None);

        result.Should().NotBeNull();
        result!.IsResolved.Should().BeTrue();
        var userBet = result.UserBets.Single();
        userBet.TotalFichasEarned.Should().Be(150); // +50 bônus + 100
        userBet.CurrentBalance.Should().Be(150);
        userBet.Selections.Single().IsCorrect.Should().BeTrue();
    }
}

// ── GetBetPreviewAsync ────────────────────────────────────────────────────────

public class BetService_PreviewTests
{
    [Fact]
    public async Task WhenMatchNotFound_ShouldReturnNull()
    {
        await using var db = DbContextFactory.Create(nameof(BetService_PreviewTests) + nameof(WhenMatchNotFound_ShouldReturnNull));
        var group = BetCoverageBuilders.MakeGroup();
        db.Groups.Add(group);
        await db.SaveChangesAsync();

        var result = await BetCoverageBuilders.MakeSut(db).GetBetPreviewAsync(
            group.Id, Guid.NewGuid(), CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task WhenNoBets_ShouldReturnScoreWithEmptyList()
    {
        await using var db = DbContextFactory.Create(nameof(WhenNoBets_ShouldReturnScoreWithEmptyList));
        var group = BetCoverageBuilders.MakeGroup();
        var match = BetCoverageBuilders.MakeMatch(group.Id, MatchStatus.Started);
        BetCoverageBuilders.SetMatchScore(match, 3, 1);
        db.Groups.Add(group); db.Matches.Add(match);
        await db.SaveChangesAsync();

        var result = await BetCoverageBuilders.MakeSut(db).GetBetPreviewAsync(
            group.Id, match.Id, CancellationToken.None);

        result.Should().NotBeNull();
        result!.CurrentScoreA.Should().Be(3);
        result.CurrentScoreB.Should().Be(1);
        result.UserBets.Should().BeEmpty();
    }

    [Fact]
    public async Task WithBets_ShouldSimulateEarningsIncludingPlayerGoals()
    {
        await using var db = DbContextFactory.Create(nameof(WithBets_ShouldSimulateEarningsIncludingPlayerGoals));
        var group = BetCoverageBuilders.MakeGroup();
        var user  = BetCoverageBuilders.MakeUser();
        var match = BetCoverageBuilders.MakeMatch(group.Id);
        db.Groups.Add(group); db.Users.Add(user); db.Matches.Add(match);
        await db.SaveChangesAsync();
        var (mpA, _) = await BetCoverageBuilders.SetTeamsAsync(db, match, group.Id);

        var sut = BetCoverageBuilders.MakeSut(db);
        var dto = new PlaceMatchBetDto(new List<BetSelectionRequestDto>
        {
            new("WinningTeam", "TeamA",        50),
            new("PlayerGoals", $"{mpA.Id}|2",  50), // vai acertar (2 gols)
        });
        (await sut.PlaceOrUpdateBetAsync(group.Id, match.Id, user.Id, dto, CancellationToken.None))
            .Success.Should().BeTrue();

        // Placar parcial 2:0 com 2 gols do mpA
        BetCoverageBuilders.SetMatchScore(match, 2, 0);
        db.Goals.Add(new GoalEntity(match.Id, group.Id, mpA.Id, null, 60));
        db.Goals.Add(new GoalEntity(match.Id, group.Id, mpA.Id, null, 120));
        await db.SaveChangesAsync();

        var result = await sut.GetBetPreviewAsync(group.Id, match.Id, CancellationToken.None);

        result.Should().NotBeNull();
        var userBet = result!.UserBets.Single();
        // WinningTeam correto: +50 (×1.0); PlayerGoals exato: +125 (50 × 2.5)
        userBet.SimulatedBetEarnings.Should().Be(175);
        userBet.SimulatedTotal.Should().Be(225); // +50 bônus
        var goalsSel = userBet.Selections.Single(s => s.Category == "PlayerGoals");
        goalsSel.IsCorrect.Should().BeTrue();
        goalsSel.ActualValue.Should().Be($"{mpA.Id}|2");

        // Preview não persiste resolução
        (await db.Set<MatchBetEntity>().AnyAsync(b => b.IsResolved)).Should().BeFalse();
    }
}

// ── ResolveMatchBetsAsync — PlayerGoals / PlayerAssists ───────────────────────

public class BetService_ResolvePlayerCategoriesTests
{
    private static async Task<(GroupEntity group, UserEntity user, MatchEntity match,
        MatchPlayerEntity mpA, MatchPlayerEntity mpB)> SeedAsync(
        BratnavaFC.Infrastructure.Data.AppDbContext db)
    {
        var group = BetCoverageBuilders.MakeGroup();
        var user  = BetCoverageBuilders.MakeUser();
        var match = BetCoverageBuilders.MakeMatch(group.Id);
        db.Groups.Add(group); db.Users.Add(user); db.Matches.Add(match);
        await db.SaveChangesAsync();
        var (mpA, mpB) = await BetCoverageBuilders.SetTeamsAsync(db, match, group.Id);
        return (group, user, match, mpA, mpB);
    }

    [Fact]
    public async Task PlayerGoals_ExactPrediction_ShouldEarnWagerX2point5()
    {
        await using var db = DbContextFactory.Create(nameof(PlayerGoals_ExactPrediction_ShouldEarnWagerX2point5));
        var (group, user, match, mpA, _) = await SeedAsync(db);
        var sut = BetCoverageBuilders.MakeSut(db);

        var dto = new PlaceMatchBetDto(new List<BetSelectionRequestDto>
        {
            new("WinningTeam", "TeamA",       30),
            new("PlayerGoals", $"{mpA.Id}|2", 100),
        });
        await sut.PlaceOrUpdateBetAsync(group.Id, match.Id, user.Id, dto, CancellationToken.None);

        BetCoverageBuilders.SetMatchScore(match, 2, 0);
        db.Goals.Add(new GoalEntity(match.Id, group.Id, mpA.Id, null, 10));
        db.Goals.Add(new GoalEntity(match.Id, group.Id, mpA.Id, null, 20));
        await db.SaveChangesAsync();

        await sut.ResolveMatchBetsAsync(match.Id, CancellationToken.None);

        var sel = await db.Set<MatchBetSelectionEntity>()
            .FirstAsync(s => s.Category == BetCategory.PlayerGoals);
        sel.FichasEarned.Should().Be(250); // 100 × 2.5
        sel.IsCorrect.Should().BeTrue();
        sel.IsPartialCredit.Should().BeFalse();
    }

    [Fact]
    public async Task PlayerGoals_OffByOne_ShouldGivePartialCreditZero()
    {
        await using var db = DbContextFactory.Create(nameof(PlayerGoals_OffByOne_ShouldGivePartialCreditZero));
        var (group, user, match, mpA, _) = await SeedAsync(db);
        var sut = BetCoverageBuilders.MakeSut(db);

        var dto = new PlaceMatchBetDto(new List<BetSelectionRequestDto>
        {
            new("WinningTeam", "TeamA",       30),
            new("PlayerGoals", $"{mpA.Id}|2", 100), // real: 1 gol
        });
        await sut.PlaceOrUpdateBetAsync(group.Id, match.Id, user.Id, dto, CancellationToken.None);

        BetCoverageBuilders.SetMatchScore(match, 1, 0);
        db.Goals.Add(new GoalEntity(match.Id, group.Id, mpA.Id, null, 10));
        await db.SaveChangesAsync();

        await sut.ResolveMatchBetsAsync(match.Id, CancellationToken.None);

        var sel = await db.Set<MatchBetSelectionEntity>()
            .FirstAsync(s => s.Category == BetCategory.PlayerGoals);
        sel.FichasEarned.Should().Be(0);
        sel.IsCorrect.Should().BeFalse();
        sel.IsPartialCredit.Should().BeTrue();
    }

    [Fact]
    public async Task PlayerAssists_WrongByTwoOrMore_ShouldLoseHalfWager()
    {
        await using var db = DbContextFactory.Create(nameof(PlayerAssists_WrongByTwoOrMore_ShouldLoseHalfWager));
        var (group, user, match, mpA, mpB) = await SeedAsync(db);
        var sut = BetCoverageBuilders.MakeSut(db);

        var dto = new PlaceMatchBetDto(new List<BetSelectionRequestDto>
        {
            new("WinningTeam",   "TeamA",       30),
            new("PlayerAssists", $"{mpB.Id}|3", 100), // real: 1 assistência
        });
        await sut.PlaceOrUpdateBetAsync(group.Id, match.Id, user.Id, dto, CancellationToken.None);

        BetCoverageBuilders.SetMatchScore(match, 1, 0);
        db.Goals.Add(new GoalEntity(match.Id, group.Id, mpA.Id, mpB.Id, 10));
        await db.SaveChangesAsync();

        await sut.ResolveMatchBetsAsync(match.Id, CancellationToken.None);

        var sel = await db.Set<MatchBetSelectionEntity>()
            .FirstAsync(s => s.Category == BetCategory.PlayerAssists);
        sel.FichasEarned.Should().Be(-50); // −(100 × 0.5)
        sel.IsCorrect.Should().BeFalse();
        sel.IsPartialCredit.Should().BeFalse();
        sel.ActualValue.Should().Be($"{mpB.Id}|1");
    }

    [Fact]
    public async Task PlayerAssists_ExactPrediction_ShouldEarnWagerX2point5()
    {
        await using var db = DbContextFactory.Create(nameof(PlayerAssists_ExactPrediction_ShouldEarnWagerX2point5));
        var (group, user, match, mpA, mpB) = await SeedAsync(db);
        var sut = BetCoverageBuilders.MakeSut(db);

        var dto = new PlaceMatchBetDto(new List<BetSelectionRequestDto>
        {
            new("WinningTeam",   "TeamA",       30),
            new("PlayerAssists", $"{mpB.Id}|1", 40),
        });
        await sut.PlaceOrUpdateBetAsync(group.Id, match.Id, user.Id, dto, CancellationToken.None);

        BetCoverageBuilders.SetMatchScore(match, 1, 0);
        db.Goals.Add(new GoalEntity(match.Id, group.Id, mpA.Id, mpB.Id, 10));
        await db.SaveChangesAsync();

        await sut.ResolveMatchBetsAsync(match.Id, CancellationToken.None);

        var sel = await db.Set<MatchBetSelectionEntity>()
            .FirstAsync(s => s.Category == BetCategory.PlayerAssists);
        sel.FichasEarned.Should().Be(100); // 40 × 2.5
        sel.IsCorrect.Should().BeTrue();
    }

    [Fact]
    public async Task OwnGoals_ShouldNotCountForPlayerGoals()
    {
        await using var db = DbContextFactory.Create(nameof(OwnGoals_ShouldNotCountForPlayerGoals));
        var (group, user, match, mpA, _) = await SeedAsync(db);
        var sut = BetCoverageBuilders.MakeSut(db);

        var dto = new PlaceMatchBetDto(new List<BetSelectionRequestDto>
        {
            new("WinningTeam", "TeamA",       30),
            new("PlayerGoals", $"{mpA.Id}|0", 40), // aposta 0 gols; o único gol é contra
        });
        await sut.PlaceOrUpdateBetAsync(group.Id, match.Id, user.Id, dto, CancellationToken.None);

        BetCoverageBuilders.SetMatchScore(match, 1, 0);
        db.Goals.Add(new GoalEntity(match.Id, group.Id, mpA.Id, null, 10, isOwnGoal: true));
        await db.SaveChangesAsync();

        await sut.ResolveMatchBetsAsync(match.Id, CancellationToken.None);

        var sel = await db.Set<MatchBetSelectionEntity>()
            .FirstAsync(s => s.Category == BetCategory.PlayerGoals);
        sel.IsCorrect.Should().BeTrue("gol contra não conta para PlayerGoals.");
        sel.ActualValue.Should().Be($"{mpA.Id}|0");
    }
}

// ── ResetBetsForMatchAsync ────────────────────────────────────────────────────

public class BetService_ResetBetsTests
{
    [Fact]
    public async Task ShouldRemoveOnlyUnresolvedBets()
    {
        await using var db = DbContextFactory.Create(nameof(ShouldRemoveOnlyUnresolvedBets));
        var group = BetCoverageBuilders.MakeGroup();
        var user1 = BetCoverageBuilders.MakeUser("u1");
        var user2 = BetCoverageBuilders.MakeUser("u2");
        var match = BetCoverageBuilders.MakeMatch(group.Id);
        db.Groups.Add(group); db.Users.AddRange(user1, user2); db.Matches.Add(match);
        await db.SaveChangesAsync();

        // Aposta não resolvida
        var pendingBet = new MatchBetEntity(group.Id, match.Id, user1.Id);
        var pendingSel = new MatchBetSelectionEntity(pendingBet.Id, BetCategory.WinningTeam, "TeamA", 50);
        pendingBet.ReplaceSelections(new List<MatchBetSelectionEntity> { pendingSel });
        db.Set<MatchBetEntity>().Add(pendingBet);
        db.Set<MatchBetSelectionEntity>().Add(pendingSel);

        // Aposta já resolvida — deve permanecer
        var resolvedBet = new MatchBetEntity(group.Id, match.Id, user2.Id);
        var resolvedSel = new MatchBetSelectionEntity(resolvedBet.Id, BetCategory.WinningTeam, "TeamB", 50);
        resolvedSel.Resolve(50, true, false, "TeamB");
        resolvedBet.ReplaceSelections(new List<MatchBetSelectionEntity> { resolvedSel });
        resolvedBet.MarkResolved();
        db.Set<MatchBetEntity>().Add(resolvedBet);
        db.Set<MatchBetSelectionEntity>().Add(resolvedSel);
        await db.SaveChangesAsync();

        await BetCoverageBuilders.MakeSut(db).ResetBetsForMatchAsync(match.Id, CancellationToken.None);

        var remaining = await db.Set<MatchBetEntity>().Where(b => b.MatchId == match.Id).ToListAsync();
        remaining.Should().ContainSingle().Which.UserId.Should().Be(user2.Id);
        (await db.Set<MatchBetSelectionEntity>().CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task WhenNoUnresolvedBets_ShouldBeNoOp()
    {
        await using var db = DbContextFactory.Create(nameof(WhenNoUnresolvedBets_ShouldBeNoOp));
        var group = BetCoverageBuilders.MakeGroup();
        var match = BetCoverageBuilders.MakeMatch(group.Id);
        db.Groups.Add(group); db.Matches.Add(match);
        await db.SaveChangesAsync();

        var act = () => BetCoverageBuilders.MakeSut(db).ResetBetsForMatchAsync(match.Id, CancellationToken.None);

        await act.Should().NotThrowAsync();
    }
}
