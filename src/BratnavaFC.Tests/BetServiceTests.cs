using BratnavaFC.Application.Services;
using BratnavaFC.Domain.Dtos;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;
using BratnavaFC.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace BratnavaFC.Tests;

/// <summary>
/// Testa PlaceOrUpdateBetAsync do BetService.
///
/// Cenários cobertos:
///   1. Nova aposta cria MatchBetEntity + MatchBetSelectionEntity no banco.
///   2. Atualizar aposta existente substitui as seleções anteriores.
///   3. Aposta com WinningTeam ausente é rejeitada.
///   4. Aposta com fichas abaixo do mínimo (30) é rejeitada.
///   5. Aposta com times não definidos na partida é rejeitada.
/// </summary>
public class BetServiceTests
{
    // ── Helpers ─────────��────────────────────────────��──────────────────���───────

    private static AppDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            // InMemory provider logs a warning when BeginTransactionAsync is called.
            // Suppress it so the BetService transaction wrapper doesn't throw in tests.
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    /// <summary>
    /// Cria uma MatchEntity já em status MatchMaking com dois jogadores
    /// atribuídos: mp1 no Time A (1) e mp2 no Time B (2).
    /// </summary>
    private static MatchEntity CreateMatchInMatchMaking(
        Guid groupId,
        out MatchPlayerEntity mp1,
        out MatchPlayerEntity mp2)
    {
        var match = new MatchEntity(groupId, DateTime.UtcNow.AddDays(1), "Quadra Teste");
        match.OpenAcceptation();

        mp1 = new MatchPlayerEntity(Guid.NewGuid());
        mp1.AcceptInvite();
        mp1.AssignToMatch(match);

        mp2 = new MatchPlayerEntity(Guid.NewGuid());
        mp2.AcceptInvite();
        mp2.AssignToMatch(match);

        match.Players.Add(mp1);
        match.Players.Add(mp2);
        match.GoToMatchMaking();

        mp1.SetTeam(1);
        mp2.SetTeam(2);

        return match;
    }

    private static PlaceMatchBetDto WinningTeamBet(string winner = "TeamA", int fichas = 50) =>
        new(new List<BetSelectionRequestDto>
        {
            new("WinningTeam", winner, fichas),
        });

    // ── Tests ─────────────��──────────────────────────────────────────────────��───

    [Fact]
    public async Task PlaceOrUpdateBetAsync_NewBet_CreatesRecordWithCorrectSelections()
    {
        // Arrange
        await using var db = CreateDb();
        var groupId = Guid.NewGuid();
        var userId  = Guid.NewGuid();

        var match = CreateMatchInMatchMaking(groupId, out _, out _);
        db.Matches.Add(match);
        await db.SaveChangesAsync();

        var service = new BetService(db);
        var dto     = WinningTeamBet("TeamA", fichas: 50);

        // Act
        var result = await service.PlaceOrUpdateBetAsync(
            groupId, match.Id, userId, dto, CancellationToken.None);

        // Assert
        Assert.True(result.Success, result.Error);

        var bets = await db.Set<MatchBetEntity>()
            .Include(b => b.Selections)
            .ToListAsync();

        Assert.Single(bets);
        Assert.Equal(userId,   bets[0].UserId);
        Assert.Equal(match.Id, bets[0].MatchId);
        Assert.Equal(groupId,  bets[0].GroupId);

        Assert.Single(bets[0].Selections);
        Assert.Equal("TeamA", bets[0].Selections[0].PredictedValue);
        Assert.Equal(50,      bets[0].Selections[0].FichasWagered);
        Assert.Equal(BetCategory.WinningTeam, bets[0].Selections[0].Category);
    }

    [Fact]
    public async Task PlaceOrUpdateBetAsync_UpdateExistingBet_ReplacesOldSelections()
    {
        // Arrange
        await using var db = CreateDb();
        var groupId = Guid.NewGuid();
        var userId  = Guid.NewGuid();

        var match = CreateMatchInMatchMaking(groupId, out _, out _);
        db.Matches.Add(match);
        await db.SaveChangesAsync();

        var service = new BetService(db);

        // Place initial bet on TeamA
        var firstResult = await service.PlaceOrUpdateBetAsync(
            groupId, match.Id, userId, WinningTeamBet("TeamA", 60), CancellationToken.None);
        Assert.True(firstResult.Success, firstResult.Error);

        // Act — update to TeamB
        var updateDto = WinningTeamBet("TeamB", 80);
        var result = await service.PlaceOrUpdateBetAsync(
            groupId, match.Id, userId, updateDto, CancellationToken.None);

        // Assert
        Assert.True(result.Success, result.Error);

        var bets = await db.Set<MatchBetEntity>()
            .Include(b => b.Selections)
            .Where(b => b.UserId == userId && b.MatchId == match.Id)
            .ToListAsync();

        Assert.Single(bets);                                 // still one bet per user
        Assert.Single(bets[0].Selections);                  // old selection was replaced
        Assert.Equal("TeamB", bets[0].Selections[0].PredictedValue);
        Assert.Equal(80,      bets[0].Selections[0].FichasWagered);
    }

    [Fact]
    public async Task PlaceOrUpdateBetAsync_MissingWinningTeam_ReturnsFailure()
    {
        // Arrange
        await using var db = CreateDb();
        var groupId = Guid.NewGuid();
        var userId  = Guid.NewGuid();

        var match = CreateMatchInMatchMaking(groupId, out _, out _);
        db.Matches.Add(match);
        await db.SaveChangesAsync();

        var service = new BetService(db);

        // DTO without the mandatory WinningTeam category
        var dto = new PlaceMatchBetDto(new List<BetSelectionRequestDto>
        {
            new("FinalScore", "2:1", 50),
        });

        // Act
        var result = await service.PlaceOrUpdateBetAsync(
            groupId, match.Id, userId, dto, CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Time vencedor", result.Error);
    }

    [Fact]
    public async Task PlaceOrUpdateBetAsync_FichasBelowMinimum_ReturnsFailure()
    {
        // Arrange
        await using var db = CreateDb();
        var groupId = Guid.NewGuid();
        var userId  = Guid.NewGuid();

        var match = CreateMatchInMatchMaking(groupId, out _, out _);
        db.Matches.Add(match);
        await db.SaveChangesAsync();

        var service = new BetService(db);
        var dto     = WinningTeamBet("TeamA", fichas: 10); // below minimum of 30

        // Act
        var result = await service.PlaceOrUpdateBetAsync(
            groupId, match.Id, userId, dto, CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("30", result.Error);
    }

    [Fact]
    public async Task PlaceOrUpdateBetAsync_TeamsNotAssigned_ReturnsFailure()
    {
        // Arrange
        await using var db = CreateDb();
        var groupId = Guid.NewGuid();
        var userId  = Guid.NewGuid();

        // Build match in MatchMaking but do NOT assign teams (keep Team = 0)
        var match = new MatchEntity(groupId, DateTime.UtcNow.AddDays(1), "Quadra");
        match.OpenAcceptation();

        var mp1 = new MatchPlayerEntity(Guid.NewGuid());
        mp1.AcceptInvite();
        mp1.AssignToMatch(match);

        var mp2 = new MatchPlayerEntity(Guid.NewGuid());
        mp2.AcceptInvite();
        mp2.AssignToMatch(match);

        match.Players.Add(mp1);
        match.Players.Add(mp2);
        match.GoToMatchMaking();
        // Intentionally: mp1 and mp2 remain on Team 0 (unassigned)

        db.Matches.Add(match);
        await db.SaveChangesAsync();

        var service = new BetService(db);
        var dto     = WinningTeamBet("TeamA", fichas: 50);

        // Act
        var result = await service.PlaceOrUpdateBetAsync(
            groupId, match.Id, userId, dto, CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("times", result.Error, StringComparison.OrdinalIgnoreCase);
    }
}
