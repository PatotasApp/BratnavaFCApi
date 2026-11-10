using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Security.Claims;
using System.Threading;
using BratnavaFC.Api.Controllers;
using BratnavaFC.Application.Abstractions;
using BratnavaFC.Application.Services;
using BratnavaFC.Application.Statistics;
using BratnavaFC.Domain.Common;
using BratnavaFC.Domain.Dtos.Retrospective;
using BratnavaFC.Domain.Entities;
using BratnavaFC.Domain.Enums;
using BratnavaFC.Infrastructure.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Moq;
using Xunit;

namespace BranavaFC.Tests;

/// <summary>Regras da retrospectiva individual (docs/retrospectiva/RETROSPECTIVA.md).</summary>
public sealed class PlayerRetrospectiveCalculatorTests
{
    private static readonly Guid Group = Guid.NewGuid();
    private static readonly Guid Ana = Guid.NewGuid(), Beto = Guid.NewGuid(), Caio = Guid.NewGuid(), Davi = Guid.NewGuid(), Edu = Guid.NewGuid();

    private static readonly Dictionary<Guid, string> Names = new()
    {
        [Ana] = "Ana", [Beto] = "Beto", [Caio] = "Caio", [Davi] = "Davi", [Edu] = "Edu",
    };

    private static DateTime Day(int month, int day) => new(2026, month, day, 23, 0, 0, DateTimeKind.Utc);

    // team = time que ganhou o gol (1 ou 2) → lado 0 ou 1 da unidade.
    private static StatGoal Goal(Guid scorer, short team, Guid? assist = null) => new(scorer, assist, false, team - 1);
    private static StatGoal Own(Guid scorer, short forTeam, Guid? assist = null) => new(scorer, assist, true, forTeam - 1);

    private static StatMatch Game(
        DateTime at, Guid[] team1, Guid[] team2, int? score1, int? score2,
        StatGoal[]? goals = null, Guid[]? mvps = null, Guid[]? votes = null, Guid? goalkeeper = null, bool headToHead = true)
    {
        var hasScore = score1.HasValue && score2.HasValue;
        StatResult Result(int team) => !hasScore ? StatResult.None
            : score1 == score2 ? StatResult.Draw
            : (team == 1) == (score1 > score2) ? StatResult.Win : StatResult.Loss;
        StatSide Side(int team, Guid[] ids) => new(
            ids.Select(id => new StatPlayer(id, id == goalkeeper, mvps?.Contains(id) == true, Result(team))).ToList(),
            Result(team),
            hasScore ? (team == 1 ? score2 : score1) : null);

        return new StatMatch(Guid.NewGuid(), Group, at, [Side(1, team1), Side(2, team2)],
            goals ?? [], votes ?? [], hasScore, headToHead, null);
    }

    private static PlayerRetrospectiveDto Build(Guid player, params StatMatch[] units) =>
        PlayerRetrospectiveCalculator.Build(player, units, Names, d => d.AddHours(-3));

    [Fact]
    public void Presenca_ContaJogosPercentualPosicaoComEmpateESequencia()
    {
        var units = new[]
        {
            Game(Day(3, 1), [Ana, Beto], [Caio], 1, 0),
            Game(Day(3, 8), [Ana], [Beto, Caio], 1, 0),
            Game(Day(3, 15), [Beto], [Caio], 1, 0),
            Game(Day(3, 22), [Ana], [Caio], 1, 0),
            Game(Day(3, 29), [Ana], [Beto], 1, 0),
        };

        var ana = Build(Ana, units);
        ana.TotalGames.Should().Be(5);
        ana.Presence.Games.Should().Be(4);
        ana.Presence.Percent.Should().Be(80);
        ana.Presence.LongestStreak.Should().Be(2);
        ana.Presence.Rank.Should().Be(1);

        // Beto e Caio também têm 4 jogos: todos empatam em 1º.
        Build(Caio, units).Presence.Rank.Should().Be(1);
        Build(Davi, units).Presence.Games.Should().Be(0);
        Build(Davi, units).Presence.Rank.Should().BeNull();
    }

    [Fact]
    public void Resultados_AproveitamentoSequenciasEMelhorMesComMinimoDeTres()
    {
        var units = new[]
        {
            // março: V V E → 78%
            Game(Day(3, 1), [Ana], [Beto], 2, 0),
            Game(Day(3, 8), [Ana], [Beto], 1, 0),
            Game(Day(3, 15), [Ana], [Beto], 1, 1),
            // abril: D V (sem placar) V → 67%
            Game(Day(4, 5), [Ana], [Beto], 0, 1),
            Game(Day(4, 12), [Ana], [Beto], 3, 0),
            // sem placar: só presença, não quebra a sequência
            Game(Day(4, 19), [Ana], [Beto], null, null),
            Game(Day(4, 26), [Ana], [Beto], 2, 1),
        };

        var r = Build(Ana, units).Results;
        (r.Wins, r.Draws, r.Losses).Should().Be((4, 1, 1));
        r.Aproveitamento.Should().Be(72); // 13 de 18
        r.LongestWinStreak.Should().Be(2);
        r.LongestUnbeatenStreak.Should().Be(3);
        r.BestMonth.Should().NotBeNull();
        r.BestMonth!.Month.Should().Be(3);
        (r.BestMonth.Wins, r.BestMonth.Draws, r.BestMonth.Losses, r.BestMonth.Aproveitamento).Should().Be((2, 1, 0, 78));
        r.Rank.Should().Be(1);
        Build(Beto, units).Results.Rank.Should().Be(2);
    }

    [Fact]
    public void Resultados_SemMinimoDeJogos_FicaSemPosicao()
    {
        var units = Enumerable.Range(1, 10)
            .Select(i => Game(Day(5, i), [Ana], [Beto], 1, 0))
            .Append(Game(Day(5, 20), [Caio], [Beto], 1, 0))
            .ToArray();

        var caio = Build(Caio, units);
        caio.MinGamesForRates.Should().Be(3);
        caio.Results.Aproveitamento.Should().Be(100);
        caio.Results.Rank.Should().BeNull();
        Build(Ana, units).Results.Rank.Should().Be(1);
    }

    [Fact]
    public void Gols_SoRegistradosComPosicaoMesesDaPatotaEMelhorNoite()
    {
        var units = new[]
        {
            Game(Day(2, 10), [Ana, Beto], [Caio], 3, 1, [Goal(Ana, 1, Beto), Goal(Ana, 1), Goal(Caio, 2), Goal(Beto, 1, Ana)]),
            Game(Day(3, 10), [Ana], [Caio], 4, 0), // placar sem gol registrado: março fica fora
            Game(Day(5, 10), [Ana, Beto], [Caio], 1, 1, [Goal(Beto, 1, Ana), Own(Ana, 2, Beto)]),
        };

        var ana = Build(Ana, units);
        ana.Goals.Total.Should().Be(2);
        ana.Goals.Rank.Should().Be(1);
        ana.Goals.BestNight!.Count.Should().Be(2);
        ana.Goals.BestNight.PlayedAt.Should().Be(Day(2, 10));
        ana.OwnGoals.Should().Be(1);
        ana.Months.Select(m => (m.Month, m.Goals, m.Assists)).Should().Equal((2, 2, 1), (5, 0, 1));

        // Assistência em gol contra não conta.
        var beto = Build(Beto, units);
        beto.Assists.Total.Should().Be(1);
        beto.Assists.MostAssistedTo!.Name.Should().Be("Ana");
        ana.Assists.MostAssistedTo!.Name.Should().Be("Beto");
        ana.Assists.MostAssistedTo.Count.Should().Be(2);
        ana.Assists.MostAssistedBy!.Name.Should().Be("Beto");
        Build(Caio, units).Goals.Rank.Should().Be(3); // Ana e Beto empatam em 1º
        Build(Davi, units).Goals.Rank.Should().BeNull();
    }

    [Fact]
    public void Decisivos_AbriuOPlacarEGolDaVitoria()
    {
        // 3x1: o 2º gol do vencedor deixa um à frente do placar final do perdedor.
        var units = new[]
        {
            Game(Day(6, 1), [Ana, Beto], [Caio], 3, 1, [Goal(Ana, 1), Goal(Caio, 2), Goal(Beto, 1), Goal(Ana, 1)]),
            // Gol contra abrindo o placar não credita ninguém; 1x2: o 2º gol do time 2 é o da vitória (Caio).
            Game(Day(6, 8), [Ana], [Caio, Beto], 1, 2, [Own(Ana, 2), Goal(Ana, 1), Goal(Caio, 2)]),
            // Registro incompleto (placar 2x0, um gol registrado): não dá para saber a ordem.
            Game(Day(6, 15), [Ana], [Caio], 2, 0, [Goal(Ana, 1)]),
        };

        var ana = Build(Ana, units);
        ana.Decisive.Openers.Should().Be(1);
        ana.Decisive.WinningGoals.Should().Be(0);
        var beto = Build(Beto, units);
        beto.Decisive.WinningGoals.Should().Be(1);
        var caio = Build(Caio, units);
        caio.Decisive.Openers.Should().Be(0);
        caio.Decisive.WinningGoals.Should().Be(1);
    }

    [Fact]
    public void Decisivos_ConsolidadoNaoEConfrontoDireto()
    {
        var unit = Game(Day(6, 1), [Ana], [Caio], 1, 0, [Goal(Ana, 1)], headToHead: false);
        PlayerRetrospectiveCalculator.DecisiveGoals(unit).Should().Be(((Guid?)null, (Guid?)null));
    }

    [Fact]
    public void Parcerias_DuplaDeOuroComMinimoDeQuatroEQuemMaisJogouDoLado()
    {
        var units = new List<StatMatch>();
        // Beto: 5 jogos juntos, 2V 3D. Caio: 4 jogos juntos, 4V. Davi: 3 jogos juntos, 3V (não chega no mínimo).
        for (var i = 0; i < 5; i++) units.Add(Game(Day(7, 1 + i), [Ana, Beto], [Edu], i < 2 ? 1 : 0, i < 2 ? 0 : 1));
        for (var i = 0; i < 4; i++) units.Add(Game(Day(8, 1 + i), [Ana, Caio], [Edu], 1, 0));
        for (var i = 0; i < 3; i++) units.Add(Game(Day(9, 1 + i), [Ana, Davi], [Edu], 1, 0));

        var ana = Build(Ana, units.ToArray());
        ana.BestPartner!.Name.Should().Be("Caio");
        ana.BestPartner.Games.Should().Be(4);
        ana.BestPartner.Aproveitamento.Should().Be(100);
        ana.FrequentPartner!.Name.Should().Be("Beto");
        ana.FrequentPartner.Games.Should().Be(5);
    }

    [Fact]
    public void Rivalidades_FreguesPositivoECarrascoSoComRetrospectoNegativo()
    {
        var units = new List<StatMatch>();
        // Contra Beto: 3V 1E. Contra Caio: 1V 3D. Contra Davi: 2V 2D (empatado: nem freguês nem carrasco).
        units.AddRange(new[] { 1, 1, 1, 0 }.Select((w, i) => Game(Day(7, 1 + i), [Ana], [Beto], w == 1 ? 1 : 0, 0)));
        units.AddRange(new[] { 1, 0, 0, 0 }.Select((w, i) => Game(Day(8, 1 + i), [Ana], [Caio], w, 1 - w)));
        units.AddRange(new[] { 1, 1, 0, 0 }.Select((w, i) => Game(Day(9, 1 + i), [Ana], [Davi], w, 1 - w)));
        // Edu: só 3 confrontos, fica de fora.
        units.AddRange(Enumerable.Range(0, 3).Select(i => Game(Day(10, 1 + i), [Ana], [Edu], 0, 1)));

        var ana = Build(Ana, units.ToArray());
        ana.Fregues!.Name.Should().Be("Beto");
        (ana.Fregues.Wins, ana.Fregues.Draws, ana.Fregues.Losses).Should().Be((3, 1, 0));
        ana.Carrasco!.Name.Should().Be("Caio");
        (ana.Carrasco.Wins, ana.Carrasco.Losses).Should().Be((1, 3));

        var beto = Build(Beto, units.ToArray());
        beto.Fregues.Should().BeNull();
        beto.Carrasco!.Name.Should().Be("Ana");
    }

    [Fact]
    public void Marcos_PrimeiroUltimoECentenariosDaPatotaNoPeriodo()
    {
        var units = new List<StatMatch>
        {
            Game(Day(2, 1), [Ana, Beto], [Caio], 1, 0, [Goal(Ana, 1, Beto)]),
        };
        // Gol contra também ocupa posição na linha do tempo, como no Spotlight.
        // Com 97 gols do Caio e um gol contra, o 100º é do Beto e o último (101º), da Ana.
        for (var i = 0; i < 97; i++)
            units.Add(Game(Day(3, 1).AddDays(i), [Caio], [Davi], 1, 0, [Goal(Caio, 1)]));
        units.Add(Game(Day(8, 31), [Davi], [Caio], 0, 1, [Own(Davi, 2)]));
        units.Add(Game(Day(9, 1), [Beto, Ana], [Caio], 2, 0, [Goal(Beto, 1, Ana), Goal(Ana, 1)]));

        var ana = Build(Ana, units.ToArray());
        ana.Milestones.Select(m => (m.Kind, m.Number)).Should().Equal(("first_goal", 1), ("last_goal", 101));
        var beto = Build(Beto, units.ToArray());
        beto.Milestones.Select(m => (m.Kind, m.Number)).Should().Equal(("first_assist", 1), ("goal_century", 100));
        Build(Caio, units.ToArray()).Milestones.Should().BeEmpty();
    }

    [Fact]
    public void Mvp_ContaMvpsVotosEPosicao()
    {
        var units = new[]
        {
            Game(Day(4, 1), [Ana], [Beto], 1, 0, mvps: [Ana], votes: [Ana, Ana, Beto]),
            Game(Day(4, 8), [Ana], [Beto], 1, 0, mvps: [Beto], votes: [Beto]),
            Game(Day(4, 15), [Ana], [Beto], 1, 0, mvps: [Ana], votes: [Ana]),
        };

        var ana = Build(Ana, units);
        (ana.Mvp.Mvps, ana.Mvp.Votes, ana.Mvp.Rank).Should().Be((2, 3, 1));
        Build(Beto, units).Mvp.Rank.Should().Be(2);
        Build(Caio, units).Mvp.Rank.Should().BeNull();
    }

    [Fact]
    public void Perfil_GoleiroEParedaoESenaoODadoMaisBemColocado()
    {
        var units = Enumerable.Range(0, 6)
            .Select(i => Game(Day(5, 1 + i), [Ana, Beto], [Caio, Davi], 1, 0, [Goal(Beto, 1, Ana)], goalkeeper: Caio))
            .ToArray();

        Build(Caio, units).Profile.Key.Should().Be("paredao");
        Build(Caio, units).IsGoalkeeper.Should().BeTrue();

        // Beto é 1º em gols por jogo; Ana 1º em assistências por jogo. Os dois empatam em presença,
        // aproveitamento e decisivos, mas o desempate de perfil começa por "fiel".
        Build(Beto, units).Profile.Key.Should().Be("fiel");

        // Davi sem gol: presença 1º (empatado) → O Fiel.
        Build(Davi, units).Profile.Key.Should().Be("fiel");
    }

    [Fact]
    public void Perfil_MatadorQuandoGolsPorJogoEstaAcimaDaPresenca()
    {
        var units = new List<StatMatch>();
        for (var i = 0; i < 6; i++)
            units.Add(Game(Day(5, 1 + i), [Ana, Beto, Edu], [Caio, Davi], 0, 1, i < 3 ? [Goal(Caio, 2)] : [Goal(Davi, 2)]));
        units.Add(Game(Day(6, 1), [Ana, Beto], [Edu], 1, 0, [Goal(Beto, 1)]));
        units.Add(Game(Day(6, 2), [Ana, Beto], [Edu], 1, 0, [Goal(Beto, 1)]));
        // Caio faltou 2 → 4º em presença, mas 1º em gols por jogo (3/6, empatado com o Davi).
        // Também é 1º em aproveitamento e decisivos: no empate, Matador vem antes.
        var caio = Build(Caio, units.ToArray());
        caio.Presence.Rank.Should().Be(4);
        caio.Profile.GoalsPerGameRank.Should().Be(1);
        caio.Profile.Key.Should().Be("matador");
    }

    [Fact]
    public void NomesCurtos_PrimeiroNomeSalvoDuplicidade()
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid(); var c = Guid.NewGuid();
        var names = ShortNames.Build([(a, "Pedro Roweder"), (b, "pedro Silva"), (c, "Marlon Souza")]);
        names[a].Should().Be("Pedro Roweder");
        names[b].Should().Be("pedro Silva");
        names[c].Should().Be("Marlon");
    }
}

/// <summary>Leitura da retrospectiva pelo banco: período, ordem dos gols e lista do admin.</summary>
public sealed class PlayerRetrospectiveServiceTests
{
    private static async Task<(GroupEntity Group, PlayerEntity[] Players)> SeedAsync(AppDbContext db)
    {
        var group = new GroupEntity("Patota Teste", null, Guid.NewGuid());
        db.Groups.Add(group);
        var players = new[] { "Ana Lima", "Beto", "Caio", "Ana Souza" }
            .Select(n => new PlayerEntity(n, null, group.Id, 0m, false))
            .ToArray();
        db.Players.AddRange(players);
        await db.SaveChangesAsync();
        return (group, players);
    }

    private static async Task<MatchEntity> SeedMatchAsync(
        AppDbContext db, Guid groupId, DateTime playedAt, PlayerEntity[] teamA, PlayerEntity[] teamB,
        Action<MatchEntity, Dictionary<Guid, MatchPlayerEntity>> goals)
    {
        var match = new MatchEntity(groupId, playedAt, "Arena");
        foreach (var p in teamA.Concat(teamB)) match.AddPlayer(new MatchPlayerEntity(p.Id), p);
        db.Matches.Add(match);
        await db.SaveChangesAsync();

        match.OpenAcceptation();
        foreach (var p in teamA.Concat(teamB)) match.AcceptInvite(p.Id);
        match.GoToMatchMaking();
        match.AssignTeams(teamA.Select(p => p.Id).ToArray(), teamB.Select(p => p.Id).ToArray());
        match.Start();
        match.End();
        match.GoToPostGame();
        goals(match, match.Players.ToDictionary(mp => mp.PlayerId));
        match.FinalizeByVotes();
        await db.SaveChangesAsync();
        return match;
    }

    [Fact]
    public async Task Ano_FiltraPeloHorarioDeBrasiliaEOrdenaOsGolsPeloHorario()
    {
        await using var db = DbContextFactory.Create(nameof(Ano_FiltraPeloHorarioDeBrasiliaEOrdenaOsGolsPeloHorario));
        var (group, p) = await SeedAsync(db);
        var (ana, beto, caio) = (p[0], p[1], p[2]);

        // 01/01/2026 01:00 UTC = 31/12/2025 22:00 em Brasília: é de 2025.
        await SeedMatchAsync(db, group.Id, new DateTime(2026, 1, 1, 1, 0, 0, DateTimeKind.Utc), [ana], [beto],
            (m, mp) => m.AddGoalByMatchPlayer(mp[ana.Id].Id, null, 1200));

        // Gols cadastrados fora de ordem: o do Caio (20:05) veio antes do da Ana (21:10) no relógio.
        await SeedMatchAsync(db, group.Id, new DateTime(2026, 3, 10, 23, 0, 0, DateTimeKind.Utc), [ana, beto], [caio],
            (m, mp) =>
            {
                m.AddGoalByMatchPlayer(mp[ana.Id].Id, mp[beto.Id].Id, 21 * 60 + 10);
                m.AddGoalByMatchPlayer(mp[caio.Id].Id, null, 20 * 60 + 5);
                m.AddGoalByMatchPlayer(mp[beto.Id].Id, null, 21 * 60 + 30);
            });

        var sut = new PlayerRetrospectiveService(db, () => new DateTime(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc));
        var result = await sut.GetAsync(group.Id, ana.Id, null, null, null, withPlayerList: true);

        result.Status.Should().Be(ResultStatus.Ok);
        var dto = result.Data!;
        dto.Period.Year.Should().Be(2026);
        dto.Period.IsPartial.Should().BeTrue();
        dto.AvailableYears.Should().Equal(2026, 2025);
        dto.TotalGames.Should().Be(1);
        dto.Goals.Total.Should().Be(1);
        dto.Player.ShortName.Should().Be("Ana Lima");
        // Ordem pelo relógio: Caio 20:05, Ana 21:10, Beto 21:30. No 2x1, o 2º gol do vencedor é o da vitória.
        dto.Decisive.Openers.Should().Be(0);
        dto.Decisive.WinningGoals.Should().Be(0);
        (await sut.GetAsync(group.Id, beto.Id, null, null, null, false)).Data!.Decisive.WinningGoals.Should().Be(1);
        dto.Players!.Select(x => x.ShortName).Should().Equal("Ana Lima", "Beto", "Caio");

        var caioDto = (await sut.GetAsync(group.Id, caio.Id, null, null, null, false)).Data!;
        caioDto.Decisive.Openers.Should().Be(1);
        caioDto.Players.Should().BeNull();

        var y2025 = (await sut.GetAsync(group.Id, ana.Id, 2025, null, null, false)).Data!;
        y2025.TotalGames.Should().Be(1);
        y2025.Goals.Total.Should().Be(1);
        y2025.Period.IsPartial.Should().BeFalse();
    }

    [Fact]
    public async Task Datas_PeriodoInclusivoEValidacoes()
    {
        await using var db = DbContextFactory.Create(nameof(Datas_PeriodoInclusivoEValidacoes));
        var (group, p) = await SeedAsync(db);
        await SeedMatchAsync(db, group.Id, new DateTime(2026, 4, 30, 23, 30, 0, DateTimeKind.Utc), [p[0]], [p[1]], (m, _) => m.SetScore(1, 0));

        var sut = new PlayerRetrospectiveService(db);
        var dto = (await sut.GetAsync(group.Id, p[0].Id, null, new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 30), false)).Data!;
        dto.TotalGames.Should().Be(1);
        dto.Period.Label.Should().Be("01/04/2026 a 30/04/2026");

        (await sut.GetAsync(group.Id, p[0].Id, null, new DateOnly(2026, 4, 1), null, false)).Status.Should().Be(ResultStatus.BadRequest);
        (await sut.GetAsync(group.Id, Guid.NewGuid(), null, null, null, false)).Status.Should().Be(ResultStatus.NotFound);
    }
}

/// <summary>A retrospectiva é só do admin da patota (e Admin/GodMode).</summary>
public sealed class RetrospectiveEndpointTests
{
    private static (RetrospectiveController Sut, Mock<IPlayerRetrospectiveService> Service) CreateSut(
        AppDbContext db, Guid userId, string role)
    {
        var service = new Mock<IPlayerRetrospectiveService>();
        service.Setup(s => s.GetAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<int?>(), It.IsAny<DateOnly?>(),
                It.IsAny<DateOnly?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<PlayerRetrospectiveDto>.Ok(new PlayerRetrospectiveDto()));
        var sut = new RetrospectiveController(service.Object, db)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, userId.ToString()), new Claim(ClaimTypes.Role, role)], "Tests")),
                },
            },
        };
        return (sut, service);
    }

    private static void VerifyNotCalled(Mock<IPlayerRetrospectiveService> service) =>
        service.Verify(s => s.GetAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<int?>(), It.IsAny<DateOnly?>(),
            It.IsAny<DateOnly?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);

    [Fact]
    public async Task JogadorQueNaoEAdmin_Recebe403MesmoParaAPropria()
    {
        await using var db = DbContextFactory.Create(nameof(JogadorQueNaoEAdmin_Recebe403MesmoParaAPropria));
        var userId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var me = new PlayerEntity("Membro", userId, groupId, 3m, false);
        db.Players.Add(me);
        await db.SaveChangesAsync();
        var (sut, service) = CreateSut(db, userId, "User");

        (await sut.Get(groupId, null, null, null, null, CancellationToken.None)).Should().BeOfType<ForbidResult>();
        (await sut.Get(groupId, me.Id, null, null, null, CancellationToken.None)).Should().BeOfType<ForbidResult>();
        VerifyNotCalled(service);
    }

    [Fact]
    public async Task AdminDeOutraPatota_Recebe403()
    {
        await using var db = DbContextFactory.Create(nameof(AdminDeOutraPatota_Recebe403));
        var userId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        db.Players.Add(new PlayerEntity("Membro", userId, groupId, 3m, false));
        db.GroupAdmins.Add(new GroupAdminEntity { UserId = userId, GroupId = Guid.NewGuid() });
        await db.SaveChangesAsync();
        var (sut, service) = CreateSut(db, userId, "User");

        (await sut.Get(groupId, null, null, null, null, CancellationToken.None)).Should().BeOfType<ForbidResult>();
        VerifyNotCalled(service);
    }

    [Fact]
    public async Task AdminDaPatota_VeQualquerJogadorComALista()
    {
        await using var db = DbContextFactory.Create(nameof(AdminDaPatota_VeQualquerJogadorComALista));
        var userId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var mine = new PlayerEntity("Admin", userId, groupId, 3m, false);
        var other = new PlayerEntity("Outro", null, groupId, 3m, false);
        db.Players.AddRange(mine, other);
        db.GroupAdmins.Add(new GroupAdminEntity { UserId = userId, GroupId = groupId });
        await db.SaveChangesAsync();
        var (sut, service) = CreateSut(db, userId, "User");

        var own = await sut.Get(groupId, null, 2026, null, null, CancellationToken.None);
        ((IStatusCodeActionResult)own).StatusCode.Should().Be(200);
        service.Verify(s => s.GetAsync(groupId, mine.Id, 2026, null, null, true, It.IsAny<CancellationToken>()), Times.Once);

        var picked = await sut.Get(groupId, other.Id, null, null, null, CancellationToken.None);
        ((IStatusCodeActionResult)picked).StatusCode.Should().Be(200);
        service.Verify(s => s.GetAsync(groupId, other.Id, null, null, null, true, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GodModeSemJogadorNaPatota_ComecaPeloPrimeiroJogador()
    {
        await using var db = DbContextFactory.Create(nameof(GodModeSemJogadorNaPatota_ComecaPeloPrimeiroJogador));
        var groupId = Guid.NewGuid();
        var beto = new PlayerEntity("Beto", null, groupId, 3m, false);
        var ana = new PlayerEntity("Ana", null, groupId, 3m, false);
        db.Players.AddRange(beto, ana);
        await db.SaveChangesAsync();
        var (sut, service) = CreateSut(db, Guid.NewGuid(), "GodMode");

        var result = await sut.Get(groupId, null, null, null, null, CancellationToken.None);
        ((IStatusCodeActionResult)result).StatusCode.Should().Be(200);
        service.Verify(s => s.GetAsync(groupId, ana.Id, null, null, null, true, It.IsAny<CancellationToken>()), Times.Once);
    }
}
