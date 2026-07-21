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
        var group = new GroupEntity("Grupo Teste", null, Guid.NewGuid());
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

    [Fact]
    public async Task GetVisualReportAsync_ShouldReturnBackendCalculatedPointsAndRanks()
    {
        await using var db = DbContextFactory.Create(nameof(GetVisualReportAsync_ShouldReturnBackendCalculatedPointsAndRanks));

        var group = await SeedGroupAsync(db);
        var p1 = await SeedPlayerAsync(db, group.Id);
        var p2 = await SeedPlayerAsync(db, group.Id);
        var p3 = await SeedPlayerAsync(db, group.Id);

        var (match1, map1) = await SeedMatchUpToPostGameAsync(db, group.Id, new[] { p1.Id }, new[] { p2.Id });
        match1.AddGoalByMatchPlayer(map1[p1.Id].Id, null, null);
        match1.FinalizeByVotes();

        var (match2, _) = await SeedMatchUpToPostGameAsync(db, group.Id, new[] { p1.Id }, new[] { p3.Id });
        match2.SetScore(1, 1);
        match2.FinalizeByVotes();

        var (match3, map3) = await SeedMatchUpToPostGameAsync(db, group.Id, new[] { p2.Id }, new[] { p3.Id });
        match3.AddGoalByMatchPlayer(map3[p2.Id].Id, null, null);
        match3.FinalizeByVotes();

        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var sut = CreateSut(db);
        var result = await sut.GetVisualReportAsync(group.Id);

        var s1 = result.Players.Single(p => p.PlayerId == p1.Id);
        var s2 = result.Players.Single(p => p.PlayerId == p2.Id);
        var s3 = result.Players.Single(p => p.PlayerId == p3.Id);

        s1.Points.Should().Be(4);
        s2.Points.Should().Be(3);
        s3.Points.Should().Be(1);

        s1.ClassificationRank.Should().Be(1);
        s2.ClassificationRank.Should().Be(2);
        s3.ClassificationRank.Should().Be(3);

        s1.GoalsRank.Should().Be(1);
        s2.GoalsRank.Should().Be(1);
        s1.OwnGoalsRank.Should().BeNull();
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

        result.Data!.Should().HaveCount(1);
        result.Data![0].NeutralOverride.Should().BeApproximately(expectedOverride, 1e-9,
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

        result.Data!.Should().HaveCount(1);
        result.Data![0].NeutralOverride.Should().BeNull(
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

        result.Data!.Should().HaveCount(1);
        result.Data![0].NeutralOverride.Should().BeNull(
            "NeutralOverride não deve ser aplicado quando o jogador tem partidas suficientes");
        result.Data![0].WinRate.Should().BeGreaterThan(0,
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

        result.Data!.Should().HaveCount(1);
        result.Data![0].NeutralOverride.Should().BeNull(
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

    // -----------------------------------------------------------------
    // Seeds (EnrichPlayersAsync — W_base / Synergy_eff)
    // -----------------------------------------------------------------

    /// <summary>
    /// Seeds a finalized match with p1+p2 on Team A and p3+p4 on Team B.
    /// Used for synergy tests (players on the *same* team build pair accumulators).
    /// </summary>
    private static async Task SeedFinalizedMatchSameTeamAsync(
        BratnavaFC.Infrastructure.Data.AppDbContext db,
        Guid groupId,
        Guid p1Id, Guid p2Id,   // Team A
        Guid p3Id, Guid p4Id,   // Team B
        bool teamAWins)
    {
        var p1 = await db.Players.FindAsync(p1Id) ?? throw new InvalidOperationException("p1 not found.");
        var p2 = await db.Players.FindAsync(p2Id) ?? throw new InvalidOperationException("p2 not found.");
        var p3 = await db.Players.FindAsync(p3Id) ?? throw new InvalidOperationException("p3 not found.");
        var p4 = await db.Players.FindAsync(p4Id) ?? throw new InvalidOperationException("p4 not found.");

        var match = new MatchEntity(groupId, DateTime.UtcNow.AddDays(-1), "Arena");
        match.AddPlayer(new MatchPlayerEntity(p1Id), p1);
        match.AddPlayer(new MatchPlayerEntity(p2Id), p2);
        match.AddPlayer(new MatchPlayerEntity(p3Id), p3);
        match.AddPlayer(new MatchPlayerEntity(p4Id), p4);

        db.Matches.Add(match);
        await db.SaveChangesAsync();

        match.OpenAcceptation();
        foreach (var pid in new[] { p1Id, p2Id, p3Id, p4Id })
            match.AcceptInvite(pid);
        await db.SaveChangesAsync();

        match.GoToMatchMaking();
        match.AssignTeams(new[] { p1Id, p2Id }, new[] { p3Id, p4Id });
        await db.SaveChangesAsync();

        match.Start();
        match.End();
        match.GoToPostGame();
        match.SetScore(teamAWins ? 1 : 0, teamAWins ? 0 : 1);
        match.FinalizeByVotes();
        await db.SaveChangesAsync();
    }

    // -----------------------------------------------------------------
    // Testes — EnrichPlayersAsync / W_base
    // -----------------------------------------------------------------

    [Fact]
    public async Task EnrichPlayersAsync_ShouldPopulateGoalsAndAssists()
    {
        await using var db = DbContextFactory.Create(nameof(EnrichPlayersAsync_ShouldPopulateGoalsAndAssists));

        var group = await SeedGroupAsync(db);
        var p1 = await SeedPlayerAsync(db, group.Id); // artilheiro, Time A
        var p2 = await SeedPlayerAsync(db, group.Id); // assistência, Time A
        var p3 = await SeedPlayerAsync(db, group.Id); // Time B

        var (match, playerMap) = await SeedMatchUpToPostGameAsync(db, group.Id,
            new[] { p1.Id, p2.Id }, new[] { p3.Id });

        // p1 marca, p2 assiste
        match.AddGoalByMatchPlayer(playerMap[p1.Id].Id, playerMap[p2.Id].Id, null);
        match.FinalizeByVotes();
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var sut  = CreateSut(db);
        var dtos = new List<PlayerRequestDto>
        {
            new(p1.Id, p1.Name, p1.IsGoalkeeper),
            new(p2.Id, p2.Name, p2.IsGoalkeeper),
            new(p3.Id, p3.Name, p3.IsGoalkeeper),
        };

        var result = await sut.EnrichPlayersAsync(dtos);

        result.Data!.Single(r => r.PlayerId == p1.Id).Goals.Should().Be(1,   "p1 marcou o gol");
        result.Data!.Single(r => r.PlayerId == p1.Id).Assists.Should().Be(0);
        result.Data!.Single(r => r.PlayerId == p2.Id).Goals.Should().Be(0);
        result.Data!.Single(r => r.PlayerId == p2.Id).Assists.Should().Be(1, "p2 deu a assistência");
        result.Data!.Single(r => r.PlayerId == p3.Id).Goals.Should().Be(0);
        result.Data!.Single(r => r.PlayerId == p3.Id).Assists.Should().Be(0);
    }

    [Fact]
    public async Task EnrichPlayersAsync_WBase_ShouldRankWinnerAboveLoser()
    {
        await using var db = DbContextFactory.Create(nameof(EnrichPlayersAsync_WBase_ShouldRankWinnerAboveLoser));

        var group  = await SeedGroupAsync(db);
        var winner = await SeedPlayerAsync(db, group.Id);
        var loser  = await SeedPlayerAsync(db, group.Id);

        // 3 partidas: winner (Time A) vence todas
        for (int i = 0; i < 3; i++)
        {
            db.ChangeTracker.Clear();
            await SeedFinalizedMatchAsync(db, group.Id, winner.Id, loser.Id);
        }
        db.ChangeTracker.Clear();

        var sut  = CreateSut(db);
        var dtos = new List<PlayerRequestDto>
        {
            new(winner.Id, winner.Name, winner.IsGoalkeeper),
            new(loser.Id,  loser.Name,  loser.IsGoalkeeper),
        };

        var result = await sut.EnrichPlayersAsync(dtos);

        var winnerStats = result.Data!.Single(r => r.PlayerId == winner.Id);
        var loserStats  = result.Data!.Single(r => r.PlayerId == loser.Id);

        // Bayesian WR: winner ≈ (3+1.5)/(3+3) = 0.75,  W_base ≥ 0.5+
        winnerStats.WinRate.Should().BeGreaterThan(0.5,
            "vencedor com 3/3 vitórias deve ter W_base > 0.5");
        // Bayesian WR: loser ≈ (0+1.5)/(3+3) = 0.25, W_base < 0.5
        loserStats.WinRate.Should().BeLessThan(0.5,
            "perdedor com 0/3 vitórias deve ter W_base < 0.5");
        winnerStats.WinRate.Should().BeGreaterThan(loserStats.WinRate,
            "vencedor deve ter W_base maior que o perdedor");
    }

    // -----------------------------------------------------------------
    // Testes — EnrichPlayersAsync / Synergy_eff
    // -----------------------------------------------------------------

    [Fact]
    public async Task EnrichPlayersAsync_SynergyEff_ShouldBeZeroWhenPlayersNeverOnSameTeam()
    {
        await using var db = DbContextFactory.Create(nameof(EnrichPlayersAsync_SynergyEff_ShouldBeZeroWhenPlayersNeverOnSameTeam));

        var group = await SeedGroupAsync(db);
        var p1 = await SeedPlayerAsync(db, group.Id);
        var p2 = await SeedPlayerAsync(db, group.Id);

        // 3 partidas com p1 e p2 sempre em times *opostos*
        for (int i = 0; i < 3; i++)
        {
            db.ChangeTracker.Clear();
            await SeedFinalizedMatchAsync(db, group.Id, p1.Id, p2.Id);
        }
        db.ChangeTracker.Clear();

        var sut  = CreateSut(db);
        var dtos = new List<PlayerRequestDto>
        {
            new(p1.Id, p1.Name, p1.IsGoalkeeper),
            new(p2.Id, p2.Name, p2.IsGoalkeeper),
        };

        var result = await sut.EnrichPlayersAsync(dtos);

        var p1Stats = result.Data!.Single(r => r.PlayerId == p1.Id);
        p1Stats.SynergyWith.Should().ContainKey(p2.Id);
        p1Stats.SynergyWith[p2.Id].Should().BeApproximately(0.0, 1e-9,
            "p1 e p2 nunca jogaram no mesmo time → Synergy_eff = 0");
    }

    [Fact]
    public async Task EnrichPlayersAsync_SynergyEff_ShouldBePositiveWhenPairConsistentlyWinsTogether()
    {
        await using var db = DbContextFactory.Create(nameof(EnrichPlayersAsync_SynergyEff_ShouldBePositiveWhenPairConsistentlyWinsTogether));

        var group = await SeedGroupAsync(db);
        var p1 = await SeedPlayerAsync(db, group.Id);
        var p2 = await SeedPlayerAsync(db, group.Id);
        var p3 = await SeedPlayerAsync(db, group.Id);
        var p4 = await SeedPlayerAsync(db, group.Id);

        // 5 partidas: p1+p2 (Time A) vencem todas
        // WR_adj individual ≈ (5+1.5)/(5+3) = 0.8125
        // WR_together_adj = (5+1)/(5+2) ≈ 0.857 > baseline 0.8125 → Synergy_eff > 0
        for (int i = 0; i < 5; i++)
        {
            db.ChangeTracker.Clear();
            await SeedFinalizedMatchSameTeamAsync(db, group.Id,
                p1.Id, p2.Id, p3.Id, p4.Id, teamAWins: true);
        }
        db.ChangeTracker.Clear();

        var sut  = CreateSut(db);
        var dtos = new List<PlayerRequestDto>
        {
            new(p1.Id, p1.Name, p1.IsGoalkeeper),
            new(p2.Id, p2.Name, p2.IsGoalkeeper),
            new(p3.Id, p3.Name, p3.IsGoalkeeper),
            new(p4.Id, p4.Name, p4.IsGoalkeeper),
        };

        var result = await sut.EnrichPlayersAsync(dtos);

        var p1Stats = result.Data!.Single(r => r.PlayerId == p1.Id);
        p1Stats.SynergyWith.Should().ContainKey(p2.Id);
        p1Stats.SynergyWith[p2.Id].Should().BeGreaterThan(0.0,
            "p1 e p2 sempre vencem juntos → WR_together_adj > baseline → Synergy_eff > 0");
    }

    [Fact]
    public async Task EnrichPlayersAsync_SynergyEff_ShouldBeNegativeWhenPairConsistentlyLosesTogether()
    {
        await using var db = DbContextFactory.Create(nameof(EnrichPlayersAsync_SynergyEff_ShouldBeNegativeWhenPairConsistentlyLosesTogether));

        var group = await SeedGroupAsync(db);
        var p1 = await SeedPlayerAsync(db, group.Id);
        var p2 = await SeedPlayerAsync(db, group.Id);
        var p3 = await SeedPlayerAsync(db, group.Id);
        var p4 = await SeedPlayerAsync(db, group.Id);

        // 5 partidas: p1+p2 (Time A) perdem todas
        // WR_adj individual ≈ (0+1.5)/(5+3) = 0.1875
        // WR_together_adj = (0+1)/(5+2) ≈ 0.143 < baseline 0.1875 → Synergy_eff < 0
        for (int i = 0; i < 5; i++)
        {
            db.ChangeTracker.Clear();
            await SeedFinalizedMatchSameTeamAsync(db, group.Id,
                p1.Id, p2.Id, p3.Id, p4.Id, teamAWins: false);
        }
        db.ChangeTracker.Clear();

        var sut  = CreateSut(db);
        var dtos = new List<PlayerRequestDto>
        {
            new(p1.Id, p1.Name, p1.IsGoalkeeper),
            new(p2.Id, p2.Name, p2.IsGoalkeeper),
            new(p3.Id, p3.Name, p3.IsGoalkeeper),
            new(p4.Id, p4.Name, p4.IsGoalkeeper),
        };

        var result = await sut.EnrichPlayersAsync(dtos);

        var p1Stats = result.Data!.Single(r => r.PlayerId == p1.Id);
        p1Stats.SynergyWith.Should().ContainKey(p2.Id);
        p1Stats.SynergyWith[p2.Id].Should().BeLessThan(0.0,
            "p1 e p2 sempre perdem juntos → WR_together_adj < baseline → Synergy_eff < 0");
    }

    // -----------------------------------------------------------------
    // Seeds (tie helpers)
    // -----------------------------------------------------------------

    /// <summary>Seeds a finalized 1-v-1 match ending in a tie (1-1).</summary>
    private static async Task SeedFinalizedMatchTieAsync(
        BratnavaFC.Infrastructure.Data.AppDbContext db,
        Guid groupId,
        Guid p1Id,
        Guid p2Id)
    {
        var p1 = await db.Players.FindAsync(p1Id) ?? throw new InvalidOperationException("p1 não encontrado.");
        var p2 = await db.Players.FindAsync(p2Id) ?? throw new InvalidOperationException("p2 não encontrado.");

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
        match.SetScore(1, 1);   // tie
        match.FinalizeByVotes();
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Seeds a 4-player finalized match ending in a tie (1-1).
    /// p1 + p2 on Team A;  p3 + p4 on Team B.
    /// </summary>
    private static async Task SeedFinalizedMatchSameTeamTieAsync(
        BratnavaFC.Infrastructure.Data.AppDbContext db,
        Guid groupId,
        Guid p1Id, Guid p2Id,   // Team A
        Guid p3Id, Guid p4Id)   // Team B
    {
        var p1 = await db.Players.FindAsync(p1Id) ?? throw new InvalidOperationException("p1 not found.");
        var p2 = await db.Players.FindAsync(p2Id) ?? throw new InvalidOperationException("p2 not found.");
        var p3 = await db.Players.FindAsync(p3Id) ?? throw new InvalidOperationException("p3 not found.");
        var p4 = await db.Players.FindAsync(p4Id) ?? throw new InvalidOperationException("p4 not found.");

        var match = new MatchEntity(groupId, DateTime.UtcNow.AddDays(-1), "Arena");
        match.AddPlayer(new MatchPlayerEntity(p1Id), p1);
        match.AddPlayer(new MatchPlayerEntity(p2Id), p2);
        match.AddPlayer(new MatchPlayerEntity(p3Id), p3);
        match.AddPlayer(new MatchPlayerEntity(p4Id), p4);

        db.Matches.Add(match);
        await db.SaveChangesAsync();

        match.OpenAcceptation();
        foreach (var pid in new[] { p1Id, p2Id, p3Id, p4Id })
            match.AcceptInvite(pid);
        await db.SaveChangesAsync();

        match.GoToMatchMaking();
        match.AssignTeams(new[] { p1Id, p2Id }, new[] { p3Id, p4Id });
        await db.SaveChangesAsync();

        match.Start();
        match.End();
        match.GoToPostGame();
        match.SetScore(1, 1);   // tie
        match.FinalizeByVotes();
        await db.SaveChangesAsync();
    }

    // -----------------------------------------------------------------
    // Testes — LoadRecentFinalizedMatchesAsync (last-20 window)
    // -----------------------------------------------------------------

    [Fact]
    public async Task EnrichPlayersAsync_ShouldUseOnlyLast20Matches()
    {
        await using var db = DbContextFactory.Create(nameof(EnrichPlayersAsync_ShouldUseOnlyLast20Matches));

        var group  = await SeedGroupAsync(db);
        var winner = await SeedPlayerAsync(db, group.Id);
        var dummy  = await SeedPlayerAsync(db, group.Id);

        // Seed 21 finalized matches where winner always wins.
        // LoadRecentFinalizedMatchesAsync caps the window at 20 → Wins == 20, not 21.
        for (int i = 0; i < 21; i++)
        {
            db.ChangeTracker.Clear();
            await SeedFinalizedMatchAsync(db, group.Id, winner.Id, dummy.Id);
        }
        db.ChangeTracker.Clear();

        var sut  = CreateSut(db);
        var dtos = new List<PlayerRequestDto>
        {
            new(winner.Id, winner.Name, winner.IsGoalkeeper),
            new(dummy.Id,  dummy.Name,  dummy.IsGoalkeeper),
        };

        var result = await sut.EnrichPlayersAsync(dtos);

        result.Data!.Single(r => r.PlayerId == winner.Id).Wins.Should().Be(20,
            "LoadRecentFinalizedMatchesAsync must cap the window at 20 matches, not 21");
    }

    // -----------------------------------------------------------------
    // Testes — BayesianWinRate with ties
    // -----------------------------------------------------------------

    [Fact]
    public async Task EnrichPlayersAsync_WBase_TiesShouldRankBetweenWinAndLoss()
    {
        await using var db = DbContextFactory.Create(nameof(EnrichPlayersAsync_WBase_TiesShouldRankBetweenWinAndLoss));

        var group     = await SeedGroupAsync(db);
        var allWins   = await SeedPlayerAsync(db, group.Id);
        var allLosses = await SeedPlayerAsync(db, group.Id);
        var allTies   = await SeedPlayerAsync(db, group.Id);
        var dummy     = await SeedPlayerAsync(db, group.Id);

        // allWins (Team A) beats allLosses (Team B) — 3 times
        for (int i = 0; i < 3; i++)
        {
            db.ChangeTracker.Clear();
            await SeedFinalizedMatchAsync(db, group.Id, allWins.Id, allLosses.Id);
        }

        // allTies vs dummy — 3 tie games
        for (int i = 0; i < 3; i++)
        {
            db.ChangeTracker.Clear();
            await SeedFinalizedMatchTieAsync(db, group.Id, allTies.Id, dummy.Id);
        }
        db.ChangeTracker.Clear();

        var sut  = CreateSut(db);
        var dtos = new List<PlayerRequestDto>
        {
            new(allWins.Id,   allWins.Name,   allWins.IsGoalkeeper),
            new(allTies.Id,   allTies.Name,   allTies.IsGoalkeeper),
            new(allLosses.Id, allLosses.Name, allLosses.IsGoalkeeper),
        };

        var result = await sut.EnrichPlayersAsync(dtos);

        var winsStats   = result.Data!.Single(r => r.PlayerId == allWins.Id);
        var tiesStats   = result.Data!.Single(r => r.PlayerId == allTies.Id);
        var lossesStats = result.Data!.Single(r => r.PlayerId == allLosses.Id);

        // BayesianWR for all-ties: effectiveWins = 0 + 0.5×3 = 1.5, total = 3
        // → (1.5 + 1.5) / (3 + 3) = 3.0 / 6.0 = 0.5  → W_base ≈ 0.5
        tiesStats.WinRate.Should().BeApproximately(0.5, 0.02,
            "3 ties → BayesianWR = 0.5 → W_base ≈ 0.5");
        winsStats.WinRate.Should().BeGreaterThan(tiesStats.WinRate,
            "all-wins must rank above all-ties");
        tiesStats.WinRate.Should().BeGreaterThan(lossesStats.WinRate,
            "all-ties must rank above all-losses");
    }

    // -----------------------------------------------------------------
    // Testes — Synergy_eff with ties
    // -----------------------------------------------------------------

    [Fact]
    public async Task EnrichPlayersAsync_SynergyEff_AllTiesTogetherShouldBeNeutral()
    {
        await using var db = DbContextFactory.Create(nameof(EnrichPlayersAsync_SynergyEff_AllTiesTogetherShouldBeNeutral));

        var group = await SeedGroupAsync(db);
        var p1 = await SeedPlayerAsync(db, group.Id);
        var p2 = await SeedPlayerAsync(db, group.Id);
        var p3 = await SeedPlayerAsync(db, group.Id);
        var p4 = await SeedPlayerAsync(db, group.Id);

        // 5 tie games: p1+p2 (Team A) vs p3+p4 (Team B), all 1-1
        // TiesTogether(p1,p2) = 5, WinsTogether = 0
        // effectiveWinsTogether = 0 + 0.5×5 = 2.5
        // wrTogetherAdj = (2.5 + 1.0) / (5 + 2.0) = 3.5 / 7 = 0.5
        // BayesianWR each: (0 + 2.5 + 1.5) / (5 + 3) = 4.0 / 8 = 0.5 → baseline = 0.5
        // Synergy_eff = confidence × (0.5 − 0.5) = 0.0
        for (int i = 0; i < 5; i++)
        {
            db.ChangeTracker.Clear();
            await SeedFinalizedMatchSameTeamTieAsync(db, group.Id, p1.Id, p2.Id, p3.Id, p4.Id);
        }
        db.ChangeTracker.Clear();

        var sut  = CreateSut(db);
        var dtos = new List<PlayerRequestDto>
        {
            new(p1.Id, p1.Name, p1.IsGoalkeeper),
            new(p2.Id, p2.Name, p2.IsGoalkeeper),
            new(p3.Id, p3.Name, p3.IsGoalkeeper),
            new(p4.Id, p4.Name, p4.IsGoalkeeper),
        };

        var result = await sut.EnrichPlayersAsync(dtos);

        var p1Stats = result.Data!.Single(r => r.PlayerId == p1.Id);
        p1Stats.SynergyWith.Should().ContainKey(p2.Id);
        p1Stats.SynergyWith[p2.Id].Should().BeApproximately(0.0, 0.005,
            "all-tie games: effectiveWinsTogether = 2.5, wrTogetherAdj = 0.5 = baseline → Synergy_eff = 0");
    }

    // -----------------------------------------------------------------
    // Testes — GetVisualReportAsync / Synergy: AssistsGiven, AssistsReceived
    // -----------------------------------------------------------------

    /// <summary>
    /// Quando p2 assiste p1, a sinergia de p1 com p2 deve ter AssistsReceived=1
    /// e a sinergia de p2 com p1 deve ter AssistsGiven=1. Direção inversa deve ser 0.
    /// </summary>
    [Fact]
    public async Task GetVisualReportAsync_Synergy_AssistsGiven_ShouldReflectCorrectDirection()
    {
        await using var db = DbContextFactory.Create(nameof(GetVisualReportAsync_Synergy_AssistsGiven_ShouldReflectCorrectDirection));

        var group = await SeedGroupAsync(db);
        var p1 = await SeedPlayerAsync(db, group.Id); // artilheiro, Time A
        var p2 = await SeedPlayerAsync(db, group.Id); // assistente, Time A
        var p3 = await SeedPlayerAsync(db, group.Id); // Time B

        var (match, playerMap) = await SeedMatchUpToPostGameAsync(db, group.Id,
            new[] { p1.Id, p2.Id }, new[] { p3.Id });

        // p1 marca, p2 assiste
        match.AddGoalByMatchPlayer(playerMap[p1.Id].Id, playerMap[p2.Id].Id, null);
        match.FinalizeByVotes();
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var sut    = CreateSut(db);
        var result = await sut.GetVisualReportAsync(group.Id);

        var p1Synergy = result.Players.Single(p => p.PlayerId == p1.Id)
                               .Synergies.Single(s => s.WithPlayerId == p2.Id);
        var p2Synergy = result.Players.Single(p => p.PlayerId == p2.Id)
                               .Synergies.Single(s => s.WithPlayerId == p1.Id);

        // p2 deu a assistência → p1 recebeu, p2 deu
        p1Synergy.AssistsReceived.Should().Be(1, "p2 assistiu p1 → p1 deve ter AssistsReceived=1");
        p1Synergy.AssistsGiven.Should().Be(0,    "p1 não assistiu p2");

        p2Synergy.AssistsGiven.Should().Be(1,    "p2 assistiu p1 → p2 deve ter AssistsGiven=1");
        p2Synergy.AssistsReceived.Should().Be(0, "p2 não recebeu assistência de p1");
    }

    /// <summary>
    /// Quando cada jogador assiste o outro na mesma partida, ambos devem ter
    /// AssistsGiven=1 e AssistsReceived=1.
    /// </summary>
    [Fact]
    public async Task GetVisualReportAsync_Synergy_AssistsBothDirections_ShouldAccumulateSeparately()
    {
        await using var db = DbContextFactory.Create(nameof(GetVisualReportAsync_Synergy_AssistsBothDirections_ShouldAccumulateSeparately));

        var group = await SeedGroupAsync(db);
        var p1 = await SeedPlayerAsync(db, group.Id); // Time A
        var p2 = await SeedPlayerAsync(db, group.Id); // Time A
        var p3 = await SeedPlayerAsync(db, group.Id); // Time B

        var (match, playerMap) = await SeedMatchUpToPostGameAsync(db, group.Id,
            new[] { p1.Id, p2.Id }, new[] { p3.Id });

        // gol 1: p1 marca, p2 assiste
        match.AddGoalByMatchPlayer(playerMap[p1.Id].Id, playerMap[p2.Id].Id, null);
        // gol 2: p2 marca, p1 assiste
        match.AddGoalByMatchPlayer(playerMap[p2.Id].Id, playerMap[p1.Id].Id, null);
        match.FinalizeByVotes();
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var sut    = CreateSut(db);
        var result = await sut.GetVisualReportAsync(group.Id);

        var p1Synergy = result.Players.Single(p => p.PlayerId == p1.Id)
                               .Synergies.Single(s => s.WithPlayerId == p2.Id);
        var p2Synergy = result.Players.Single(p => p.PlayerId == p2.Id)
                               .Synergies.Single(s => s.WithPlayerId == p1.Id);

        p1Synergy.AssistsGiven.Should().Be(1,    "p1 assistiu p2 uma vez");
        p1Synergy.AssistsReceived.Should().Be(1, "p1 recebeu uma assistência de p2");

        p2Synergy.AssistsGiven.Should().Be(1,    "p2 assistiu p1 uma vez");
        p2Synergy.AssistsReceived.Should().Be(1, "p2 recebeu uma assistência de p1");
    }

    /// <summary>
    /// Assistências de p2 a p1 em partidas distintas devem acumular corretamente.
    /// </summary>
    [Fact]
    public async Task GetVisualReportAsync_Synergy_AssistsAccumulateAcrossMatches()
    {
        await using var db = DbContextFactory.Create(nameof(GetVisualReportAsync_Synergy_AssistsAccumulateAcrossMatches));

        var group = await SeedGroupAsync(db);
        var p1 = await SeedPlayerAsync(db, group.Id); // artilheiro recorrente
        var p2 = await SeedPlayerAsync(db, group.Id); // assistente recorrente
        var p3 = await SeedPlayerAsync(db, group.Id); // oponente

        // Partida 1: p1 marca, p2 assiste
        {
            var (match, playerMap) = await SeedMatchUpToPostGameAsync(db, group.Id,
                new[] { p1.Id, p2.Id }, new[] { p3.Id });
            match.AddGoalByMatchPlayer(playerMap[p1.Id].Id, playerMap[p2.Id].Id, null);
            match.FinalizeByVotes();
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
        }

        // Partida 2: p1 marca novamente, p2 assiste de novo
        {
            var (match, playerMap) = await SeedMatchUpToPostGameAsync(db, group.Id,
                new[] { p1.Id, p2.Id }, new[] { p3.Id });
            match.AddGoalByMatchPlayer(playerMap[p1.Id].Id, playerMap[p2.Id].Id, null);
            match.FinalizeByVotes();
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
        }

        var sut    = CreateSut(db);
        var result = await sut.GetVisualReportAsync(group.Id);

        var p2Synergy = result.Players.Single(p => p.PlayerId == p2.Id)
                               .Synergies.Single(s => s.WithPlayerId == p1.Id);

        p2Synergy.AssistsGiven.Should().Be(2,    "p2 assistiu p1 em 2 partidas → deve acumular para 2");
        p2Synergy.AssistsReceived.Should().Be(0, "p1 nunca assistiu p2");

        var p1Synergy = result.Players.Single(p => p.PlayerId == p1.Id)
                               .Synergies.Single(s => s.WithPlayerId == p2.Id);
        p1Synergy.AssistsReceived.Should().Be(2, "p1 recebeu assistência de p2 em 2 partidas");
        p1Synergy.AssistsGiven.Should().Be(0,    "p1 nunca assistiu p2");
    }

    /// <summary>
    /// Gol contra não tem assistente → nenhuma assistência de par deve ser registrada.
    /// </summary>
    [Fact]
    public async Task GetVisualReportAsync_Synergy_OwnGoal_ShouldNotCreateAssistPair()
    {
        await using var db = DbContextFactory.Create(nameof(GetVisualReportAsync_Synergy_OwnGoal_ShouldNotCreateAssistPair));

        var group = await SeedGroupAsync(db);
        var p1 = await SeedPlayerAsync(db, group.Id); // gol contra, Time A
        var p2 = await SeedPlayerAsync(db, group.Id); // colega, Time A
        var p3 = await SeedPlayerAsync(db, group.Id); // Time B

        var (match, playerMap) = await SeedMatchUpToPostGameAsync(db, group.Id,
            new[] { p1.Id, p2.Id }, new[] { p3.Id });

        match.AddGoalByMatchPlayer(playerMap[p1.Id].Id, null, null, isOwnGoal: true);
        match.FinalizeByVotes();
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var sut    = CreateSut(db);
        var result = await sut.GetVisualReportAsync(group.Id);

        // nenhum par deve ter assistência
        result.Players.SelectMany(p => p.Synergies)
              .Should().OnlyContain(s => s.AssistsGiven == 0 && s.AssistsReceived == 0,
                  "gol contra não registra assistência em nenhum par");
    }

    // -----------------------------------------------------------------
    // Testes — GetVisualReportAsync / Synergy: MatchesTogether, WinsTogether
    // -----------------------------------------------------------------

    /// <summary>
    /// Verifica que MatchesTogether, WinsTogether e WinRateTogether são preenchidos
    /// corretamente na sinergia visual.
    /// </summary>
    [Fact]
    public async Task GetVisualReportAsync_Synergy_MatchesAndWinsTogetherShouldBePopulated()
    {
        await using var db = DbContextFactory.Create(nameof(GetVisualReportAsync_Synergy_MatchesAndWinsTogetherShouldBePopulated));

        var group = await SeedGroupAsync(db);
        var p1 = await SeedPlayerAsync(db, group.Id); // Time A (vence)
        var p2 = await SeedPlayerAsync(db, group.Id); // Time A (vence)
        var p3 = await SeedPlayerAsync(db, group.Id); // Time B
        var p4 = await SeedPlayerAsync(db, group.Id); // Time B

        // 2 partidas: p1+p2 (Time A) vence as duas
        for (int i = 0; i < 2; i++)
        {
            db.ChangeTracker.Clear();
            await SeedFinalizedMatchSameTeamAsync(db, group.Id, p1.Id, p2.Id, p3.Id, p4.Id, teamAWins: true);
        }
        db.ChangeTracker.Clear();

        var sut    = CreateSut(db);
        var result = await sut.GetVisualReportAsync(group.Id);

        var p1Synergy = result.Players.Single(p => p.PlayerId == p1.Id)
                               .Synergies.Single(s => s.WithPlayerId == p2.Id);

        p1Synergy.MatchesTogether.Should().Be(2, "p1 e p2 jogaram juntos em 2 partidas");
        p1Synergy.WinsTogether.Should().Be(2,    "p1 e p2 venceram nas 2 partidas");
        p1Synergy.WinRateTogether.Should().BeApproximately(1.0, 1e-9,
            "2 vitórias em 2 partidas → WinRateTogether = 1.0");
    }

    /// <summary>
    /// Jogadores de times opostos não devem ter MatchesTogether > 0 entre si.
    /// </summary>
    [Fact]
    public async Task GetVisualReportAsync_Synergy_OpposingTeamPlayers_ShouldHaveZeroMatchesTogether()
    {
        await using var db = DbContextFactory.Create(nameof(GetVisualReportAsync_Synergy_OpposingTeamPlayers_ShouldHaveZeroMatchesTogether));

        var group = await SeedGroupAsync(db);
        var p1 = await SeedPlayerAsync(db, group.Id); // Time A
        var p2 = await SeedPlayerAsync(db, group.Id); // Time B

        await SeedFinalizedMatchAsync(db, group.Id, p1.Id, p2.Id);
        db.ChangeTracker.Clear();

        var sut    = CreateSut(db);
        var result = await sut.GetVisualReportAsync(group.Id);

        var p1Synergy = result.Players.Single(p => p.PlayerId == p1.Id)
                               .Synergies.Single(s => s.WithPlayerId == p2.Id);

        p1Synergy.MatchesTogether.Should().Be(0,
            "p1 e p2 estiveram em times opostos → MatchesTogether = 0");
        p1Synergy.WinsTogether.Should().Be(0);
    }
}
