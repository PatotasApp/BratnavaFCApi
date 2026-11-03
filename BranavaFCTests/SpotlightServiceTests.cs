using System.Security.Claims;
using BratnavaFC.Api.Controllers;
using BratnavaFC.Application.Services;
using BratnavaFC.Domain.Championships;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Models;
using BratnavaFC.Infrastructure.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BranavaFC.Tests;

/// <summary>Spotlight do telão: configuração, período, nomes e acesso só de admin.</summary>
public sealed class SpotlightServiceTests
{
    private static readonly DateTime Now = new(2026, 11, 2, 15, 0, 0, DateTimeKind.Utc);

    private static SpotlightService Sut(AppDbContext db) => new(db, () => Now);

    // ── Configuração ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Configuracao_PadraoQuandoNuncaSalva_ESalvaSemMexerNoResto()
    {
        await using var db = DbContextFactory.Create(nameof(Configuracao_PadraoQuandoNuncaSalva_ESalvaSemMexerNoResto));
        var group = await SeedGroupAsync(db);

        var initial = await Sut(db).GetSettingsAsync(group.Id);
        initial.Data!.Should().BeEquivalentTo(new SpotlightSettings());

        var saved = await Sut(db).SaveSettingsAsync(group.Id, new SpotlightSettings
        {
            Title = "  Entrevero!  ",
            Subtitle = "Encerramento 2026",
            Emoji = "🥘",
            Color = "#16A34A",
            HiddenChapters = ["duplas", "inexistente", "duplas"],
            Pace = 1.3,
            PeriodMode = SpotlightPeriodMode.LastGames,
            LastGames = 10,
            OnlyMonthly = false,
            IncludeChampionships = false,
            MinGames = 4,
        });

        saved.Success.Should().BeTrue(saved.Error);
        db.ChangeTracker.Clear();
        var reloaded = (await Sut(db).GetSettingsAsync(group.Id)).Data!;
        reloaded.Title.Should().Be("Entrevero!");
        reloaded.Color.Should().Be("#16a34a");
        reloaded.HiddenChapters.Should().Equal("duplas");
        reloaded.PeriodMode.Should().Be(SpotlightPeriodMode.LastGames);
        reloaded.LastGames.Should().Be(10);
        reloaded.MinGames.Should().Be(4);

        // Linha nova do GroupSettings com os padrões do GET das configurações.
        var settings = await db.GroupSettings.SingleAsync(x => x.GroupId == group.Id);
        (settings.MinPlayers, settings.MaxPlayers).Should().Be((5, 6));
    }

    [Theory]
    [InlineData("azul", null, null)]
    [InlineData("#2f6df6", 9.0, null)]
    [InlineData("#2f6df6", 1.0, 0)]
    public async Task Configuracao_Invalida_EhRecusada(string color, double? pace, int? lastGames)
    {
        await using var db = DbContextFactory.Create(nameof(Configuracao_Invalida_EhRecusada) + color + pace + lastGames);
        var group = await SeedGroupAsync(db);

        var result = await Sut(db).SaveSettingsAsync(group.Id, new SpotlightSettings
        {
            Color = color,
            Pace = pace ?? 1,
            PeriodMode = lastGames.HasValue ? SpotlightPeriodMode.LastGames : SpotlightPeriodMode.Season,
            LastGames = lastGames,
        });

        result.Success.Should().BeFalse();
        (await db.GroupSettings.AnyAsync()).Should().BeFalse();
    }

    // ── Nomes ────────────────────────────────────────────────────────────────

    [Fact]
    public void Nomes_PrimeiroNome_SalvoQuandoOutroDaPatotaTemOMesmo()
    {
        var (pedro1, pedro2, caio, luis) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        var names = SpotlightService.DisplayNames([(pedro1, "Pedro Roweder"), (pedro2, "pedro"), (caio, "Caio Souza"), (luis, " Luis ")]);

        names[pedro1].Should().Be("Pedro Roweder");
        names[pedro2].Should().Be("pedro");
        names[caio].Should().Be("Caio");
        names[luis].Should().Be("Luis");
    }

    // ── Período ──────────────────────────────────────────────────────────────

    [Fact]
    public void Temporada_EhOAnoNoHorarioDeBrasilia()
    {
        var (from, to) = SpotlightService.ResolveDates(new SpotlightSettings { PeriodMode = SpotlightPeriodMode.Season }, Now);

        from.Should().Be(new DateTime(2026, 1, 1, 3, 0, 0, DateTimeKind.Utc));
        to.Should().Be(new DateTime(2027, 1, 1, 3, 0, 0, DateTimeKind.Utc));

        var since = SpotlightService.ResolveDates(
            new SpotlightSettings { PeriodMode = SpotlightPeriodMode.Since, SinceDate = new DateOnly(2026, 8, 16) }, Now);
        since.From.Should().Be(new DateTime(2026, 8, 16, 3, 0, 0, DateTimeKind.Utc));
        since.To.Should().BeNull();
    }

    [Fact]
    public async Task Relatorio_RespeitaTemporada_EUltimosJogos()
    {
        await using var db = DbContextFactory.Create(nameof(Relatorio_RespeitaTemporada_EUltimosJogos));
        var group = await SeedGroupAsync(db);
        var (ana, bia) = (await SeedPlayerAsync(db, group.Id, "Ana"), await SeedPlayerAsync(db, group.Id, "Bia"));

        // 31/12/2025 às 23h30 em Brasília ainda é 2025, mesmo já sendo 2026 em UTC.
        await FinalizedMatchAsync(db, group.Id, new DateTime(2026, 1, 1, 2, 30, 0, DateTimeKind.Utc), [ana], [bia], [(ana, null, false, null)]);
        await FinalizedMatchAsync(db, group.Id, new DateTime(2026, 3, 1, 23, 0, 0, DateTimeKind.Utc), [ana], [bia], [(bia, null, false, null)]);
        await FinalizedMatchAsync(db, group.Id, new DateTime(2026, 4, 1, 23, 0, 0, DateTimeKind.Utc), [ana], [bia], [(ana, null, false, null)]);

        var season = (await Sut(db).GetReportAsync(group.Id)).Data!;
        season.Numbers.Games.Should().Be(2);
        season.Period.SeasonYear.Should().Be(2026);
        season.Period.FirstGameAt.Should().Be(new DateTime(2026, 3, 1, 23, 0, 0, DateTimeKind.Utc));
        season.GroupName.Should().Be("Patota Teste");

        await Sut(db).SaveSettingsAsync(group.Id, new SpotlightSettings { PeriodMode = SpotlightPeriodMode.LastGames, LastGames = 1 });
        var last = (await Sut(db).GetReportAsync(group.Id)).Data!;
        last.Numbers.Games.Should().Be(1);
        last.Milestones!.FirstGoal!.ScorerName.Should().Be("Ana");

        await Sut(db).SaveSettingsAsync(group.Id, new SpotlightSettings { PeriodMode = SpotlightPeriodMode.All });
        (await Sut(db).GetReportAsync(group.Id)).Data!.Numbers.Games.Should().Be(3);
    }

    [Fact]
    public async Task Relatorio_OrdemDosGols_PeloHorarioEDepoisPeloCadastro()
    {
        await using var db = DbContextFactory.Create(nameof(Relatorio_OrdemDosGols_PeloHorarioEDepoisPeloCadastro));
        var group = await SeedGroupAsync(db);
        var (ana, bia, caio) = (await SeedPlayerAsync(db, group.Id, "Ana"), await SeedPlayerAsync(db, group.Id, "Bia"), await SeedPlayerAsync(db, group.Id, "Caio"));

        // Cadastro fora de ordem: o gol das 21:05 foi lançado antes do das 21:01; Bia e Caio empatam às 21:01.
        await FinalizedMatchAsync(db, group.Id, new DateTime(2026, 5, 1, 23, 0, 0, DateTimeKind.Utc), [ana, bia], [caio],
            [(ana, null, false, 1265), (bia, null, false, 1261), (caio, null, false, 1261)]);

        var report = (await Sut(db).GetReportAsync(group.Id)).Data!;

        report.Milestones!.FirstGoal!.ScorerName.Should().Be("Bia");
        report.Milestones.FirstGoal.TimeSeconds.Should().Be(1261);
        report.Milestones.LastGoal!.ScorerName.Should().Be("Ana");
        report.Decisive!.OpenedScoring.Select(x => x.Name).Should().Equal("Bia");
        report.Milestones.FirstGoal.Should().BeEquivalentTo(new { ScoreFor = 2, ScoreAgainst = 1 });
    }

    [Fact]
    public async Task Relatorio_SoMensalistas_ESemCampeonato_ComoConfigurado()
    {
        await using var db = DbContextFactory.Create(nameof(Relatorio_SoMensalistas_ESemCampeonato_ComoConfigurado));
        var group = await SeedGroupAsync(db);
        var ana = await SeedPlayerAsync(db, group.Id, "Ana");
        var guest = await SeedPlayerAsync(db, group.Id, "Visita", guest: true);
        await FinalizedMatchAsync(db, group.Id, new DateTime(2026, 5, 1, 23, 0, 0, DateTimeKind.Utc), [ana], [guest],
            [(guest, null, false, null), (guest, null, false, null)]);

        var mensalistas = (await Sut(db).GetReportAsync(group.Id)).Data!;
        mensalistas.Players.Select(p => p.Name).Should().Equal("Ana");
        mensalistas.Numbers.Goals.Should().Be(2);

        await Sut(db).SaveSettingsAsync(group.Id, new SpotlightSettings { OnlyMonthly = false });
        var todos = (await Sut(db).GetReportAsync(group.Id)).Data!;
        todos.Players.Select(p => p.Name).Should().Equal("Ana", "Visita");
        todos.Rankings.Goals!.Single().Name.Should().Be("Visita");
    }

    // ── Acesso: só admin da patota ───────────────────────────────────────────

    [Fact]
    public async Task Controller_MembroQueNaoEhAdmin_Recebe403EmTudo()
    {
        await using var db = DbContextFactory.Create(nameof(Controller_MembroQueNaoEhAdmin_Recebe403EmTudo));
        var group = await SeedGroupAsync(db);
        var userId = Guid.NewGuid();
        await SeedPlayerAsync(db, group.Id, "Membro", userId: userId);
        var sut = Controller(db, userId, "User");

        (await sut.GetReport(group.Id, CancellationToken.None)).Should().BeOfType<ForbidResult>();
        (await sut.GetSettings(group.Id, CancellationToken.None)).Should().BeOfType<ForbidResult>();
        (await sut.SaveSettings(group.Id, new SpotlightSettings(), CancellationToken.None)).Should().BeOfType<ForbidResult>();
        (await db.GroupSettings.AnyAsync()).Should().BeFalse();
    }

    [Theory]
    [InlineData("User", true)]
    [InlineData("Admin", false)]
    [InlineData("GodMode", false)]
    public async Task Controller_AdminDaPatotaOuDaPlataforma_Acessa(string role, bool groupAdmin)
    {
        await using var db = DbContextFactory.Create(nameof(Controller_AdminDaPatotaOuDaPlataforma_Acessa) + role);
        var group = await SeedGroupAsync(db);
        var userId = Guid.NewGuid();
        if (groupAdmin)
        {
            db.GroupAdmins.Add(new GroupAdminEntity { UserId = userId, GroupId = group.Id });
            await db.SaveChangesAsync();
        }
        var sut = Controller(db, userId, role);

        (await sut.SaveSettings(group.Id, new SpotlightSettings { Title = "Festa" }, CancellationToken.None))
            .Should().BeOfType<OkObjectResult>();
        (await sut.GetReport(group.Id, CancellationToken.None)).Should().BeOfType<OkObjectResult>();
        (await sut.GetSettings(group.Id, CancellationToken.None)).Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task Controller_AdminDeOutraPatota_Recebe403()
    {
        await using var db = DbContextFactory.Create(nameof(Controller_AdminDeOutraPatota_Recebe403));
        var group = await SeedGroupAsync(db);
        var other = await SeedGroupAsync(db);
        var userId = Guid.NewGuid();
        db.GroupAdmins.Add(new GroupAdminEntity { UserId = userId, GroupId = other.Id });
        await db.SaveChangesAsync();

        (await Controller(db, userId, "User").GetReport(group.Id, CancellationToken.None)).Should().BeOfType<ForbidResult>();
    }

    // ── Montagem ─────────────────────────────────────────────────────────────

    private static SpotlightController Controller(AppDbContext db, Guid userId, string role)
    {
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, userId.ToString()), new Claim(ClaimTypes.Role, role)],
            "Tests", ClaimTypes.Name, ClaimTypes.Role);
        return new SpotlightController(Sut(db), db)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) } },
        };
    }

    private static async Task<GroupEntity> SeedGroupAsync(AppDbContext db)
    {
        var group = new GroupEntity("Patota Teste", null, Guid.NewGuid());
        db.Groups.Add(group);
        await db.SaveChangesAsync();
        return group;
    }

    private static async Task<PlayerEntity> SeedPlayerAsync(AppDbContext db, Guid groupId, string name, bool guest = false, Guid? userId = null)
    {
        var player = new PlayerEntity(name, userId, groupId, 3m, false, guest);
        db.Players.Add(player);
        await db.SaveChangesAsync();
        return player;
    }

    private static async Task FinalizedMatchAsync(
        AppDbContext db, Guid groupId, DateTime playedAt,
        IReadOnlyList<PlayerEntity> teamA, IReadOnlyList<PlayerEntity> teamB,
        IReadOnlyList<(PlayerEntity Scorer, PlayerEntity? Assist, bool Own, int? Time)> goals)
    {
        var all = teamA.Concat(teamB).ToList();
        var match = new MatchEntity(groupId, playedAt, "Arena");
        foreach (var p in all)
            match.AddPlayer(new MatchPlayerEntity(p.Id), p);

        match.OpenAcceptation();
        foreach (var p in all) match.AcceptInvite(p.Id);
        match.GoToMatchMaking();
        match.AssignTeams(teamA.Select(p => p.Id).ToArray(), teamB.Select(p => p.Id).ToArray());
        match.Start();
        match.End();
        match.GoToPostGame();

        var byPlayer = match.Players.ToDictionary(mp => mp.PlayerId);
        foreach (var (scorer, assist, own, time) in goals)
            match.AddGoalByMatchPlayer(byPlayer[scorer.Id].Id, assist is null ? null : byPlayer[assist.Id].Id, time, own);
        if (goals.Count == 0)
            match.SetScore(0, 0);

        match.FinalizeByVotes();
        db.Matches.Add(match);
        await db.SaveChangesAsync();
    }
}

/// <summary>Campeonatos no Spotlight pelas regras deles (Postgres, como as outras estatísticas de campeonato).</summary>
[Trait("Category", "Postgres")]
public sealed class SpotlightCampeonatoTests : IClassFixture<CampeonatoPostgresFixture>
{
    private readonly CampeonatoPostgresFixture _fixture;

    public SpotlightCampeonatoTests(CampeonatoPostgresFixture fixture) => _fixture = fixture;

    private static CampeonatoEstatisticasTests.Caso Caso(HistoryMode mode) => CampeonatoEstatisticasTests.Casos
        .Cast<object[]>().Select(x => (CampeonatoEstatisticasTests.Caso)x[0])
        .First(c => c.History == mode && c.GoalsCount);

    [Fact]
    public async Task Consolidado_ContaComoUmJogo_SomandoGols_ForaDosRecordesDePartida()
    {
        await using var db = await _fixture.CreateDatabaseAsync(nameof(Consolidado_ContaComoUmJogo_SomandoGols_ForaDosRecordesDePartida));
        var (groupId, teams) = await CampeonatoEstatisticasTests.PlayChampionshipAsync(db, Caso(HistoryMode.Consolidated));
        db.ChangeTracker.Clear();
        var sut = new SpotlightService(db, () => new DateTime(2026, 12, 21, 12, 0, 0, DateTimeKind.Utc));

        var report = (await sut.GetReportAsync(groupId)).Data!;

        report.Numbers.Games.Should().Be(1, "o campeonato consolidado é 1 jogo");
        report.Numbers.Goals.Should().Be(7);
        report.Rankings.Goals!.Select(r => (r.PlayerId, r.Value, r.Rank)).Should().BeEquivalentTo(new[]
        {
            (teams[0][0], 3.0, 1), (teams[1][0], 3.0, 1), (teams[2][0], 1.0, 3)
        });
        report.Records.Hauls.Should().BeEmpty("3 gols em jogos diferentes não são recorde de partida");
        report.Numbers.MostGoalsGame.Should().BeNull();

        await sut.SaveSettingsAsync(groupId, new SpotlightSettings { IncludeChampionships = false });
        (await sut.GetReportAsync(groupId)).Data!.Numbers.Games.Should().Be(0);
    }

    [Fact]
    public async Task JogosSeparados_CadaJogoEhUmaPartida()
    {
        await using var db = await _fixture.CreateDatabaseAsync(nameof(JogosSeparados_CadaJogoEhUmaPartida));
        var (groupId, teams) = await CampeonatoEstatisticasTests.PlayChampionshipAsync(db, Caso(HistoryMode.SeparateGames));
        db.ChangeTracker.Clear();
        var sut = new SpotlightService(db, () => new DateTime(2026, 12, 21, 12, 0, 0, DateTimeKind.Utc));

        var report = (await sut.GetReportAsync(groupId)).Data!;

        report.Numbers.Games.Should().Be(3);
        report.Records.Hauls!.First().Should().BeEquivalentTo(new { PlayerId = teams[1][0], Goals = 3, Rank = 1 });
        (report.Numbers.MostGoalsGame!.GoalsA, report.Numbers.MostGoalsGame.GoalsB).Should().Be((3, 0));
        report.Milestones!.FirstGoal!.Number.Should().Be(1);
        report.Milestones.LastGoal!.Number.Should().Be(7);
    }
}
