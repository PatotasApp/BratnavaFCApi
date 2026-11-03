using BratnavaFC.Application.Statistics;
using BratnavaFC.Domain.Models;
using FluentAssertions;

namespace BranavaFC.Tests;

/// <summary>Cálculos do Spotlight do telão sobre unidades montadas à mão (sem banco).</summary>
public sealed class SpotlightCalculatorTests
{
    private static readonly Guid Group = Guid.NewGuid();

    // ── Montagem ─────────────────────────────────────────────────────────────

    private static StatGoal Gol(Guid scorer, int side, Guid? assist = null, bool own = false, int? time = null)
        => new(scorer, assist, own, side, time);

    /// <summary>Partida de dois lados; o placar sai dos gols, a não ser que venha um explícito.</summary>
    private static SpotlightUnit Game(int day, Guid[] a, Guid[] b, StatGoal[]? goals = null, (int A, int B)? score = null,
        Guid? mvp = null, bool singleGame = true)
    {
        goals ??= [];
        var (sa, sb) = score ?? (goals.Count(g => g.Side == 0), goals.Count(g => g.Side == 1));
        static StatResult R(int me, int other) => me > other ? StatResult.Win : me == other ? StatResult.Draw : StatResult.Loss;

        var sides = new List<StatSide>
        {
            new(a.Select(id => new StatPlayer(id, false, id == mvp, R(sa, sb))).ToList(), R(sa, sb), sb),
            new(b.Select(id => new StatPlayer(id, false, id == mvp, R(sb, sa))).ToList(), R(sb, sa), sa),
        };
        var stat = new StatMatch(Guid.NewGuid(), Group, new DateTime(2026, 3, day, 23, 0, 0, DateTimeKind.Utc), sides, goals,
            mvp is null ? [] : [mvp.Value, mvp.Value], HasScore: true, IsHeadToHead: singleGame, ChampionshipId: null);
        return new SpotlightUnit(stat, singleGame);
    }

    private sealed class Patota
    {
        public Dictionary<Guid, SpotlightPlayerInfo> Players { get; } = new();

        public Guid Add(string name, bool guest = false)
        {
            var id = Guid.NewGuid();
            Players[id] = new SpotlightPlayerInfo(id, name, name + " Completo", guest, false);
            return id;
        }

        public Guid[] Many(int count) => Enumerable.Range(1, count).Select(i => Add($"J{i:00}")).ToArray();

        public SpotlightEventReport Build(IReadOnlyList<SpotlightUnit> units, int? minGames = null, bool onlyMonthly = false, int replays = 0)
        {
            var tracked = Players.Values.Where(p => !onlyMonthly || !p.IsGuest).Select(p => p.Id).ToHashSet();
            return SpotlightCalculator.Build(units, Players, tracked, replays, minGames);
        }
    }

    // ── Números do ano ───────────────────────────────────────────────────────

    [Fact]
    public void Numeros_ContamSoGolsRegistrados_EParticipacoesSemGolContra()
    {
        var p = new Patota();
        var (a1, a2, b1, b2) = (p.Add("Ana"), p.Add("Bia"), p.Add("Caio"), p.Add("Duda"));

        var units = new[]
        {
            // 3x1: gol com assistência, gol sem, gol contra (com "assistência", que não conta) e gol do outro lado.
            Game(1, [a1, a2], [b1, b2], [Gol(a1, 0, a2), Gol(a1, 0), Gol(b1, 0, b2, own: true), Gol(b2, 1, b1)]),
            // Empate no placar sem nenhum gol registrado: conta como jogo, não entra na média.
            Game(2, [a1, b1], [a2, b2], score: (2, 2)),
        };

        var n = p.Build(units, replays: 7).Numbers;

        n.Games.Should().Be(2);
        n.Goals.Should().Be(4, "gols registrados, inclusive o contra");
        n.GamesWithGoals.Should().Be(1);
        n.GoalsPerGame.Should().Be(4.0, "a média ignora a partida sem gols registrados");
        n.Assists.Should().Be(2, "assistência em gol contra nunca conta");
        n.Participations.Should().Be(5, "3 gols (sem o contra) + 2 assistências");
        n.OwnGoals.Should().Be(1);
        n.Draws.Should().Be(1);
        n.Players.Should().Be(4);
        n.Replays.Should().Be(7);
        n.MostGoalsGame!.GoalsA.Should().Be(3);
        n.MostGoalsGame.GoalsB.Should().Be(1);
        n.BiggestWin!.GoalsA.Should().Be(3);
    }

    [Fact]
    public void JogoComMaisGols_PelosRegistrados_EGoleada_PeloPlacar()
    {
        var p = new Patota();
        var (a, b) = (p.Add("Ana"), p.Add("Bia"));

        var units = new[]
        {
            Game(1, [a], [b], [Gol(a, 0), Gol(b, 1), Gol(a, 0), Gol(b, 1)], score: (2, 2)),
            Game(2, [a], [b], [Gol(a, 0)], score: (9, 1)),
        };

        var n = p.Build(units).Numbers;

        (n.MostGoalsGame!.GoalsA, n.MostGoalsGame.GoalsB).Should().Be((2, 2));
        (n.BiggestWin!.GoalsA, n.BiggestWin.GoalsB).Should().Be((9, 1));
    }

    // ── Rankings ─────────────────────────────────────────────────────────────

    [Fact]
    public void Ranking_EmpateTipoCompeticao_EntraTodoMundoAte5oLugar()
    {
        var p = new Patota();
        var ids = p.Many(9);
        var goals = new List<StatGoal>();
        // Gols: 3, 3, 2, 1, 1, 1, 1, 0, 0 → colocações 1, 1, 3, 4, 4, 4, 4.
        int[] perPlayer = [3, 3, 2, 1, 1, 1, 1, 0, 0];
        for (var i = 0; i < ids.Length; i++)
            goals.AddRange(Enumerable.Repeat(Gol(ids[i], i % 2), perPlayer[i]));

        var r = p.Build([Game(1, ids.Where((_, i) => i % 2 == 0).ToArray(), ids.Where((_, i) => i % 2 == 1).ToArray(), goals.ToArray())]);

        r.Rankings.Goals!.Select(x => x.Rank).Should().Equal(1, 1, 3, 4, 4, 4, 4);
        r.Rankings.Goals.Select(x => x.PlayerId).Should().NotContain([ids[7], ids[8]], "zero não entra");
    }

    [Fact]
    public void Ranking_SemEmpates_ParaNo5o()
    {
        var p = new Patota();
        var ids = p.Many(7);
        var goals = ids.SelectMany((id, i) => Enumerable.Repeat(Gol(id, 0), 7 - i)).ToArray();

        var r = p.Build([Game(1, ids, [p.Add("Goleiro")], goals)]);

        r.Rankings.Goals!.Select(x => x.Rank).Should().Equal(1, 2, 3, 4, 5);
    }

    [Fact]
    public void Aproveitamento_VitoriaTresEmpateUm_ComMinimoDeJogos()
    {
        var p = new Patota();
        var (a, b, c) = (p.Add("Ana"), p.Add("Bia"), p.Add("Caio"));

        var units = new[]
        {
            Game(1, [a], [b], score: (1, 0)),
            Game(2, [a], [b], score: (1, 1)),
            Game(3, [a, c], [b], score: (0, 1)),
        };

        var r = p.Build(units, minGames: 2);

        // Ana: V, E, D = 4 pontos de 9 = 44%. Bia: D, E, V = 44%. Caio só tem 1 jogo.
        r.Rankings.PointsRate.Select(x => (x.Name, x.Value, x.Rank)).Should().BeEquivalentTo(new[] { ("Ana", 44.0, 1), ("Bia", 44.0, 1) });
        r.MinGames.Should().Be(2);
    }

    [Fact]
    public void Medias_UsamSoJogosComGolsRegistrados()
    {
        var p = new Patota();
        var (a, b) = (p.Add("Ana"), p.Add("Bia"));

        var units = new[]
        {
            Game(1, [a], [b], [Gol(a, 0), Gol(a, 0, b, own: false)]),
            Game(2, [a], [b], [Gol(a, 0)]),
            Game(3, [a], [b], score: (0, 0)),
        };

        var r = p.Build(units, minGames: 2);

        r.Averages!.Goals.Single(x => x.Name == "Ana").Value.Should().Be(1.5, "3 gols em 2 jogos com gols registrados");
    }

    // ── Marcos ───────────────────────────────────────────────────────────────

    [Fact]
    public void Marcos_PrimeiroCentenariosEUltimo_PelaDataDaPartida()
    {
        var p = new Patota();
        var (a, b, c) = (p.Add("Ana"), p.Add("Bia"), p.Add("Caio"));

        var depois = Game(2, [b], [c], Enumerable.Range(0, 55).Select(i => Gol(b, 0, i == 0 ? c : null, time: 1200 + i)).ToArray());
        var antes = Game(1, [a], [c], Enumerable.Range(0, 150).Select(i => Gol(a, 0, time: 1200 + i)).ToArray());

        var m = p.Build([depois, antes]).Milestones!;

        m.FirstGoal!.ScorerName.Should().Be("Ana", "a partida mais antiga vem primeiro");
        m.FirstGoal.TimeSeconds.Should().Be(1200);
        m.GoalCenturies.Select(g => (g.Number, g.ScorerName)).Should().Equal((100, "Ana"), (200, "Bia"));
        m.LastGoal!.Number.Should().Be(205);
        m.LastGoal.ScorerName.Should().Be("Bia");
        m.FirstAssist!.AssistName.Should().Be("Caio");
        m.TotalAssists.Should().Be(1);
        m.AssistCenturies.Should().BeEmpty();
    }

    [Fact]
    public void ClubeDoGolContra_ListaQuemMarcouContra()
    {
        var p = new Patota();
        var (a, b) = (p.Add("Ana"), p.Add("Bia"));

        var r = p.Build([Game(1, [a], [b], [Gol(a, 1, own: true), Gol(a, 1, own: true), Gol(b, 0, own: true)])]);

        r.Milestones!.OwnGoalsTotal.Should().Be(3);
        r.Milestones.OwnGoalClub.Select(x => (x.Name, x.Count)).Should().Equal(("Ana", 2), ("Bia", 1));
    }

    // ── Recordes e decisivos ─────────────────────────────────────────────────

    [Fact]
    public void Virada_GolDaVitoria_EAbriuOPlacar()
    {
        var p = new Patota();
        var (a1, b1, b2) = (p.Add("Ana"), p.Add("Bia"), p.Add("Caio"));

        var virada = Game(1, [a1], [b1, b2],
            [Gol(a1, 0), Gol(a1, 0), Gol(a1, 0), Gol(b1, 1), Gol(b2, 1), Gol(b1, 1), Gol(b2, 1)]);
        // Placar 5x0 com um gol só registrado: abre o placar, mas não decide nem vira.
        var incompleto = Game(2, [a1], [b1], [Gol(b1, 0, own: true)], score: (5, 0));
        var normal = Game(3, [a1], [b1], [Gol(b1, 1), Gol(a1, 0), Gol(b1, 1)]);

        var r = p.Build([virada, incompleto, normal]);

        r.Records.ComebacksTotal.Should().Be(1);
        r.Records.Comebacks.Single().Should().BeEquivalentTo(new { Deficit = 3, WinnerGoals = 4, LoserGoals = 3, Rank = 1 });
        r.Decisive!.WinningGoals.Select(x => (x.Name, x.Value)).Should().BeEquivalentTo(new[] { ("Caio", 1.0), ("Bia", 1.0) },
            "na virada o 4º gol foi do Caio; no 2x1 o 2º gol da Bia");
        r.Decisive.OpenedScoring.Select(x => (x.Name, x.Value)).Should().BeEquivalentTo(new[] { ("Ana", 1.0), ("Bia", 1.0) },
            "gol contra abrindo o placar não conta para ninguém");
    }

    [Fact]
    public void MaisGolsNumaPartida_EmpatadosNaMesmaColocacao()
    {
        var p = new Patota();
        var (a, b, c) = (p.Add("Ana"), p.Add("Bia"), p.Add("Caio"));

        var r = p.Build(
        [
            Game(1, [a, b], [c], [Gol(a, 0), Gol(a, 0), Gol(a, 0), Gol(b, 0)]),
            Game(2, [b], [c], [Gol(b, 1, own: true), Gol(b, 0), Gol(b, 0), Gol(b, 0), Gol(c, 1)]),
        ]);

        r.Records.Hauls!.Select(h => (h.Name, h.Goals, h.Rank)).Should().Equal(("Ana", 3, 1), ("Bia", 3, 1), ("Bia", 1, 3), ("Caio", 1, 3));
    }

    // ── Sequências ───────────────────────────────────────────────────────────

    [Fact]
    public void Sequencias_FaltarZeraPresenca_EmpateZeraVitorias()
    {
        var p = new Patota();
        var (a, b, c) = (p.Add("Ana"), p.Add("Bia"), p.Add("Caio"));

        // Ana joga tudo: V V E V V. Bia falta no 3º jogo.
        var r = p.Build(
        [
            Game(1, [a], [b], score: (1, 0)),
            Game(2, [a], [b], score: (1, 0)),
            Game(3, [a], [c], score: (1, 1)),
            Game(4, [a], [b], score: (1, 0)),
            Game(5, [a], [b], score: (1, 0)),
        ]);

        r.Streaks.Presence.First().Should().BeEquivalentTo(new { Name = "Ana", Value = 5.0, Rank = 1 });
        r.Streaks.Presence.Single(x => x.Name == "Bia").Value.Should().Be(2);
        r.Streaks.Wins.Single(x => x.Name == "Ana").Value.Should().Be(2);
        r.Streaks.Unbeaten.Single(x => x.Name == "Ana").Value.Should().Be(5);
    }

    // ── Duplas e jogadores ───────────────────────────────────────────────────

    [Fact]
    public void Duplas_MesmoTime_ComMinimoParaMelhorAproveitamento()
    {
        var p = new Patota();
        var (a, b, c, d) = (p.Add("Ana"), p.Add("Bia"), p.Add("Caio"), p.Add("Duda"));

        var r = p.Build(
        [
            Game(1, [a, b], [c, d], [Gol(a, 0, b)]),
            Game(2, [a, b], [c, d], [Gol(a, 0, b)]),
            Game(3, [a, c], [b, d], [Gol(d, 1, b)]),
        ], minGames: 2);

        r.PairMinGames.Should().Be(2);
        r.Pairs.MostTogether.First().Should().BeEquivalentTo(new { Games = 2, Rank = 1 });
        r.Pairs.BestTogether.Select(x => (x.PointsRate, x.Rank)).Should().Equal((100, 1));
        r.Pairs.BestTogether.Single().Should().Match<SpotlightPairRow>(x => new[] { x.PlayerAName, x.PlayerBName }.Order().SequenceEqual(new[] { "Ana", "Bia" }));
        r.Pairs.AssistCombos!.First().Should().BeEquivalentTo(new { AssistName = "Bia", ScorerName = "Ana", Goals = 2, Rank = 1 });
    }

    [Fact]
    public void Jogador_CarrascoSoComRetrospectoNegativo_EFregues()
    {
        var p = new Patota();
        var (a, b, c) = (p.Add("Ana"), p.Add("Bia"), p.Add("Caio"));

        // Ana x Bia: 2V 1E 1D para Ana. Ana x Caio: 1V 0E 1D (equilibrado: nem carrasco nem freguês).
        var r = p.Build(
        [
            Game(1, [a], [b], score: (1, 0)),
            Game(2, [a], [b], score: (1, 0)),
            Game(3, [a], [b], score: (1, 1)),
            Game(4, [a], [b], score: (0, 1)),
            Game(5, [a], [c], score: (1, 0)),
            Game(6, [a], [c], score: (0, 1)),
        ], minGames: 2);

        var ana = r.Players.Single(x => x.Name == "Ana");
        ana.Victims.Select(v => (v.Name, v.Wins, v.Draws, v.Losses)).Should().Equal(("Bia", 2, 1, 1));
        ana.Nemeses.Should().BeEmpty();

        var bia = r.Players.Single(x => x.Name == "Bia");
        bia.Nemeses.Select(v => (v.Name, v.Wins, v.Draws, v.Losses)).Should().Equal(("Ana", 1, 1, 2));
        bia.Victims.Should().BeEmpty();
    }

    [Fact]
    public void Jogador_MelhoresEPioresParceiros_NaoSeRepetem()
    {
        var p = new Patota();
        var (a, b, c, d) = (p.Add("Ana"), p.Add("Bia"), p.Add("Caio"), p.Add("Duda"));

        var r = p.Build(
        [
            Game(1, [a, b], [c, d], score: (1, 0)),
            Game(2, [a, b], [c, d], score: (1, 0)),
            Game(3, [a, c], [b, d], score: (0, 1)),
            Game(4, [a, c], [b, d], score: (0, 1)),
        ], minGames: 2);

        var ana = r.Players.Single(x => x.Name == "Ana");
        ana.BestPartners.Select(x => (x.Name, x.PointsRate)).Should().Equal(("Bia", 100));
        ana.WorstPartners.Select(x => (x.Name, x.PointsRate)).Should().Equal(("Caio", 0));
        r.Players.Select(x => x.Name).Should().Equal("Ana", "Bia", "Caio", "Duda");
    }

    [Fact]
    public void FatoDoAno_EhOMelhorColocado_ComEmpateMarcado()
    {
        var p = new Patota();
        var (a, b, c) = (p.Add("Ana"), p.Add("Bia"), p.Add("Caio"));

        var r = p.Build(
        [
            Game(1, [a, c], [b], [Gol(a, 0), Gol(a, 0), Gol(a, 0), Gol(b, 1)], mvp: b),
            Game(2, [a, c], [b], [Gol(b, 1)], mvp: c),
        ]);

        r.Players.Single(x => x.Name == "Ana").Fact.Should().BeEquivalentTo(new { Kind = "goals", Value = 3.0, Rank = 1, Tied = false });
        // Bia é 2ª em gols, mas divide o 1º lugar de MVPs com o Caio: vale a melhor colocação.
        r.Players.Single(x => x.Name == "Bia").Fact.Should().BeEquivalentTo(new { Kind = "mvps", Rank = 1, Tied = true });
        r.Players.Single(x => x.Name == "Caio").Fact.Should().BeEquivalentTo(new { Kind = "mvps", Rank = 1, Tied = true });
    }

    // ── Filtros e campeonato ─────────────────────────────────────────────────

    [Fact]
    public void SoMensalistas_ConvidadoForaDosRankings_MasNosNumeros()
    {
        var p = new Patota();
        var (a, guest) = (p.Add("Ana"), p.Add("Visita", guest: true));

        var r = p.Build([Game(1, [a], [guest], [Gol(guest, 1), Gol(guest, 1), Gol(a, 0)])], onlyMonthly: true);

        r.Numbers.Goals.Should().Be(3);
        r.Numbers.Players.Should().Be(2);
        r.Rankings.Goals!.Select(x => x.Name).Should().Equal("Ana");
        r.Players.Select(x => x.Name).Should().Equal("Ana");
    }

    [Fact]
    public void CampeonatoConsolidado_ContaComoUmJogo_MasNaoEhRecordeDePartida()
    {
        var p = new Patota();
        var (a, b) = (p.Add("Ana"), p.Add("Bia"));

        var consolidado = Game(1, [a], [b], [Gol(a, 0), Gol(a, 0), Gol(a, 0), Gol(a, 0), Gol(b, 1)], singleGame: false);

        var r = p.Build([consolidado]);

        r.Numbers.Games.Should().Be(1);
        r.Numbers.Goals.Should().Be(5);
        r.Rankings.Goals!.First().Should().BeEquivalentTo(new { Name = "Ana", Value = 4.0 });
        r.Records.Hauls.Should().BeEmpty();
        r.Numbers.MostGoalsGame.Should().BeNull();
        r.Decisive!.OpenedScoring.Should().BeEmpty();
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(2, 1)]
    [InlineData(10, 3)]
    [InlineData(46, 12)]
    public void MinimoAutomatico_EhUmQuartoDosJogos(int games, int expected)
        => SpotlightCalculator.AutoMinGames(games).Should().Be(expected);
}
