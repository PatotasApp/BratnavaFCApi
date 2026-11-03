using BratnavaFC.Domain.Models;

namespace BratnavaFC.Application.Statistics;

/// <summary>Uma unidade do período. IsSingleGame: uma partida de verdade (não um campeonato consolidado).</summary>
public sealed record SpotlightUnit(StatMatch Stat, bool IsSingleGame);

/// <summary>Name: como aparece no telão (primeiro nome, ou completo se outro da patota tiver o mesmo).</summary>
public sealed record SpotlightPlayerInfo(Guid Id, string Name, string FullName, bool IsGuest, bool IsGoalkeeper);

/// <summary>
/// Cálculos do Spotlight do telão a partir das unidades de estatística do período.
///
/// Regras (decididas com o Luis, nov/2026):
/// - Gols são só os registrados com autor; média e "jogo com mais gols" também. V/E/D e goleada pelo placar.
/// - Participações = gols (sem gol contra) + assistências; assistência em gol contra nunca conta.
/// - Presença pelo time escalado (a unidade já vem sem quem não jogou). "Juntos" = mesmo time.
/// - Aproveitamento: vitória 3, empate 1, sobre os jogos com resultado.
/// - Colocação com empate tipo competição (1, 1, 3) e listas até o 5º lugar.
/// - Ordem dos gols: data da partida, horário anotado, ordem de cadastro (já vem assim na unidade).
/// - Campeonato consolidado é 1 jogo: conta em totais e rankings, mas fica fora dos recordes de partida.
/// - Números da patota contam todo mundo; rankings, duplas e slides só os jogadores acompanhados
///   (todos, ou só mensalistas).
/// </summary>
public static class SpotlightCalculator
{
    public const int TopPlaces = 5;
    public const int PairMinGames = 8;
    public const int RelationMinGames = 4;
    public const int RelationTop = 5;
    public const int ComebackMinDeficit = 3;

    /// <summary>Automático: 1/4 dos jogos do período (pelo menos 1).</summary>
    public static int AutoMinGames(int games) => Math.Max(1, (int)Math.Round(games / 4.0, MidpointRounding.AwayFromZero));

    public static SpotlightEventReport Build(
        IReadOnlyList<SpotlightUnit> unitsInPeriod,
        IReadOnlyDictionary<Guid, SpotlightPlayerInfo> players,
        IReadOnlySet<Guid> tracked,
        int replays,
        int? minGamesSetting)
    {
        var units = unitsInPeriod.OrderBy(u => u.Stat.PlayedAt).ThenBy(u => u.Stat.Id).ToList();
        var minGames = minGamesSetting ?? AutoMinGames(units.Count);
        var pairMin = Math.Min(PairMinGames, minGames);
        var relationMin = Math.Min(RelationMinGames, minGames);

        string NameOf(Guid id) => players.TryGetValue(id, out var p) ? p.Name : "?";

        var acc = tracked.ToDictionary(id => id, _ => new PlayerAcc());
        var pairs = new Dictionary<(Guid A, Guid B), PairAcc>();
        var rivals = new Dictionary<(Guid Me, Guid Other), RivalAcc>();
        var assistPairs = new Dictionary<(Guid Assist, Guid Scorer), int>();
        var hauls = new List<SpotlightHaul>();
        var comebacks = new List<SpotlightComeback>();
        var timeline = new List<(SpotlightUnit Unit, StatGoal Goal)>();

        foreach (var unit in units)
        {
            var stat = unit.Stat;
            var countable = stat.Goals.Where(g => g.ScorerPlayerId is not null).ToList();
            var present = stat.AllPlayers.Select(p => p.PlayerId).Where(tracked.Contains).ToHashSet();

            // ── Presença, resultado, MVP e sequências ──
            foreach (var (id, a) in acc)
            {
                if (!present.Contains(id))
                {
                    a.PresenceRun = 0;
                    continue;
                }

                var player = stat.AllPlayers.First(p => p.PlayerId == id);
                a.Games++;
                a.PresenceRun++;
                a.BestPresenceRun = Math.Max(a.BestPresenceRun, a.PresenceRun);
                if (player.IsMvp) a.Mvps++;
                if (countable.Count > 0) a.GamesWithGoals++;

                switch (player.Result)
                {
                    case StatResult.Win: a.Wins++; a.WinRun++; a.UnbeatenRun++; break;
                    case StatResult.Draw: a.Draws++; a.WinRun = 0; a.UnbeatenRun++; break;
                    case StatResult.Loss: a.Losses++; a.WinRun = 0; a.UnbeatenRun = 0; break;
                }
                a.BestWinRun = Math.Max(a.BestWinRun, a.WinRun);
                a.BestUnbeatenRun = Math.Max(a.BestUnbeatenRun, a.UnbeatenRun);
            }

            // ── Duplas (mesmo time) ──
            foreach (var side in stat.Sides)
            {
                var ids = side.Players.Select(p => p.PlayerId).Where(tracked.Contains).Distinct().OrderBy(id => id).ToList();
                for (var i = 0; i < ids.Count; i++)
                    for (var j = i + 1; j < ids.Count; j++)
                    {
                        var pair = pairs.TryGetValue((ids[i], ids[j]), out var pa) ? pa : pairs[(ids[i], ids[j])] = new PairAcc();
                        pair.Games++;
                        pair.Add(side.Result);
                    }
            }

            // ── Confronto direto (times opostos, resultado de verdade) ──
            if (stat.IsHeadToHead && stat.Sides.Count == 2 && stat.Sides.All(s => s.Result != StatResult.None))
            {
                for (var s = 0; s < 2; s++)
                {
                    var mine = stat.Sides[s].Players.Select(p => p.PlayerId).Where(tracked.Contains).Distinct();
                    var theirs = stat.Sides[1 - s].Players.Select(p => p.PlayerId).Where(tracked.Contains).Distinct().ToList();
                    foreach (var me in mine)
                        foreach (var other in theirs)
                        {
                            var rival = rivals.TryGetValue((me, other), out var ra) ? ra : rivals[(me, other)] = new RivalAcc();
                            rival.Add(stat.Sides[s].Result);
                        }
                }
            }

            // ── Gols e assistências ──
            foreach (var goal in countable)
            {
                timeline.Add((unit, goal));
                var scorer = goal.ScorerPlayerId!.Value;
                if (goal.IsOwnGoal)
                {
                    if (acc.TryGetValue(scorer, out var og)) og.OwnGoals++;
                    continue;
                }

                if (acc.TryGetValue(scorer, out var sa)) sa.Goals++;
                if (goal.AssistPlayerId is Guid assist)
                {
                    if (acc.TryGetValue(assist, out var aa)) aa.Assists++;
                    if (assist != scorer && tracked.Contains(assist) && tracked.Contains(scorer))
                        assistPairs[(assist, scorer)] = assistPairs.GetValueOrDefault((assist, scorer)) + 1;
                }
            }

            if (!unit.IsSingleGame) continue;

            // ── Recordes de partida ──
            foreach (var g in countable.Where(g => !g.IsOwnGoal && tracked.Contains(g.ScorerPlayerId!.Value)).GroupBy(g => g.ScorerPlayerId!.Value))
            {
                var count = g.Count();
                hauls.Add(new SpotlightHaul { PlayerId = g.Key, Name = NameOf(g.Key), Goals = count, PlayedAt = stat.PlayedAt });
                acc[g.Key].BestHaul = Math.Max(acc[g.Key].BestHaul, count);
            }

            var opener = countable.FirstOrDefault();
            if (opener is { IsOwnGoal: false } && acc.TryGetValue(opener.ScorerPlayerId!.Value, out var oa))
                oa.OpenedScoring++;

            if (Score(stat) is not (int scoreA, int scoreB) || scoreA == scoreB || !IsComplete(stat, scoreA, scoreB))
                continue;

            var winner = scoreA > scoreB ? 0 : 1;
            var loserScore = Math.Min(scoreA, scoreB);

            // Gol da vitória: o gol que deixou o vencedor um gol à frente do placar final do perdedor.
            var decisive = stat.Goals.Where(g => g.Side == winner).ElementAt(loserScore);
            if (decisive is { IsOwnGoal: false, ScorerPlayerId: Guid hero } && acc.TryGetValue(hero, out var ha))
                ha.WinningGoals++;

            // Virada: o vencedor chegou a perder por 3 ou mais.
            int diff = 0, worst = 0;
            foreach (var g in stat.Goals)
            {
                diff += g.Side == winner ? 1 : -1;
                worst = Math.Min(worst, diff);
            }
            if (-worst >= ComebackMinDeficit)
                comebacks.Add(new SpotlightComeback
                {
                    PlayedAt = stat.PlayedAt,
                    Deficit = -worst,
                    WinnerGoals = Math.Max(scoreA, scoreB),
                    LoserGoals = loserScore
                });
        }

        var report = new SpotlightEventReport
        {
            MinGames = minGames,
            PairMinGames = pairMin,
            Numbers = Numbers(units, replays),
        };

        // ── Rankings ──
        var tops = acc.Where(kv => kv.Value.Games > 0).Select(kv => (Id: kv.Key, A: kv.Value)).ToList();
        List<SpotlightRankRow> Rank(Func<PlayerAcc, double> value, Func<PlayerAcc, bool>? eligible = null) =>
            TopRanked(tops.Where(t => eligible?.Invoke(t.A) ?? true), t => value(t.A), t => NameOf(t.Id))
                .Select(x => new SpotlightRankRow { PlayerId = x.Item.Id, Name = NameOf(x.Item.Id), Value = value(x.Item.A), Rank = x.Rank })
                .ToList();

        bool RateEligible(PlayerAcc a) => a.ResultGames >= minGames;
        bool AverageEligible(PlayerAcc a) => a.GamesWithGoals >= minGames;

        report.Rankings = new SpotlightRankings
        {
            Goals = Rank(a => a.Goals),
            Assists = Rank(a => a.Assists),
            Participations = Rank(a => a.Goals + a.Assists),
            Presences = Rank(a => a.Games),
            Wins = Rank(a => a.Wins),
            Mvps = Rank(a => a.Mvps),
            PointsRate = Rank(a => a.PointsRate ?? 0, RateEligible),
        };
        report.Averages = new SpotlightAverages
        {
            Goals = Rank(a => PerGame(a.Goals, a.GamesWithGoals), AverageEligible),
            Assists = Rank(a => PerGame(a.Assists, a.GamesWithGoals), AverageEligible),
            Participations = Rank(a => PerGame(a.Goals + a.Assists, a.GamesWithGoals), AverageEligible),
        };
        report.Streaks = new SpotlightStreaks
        {
            Presence = Rank(a => a.BestPresenceRun),
            Wins = Rank(a => a.BestWinRun),
            Unbeaten = Rank(a => a.BestUnbeatenRun),
        };
        report.Decisive = new SpotlightDecisive
        {
            OpenedScoring = Rank(a => a.OpenedScoring),
            WinningGoals = Rank(a => a.WinningGoals),
        };

        // ── Marcos ──
        report.Milestones = Milestones(timeline, NameOf, acc);

        // ── Recordes ──
        report.Records = new SpotlightRecords
        {
            Hauls = TopRanked(hauls, h => h.Goals, h => h.PlayedAt.Ticks.ToString("D19"))
                .Select(x => { x.Item.Rank = x.Rank; return x.Item; }).ToList(),
            Comebacks = TopRanked(comebacks, c => c.Deficit, c => c.PlayedAt.Ticks.ToString("D19"))
                .Select(x => { x.Item.Rank = x.Rank; return x.Item; }).ToList(),
            ComebacksTotal = comebacks.Count,
        };

        // ── Duplas ──
        SpotlightPairRow PairRow((Guid A, Guid B) key, PairAcc p, int rank) => new()
        {
            PlayerAId = key.A, PlayerAName = NameOf(key.A), PlayerBId = key.B, PlayerBName = NameOf(key.B),
            Games = p.Games, PointsRate = p.PointsRate ?? 0, Rank = rank
        };
        string PairName(KeyValuePair<(Guid A, Guid B), PairAcc> kv) => NameOf(kv.Key.A) + " " + NameOf(kv.Key.B);

        report.Pairs = new SpotlightPairs
        {
            MostTogether = TopRanked(pairs, kv => kv.Value.Games, PairName)
                .Select(x => PairRow(x.Item.Key, x.Item.Value, x.Rank)).ToList(),
            BestTogether = TopRanked(pairs.Where(kv => kv.Value.ResultGames >= pairMin), kv => kv.Value.PointsRate ?? 0, PairName)
                .Select(x => PairRow(x.Item.Key, x.Item.Value, x.Rank)).ToList(),
            AssistCombos = TopRanked(assistPairs, kv => kv.Value, kv => NameOf(kv.Key.Assist) + " " + NameOf(kv.Key.Scorer))
                .Select(x => new SpotlightAssistPairRow
                {
                    AssistId = x.Item.Key.Assist, AssistName = NameOf(x.Item.Key.Assist),
                    ScorerId = x.Item.Key.Scorer, ScorerName = NameOf(x.Item.Key.Scorer),
                    Goals = x.Item.Value, Rank = x.Rank
                }).ToList(),
        };

        // ── Um slide por jogador que jogou ──
        var factRanks = FactRanks(tops, minGames);
        bool QualifiesForRelation(Guid id) =>
            players.TryGetValue(id, out var info) && (!info.IsGuest || acc[id].Games >= minGames);

        report.Players = tops
            .Select(t => PlayerSlide(t.Id, t.A, players, NameOf, pairs, rivals, assistPairs, relationMin, QualifiesForRelation, factRanks))
            .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        return report;
    }

    // ── Números da patota (todo mundo) ───────────────────────────────────────

    private static SpotlightNumbers Numbers(IReadOnlyList<SpotlightUnit> units, int replays)
    {
        var goals = units.SelectMany(u => u.Stat.Goals.Where(g => g.ScorerPlayerId is not null)).ToList();
        var gamesWithGoals = units.Count(u => u.Stat.Goals.Any(g => g.ScorerPlayerId is not null));
        var assists = goals.Count(g => !g.IsOwnGoal && g.AssistPlayerId is not null);

        SpotlightScoreline? mostGoals = null;
        var most = units.Where(u => u.IsSingleGame && u.Stat.Sides.Count == 2)
            .Select(u => (u.Stat.PlayedAt, A: u.Stat.Goals.Count(g => g.ScorerPlayerId is not null && g.Side == 0),
                B: u.Stat.Goals.Count(g => g.ScorerPlayerId is not null && g.Side == 1)))
            .Where(x => x.A + x.B > 0)
            .OrderByDescending(x => x.A + x.B).ThenBy(x => x.PlayedAt)
            .FirstOrDefault();
        if (most.A + most.B > 0)
            mostGoals = new SpotlightScoreline { PlayedAt = most.PlayedAt, GoalsA = Math.Max(most.A, most.B), GoalsB = Math.Min(most.A, most.B) };

        SpotlightScoreline? biggest = null;
        var win = units.Where(u => u.IsSingleGame)
            .Select(u => (u.Stat.PlayedAt, Score: Score(u.Stat)))
            .Where(x => x.Score is { } s && s.A != s.B)
            .Select(x => (x.PlayedAt, Hi: Math.Max(x.Score!.Value.A, x.Score.Value.B), Lo: Math.Min(x.Score.Value.A, x.Score.Value.B)))
            .OrderByDescending(x => x.Hi - x.Lo).ThenByDescending(x => x.Hi).ThenBy(x => x.PlayedAt)
            .FirstOrDefault();
        if (win.Hi > win.Lo)
            biggest = new SpotlightScoreline { PlayedAt = win.PlayedAt, GoalsA = win.Hi, GoalsB = win.Lo };

        return new SpotlightNumbers
        {
            Games = units.Count,
            Goals = goals.Count,
            GamesWithGoals = gamesWithGoals,
            GoalsPerGame = gamesWithGoals == 0 ? null : Math.Round(goals.Count / (double)gamesWithGoals, 1),
            Assists = assists,
            Participations = goals.Count(g => !g.IsOwnGoal) + assists,
            Players = units.SelectMany(u => u.Stat.AllPlayers).Select(p => p.PlayerId).Distinct().Count(),
            Draws = units.Count(u => u.Stat.IsHeadToHead && u.Stat.Sides.Count == 2 && u.Stat.Sides[0].Result == StatResult.Draw),
            OwnGoals = goals.Count(g => g.IsOwnGoal),
            MvpVotes = units.Sum(u => u.Stat.MvpVotesFor.Count),
            Replays = replays,
            MostGoalsGame = mostGoals,
            BiggestWin = biggest,
        };
    }

    // ── Marcos ───────────────────────────────────────────────────────────────

    private static SpotlightMilestones Milestones(
        List<(SpotlightUnit Unit, StatGoal Goal)> timeline,
        Func<Guid, string> nameOf,
        Dictionary<Guid, PlayerAcc> acc)
    {
        SpotlightGoalMoment Moment(int number, (SpotlightUnit Unit, StatGoal Goal) x)
        {
            var score = Score(x.Unit.Stat);
            var side = x.Goal.Side;
            return new SpotlightGoalMoment
            {
                Number = number,
                PlayedAt = x.Unit.Stat.PlayedAt,
                ScorerName = nameOf(x.Goal.ScorerPlayerId!.Value),
                AssistName = !x.Goal.IsOwnGoal && x.Goal.AssistPlayerId is Guid a ? nameOf(a) : null,
                IsOwnGoal = x.Goal.IsOwnGoal,
                TimeSeconds = x.Goal.TimeSeconds,
                ScoreFor = score is { } s && side is 0 or 1 ? (side == 0 ? s.A : s.B) : null,
                ScoreAgainst = score is { } t && side is 0 or 1 ? (side == 0 ? t.B : t.A) : null,
            };
        }

        var assisted = timeline.Where(x => !x.Goal.IsOwnGoal && x.Goal.AssistPlayerId is not null).ToList();

        return new SpotlightMilestones
        {
            FirstGoal = timeline.Count > 0 ? Moment(1, timeline[0]) : null,
            LastGoal = timeline.Count > 0 ? Moment(timeline.Count, timeline[^1]) : null,
            GoalCenturies = Enumerable.Range(1, timeline.Count / 100).Select(k => Moment(k * 100, timeline[k * 100 - 1])).ToList(),
            FirstAssist = assisted.Count > 0 ? Moment(1, assisted[0]) : null,
            AssistCenturies = Enumerable.Range(1, assisted.Count / 100).Select(k => Moment(k * 100, assisted[k * 100 - 1])).ToList(),
            TotalAssists = assisted.Count,
            OwnGoalsTotal = timeline.Count(x => x.Goal.IsOwnGoal),
            OwnGoalClub = acc.Where(kv => kv.Value.OwnGoals > 0)
                .Select(kv => new SpotlightCountRow { PlayerId = kv.Key, Name = nameOf(kv.Key), Count = kv.Value.OwnGoals })
                .OrderByDescending(r => r.Count).ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList(),
        };
    }

    // ── Slide do jogador ─────────────────────────────────────────────────────

    private static SpotlightPlayer PlayerSlide(
        Guid id, PlayerAcc a,
        IReadOnlyDictionary<Guid, SpotlightPlayerInfo> players,
        Func<Guid, string> nameOf,
        Dictionary<(Guid A, Guid B), PairAcc> pairs,
        Dictionary<(Guid Me, Guid Other), RivalAcc> rivals,
        Dictionary<(Guid Assist, Guid Scorer), int> assistPairs,
        int relationMin,
        Func<Guid, bool> qualifies,
        Dictionary<Guid, SpotlightFact> facts)
    {
        var info = players.TryGetValue(id, out var p) ? p : new SpotlightPlayerInfo(id, "?", "?", false, false);

        var partners = pairs
            .Where(kv => (kv.Key.A == id || kv.Key.B == id) && kv.Value.ResultGames >= relationMin)
            .Select(kv => (Other: kv.Key.A == id ? kv.Key.B : kv.Key.A, Pair: kv.Value))
            .Where(x => qualifies(x.Other))
            .Select(x => new SpotlightPartner { PlayerId = x.Other, Name = nameOf(x.Other), Games = x.Pair.Games, PointsRate = x.Pair.PointsRate ?? 0 })
            .ToList();
        // Os melhores e os piores não se repetem: com poucos parceiros, metade para cada lado.
        var best = partners
            .OrderByDescending(x => x.PointsRate).ThenByDescending(x => x.Games).ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            .Take(Math.Min(RelationTop, (partners.Count + 1) / 2))
            .ToList();
        var worst = partners.Except(best)
            .OrderBy(x => x.PointsRate).ThenByDescending(x => x.Games).ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            .Take(RelationTop)
            .ToList();

        var record = rivals
            .Where(kv => kv.Key.Me == id && kv.Value.Games >= relationMin && qualifies(kv.Key.Other))
            .Select(kv => (Other: kv.Key.Other, R: kv.Value))
            .ToList();
        SpotlightRival Rival((Guid Other, RivalAcc R) x) => new()
        { PlayerId = x.Other, Name = nameOf(x.Other), Wins = x.R.Wins, Draws = x.R.Draws, Losses = x.R.Losses };

        var nemeses = record.Where(x => x.R.Losses > x.R.Wins)
            .OrderBy(x => x.R.PointsRate).ThenByDescending(x => x.R.Games).ThenBy(x => nameOf(x.Other), StringComparer.CurrentCultureIgnoreCase)
            .Take(RelationTop).Select(Rival).ToList();
        var victims = record.Where(x => x.R.Wins > x.R.Losses)
            .OrderByDescending(x => x.R.PointsRate).ThenByDescending(x => x.R.Games).ThenBy(x => nameOf(x.Other), StringComparer.CurrentCultureIgnoreCase)
            .Take(RelationTop).Select(Rival).ToList();

        List<SpotlightCountRow> Assists(Func<(Guid Assist, Guid Scorer), Guid?> other) => assistPairs
            .Select(kv => (Other: other(kv.Key), Count: kv.Value))
            .Where(x => x.Other is Guid o && qualifies(o))
            .Select(x => new SpotlightCountRow { PlayerId = x.Other!.Value, Name = nameOf(x.Other.Value), Count = x.Count })
            .OrderByDescending(x => x.Count).ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            .Take(RelationTop)
            .ToList();

        return new SpotlightPlayer
        {
            PlayerId = id,
            Name = info.Name,
            FullName = info.FullName,
            IsGoalkeeper = info.IsGoalkeeper,
            IsGuest = info.IsGuest,
            Games = a.Games,
            Wins = a.Wins,
            Draws = a.Draws,
            Losses = a.Losses,
            Goals = a.Goals,
            Assists = a.Assists,
            OwnGoals = a.OwnGoals,
            Mvps = a.Mvps,
            PointsRate = a.PointsRate,
            BestPartners = best,
            WorstPartners = worst,
            Nemeses = nemeses,
            Victims = victims,
            AssistedTo = Assists(k => k.Assist == id ? k.Scorer : null),
            AssistedBy = Assists(k => k.Scorer == id ? k.Assist : null),
            Fact = facts.GetValueOrDefault(id),
        };
    }

    /// <summary>
    /// "Fato do ano": o dado em que cada jogador fica mais bem colocado na patota inteira
    /// (não só no top 5). No empate de colocação vale a ordem da lista.
    /// </summary>
    private static Dictionary<Guid, SpotlightFact> FactRanks(List<(Guid Id, PlayerAcc A)> players, int minGames)
    {
        var kinds = new List<(string Kind, Func<PlayerAcc, double?> Value)>
        {
            ("goals", a => a.Goals),
            ("assists", a => a.Assists),
            ("participations", a => a.Goals + a.Assists),
            ("mvps", a => a.Mvps),
            ("winningGoals", a => a.WinningGoals),
            ("openedScoring", a => a.OpenedScoring),
            ("winStreak", a => a.BestWinRun),
            ("unbeatenStreak", a => a.BestUnbeatenRun),
            ("presenceStreak", a => a.BestPresenceRun),
            ("pointsRate", a => a.ResultGames >= minGames ? a.PointsRate : null),
            ("haul", a => a.BestHaul),
            ("wins", a => a.Wins),
            ("presences", a => a.Games),
        };

        var facts = new Dictionary<Guid, SpotlightFact>();
        foreach (var (kind, value) in kinds)
        {
            var values = players.Select(p => (p.Id, V: value(p.A))).Where(x => x.V is > 0).Select(x => (x.Id, V: x.V!.Value)).ToList();
            foreach (var (pid, v) in values)
            {
                var rank = 1 + values.Count(x => x.V > v);
                if (facts.TryGetValue(pid, out var current) && current.Rank <= rank) continue;
                facts[pid] = new SpotlightFact { Kind = kind, Value = v, Rank = rank, Tied = values.Count(x => x.V == v) > 1 };
            }
        }
        return facts;
    }

    // ── Ajudantes ────────────────────────────────────────────────────────────

    /// <summary>
    /// Colocação tipo competição (1, 1, 3): todo mundo até o 5º lugar, só valores acima de zero.
    /// Dentro do empate, ordena pela chave (nome, data).
    /// </summary>
    public static List<(T Item, int Rank)> TopRanked<T>(IEnumerable<T> items, Func<T, double> value, Func<T, string> tieKey)
    {
        var list = items.Select(i => (Item: i, V: value(i))).Where(x => x.V > 0).ToList();
        return list
            .Select(x => (x.Item, Rank: 1 + list.Count(y => y.V > x.V), x.V))
            .Where(x => x.Rank <= TopPlaces)
            .OrderBy(x => x.Rank).ThenBy(x => tieKey(x.Item), StringComparer.CurrentCultureIgnoreCase)
            .Select(x => (x.Item, x.Rank))
            .ToList();
    }

    /// <summary>Aproveitamento em % (V=3, E=1) arredondado; nulo sem jogos com resultado.</summary>
    public static int? PointsRateOf(int wins, int draws, int resultGames) =>
        resultGames == 0 ? null : (int)Math.Round(100.0 * (3 * wins + draws) / (3.0 * resultGames), MidpointRounding.AwayFromZero);

    private static double PerGame(int value, int games) => games == 0 ? 0 : Math.Round(value / (double)games, 2);

    /// <summary>Placar de uma partida de dois lados (lado 0, lado 1); nulo sem placar.</summary>
    private static (int A, int B)? Score(StatMatch stat) =>
        stat.Sides.Count == 2 && stat.Sides[1].GoalsAgainst is int a && stat.Sides[0].GoalsAgainst is int b ? (a, b) : null;

    /// <summary>Todos os gols do placar estão registrados (senão a ordem não diz quem virou ou decidiu).</summary>
    private static bool IsComplete(StatMatch stat, int scoreA, int scoreB) =>
        stat.Goals.All(g => g.Side is 0 or 1)
        && stat.Goals.Count(g => g.Side == 0) == scoreA
        && stat.Goals.Count(g => g.Side == 1) == scoreB;

    private sealed class PlayerAcc
    {
        public int Games, Wins, Draws, Losses, Goals, Assists, OwnGoals, Mvps, GamesWithGoals;
        public int OpenedScoring, WinningGoals, BestHaul;
        public int PresenceRun, BestPresenceRun, WinRun, BestWinRun, UnbeatenRun, BestUnbeatenRun;
        public int ResultGames => Wins + Draws + Losses;
        public int? PointsRate => PointsRateOf(Wins, Draws, ResultGames);
    }

    private sealed class PairAcc
    {
        public int Games, Wins, Draws, Losses;
        public int ResultGames => Wins + Draws + Losses;
        public int? PointsRate => PointsRateOf(Wins, Draws, ResultGames);

        public void Add(StatResult result)
        {
            switch (result)
            {
                case StatResult.Win: Wins++; break;
                case StatResult.Draw: Draws++; break;
                case StatResult.Loss: Losses++; break;
            }
        }
    }

    private sealed class RivalAcc
    {
        public int Wins, Draws, Losses;
        public int Games => Wins + Draws + Losses;
        public int PointsRate => PointsRateOf(Wins, Draws, Games) ?? 0;

        public void Add(StatResult result)
        {
            switch (result)
            {
                case StatResult.Win: Wins++; break;
                case StatResult.Draw: Draws++; break;
                case StatResult.Loss: Losses++; break;
            }
        }
    }
}
