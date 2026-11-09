using BratnavaFC.Application.Services;
using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos.Seasons;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Infrastructure.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BranavaFC.Tests;

/// <summary>Temporadas (fase 1): períodos da patota, fechar, desfazer, ajustar o corte e a virada do ano.</summary>
[Trait("Category", "Postgres")]
public sealed class TemporadasTests : IClassFixture<CampeonatoPostgresFixture>
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private readonly CampeonatoPostgresFixture _fixture;

    public TemporadasTests(CampeonatoPostgresFixture fixture) => _fixture = fixture;

    // Horários de Brasília (UTC-3) convertidos para UTC.
    private static DateTimeOffset Brt(int year, int month, int day, int hour = 0, int minute = 0) =>
        new(year, month, day, hour, minute, 0, TimeSpan.FromHours(-3));

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now.ToUniversalTime();
    }

    [Fact]
    public async Task SemMexerEmNada_TemporadaEOAno_EViraSozinhaNoAnoNovo()
    {
        await using var db = await _fixture.CreateDatabaseAsync(nameof(SemMexerEmNada_TemporadaEOAno_EViraSozinhaNoAnoNovo));
        var groupId = await SeedGroupAsync(db);
        var clock = new Clock(Brt(2026, 11, 2, 10));
        var sut = new SeasonService(db, clock);

        var list = Ok(await sut.ListAsync(groupId, Ct));
        var only = list.Seasons.Should().ContainSingle().Subject;
        only.DisplayName.Should().Be("2026");
        only.StartsAt.Should().Be(Brt(2026, 1, 1).UtcDateTime);
        only.IsOpen.Should().BeTrue();
        list.ManualMode.Should().BeFalse();

        clock.Now = Brt(2027, 1, 1, 0, 1);
        list = Ok(await sut.ListAsync(groupId, Ct));
        list.Seasons.Select(s => s.DisplayName).Should().Equal("2026", "2027");
        list.Seasons[0].EndsAt.Should().Be(Brt(2027, 1, 1).UtcDateTime);
        list.Seasons[0].ClosedByName.Should().BeNull();
        list.Seasons[1].IsCurrent.Should().BeTrue();
        list.CanUndoClose.Should().BeFalse();
    }

    [Fact]
    public async Task FecharNoEvento_AbrePreTemporadaNoMesmoAno_ESemViradaAutomatica()
    {
        await using var db = await _fixture.CreateDatabaseAsync(nameof(FecharNoEvento_AbrePreTemporadaNoMesmoAno_ESemViradaAutomatica));
        var groupId = await SeedGroupAsync(db);
        var adminId = await SeedUserAsync(db, "Luis");
        await SeedMatchAsync(db, groupId, Brt(2026, 3, 10, 20), finalized: true);
        await SeedMatchAsync(db, groupId, Brt(2026, 11, 21, 20), finalized: true);
        var clock = new Clock(Brt(2026, 11, 21, 23, 30));
        var sut = new SeasonService(db, clock);

        // Corte às 23:59 do dia do evento, agendado: o jogo das 20h fica na temporada que fecha.
        var preview = Ok(await sut.PreviewCloseAsync(groupId, Brt(2026, 11, 21, 23, 59), Ct));
        preview.ClosingDisplayName.Should().Be("2026/1");
        preview.ClosingMatches.Should().Be(2);
        preview.NextYear.Should().Be(2026);
        preview.NextNumber.Should().Be(2);
        preview.NextDefaultName.Should().Be("2026/2");
        preview.MovedMatches.Should().Be(0);

        Ok(await sut.CloseAsync(groupId, adminId, new CloseSeasonDto(Brt(2026, 11, 21, 23, 59), "Pré-temporada", true), Ct));

        var list = Ok(await sut.ListAsync(groupId, Ct));
        list.Seasons.Select(s => s.DisplayName).Should().Equal("2026/1", "Pré-temporada");
        list.Seasons[0].Matches.Should().Be(2);
        list.Seasons[0].ClosedByName.Should().Be("Luis Teste");
        list.Seasons[1].IsPreSeason.Should().BeTrue();
        list.Seasons[1].Year.Should().Be(2026, "a pré-temporada pertence ao ano em que começa");
        list.ManualMode.Should().BeTrue();
        list.CanUndoClose.Should().BeTrue();

        // Ano novo: com admin no controle, a pré-temporada continua aberta.
        clock.Now = Brt(2027, 1, 15, 9);
        list = Ok(await sut.ListAsync(groupId, Ct));
        list.Seasons.Should().HaveCount(2);
        list.Seasons[1].IsOpen.Should().BeTrue();

        // O admin abre 2027/1: pertence a 2027, número 1.
        Ok(await sut.CloseAsync(groupId, adminId, new CloseSeasonDto(null, null, false), Ct));
        list = Ok(await sut.ListAsync(groupId, Ct));
        list.Seasons.Select(s => s.DisplayName).Should().Equal("2026/1", "Pré-temporada", "2027");
        list.Seasons[2].Year.Should().Be(2027);
        list.Seasons[2].Number.Should().Be(1);
    }

    [Fact]
    public async Task DesfazerUltimoFechamento_JuntaDeVolta()
    {
        await using var db = await _fixture.CreateDatabaseAsync(nameof(DesfazerUltimoFechamento_JuntaDeVolta));
        var groupId = await SeedGroupAsync(db);
        var adminId = await SeedUserAsync(db, "Admin");
        var sut = new SeasonService(db, new Clock(Brt(2026, 11, 22, 10)));

        Ok(await sut.CloseAsync(groupId, adminId, new CloseSeasonDto(Brt(2026, 11, 21, 23, 59), null, true), Ct));
        Ok(await sut.UndoCloseAsync(groupId, Ct));

        var list = Ok(await sut.ListAsync(groupId, Ct));
        var only = list.Seasons.Should().ContainSingle().Subject;
        only.IsOpen.Should().BeTrue();
        only.ClosedAt.Should().BeNull();
        list.CanUndoClose.Should().BeFalse();
        (await sut.UndoCloseAsync(groupId, Ct)).Error.Should().Be("Nao ha fechamento feito pelo admin para desfazer.");
    }

    [Fact]
    public async Task Corte_ForaDosLimites_ERecusado()
    {
        await using var db = await _fixture.CreateDatabaseAsync(nameof(Corte_ForaDosLimites_ERecusado));
        var groupId = await SeedGroupAsync(db);
        var sut = new SeasonService(db, new Clock(Brt(2026, 11, 2, 10)));

        (await sut.CloseAsync(groupId, null, new CloseSeasonDto(Brt(2025, 12, 31), null, false), Ct))
            .Error.Should().Be("O corte tem de ser depois do inicio da temporada atual.");
        (await sut.CloseAsync(groupId, null, new CloseSeasonDto(Brt(2026, 12, 20), null, false), Ct))
            .Error.Should().Be("O corte pode ser agendado para no maximo 31 dias a frente.");
        Ok(await sut.ListAsync(groupId, Ct)).Seasons.Should().ContainSingle("nada foi gravado");
    }

    [Fact]
    public async Task AjustarCorte_MoveOsJogosEntreAsTemporadas()
    {
        await using var db = await _fixture.CreateDatabaseAsync(nameof(AjustarCorte_MoveOsJogosEntreAsTemporadas));
        var groupId = await SeedGroupAsync(db);
        var adminId = await SeedUserAsync(db, "Admin");
        await SeedMatchAsync(db, groupId, Brt(2026, 11, 21, 20), finalized: true);
        var sut = new SeasonService(db, new Clock(Brt(2026, 11, 25, 10)));

        // Fechou cedo demais (antes do jogo do evento): o jogo caiu na pré-temporada.
        Ok(await sut.CloseAsync(groupId, adminId, new CloseSeasonDto(Brt(2026, 11, 21, 18), null, true), Ct));
        var list = Ok(await sut.ListAsync(groupId, Ct));
        list.Seasons[1].Matches.Should().Be(1);

        Ok(await sut.MoveStartAsync(groupId, list.Seasons[1].Id, new MoveSeasonStartDto(Brt(2026, 11, 21, 23, 59)), Ct));
        list = Ok(await sut.ListAsync(groupId, Ct));
        list.Seasons[0].Matches.Should().Be(1);
        list.Seasons[1].Matches.Should().Be(0);
        list.Seasons[0].EndsAt.Should().Be(list.Seasons[1].StartsAt);

        (await sut.MoveStartAsync(groupId, list.Seasons[0].Id, new MoveSeasonStartDto(Brt(2026, 2, 1)), Ct))
            .Error.Should().Be("A primeira temporada comeca no inicio do ano e nao muda.");
        (await sut.MoveStartAsync(groupId, list.Seasons[1].Id, new MoveSeasonStartDto(Brt(2025, 12, 1)), Ct))
            .Error.Should().Be("O corte tem de ser depois do inicio da temporada anterior.");
    }

    [Fact]
    public async Task RenomearEMarcarPreTemporada_EVoltarAoNomePadrao()
    {
        await using var db = await _fixture.CreateDatabaseAsync(nameof(RenomearEMarcarPreTemporada_EVoltarAoNomePadrao));
        var groupId = await SeedGroupAsync(db);
        var sut = new SeasonService(db, new Clock(Brt(2026, 11, 2, 10)));
        var id = Ok(await sut.ListAsync(groupId, Ct)).Seasons.Single().Id;

        Ok(await sut.UpdateAsync(groupId, id, new UpdateSeasonDto("  Temporada do Boca  ", true), Ct));
        var season = Ok(await sut.ListAsync(groupId, Ct)).Seasons.Single();
        season.DisplayName.Should().Be("Temporada do Boca");
        season.IsPreSeason.Should().BeTrue();

        Ok(await sut.UpdateAsync(groupId, id, new UpdateSeasonDto("", false), Ct));
        Ok(await sut.ListAsync(groupId, Ct)).Seasons.Single().DisplayName.Should().Be("2026");

        (await sut.UpdateAsync(groupId, id, new UpdateSeasonDto(new string('x', 41), false), Ct))
            .Error.Should().Be("O nome da temporada pode ter no maximo 40 caracteres.");
    }

    [Fact]
    public async Task JogoAntesDaPrimeiraTemporada_CriaOsAnosQueFaltam()
    {
        await using var db = await _fixture.CreateDatabaseAsync(nameof(JogoAntesDaPrimeiraTemporada_CriaOsAnosQueFaltam));
        var groupId = await SeedGroupAsync(db);
        var sut = new SeasonService(db, new Clock(Brt(2026, 11, 2, 10)));
        Ok(await sut.ListAsync(groupId, Ct)).Seasons.Should().ContainSingle();

        await SeedMatchAsync(db, groupId, Brt(2024, 6, 1, 20), finalized: true);

        var list = Ok(await sut.ListAsync(groupId, Ct));
        list.Seasons.Select(s => s.DisplayName).Should().Equal("2024", "2025", "2026");
        list.Seasons[0].Matches.Should().Be(1);
        list.Seasons[1].EndsAt.Should().Be(list.Seasons[2].StartsAt);
        list.ManualMode.Should().BeFalse();
    }

    [Fact]
    public async Task Migracao_CriaUmaTemporadaPorAnoDesdeOPrimeiroJogo()
    {
        await using var db = await _fixture.CreateDatabaseAsync(nameof(Migracao_CriaUmaTemporadaPorAnoDesdeOPrimeiroJogo));
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261029070350_AddChampionshipBets");

        var comJogos = await SeedGroupAsync(db);
        var semJogos = await SeedGroupAsync(db);
        // 31/12/2024 23:30 em Brasília já é 2025 em UTC: o ano conta pelo horário de Brasília.
        await SeedMatchAsync(db, comJogos, Brt(2024, 12, 31, 23, 30), finalized: true);

        await migrator.MigrateAsync();

        var seasons = await db.Seasons.AsNoTracking().OrderBy(s => s.StartsAt).ToListAsync();
        var anoAtual = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(-3)).Year;

        var doGrupo = seasons.Where(s => s.GroupId == comJogos).ToList();
        doGrupo.Select(s => s.Year).Should().Equal(Enumerable.Range(2024, anoAtual - 2024 + 1));
        doGrupo.Should().OnlyContain(s => s.Number == 1 && !s.IsPreSeason && s.ClosedByUserId == null);
        doGrupo[0].StartsAt.Should().Be(Brt(2024, 1, 1).UtcDateTime);
        doGrupo.Last().EndsAt.Should().BeNull();
        doGrupo.Zip(doGrupo.Skip(1)).Should().OnlyContain(pair => pair.First.EndsAt == pair.Second.StartsAt);

        seasons.Where(s => s.GroupId == semJogos).Should().ContainSingle()
            .Which.Year.Should().Be(anoAtual);
    }

    // ── Auxiliares ───────────────────────────────────────────────────────────

    private static async Task<Guid> SeedGroupAsync(AppDbContext db)
    {
        var group = new GroupEntity("Bratnava FC", null, Guid.NewGuid());
        db.Groups.Add(group);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return group.Id;
    }

    private static async Task<Guid> SeedUserAsync(AppDbContext db, string firstName)
    {
        var user = new UserEntity(firstName.ToLowerInvariant(), firstName, "Teste", $"{firstName.ToLowerInvariant()}@t.com", "hash", null, null);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return user.Id;
    }

    private static async Task SeedMatchAsync(AppDbContext db, Guid groupId, DateTimeOffset playedAt, bool finalized)
    {
        var match = new MatchEntity(groupId, playedAt.UtcDateTime, "Arena");
        db.Matches.Add(match);
        await db.SaveChangesAsync();
        if (finalized)
            await db.Database.ExecuteSqlInterpolatedAsync($@"UPDATE ""Matches"" SET ""Status"" = 6 WHERE ""Id"" = {match.Id}");
        db.ChangeTracker.Clear();
    }

    private static T Ok<T>(Result<T> result)
    {
        result.Success.Should().BeTrue(result.Error);
        return result.Data!;
    }

    private static void Ok(Result result) => result.Success.Should().BeTrue(result.Error);
}
