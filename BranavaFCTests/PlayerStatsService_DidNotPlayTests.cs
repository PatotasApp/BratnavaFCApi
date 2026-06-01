using BratnavaFC.Application.Services;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BranavaFC.Tests;

/// <summary>
/// Testes para verificar que jogadores marcados com DidNotPlay=true são
/// excluídos de todas as estatísticas: GamesPlayed, W/L/T, MVPs, goals, assists.
/// </summary>
public sealed class PlayerStatsService_DidNotPlayTests
{
    private static PlayerStatsService CreateSut(BratnavaFC.Infrastructure.Data.AppDbContext db)
        => new(db);

    // ── Seeds ─────────────────────────────────────────────────────────────────

    private static async Task<GroupEntity> SeedGroupAsync(BratnavaFC.Infrastructure.Data.AppDbContext db)
    {
        var g = new GroupEntity("DidNotPlayGroup", null, Guid.NewGuid());
        db.Groups.Add(g);
        await db.SaveChangesAsync();
        return g;
    }

    private static async Task<PlayerEntity> SeedPlayerAsync(
        BratnavaFC.Infrastructure.Data.AppDbContext db, Guid groupId, string name = "P")
    {
        var p = new PlayerEntity(name, null, groupId, 0m, false, false, Status.Active);
        db.Players.Add(p);
        await db.SaveChangesAsync();
        return p;
    }

    /// <summary>
    /// Seed uma partida finalizada onde p1 (Time A) é marcado como DidNotPlay.
    /// p2 está no Time B e não é marcado como ausente.
    /// Placar: 1-0 (Time A vence).
    /// </summary>
    private static async Task<MatchEntity> SeedFinalizedMatchWithDidNotPlayAsync(
        BratnavaFC.Infrastructure.Data.AppDbContext db,
        Guid groupId,
        Guid p1Id, // DidNotPlay = true
        Guid p2Id,
        bool addGoalForP1 = false)
    {
        var p1 = await db.Players.FindAsync(p1Id)!;
        var p2 = await db.Players.FindAsync(p2Id)!;

        var match = new MatchEntity(groupId, DateTime.UtcNow.AddDays(-1), "Arena");
        var mp1   = new MatchPlayerEntity(p1!.Id);
        var mp2   = new MatchPlayerEntity(p2!.Id);
        match.AddPlayer(mp1, p1);
        match.AddPlayer(mp2, p2);
        db.Matches.Add(match);
        await db.SaveChangesAsync();

        match.OpenAcceptation();
        match.AcceptInvite(p1.Id);
        match.AcceptInvite(p2.Id);
        match.GoToMatchMaking();
        match.AssignTeams(new[] { p1.Id }, new[] { p2.Id });
        match.Start();
        match.End();
        match.GoToPostGame();
        match.SetScore(1, 0);

        if (addGoalForP1)
            match.AddGoalByMatchPlayer(mp1.Id, null, timeSeconds: 10);

        match.FinalizeByVotes();

        // Marca p1 como não foi jogar
        mp1.SetDidNotPlay(true);
        await db.SaveChangesAsync();

        return match;
    }

    // ==========================================================================
    // GetVisualReportAsync — DidNotPlay exclusion
    // ==========================================================================

    [Fact]
    public async Task GetVisualReport_WhenPlayerDidNotPlay_ShouldNotCountGamesPlayed()
    {
        // Arrange — p1 tem DidNotPlay=true em 1 partida; p2 jogou normalmente
        await using var db = DbContextFactory.Create(nameof(GetVisualReport_WhenPlayerDidNotPlay_ShouldNotCountGamesPlayed));
        var grp = await SeedGroupAsync(db);
        var p1  = await SeedPlayerAsync(db, grp.Id, "Ausente");
        var p2  = await SeedPlayerAsync(db, grp.Id, "Presente");

        await SeedFinalizedMatchWithDidNotPlayAsync(db, grp.Id, p1.Id, p2.Id);

        var sut    = CreateSut(db);
        var report = await sut.GetVisualReportAsync(grp.Id);

        // Assert
        var statP1 = report.Players.FirstOrDefault(p => p.PlayerId == p1.Id);
        var statP2 = report.Players.FirstOrDefault(p => p.PlayerId == p2.Id);

        statP1.Should().NotBeNull();
        statP2.Should().NotBeNull();

        statP1!.GamesPlayed.Should().Be(0,
            "p1 foi marcado como DidNotPlay — não deve contar como partida jogada.");
        statP2!.GamesPlayed.Should().Be(1,
            "p2 jogou normalmente e deve ter 1 partida registrada.");
    }

    [Fact]
    public async Task GetVisualReport_WhenPlayerDidNotPlay_ShouldNotCountWinsOrLosses()
    {
        // Arrange — p1 (Time A) tem DidNotPlay=true; placar 1-0 (Time A vence)
        await using var db = DbContextFactory.Create(nameof(GetVisualReport_WhenPlayerDidNotPlay_ShouldNotCountWinsOrLosses));
        var grp = await SeedGroupAsync(db);
        var p1  = await SeedPlayerAsync(db, grp.Id, "Ausente");
        var p2  = await SeedPlayerAsync(db, grp.Id, "Presente");

        await SeedFinalizedMatchWithDidNotPlayAsync(db, grp.Id, p1.Id, p2.Id);

        var sut    = CreateSut(db);
        var report = await sut.GetVisualReportAsync(grp.Id);

        var statP1 = report.Players.First(p => p.PlayerId == p1.Id);

        statP1.Wins.Should().Be(0, "DidNotPlay — não ganha vitória mesmo que seu time vença.");
        statP1.Losses.Should().Be(0, "DidNotPlay — não ganha derrota.");
        statP1.Ties.Should().Be(0, "DidNotPlay — não ganha empate.");
    }

    [Fact]
    public async Task GetVisualReport_WhenPlayerDidNotPlay_GoalsShouldNotCount()
    {
        // Arrange — p1 tem DidNotPlay=true e tinha um gol registrado
        await using var db = DbContextFactory.Create(nameof(GetVisualReport_WhenPlayerDidNotPlay_GoalsShouldNotCount));
        var grp = await SeedGroupAsync(db);
        var p1  = await SeedPlayerAsync(db, grp.Id, "Ausente");
        var p2  = await SeedPlayerAsync(db, grp.Id, "Presente");

        await SeedFinalizedMatchWithDidNotPlayAsync(db, grp.Id, p1.Id, p2.Id, addGoalForP1: true);

        var sut    = CreateSut(db);
        var report = await sut.GetVisualReportAsync(grp.Id);

        var statP1 = report.Players.First(p => p.PlayerId == p1.Id);
        statP1.Goals.Should().Be(0,
            "gols de jogadores DidNotPlay não devem ser contabilizados nas estatísticas.");
    }

    [Fact]
    public async Task GetVisualReport_WhenPlayerDidNotPlay_ShouldNotReceiveMvpVotes()
    {
        // Arrange — cria tudo em memória (zero SaveChangesAsync intermediário),
        // igual ao padrão de SeedFinalizedMatchWithMvpAsync que funciona.
        await using var db = DbContextFactory.Create(nameof(GetVisualReport_WhenPlayerDidNotPlay_ShouldNotReceiveMvpVotes));

        var groupId = Guid.NewGuid();

        // Grupo e players criados inline (sem saves parciais)
        var group = new GroupEntity("G", null, Guid.NewGuid());
        db.Groups.Add(group);

        var p1 = new PlayerEntity("Ausente",  null, group.Id, 0, false, false, Status.Active);
        var p2 = new PlayerEntity("Presente", null, group.Id, 0, false, false, Status.Active);
        db.Players.Add(p1);
        db.Players.Add(p2);
        await db.SaveChangesAsync(); // salva grupo e players → "Unchanged"

        var mp1 = new MatchPlayerEntity(p1.Id);
        var mp2 = new MatchPlayerEntity(p2.Id);

        // Usa p1/p2 que já são rastreados (mesma instância)
        var match = new MatchEntity(group.Id, DateTime.UtcNow.AddDays(-1), "Arena");
        match.AddPlayer(mp1, p1);
        match.AddPlayer(mp2, p2);
        match.OpenAcceptation();
        match.AcceptInvite(p1.Id); match.AcceptInvite(p2.Id);
        match.GoToMatchMaking();
        match.AssignTeams(new[] { p1.Id }, new[] { p2.Id });
        match.Start(); match.End(); match.GoToPostGame();
        match.SetScore(1, 0);

        match.FinalizeByVotes();

        // Marca p1 como DidNotPlay ANTES do save
        mp1.SetDidNotPlay(true);

        // 1ª save: match + players (sem votos)
        db.Matches.Add(match);
        await db.SaveChangesAsync();

        // 2ª save: voto adicionado diretamente ao DbSet (evita conflito de rastreamento)
        var vote = new VoteEntity(match.Id, mp2.Id, mp1.Id);
        db.Votes.Add(vote);
        await db.SaveChangesAsync();

        var sut    = CreateSut(db);
        var report = await sut.GetVisualReportAsync(group.Id);

        var statP1 = report.Players.First(p => p.PlayerId == p1.Id);
        statP1.MvpVotes.Should().Be(0,
            "votos em jogador DidNotPlay não devem ser contabilizados.");
    }

    // ==========================================================================
    // GetVisualReport — DidNotPlay: múltiplas partidas (uma jogou, uma não)
    // ==========================================================================

    [Fact]
    public async Task GetVisualReport_WhenPlayerPlayedOneAndSkippedOne_ShouldCountOnlyPlayedMatch()
    {
        // Arrange — p1 joga partida 1 normalmente, tem DidNotPlay=true na partida 2
        await using var db = DbContextFactory.Create(nameof(GetVisualReport_WhenPlayerPlayedOneAndSkippedOne_ShouldCountOnlyPlayedMatch));
        var grp = await SeedGroupAsync(db);
        var p1  = await SeedPlayerAsync(db, grp.Id, "P1");
        var p2  = await SeedPlayerAsync(db, grp.Id, "P2");

        // Partida 1: p1 joga normalmente
        var p1E = await db.Players.FindAsync(p1.Id)!;
        var p2E = await db.Players.FindAsync(p2.Id)!;
        var m1  = new MatchEntity(grp.Id, DateTime.UtcNow.AddDays(-3), "Arena");
        var m1p1 = new MatchPlayerEntity(p1E!.Id);
        var m1p2 = new MatchPlayerEntity(p2E!.Id);
        m1.AddPlayer(m1p1, p1E); m1.AddPlayer(m1p2, p2E);
        db.Matches.Add(m1);
        await db.SaveChangesAsync();
        m1.OpenAcceptation(); m1.AcceptInvite(p1.Id); m1.AcceptInvite(p2.Id);
        m1.GoToMatchMaking(); m1.AssignTeams(new[] { p1.Id }, new[] { p2.Id });
        m1.Start(); m1.End(); m1.GoToPostGame(); m1.SetScore(1, 0); m1.FinalizeByVotes();
        await db.SaveChangesAsync();

        // Partida 2: p1 é marcado como DidNotPlay
        await SeedFinalizedMatchWithDidNotPlayAsync(db, grp.Id, p1.Id, p2.Id);

        var sut    = CreateSut(db);
        var report = await sut.GetVisualReportAsync(grp.Id);

        var statP1 = report.Players.First(p => p.PlayerId == p1.Id);
        statP1.GamesPlayed.Should().Be(1,
            "p1 jogou 1 partida e faltou em outra — deve contar apenas 1.");
    }
}
