using BratnavaFC.Application.Abstractions;
using BratnavaFC.Application.Services;
using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos.Seasons;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace BranavaFC.Tests;

/// <summary>
/// Temporadas (fases 2 e 4): títulos selados no fechamento, títulos do ano, conquistas
/// e período por temporada, e a retrospectiva com a classificação real.
/// </summary>
[Trait("Category", "Postgres")]
public sealed class TemporadasTitulosTests : IClassFixture<CampeonatoPostgresFixture>
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private readonly CampeonatoPostgresFixture _fixture;

    public TemporadasTitulosTests(CampeonatoPostgresFixture fixture) => _fixture = fixture;

    private static DateTimeOffset Brt(int year, int month, int day, int hour = 0, int minute = 0) =>
        new(year, month, day, hour, minute, 0, TimeSpan.FromHours(-3));

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now.ToUniversalTime();
    }

    private sealed record Cenario(Guid GroupId, Guid AdminId, List<Guid> Players, SeasonService Sut, Clock Clock, Mock<IPushService> Push);

    /// <summary>
    /// 5 jogadores com 3 jogos cada em março/2026. Gols: P0=5, P1=3, P2=3, P3=1, P4=0.
    /// Vitórias: P0 3, P1 2, P2 1, P3 0, P4 0.
    /// </summary>
    private async Task<Cenario> MontarAsync(string name, DateTimeOffset now)
    {
        var db = await _fixture.CreateDatabaseAsync(name);
        var group = new GroupEntity("Bratnava FC", null, Guid.NewGuid());
        db.Groups.Add(group);
        var admin = new UserEntity("admin", "Luis", "Mello", "admin@t.com", "hash", null, null);
        db.Users.Add(admin);
        var players = Enumerable.Range(0, 5)
            .Select(i => new PlayerEntity($"Jogador {i}", null, group.Id, 3m, isGoalkeeper: i == 4)).ToList();
        db.Players.AddRange(players);
        await db.SaveChangesAsync();

        int[] goals = [5, 3, 3, 1, 0];
        int[] wins = [3, 2, 1, 0, 0];
        for (var game = 0; game < 3; game++)
            await AddGameAsync(db, group.Id, Brt(2026, 3, 10 + game, 20),
                players.Select((p, i) => (p.Id, Goals: game == 0 ? goals[i] : 0, Win: game < wins[i])).ToList());

        var clock = new Clock(now);
        var push = new Mock<IPushService>();
        var sut = new SeasonService(db, clock, new ConquistaProjectionService(db), push.Object);
        return new Cenario(group.Id, admin.Id, players.Select(p => p.Id).ToList(), sut, clock, push);
    }

    /// <summary>Partida (não finalizada, para nada recalcular por cima) e as contribuições dela.</summary>
    private static async Task AddGameAsync(AppDbContext db, Guid groupId, DateTimeOffset playedAt,
        IReadOnlyList<(Guid PlayerId, int Goals, bool Win)> rows)
    {
        var match = new MatchEntity(groupId, playedAt.UtcDateTime, "Arena");
        db.Matches.Add(match);
        await db.SaveChangesAsync();
        db.PlayerMatchStatContributions.AddRange(rows.Select(r => new PlayerMatchStatContributionEntity(
            match.Id, groupId, r.PlayerId, playedAt.Year, playedAt.ToUniversalTime(), r.Win ? "W" : "L", false, r.Goals, 0, 0, 0, 1)));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    private static AppDbContext Db(Cenario c) =>
        (AppDbContext)typeof(SeasonService).GetField("_db", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(c.Sut)!;

    [Fact]
    public async Task FecharTemporada_SelaOsTitulos_EAvisaAPatota()
    {
        var c = await MontarAsync(nameof(FecharTemporada_SelaOsTitulos_EAvisaAPatota), Brt(2026, 11, 22, 10));
        var current = Ok(await c.Sut.ListAsync(c.GroupId, Ct)).Seasons.Single();

        Ok(await c.Sut.CloseAsync(c.GroupId, c.AdminId, new CloseSeasonDto(Brt(2026, 11, 21, 23, 59), "Pré-temporada", true), Ct));

        var titles = await Db(c).SeasonTitles.AsNoTracking().Where(t => t.Category == "Gols").OrderBy(t => t.Position).ToListAsync();
        titles.Should().OnlyContain(t => t.SeasonId == current.Id && t.Scope == SeasonTitleEntity.ScopeSeason && t.Season == 2026);
        titles.Select(t => (t.PlayerId, t.Position, t.Value)).Should().Equal(
            (c.Players[0], 1, 5), (c.Players[1], 2, 3), (c.Players[2], 2, 3), (c.Players[3], 3, 1));

        c.Push.Verify(p => p.SendToGroupAsync(c.GroupId, "Temporada 2026/1 encerrada", It.IsAny<string>(),
            It.Is<Dictionary<string, string>>(d => d["type"] == "season_closed" && d["seasonId"] == current.Id.ToString()),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PreTemporada_NaoGeraTitulo_EAnoComUmaTemporadaRegular_NaoDuplica()
    {
        var c = await MontarAsync(nameof(PreTemporada_NaoGeraTitulo_EAnoComUmaTemporadaRegular_NaoDuplica), Brt(2026, 11, 22, 10));
        Ok(await c.Sut.CloseAsync(c.GroupId, c.AdminId, new CloseSeasonDto(Brt(2026, 11, 21, 23, 59), null, true), Ct));
        await AddGameAsync(Db(c), c.GroupId, Brt(2026, 12, 5, 20), c.Players.Select(p => (p, 2, true)).ToList());

        c.Clock.Now = Brt(2027, 1, 10, 10);
        Ok(await c.Sut.CloseAsync(c.GroupId, c.AdminId, new CloseSeasonDto(null, null, false), Ct));

        var titles = await Db(c).SeasonTitles.AsNoTracking().ToListAsync();
        titles.Should().OnlyContain(t => t.Scope == SeasonTitleEntity.ScopeSeason, "2026 teve uma temporada regular só");
        titles.Select(t => t.SeasonId).Distinct().Should().ContainSingle("a pré-temporada não gera título");
        c.Push.Verify(p => p.SendToGroupAsync(c.GroupId, It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<Dictionary<string, string>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AnoComDuasTemporadasRegulares_GanhaTitulosDoAno_EConquistasMostramOsDois()
    {
        var c = await MontarAsync(nameof(AnoComDuasTemporadasRegulares_GanhaTitulosDoAno_EConquistasMostramOsDois), Brt(2026, 7, 1, 10));
        Ok(await c.Sut.CloseAsync(c.GroupId, c.AdminId, new CloseSeasonDto(Brt(2026, 7, 1), null, false), Ct));
        // 2º semestre: P4 vira artilheiro (6 gols em 3 jogos).
        for (var game = 0; game < 3; game++)
            await AddGameAsync(Db(c), c.GroupId, Brt(2026, 9, 1 + game, 20),
                c.Players.Select((p, i) => (p, i == 4 && game == 0 ? 6 : 0, false)).ToList());

        c.Clock.Now = Brt(2027, 1, 5, 10);
        Ok(await c.Sut.CloseAsync(c.GroupId, c.AdminId, new CloseSeasonDto(null, null, false), Ct));

        var titles = await Db(c).SeasonTitles.AsNoTracking().Where(t => t.Category == "Gols" && t.Position == 1).ToListAsync();
        titles.Where(t => t.Scope == SeasonTitleEntity.ScopeSeason).Select(t => t.PlayerId).Should().BeEquivalentTo([c.Players[0], c.Players[4]]);
        titles.Single(t => t.Scope == SeasonTitleEntity.ScopeYear).PlayerId.Should().Be(c.Players[4], "6 gols no ano contra 5");

        var conquistas = await new ConquistaService(Db(c), new ConquistaProjectionService(Db(c))).GetGroupConquistasAsync(c.GroupId, Ct);
        var nomes = conquistas.Players.Single(p => p.PlayerId == c.Players[4]).Titulos.Select(t => t.Nome).ToList();
        nomes.Should().Contain("Artilheiro de 2026").And.Contain("Artilheiro da Temporada 2026/2");
    }

    [Fact]
    public async Task DesfazerFechamento_TiraOsTitulosDaTemporadaReaberta()
    {
        var c = await MontarAsync(nameof(DesfazerFechamento_TiraOsTitulosDaTemporadaReaberta), Brt(2026, 11, 22, 10));
        Ok(await c.Sut.CloseAsync(c.GroupId, c.AdminId, new CloseSeasonDto(Brt(2026, 11, 21, 23, 59), null, true), Ct));
        (await Db(c).SeasonTitles.AnyAsync()).Should().BeTrue();

        Ok(await c.Sut.UndoCloseAsync(c.GroupId, Ct));

        (await Db(c).SeasonTitles.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Retrospectiva_ClassificacaoReal_ESoDepoisDeFechar()
    {
        var c = await MontarAsync(nameof(Retrospectiva_ClassificacaoReal_ESoDepoisDeFechar), Brt(2026, 11, 22, 10));
        var id = Ok(await c.Sut.ListAsync(c.GroupId, Ct)).Seasons.Single().Id;

        (await c.Sut.GetSummaryAsync(c.GroupId, id, Ct)).Error.Should().Be("A retrospectiva sai quando a temporada fecha.");

        Ok(await c.Sut.CloseAsync(c.GroupId, c.AdminId, new CloseSeasonDto(Brt(2026, 11, 21, 23, 59), null, true), Ct));
        var summary = Ok(await c.Sut.GetSummaryAsync(c.GroupId, id, Ct));

        summary.Season.DisplayName.Should().Be("2026/1");
        summary.TitlesSealed.Should().BeTrue();
        summary.Goals.Should().Be(12);
        summary.Players.Should().Be(5);

        var gols = summary.Rankings.Single(r => r.Category == "Gols");
        gols.Rows.Select(r => (r.PlayerName, r.Position, r.Value)).Should().Equal(
            ("Jogador 0", 1, 5), ("Jogador 1", 2, 3), ("Jogador 2", 2, 3), ("Jogador 3", 4, 1));

        // Aproveitamento exige 5 jogos: ninguém tem.
        summary.Rankings.Single(r => r.Category == "Aproveitamento").Rows.Should().BeEmpty();
        summary.Rankings.Single(r => r.Category == "Presenca").Rows.Should().OnlyContain(r => r.Position == 1 && r.Value == 3);
    }

    [Fact]
    public async Task Periodo_PorTemporadaEPorAno_SemPreTemporada()
    {
        var c = await MontarAsync(nameof(Periodo_PorTemporadaEPorAno_SemPreTemporada), Brt(2026, 11, 22, 10));
        Ok(await c.Sut.CloseAsync(c.GroupId, c.AdminId, new CloseSeasonDto(Brt(2026, 11, 21, 23, 59), "Pré-temporada", true), Ct));
        var seasons = Ok(await c.Sut.ListAsync(c.GroupId, Ct)).Seasons;

        var pre = Ok(await c.Sut.ResolvePeriodAsync(c.GroupId, seasons[1].Id, null, false, Ct))!;
        pre.Label.Should().Be("Pré-temporada");
        pre.Contains(Brt(2026, 12, 1).UtcDateTime).Should().BeTrue();
        pre.Contains(Brt(2026, 3, 10).UtcDateTime).Should().BeFalse();

        var ano = Ok(await c.Sut.ResolvePeriodAsync(c.GroupId, null, 2026, false, Ct))!;
        ano.Contains(Brt(2026, 3, 10).UtcDateTime).Should().BeTrue();
        ano.Contains(Brt(2026, 12, 1).UtcDateTime).Should().BeFalse("sem a pré-temporada");
        Ok(await c.Sut.ResolvePeriodAsync(c.GroupId, null, 2026, true, Ct))!.Contains(Brt(2026, 12, 1).UtcDateTime).Should().BeTrue();

        Ok(await c.Sut.ResolvePeriodAsync(c.GroupId, null, null, false, Ct)).Should().BeNull("sem filtro = tudo");

        // Conquistas: "na temporada" da pré-temporada não tem ninguém; da 2026/1, o artilheiro é P0.
        var conquistas = new ConquistaService(Db(c), new ConquistaProjectionService(Db(c)));
        (await conquistas.GetGroupConquistasAsync(c.GroupId, seasons[1].Id, Ct)).Players.Should().OnlyContain(p => p.Temporada.Count == 0);
        var primeira = await conquistas.GetGroupConquistasAsync(c.GroupId, seasons[0].Id, Ct);
        primeira.SeasonName.Should().Be("2026/1");
        primeira.Players.Single(p => p.PlayerId == c.Players[0]).Temporada.Single(s => s.Categoria == "Gols").Posicao.Should().Be(1);
    }

    private static T Ok<T>(Result<T> result)
    {
        result.Success.Should().BeTrue(result.Error);
        return result.Data!;
    }

    private static void Ok(Result result) => result.Success.Should().BeTrue(result.Error);
}
