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

    // -----------------------------------------------------------------
    // Seeds (GetVisualReportAsync)
    // -----------------------------------------------------------------

    /// <summary>
    /// Seed de partida do estado Created até PostGame (sem finalizar).
    /// Retorna o match e um mapa playerId → MatchPlayerEntity para facilitar testes de gols/votos.
    /// </summary>
    private static async Task<(MatchEntity Match, Dictionary<Guid, MatchPlayerEntity> PlayerMap)>
        SeedMatchUpToPostGameAsync(
            BratnavaFC.Infrastructure.Data.AppDbContext db,
            Guid groupId,
            IReadOnlyList<Guid> teamAPlayerIds,
            IReadOnlyList<Guid> teamBPlayerIds)
    {
        var allIds = teamAPlayerIds.Concat(teamBPlayerIds).ToList();

        var match = new MatchEntity(groupId, DateTime.UtcNow.AddDays(-1), "Arena");

        foreach (var pid in allIds)
        {
            var player = await db.Players.FindAsync(pid)
                         ?? throw new InvalidOperationException($"Player {pid} não encontrado.");
            match.AddPlayer(new MatchPlayerEntity(pid), player);
        }

        db.Matches.Add(match);
        await db.SaveChangesAsync();

        match.OpenAcceptation();
        foreach (var pid in allIds)
            match.AcceptInvite(pid);
        await db.SaveChangesAsync();

        match.GoToMatchMaking();
        match.AssignTeams(teamAPlayerIds.ToArray(), teamBPlayerIds.ToArray());
        await db.SaveChangesAsync();

        match.Start();
        match.End();
        match.GoToPostGame();
        await db.SaveChangesAsync();

        return (match, match.Players.ToDictionary(mp => mp.PlayerId));
    }

    // -----------------------------------------------------------------
    // Testes — GetVisualReportAsync
    // -----------------------------------------------------------------

    [Fact]
    public async Task GetVisualReportAsync_WhenGroupHasNoPlayers_ShouldReturnEmptyReport()
    {
        await using var db = DbContextFactory.Create(nameof(GetVisualReportAsync_WhenGroupHasNoPlayers_ShouldReturnEmptyReport));

        var sut = CreateSut(db);
        var unknownGroupId = Guid.NewGuid();

        var result = await sut.GetVisualReportAsync(unknownGroupId);

        result.GroupId.Should().Be(unknownGroupId);
        result.TotalMatchesConsidered.Should().Be(0);
        result.TotalFinalizedMatches.Should().Be(0);
        result.Players.Should().BeEmpty("sem jogadores no grupo o relatório deve estar vazio");
    }

    [Fact]
    public async Task GetVisualReportAsync_WhenGroupHasPlayersButNoMatches_ShouldReturnZeroStats()
    {
        await using var db = DbContextFactory.Create(nameof(GetVisualReportAsync_WhenGroupHasPlayersButNoMatches_ShouldReturnZeroStats));

        var group = await SeedGroupAsync(db);
        await SeedPlayerAsync(db, group.Id);
        await SeedPlayerAsync(db, group.Id);

        var sut = CreateSut(db);
        var result = await sut.GetVisualReportAsync(group.Id);

        result.TotalMatchesConsidered.Should().Be(0);
        result.Players.Should().HaveCount(2);
        result.Players.Should().OnlyContain(p =>
            p.GamesPlayed == 0 && p.Wins == 0 && p.Losses == 0 && p.Ties == 0 && p.Goals == 0,
            "sem partidas todos os contadores devem ser zero");
    }

    [Fact]
    public async Task GetVisualReportAsync_ShouldOnlyConsiderFinalizedMatches()
    {
        await using var db = DbContextFactory.Create(nameof(GetVisualReportAsync_ShouldOnlyConsiderFinalizedMatches));

        var group = await SeedGroupAsync(db);
        var p1 = await SeedPlayerAsync(db, group.Id);
        var p2 = await SeedPlayerAsync(db, group.Id);

        // 1 partida finalizada
        await SeedFinalizedMatchAsync(db, group.Id, p1.Id, p2.Id);

        // 1 partida não-finalizada (status Created)
        db.ChangeTracker.Clear();
        db.Matches.Add(new MatchEntity(group.Id, DateTime.UtcNow, "Arena"));
        await db.SaveChangesAsync();

        db.ChangeTracker.Clear();

        var sut = CreateSut(db);
        var result = await sut.GetVisualReportAsync(group.Id);

        result.TotalMatchesConsidered.Should().Be(1, "apenas partidas finalizadas devem ser consideradas");
        result.TotalFinalizedMatches.Should().Be(1);
    }

    [Fact]
    public async Task GetVisualReportAsync_ShouldCountWinsAndLossesCorrectly()
    {
        await using var db = DbContextFactory.Create(nameof(GetVisualReportAsync_ShouldCountWinsAndLossesCorrectly));

        var group = await SeedGroupAsync(db);
        var p1 = await SeedPlayerAsync(db, group.Id); // Time A — vence
        var p2 = await SeedPlayerAsync(db, group.Id); // Time B — perde

        var (match, _) = await SeedMatchUpToPostGameAsync(db, group.Id,
            new[] { p1.Id }, new[] { p2.Id });

        match.SetScore(2, 0); // Time A vence
        match.FinalizeByVotes();
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var sut = CreateSut(db);
        var result = await sut.GetVisualReportAsync(group.Id);

        var stat1 = result.Players.Single(p => p.PlayerId == p1.Id);
        var stat2 = result.Players.Single(p => p.PlayerId == p2.Id);

        stat1.GamesPlayed.Should().Be(1);
        stat1.Wins.Should().Be(1);
        stat1.Losses.Should().Be(0);

        stat2.GamesPlayed.Should().Be(1);
        stat2.Wins.Should().Be(0);
        stat2.Losses.Should().Be(1);
    }

    [Fact]
    public async Task GetVisualReportAsync_ShouldCountTiesCorrectly()
    {
        await using var db = DbContextFactory.Create(nameof(GetVisualReportAsync_ShouldCountTiesCorrectly));

        var group = await SeedGroupAsync(db);
        var p1 = await SeedPlayerAsync(db, group.Id);
        var p2 = await SeedPlayerAsync(db, group.Id);

        var (match, _) = await SeedMatchUpToPostGameAsync(db, group.Id,
            new[] { p1.Id }, new[] { p2.Id });

        match.SetScore(1, 1); // Empate
        match.FinalizeByVotes();
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var sut = CreateSut(db);
        var result = await sut.GetVisualReportAsync(group.Id);

        var stat1 = result.Players.Single(p => p.PlayerId == p1.Id);
        var stat2 = result.Players.Single(p => p.PlayerId == p2.Id);

        stat1.Ties.Should().Be(1);
        stat1.Wins.Should().Be(0);
        stat1.Losses.Should().Be(0);

        stat2.Ties.Should().Be(1);
        stat2.Wins.Should().Be(0);
        stat2.Losses.Should().Be(0);
    }

    [Fact]
    public async Task GetVisualReportAsync_ShouldCountGoalsAndAssistsCorrectly()
    {
        await using var db = DbContextFactory.Create(nameof(GetVisualReportAsync_ShouldCountGoalsAndAssistsCorrectly));

        var group = await SeedGroupAsync(db);
        var p1 = await SeedPlayerAsync(db, group.Id); // artilheiro, Time A
        var p2 = await SeedPlayerAsync(db, group.Id); // assistência, Time A
        var p3 = await SeedPlayerAsync(db, group.Id); // Time B

        var (match, playerMap) = await SeedMatchUpToPostGameAsync(db, group.Id,
            new[] { p1.Id, p2.Id }, new[] { p3.Id });

        // p1 marca, p2 assiste
        match.AddGoalByMatchPlayer(playerMap[p1.Id].Id, playerMap[p2.Id].Id, null);
        // FinalizeByVotes recalcula placar a partir dos gols
        match.FinalizeByVotes();
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var sut = CreateSut(db);
        var result = await sut.GetVisualReportAsync(group.Id);

        var stat1 = result.Players.Single(p => p.PlayerId == p1.Id);
        var stat2 = result.Players.Single(p => p.PlayerId == p2.Id);
        var stat3 = result.Players.Single(p => p.PlayerId == p3.Id);

        stat1.Goals.Should().Be(1, "p1 marcou o gol");
        stat1.Assists.Should().Be(0);

        stat2.Goals.Should().Be(0);
        stat2.Assists.Should().Be(1, "p2 deu a assistência");

        stat3.Goals.Should().Be(0);
        stat3.Assists.Should().Be(0);
    }

    [Fact]
    public async Task GetVisualReportAsync_ShouldCountOwnGoalsCorrectly()
    {
        await using var db = DbContextFactory.Create(nameof(GetVisualReportAsync_ShouldCountOwnGoalsCorrectly));

        var group = await SeedGroupAsync(db);
        var p1 = await SeedPlayerAsync(db, group.Id); // Time A — marca gol contra
        var p2 = await SeedPlayerAsync(db, group.Id); // Time B

        var (match, playerMap) = await SeedMatchUpToPostGameAsync(db, group.Id,
            new[] { p1.Id }, new[] { p2.Id });

        // p1 marca gol contra (isOwnGoal = true)
        match.AddGoalByMatchPlayer(playerMap[p1.Id].Id, null, null, isOwnGoal: true);
        match.FinalizeByVotes();
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var sut = CreateSut(db);
        var result = await sut.GetVisualReportAsync(group.Id);

        var stat1 = result.Players.Single(p => p.PlayerId == p1.Id);

        stat1.OwnGoals.Should().Be(1, "p1 marcou um gol contra");
        stat1.Goals.Should().Be(0, "gol contra não conta como gol normal");
    }

    /// <summary>
    /// Seed de partida finalizada onde mvpPlayerId recebe o MVP via votação real.
    /// Toda a máquina de estados é percorrida em memória antes de qualquer Save,
    /// garantindo que o único SaveChangesAsync efetue apenas INSERTs (estado Added)
    /// e nunca UPDATEs — evitando o DbUpdateConcurrencyException do EF InMemory.
    /// </summary>
    private static async Task SeedFinalizedMatchWithMvpAsync(
        BratnavaFC.Infrastructure.Data.AppDbContext db,
        Guid groupId,
        Guid mvpPlayerId,
        Guid voterPlayerId)
    {
        // Carrega os PlayerEntities (precisamos de GroupId para AddPlayer)
        var mvp   = await db.Players.FindAsync(mvpPlayerId)   ?? throw new InvalidOperationException("mvp não encontrado.");
        var voter = await db.Players.FindAsync(voterPlayerId) ?? throw new InvalidOperationException("voter não encontrado.");

        var mpMvp   = new MatchPlayerEntity(mvp.Id);
        var mpVoter = new MatchPlayerEntity(voter.Id);

        var match = new MatchEntity(groupId, DateTime.UtcNow.AddDays(-1), "Arena");
        match.AddPlayer(mpMvp, mvp);
        match.AddPlayer(mpVoter, voter);

        // Percorre toda a máquina de estados em memória
        match.OpenAcceptation();
        match.AcceptInvite(mvp.Id);
        match.AcceptInvite(voter.Id);
        match.GoToMatchMaking();
        match.AssignTeams(new[] { mvp.Id }, new[] { voter.Id });
        match.Start();
        match.End();
        match.GoToPostGame();
        match.SetScore(1, 0);

        // Adiciona o voto (ainda em memória, antes de qualquer tracking pelo EF)
        var vote = new VoteEntity(match.Id, mpVoter.Id, mpMvp.Id);
        match.Votes.Add(vote);

        // FinalizeByVotes encontra o voto em match.Votes e chama SetMvp no mpMvp
        match.FinalizeByVotes();

        // Único Save: todos os objetos estão em estado Added → apenas INSERTs, sem UPDATEs
        db.Matches.Add(match); // propaga para Players, Votes e Goals via navegação
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task GetVisualReportAsync_ShouldCountMvpsCorrectly()
    {
        await using var db = DbContextFactory.Create(nameof(GetVisualReportAsync_ShouldCountMvpsCorrectly));

        var group = await SeedGroupAsync(db);
        var p1 = await SeedPlayerAsync(db, group.Id); // MVP
        var p2 = await SeedPlayerAsync(db, group.Id); // Votante

        // Seed de partida finalizada com mvp (p1) via votação real
        await SeedFinalizedMatchWithMvpAsync(db, group.Id, mvpPlayerId: p1.Id, voterPlayerId: p2.Id);

        db.ChangeTracker.Clear();

        var sut = CreateSut(db);
        var result = await sut.GetVisualReportAsync(group.Id);

        var stat1 = result.Players.Single(p => p.PlayerId == p1.Id);
        var stat2 = result.Players.Single(p => p.PlayerId == p2.Id);

        stat1.Mvps.Should().Be(1, "p1 recebeu o voto de MVP e FinalizeByVotes o marcou como tal");
        stat2.Mvps.Should().Be(0);
    }

    [Fact]
    public async Task GetVisualReportAsync_ShouldReturnCorrectMatchTotals()
    {
        await using var db = DbContextFactory.Create(nameof(GetVisualReportAsync_ShouldReturnCorrectMatchTotals));

        var group = await SeedGroupAsync(db);
        var p1 = await SeedPlayerAsync(db, group.Id);
        var p2 = await SeedPlayerAsync(db, group.Id);

        // Seed 3 partidas finalizadas (todas com placar)
        for (var i = 0; i < 3; i++)
        {
            db.ChangeTracker.Clear();
            await SeedFinalizedMatchAsync(db, group.Id, p1.Id, p2.Id);
        }

        db.ChangeTracker.Clear();

        var sut = CreateSut(db);
        var result = await sut.GetVisualReportAsync(group.Id);

        result.TotalMatchesConsidered.Should().Be(3);
        result.TotalFinalizedMatches.Should().Be(3);
        result.TotalMatchesWithScore.Should().Be(3, "todas as partidas finalizadas têm placar definido");

        // p1 e p2 devem ter 3 partidas jogadas cada
        result.Players.Should().OnlyContain(p => p.GamesPlayed == 3);
    }

    [Fact]
    public async Task GetVisualReportAsync_ShouldOrderPlayersByWinRateThenGamesPlayed()
    {
        await using var db = DbContextFactory.Create(nameof(GetVisualReportAsync_ShouldOrderPlayersByWinRateThenGamesPlayed));

        var group = await SeedGroupAsync(db);
        var p1 = await SeedPlayerAsync(db, group.Id); // vai vencer → maior WinRate
        var p2 = await SeedPlayerAsync(db, group.Id); // vai perder → menor WinRate

        var (match, _) = await SeedMatchUpToPostGameAsync(db, group.Id,
            new[] { p1.Id }, new[] { p2.Id });

        match.SetScore(3, 0); // p1 vence
        match.FinalizeByVotes();
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var sut = CreateSut(db);
        var result = await sut.GetVisualReportAsync(group.Id);

        result.Players.Should().HaveCount(2);
        result.Players[0].PlayerId.Should().Be(p1.Id,
            "p1 tem WinRate 1.0 e deve aparecer primeiro na ordenação");
        result.Players[1].PlayerId.Should().Be(p2.Id,
            "p2 tem WinRate 0.0 e deve aparecer por último");
    }
}
