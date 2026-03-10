using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BratnavaFC.Application.Services;
using BratnavaFC.Domain.Dtos;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BranavaFC.Tests;

public sealed class PlayerStatsServiceTests
{
    private static PlayerStatsService CreateSut(BratnavaFC.Infrastructure.Data.AppDbContext db)
        => new PlayerStatsService(db);

    // -----------------------------------------------------------------
    // Seeds
    // -----------------------------------------------------------------

    private static async Task<GroupEntity> SeedGroupAsync(BratnavaFC.Infrastructure.Data.AppDbContext db)
    {
        var group = new GroupEntity("Grupo Teste", null);
        db.Groups.Add(group);
        await db.SaveChangesAsync();
        return group;
    }

    private static async Task<PlayerEntity> SeedPlayerAsync(
        BratnavaFC.Infrastructure.Data.AppDbContext db,
        Guid groupId,
        int? guestStarRating = null,
        bool isGuest = false)
    {
        var player = new PlayerEntity("Jogador", null, groupId, 0m, false, isGuest, Status.Active);
        if (guestStarRating.HasValue)
            player.SetGuestStarRating(guestStarRating.Value);
        db.Players.Add(player);
        await db.SaveChangesAsync();
        return player;
    }

    /// <summary>
    /// Seed de uma partida completamente finalizada com p1 no Time A e p2 no Time B.
    /// Usado para acumular MatchesPlayed no PlayerStatsService.
    /// </summary>
    private static async Task SeedFinalizedMatchAsync(
        BratnavaFC.Infrastructure.Data.AppDbContext db,
        Guid groupId,
        Guid p1Id,
        Guid p2Id)
    {
        var p1 = await db.Players.FindAsync(p1Id)
                 ?? throw new InvalidOperationException("p1 não encontrado.");
        var p2 = await db.Players.FindAsync(p2Id)
                 ?? throw new InvalidOperationException("p2 não encontrado.");

        var match = new MatchEntity(groupId, DateTime.UtcNow.AddDays(-1), "Arena");
        match.AddPlayer(new MatchPlayerEntity(p1.Id), p1);
        match.AddPlayer(new MatchPlayerEntity(p2.Id), p2);
        db.Matches.Add(match);
        await db.SaveChangesAsync();

        match.OpenAcceptation();
        match.AcceptInvite(p1.Id);
        match.AcceptInvite(p2.Id);
        await db.SaveChangesAsync();

        match.GoToMatchMaking();
        match.AssignTeams(new[] { p1.Id }, new[] { p2.Id });
        await db.SaveChangesAsync();

        match.Start();
        match.End();
        match.GoToPostGame();
        match.SetScore(1, 0);
        match.FinalizeByVotes();
        await db.SaveChangesAsync();
    }

    // -----------------------------------------------------------------
    // Testes
    // -----------------------------------------------------------------

    /// <summary>
    /// Verifica o mapeamento correto de estrelas (1-5) para a escala 0-1.
    /// 1→0.00  2→0.25  3→0.50  4→0.75  5→1.00
    /// </summary>
    [Theory]
    [InlineData(1, 0.00)]
    [InlineData(2, 0.25)]
    [InlineData(3, 0.50)]
    [InlineData(4, 0.75)]
    [InlineData(5, 1.00)]
    public async Task EnrichPlayersAsync_StarRatingMapping_ShouldProduceCorrectNeutralOverride(
        int starRating, double expectedOverride)
    {
        await using var db = DbContextFactory.Create($"Stats_StarRating_{starRating}");

        var group = await SeedGroupAsync(db);
        var player = await SeedPlayerAsync(db, group.Id, guestStarRating: starRating, isGuest: true);

        var sut = CreateSut(db);
        var dto = new PlayerRequestDto(player.Id, player.Name, player.IsGoalkeeper);

        var result = await sut.EnrichPlayersAsync(new List<PlayerRequestDto> { dto });

        result.Should().HaveCount(1);
        result[0].NeutralOverride.Should().BeApproximately(expectedOverride, 1e-9,
            $"GuestStarRating={starRating} deve produzir NeutralOverride={expectedOverride}");
    }

    [Fact]
    public async Task EnrichPlayersAsync_WhenPlayerHasNoStarRating_ShouldReturnNullNeutralOverride()
    {
        await using var db = DbContextFactory.Create(nameof(EnrichPlayersAsync_WhenPlayerHasNoStarRating_ShouldReturnNullNeutralOverride));

        var group = await SeedGroupAsync(db);
        var player = await SeedPlayerAsync(db, group.Id, guestStarRating: null);

        var sut = CreateSut(db);
        var dto = new PlayerRequestDto(player.Id, player.Name, player.IsGoalkeeper);

        var result = await sut.EnrichPlayersAsync(new List<PlayerRequestDto> { dto });

        result.Should().HaveCount(1);
        result[0].NeutralOverride.Should().BeNull(
            "sem GuestStarRating a constante padrão 0.5 deve ser usada (NeutralOverride = null)");
    }

    [Fact]
    public async Task EnrichPlayersAsync_WhenPlayerHasEnoughMatches_ShouldIgnoreStarRating()
    {
        await using var db = DbContextFactory.Create(nameof(EnrichPlayersAsync_WhenPlayerHasEnoughMatches_ShouldIgnoreStarRating));

        var group = await SeedGroupAsync(db);

        // Jogador com 5 estrelas mas com partidas suficientes (>= 3)
        var target = await SeedPlayerAsync(db, group.Id, guestStarRating: 5, isGuest: true);
        var dummy  = await SeedPlayerAsync(db, group.Id);

        // Seed de 3 partidas finalizadas para acumular MatchesPlayed >= minMatchesNonNeutral
        for (var i = 0; i < 3; i++)
        {
            db.ChangeTracker.Clear();
            await SeedFinalizedMatchAsync(db, group.Id, target.Id, dummy.Id);
        }

        db.ChangeTracker.Clear();

        var sut = CreateSut(db);
        var dto = new PlayerRequestDto(target.Id, target.Name, target.IsGoalkeeper);

        var result = await sut.EnrichPlayersAsync(new List<PlayerRequestDto> { dto });

        result.Should().HaveCount(1);
        result[0].NeutralOverride.Should().BeNull(
            "NeutralOverride não deve ser aplicado quando o jogador tem partidas suficientes");
        result[0].WinRate.Should().BeGreaterThan(0,
            "WinRate real deve ser calculado com base nas partidas finalizadas");
    }

    [Fact]
    public async Task EnrichPlayersAsync_WhenPlayerNotInDb_ShouldReturnNullNeutralOverride()
    {
        await using var db = DbContextFactory.Create(nameof(EnrichPlayersAsync_WhenPlayerNotInDb_ShouldReturnNullNeutralOverride));

        // Nenhum player no banco; o DTO usa um ID desconhecido
        var sut = CreateSut(db);
        var dto = new PlayerRequestDto(Guid.NewGuid(), "Fantasma", false);

        var result = await sut.EnrichPlayersAsync(new List<PlayerRequestDto> { dto });

        result.Should().HaveCount(1);
        result[0].NeutralOverride.Should().BeNull(
            "jogador ausente no banco não possui GuestStarRating, portanto NeutralOverride = null");
    }
}
