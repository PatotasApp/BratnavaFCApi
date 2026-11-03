using BratnavaFC.Domain.Dtos.Retrospective;

namespace BratnavaFC.Application.Statistics;

/// <summary>
/// Retrospectiva individual calculada a partir das unidades de estatística do período
/// (as mesmas das Estatísticas: presença pelo time escalado, gols só os registrados,
/// assistência de gol contra não conta, campeonato consolidado = 1 jogo).
/// Regras em docs/retrospectiva/RETROSPECTIVA.md.
/// </summary>
public static class PlayerRetrospectiveCalculator
{
    public const int MinGamesTogether = 4;
    public const int MinGamesAgainst = 4;
    public const int MinGamesBestMonth = 3;

    /// <summary>Mínimo para os rankings de taxa: 20% dos jogos da patota, no mínimo 3.</summary>
    public static int MinGamesForRates(int totalGames) => Math.Max(3, (int)Math.Ceiling(totalGames * 0.2));

    /// <summary>Desempate do perfil quando duas posições são iguais.</summary>
    public static readonly string[] ProfileOrder = ["fiel", "matador", "garcom", "pequente", "craque", "decisivo"];

    /// <param name="units">Unidades da patota no período.</param>
    /// <param name="names">Nome curto de cada jogador da patota (para parceiros, rivais e assistências).</param>
    /// <param name="toLocal">Converte o PlayedAt (UTC) para o horário da patota (meses).</param>
    public static PlayerRetrospectiveDto Build(
        Guid playerId,
        IEnumerable<StatMatch> units,
        IReadOnlyDictionary<Guid, string> names,
        Func<DateTime, DateTime> toLocal)
    {
        var ordered = units
            .Where(u => u.AllPlayers.Any())
            .OrderBy(u => u.PlayedAt).ThenBy(u => u.Id)
            .ToList();

        var total = ordered.Count;
        var minRates = MinGamesForRates(total);
        var agg = new Dictionary<Guid, Agg>();
        Agg Of(Guid id) => agg.TryGetValue(id, out var a) ? a : agg[id] = new Agg();
        string NameOf(Guid id) => names.TryGetValue(id, out var n) ? n : "";

        // ── Totais de todos (base dos rankings) ──────────────────────────────
        foreach (var unit in ordered)
        {
            foreach (var p in Distinct(unit))
            {
                var a = Of(p.PlayerId);
                a.Games++;
                if (p.IsGoalkeeper) a.GoalkeeperGames++;
                if (p.IsMvp) a.Mvps++;
                switch (p.Result)
                {
                    case StatResult.Win: a.Wins++; break;
                    case StatResult.Draw: a.Draws++; break;
                    case StatResult.Loss: a.Losses++; break;
                }
            }

            foreach (var voted in unit.MvpVotesFor) Of(voted).Votes++;

            foreach (var goal in unit.Goals)
            {
                if (goal.IsOwnGoal)
                {
                    if (goal.ScorerPlayerId is Guid og) Of(og).OwnGoals++;
                    continue;
                }
                if (goal.ScorerPlayerId is Guid s) Of(s).Goals++;
                if (goal.AssistPlayerId is Guid ap) Of(ap).Assists++;
            }

            var (opener, winner) = DecisiveGoals(unit);
            if (opener is Guid o) Of(o).Openers++;
            if (winner is Guid w) Of(w).WinningGoals++;
        }

        var me = Of(playerId);
        var rated = agg.Where(kv => kv.Value.Games >= minRates).ToDictionary(kv => kv.Key, kv => kv.Value);
        var withResults = agg.Where(kv => kv.Value.Results >= minRates).ToDictionary(kv => kv.Key, kv => kv.Value);

        var dto = new PlayerRetrospectiveDto
        {
            TotalGames = total,
            MinGamesForRates = minRates,
            RatedPlayers = rated.Count,
            IsGoalkeeper = me.Games > 0 && me.GoalkeeperGames * 2 > me.Games,
            OwnGoals = me.OwnGoals,
        };

        // ── Presença ─────────────────────────────────────────────────────────
        dto.Presence = new RetrospectivePresenceDto
        {
            Games = me.Games,
            Percent = total == 0 ? 0 : (int)Math.Round(100.0 * me.Games / total, MidpointRounding.AwayFromZero),
            Rank = me.Games > 0 ? Rank(agg, playerId, a => a.Games) : null,
            LongestStreak = LongestRun(ordered.Select(u => u.AllPlayers.Any(p => p.PlayerId == playerId))),
        };

        // ── Resultados ───────────────────────────────────────────────────────
        var myUnits = ordered
            .Select(u => (Unit: u, Me: u.AllPlayers.FirstOrDefault(p => p.PlayerId == playerId)))
            .Where(x => x.Me is not null)
            .Select(x => (x.Unit, Me: x.Me!))
            .ToList();
        var myResults = myUnits.Where(x => x.Me.Result != StatResult.None).ToList();

        dto.Results = new RetrospectiveResultsDto
        {
            Wins = me.Wins,
            Draws = me.Draws,
            Losses = me.Losses,
            Aproveitamento = Percent(me.Wins, me.Draws, me.Losses),
            Rank = withResults.ContainsKey(playerId) ? Rank(withResults, playerId, a => a.Aproveitamento) : null,
            LongestWinStreak = LongestRun(myResults.Select(x => x.Me.Result == StatResult.Win)),
            LongestUnbeatenStreak = LongestRun(myResults.Select(x => x.Me.Result != StatResult.Loss)),
            BestMonth = myResults
                .GroupBy(x => { var d = toLocal(x.Unit.PlayedAt); return (d.Year, d.Month); })
                .Select(g => new RetrospectiveBestMonthDto
                {
                    Year = g.Key.Year,
                    Month = g.Key.Month,
                    Wins = g.Count(x => x.Me.Result == StatResult.Win),
                    Draws = g.Count(x => x.Me.Result == StatResult.Draw),
                    Losses = g.Count(x => x.Me.Result == StatResult.Loss),
                })
                .Where(m => m.Wins + m.Draws + m.Losses >= MinGamesBestMonth)
                .Select(m => { m.Aproveitamento = Percent(m.Wins, m.Draws, m.Losses); return m; })
                .OrderByDescending(m => Ratio(m.Wins, m.Draws, m.Losses))
                .ThenByDescending(m => m.Wins + m.Draws + m.Losses)
                .ThenBy(m => m.Year).ThenBy(m => m.Month)
                .FirstOrDefault(),
        };

        // ── Gols e assistências ──────────────────────────────────────────────
        int GoalsIn(StatMatch u) => u.Goals.Count(g => !g.IsOwnGoal && g.ScorerPlayerId == playerId);
        int AssistsIn(StatMatch u) => u.Goals.Count(g => !g.IsOwnGoal && g.AssistPlayerId == playerId);

        dto.Goals = new RetrospectiveGoalsDto
        {
            Total = me.Goals,
            Rank = me.Goals > 0 ? Rank(agg, playerId, a => a.Goals) : null,
            BestNight = BestNight(myUnits.Select(x => x.Unit), GoalsIn),
        };

        var myGoals = ordered.SelectMany(u => u.Goals).Where(g => !g.IsOwnGoal).ToList();
        dto.Assists = new RetrospectiveAssistsDto
        {
            Total = me.Assists,
            Rank = me.Assists > 0 ? Rank(agg, playerId, a => a.Assists) : null,
            BestNight = BestNight(myUnits.Select(x => x.Unit), AssistsIn),
            MostAssistedTo = TopCount(myGoals.Where(g => g.AssistPlayerId == playerId && g.ScorerPlayerId is Guid s && s != playerId)
                .Select(g => g.ScorerPlayerId!.Value), NameOf),
            MostAssistedBy = TopCount(myGoals.Where(g => g.ScorerPlayerId == playerId && g.AssistPlayerId is Guid a && a != playerId)
                .Select(g => g.AssistPlayerId!.Value), NameOf),
        };

        // Meses com gol registrado na patota, na ordem.
        dto.Months = ordered
            .Where(u => u.Goals.Any(g => !g.IsOwnGoal))
            .GroupBy(u => { var d = toLocal(u.PlayedAt); return (d.Year, d.Month); })
            .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month)
            .Select(g => new RetrospectiveMonthDto
            {
                Year = g.Key.Year,
                Month = g.Key.Month,
                Goals = g.Sum(GoalsIn),
                Assists = g.Sum(AssistsIn),
            })
            .ToList();

        // ── Gols decisivos ───────────────────────────────────────────────────
        dto.Decisive = new RetrospectiveDecisiveDto
        {
            Openers = me.Openers,
            WinningGoals = me.WinningGoals,
            PerGameRank = RateRank(rated, playerId, a => a.Openers + a.WinningGoals),
        };

        // ── Parcerias (mesmo time) ───────────────────────────────────────────
        var partners = new Dictionary<Guid, Record>();
        foreach (var (unit, mine) in myUnits)
        {
            var side = unit.Sides.FirstOrDefault(s => s.Players.Any(p => p.PlayerId == playerId));
            if (side is null) continue;
            foreach (var mate in side.Players.Select(p => p.PlayerId).Distinct().Where(id => id != playerId))
            {
                var r = partners.TryGetValue(mate, out var x) ? x : partners[mate] = new Record();
                r.Games++;
                r.Add(mine.Result);
            }
        }

        dto.BestPartner = partners
            .Where(kv => kv.Value.Results >= MinGamesTogether)
            .OrderByDescending(kv => kv.Value.Ratio)
            .ThenByDescending(kv => kv.Value.Results)
            .ThenBy(kv => NameOf(kv.Key), StringComparer.OrdinalIgnoreCase)
            .Select(kv => Partner(kv.Key, kv.Value, NameOf))
            .FirstOrDefault();

        dto.FrequentPartner = partners
            .OrderByDescending(kv => kv.Value.Games)
            .ThenByDescending(kv => kv.Value.Ratio)
            .ThenBy(kv => NameOf(kv.Key), StringComparer.OrdinalIgnoreCase)
            .Select(kv => Partner(kv.Key, kv.Value, NameOf))
            .FirstOrDefault();

        // ── Rivalidades (times opostos, confronto direto com resultado) ──────
        var rivals = new Dictionary<Guid, Record>();
        foreach (var (unit, mine) in myUnits)
        {
            if (!unit.IsHeadToHead || unit.Sides.Count != 2 || mine.Result == StatResult.None) continue;
            var other = unit.Sides.FirstOrDefault(s => s.Players.All(p => p.PlayerId != playerId));
            if (other is null) continue;
            foreach (var rival in other.Players.Select(p => p.PlayerId).Distinct())
            {
                var r = rivals.TryGetValue(rival, out var x) ? x : rivals[rival] = new Record();
                r.Games++;
                r.Add(mine.Result);
            }
        }

        var eligibleRivals = rivals.Where(kv => kv.Value.Results >= MinGamesAgainst).ToList();
        dto.Fregues = eligibleRivals
            .Where(kv => kv.Value.Wins > kv.Value.Losses)
            .OrderByDescending(kv => kv.Value.Ratio)
            .ThenByDescending(kv => kv.Value.Results)
            .ThenByDescending(kv => kv.Value.Wins)
            .ThenBy(kv => NameOf(kv.Key), StringComparer.OrdinalIgnoreCase)
            .Select(kv => Rival(kv.Key, kv.Value, NameOf))
            .FirstOrDefault();
        dto.Carrasco = eligibleRivals
            .Where(kv => kv.Value.Losses > kv.Value.Wins)
            .OrderBy(kv => kv.Value.Ratio)
            .ThenByDescending(kv => kv.Value.Results)
            .ThenByDescending(kv => kv.Value.Losses)
            .ThenBy(kv => NameOf(kv.Key), StringComparer.OrdinalIgnoreCase)
            .Select(kv => Rival(kv.Key, kv.Value, NameOf))
            .FirstOrDefault();

        // ── Marcos da patota no período ──────────────────────────────────────
        dto.Milestones = Milestones(ordered, playerId);

        // ── MVP ──────────────────────────────────────────────────────────────
        dto.Mvp = new RetrospectiveMvpDto
        {
            Mvps = me.Mvps,
            Votes = me.Votes,
            Rank = me.Mvps > 0 ? Rank(agg, playerId, a => a.Mvps) : null,
        };

        // ── Perfil: o dado em que ele fica mais bem colocado ─────────────────
        var goalsPerGameRank = RateRank(rated, playerId, a => a.Goals);
        var assistsPerGameRank = RateRank(rated, playerId, a => a.Assists);
        dto.Profile = new RetrospectiveProfileDto
        {
            GoalsPerGame = me.Games == 0 ? 0 : Math.Round(me.Goals / (double)me.Games, 2),
            GoalsPerGameRank = goalsPerGameRank,
            AssistsPerGame = me.Games == 0 ? 0 : Math.Round(me.Assists / (double)me.Games, 2),
            AssistsPerGameRank = assistsPerGameRank,
            GoalkeeperGames = me.GoalkeeperGames,
        };

        if (dto.IsGoalkeeper)
        {
            dto.Profile.Key = "paredao";
        }
        else
        {
            var candidates = new (string Key, int? Rank)[]
            {
                ("fiel", dto.Presence.Rank),
                ("matador", goalsPerGameRank),
                ("garcom", assistsPerGameRank),
                ("pequente", dto.Results.Rank),
                ("craque", dto.Mvp.Rank),
                ("decisivo", dto.Decisive.PerGameRank),
            };
            var best = candidates
                .Where(c => c.Rank.HasValue)
                .OrderBy(c => c.Rank!.Value)
                .ThenBy(c => Array.IndexOf(ProfileOrder, c.Key))
                .FirstOrDefault();
            dto.Profile.Key = best.Key ?? "fiel";
            dto.Profile.Rank = best.Key is null ? dto.Presence.Rank : best.Rank;
        }

        return dto;
    }

    /// <summary>
    /// Quem abriu o placar e quem fez o gol da vitória (o gol que deixou o vencedor um à frente
    /// do placar final do perdedor). Só em confronto direto com placar e com todos os gols
    /// registrados: sem isso a ordem não é confiável. Gol contra não credita ninguém.
    /// </summary>
    public static (Guid? Opener, Guid? Winner) DecisiveGoals(StatMatch unit)
    {
        if (!unit.IsHeadToHead || !unit.HasScore || unit.Sides.Count != 2) return default;

        // GoalsAgainst de um lado = placar do outro (lado 0 = time 1).
        var score1 = unit.Sides[1].GoalsAgainst;
        var score2 = unit.Sides[0].GoalsAgainst;
        if (score1 is null || score2 is null || score1 + score2 == 0) return default;
        if (unit.Goals.Any(g => g.Side is not (0 or 1))) return default;
        if (unit.Goals.Count(g => g.Side == 0) != score1 || unit.Goals.Count(g => g.Side == 1) != score2) return default;

        var first = unit.Goals[0];
        Guid? opener = first.IsOwnGoal ? null : first.ScorerPlayerId;

        Guid? winner = null;
        if (score1 != score2)
        {
            var winSide = score1 > score2 ? 0 : 1;
            var decisive = unit.Goals.Where(g => g.Side == winSide).ElementAt(Math.Min(score1.Value, score2.Value));
            winner = decisive.IsOwnGoal ? null : decisive.ScorerPlayerId;
        }

        return (opener, winner);
    }

    private static List<RetrospectiveMilestoneDto> Milestones(IReadOnlyList<StatMatch> ordered, Guid playerId)
    {
        var list = new List<RetrospectiveMilestoneDto>();
        int goalNo = 0, assistNo = 0;
        (int Number, Guid? Scorer, DateTime At) last = default;

        foreach (var unit in ordered)
            foreach (var goal in unit.Goals.Where(g => !g.IsOwnGoal))
            {
                goalNo++;
                last = (goalNo, goal.ScorerPlayerId, unit.PlayedAt);
                if (goal.ScorerPlayerId == playerId && (goalNo == 1 || goalNo % 100 == 0))
                    list.Add(new RetrospectiveMilestoneDto { Kind = goalNo == 1 ? "first_goal" : "goal_century", Number = goalNo, PlayedAt = unit.PlayedAt });

                if (goal.AssistPlayerId is null) continue;
                assistNo++;
                if (goal.AssistPlayerId == playerId && (assistNo == 1 || assistNo % 100 == 0))
                    list.Add(new RetrospectiveMilestoneDto { Kind = assistNo == 1 ? "first_assist" : "assist_century", Number = assistNo, PlayedAt = unit.PlayedAt });
            }

        if (last.Number > 1 && last.Scorer == playerId)
            list.Add(new RetrospectiveMilestoneDto { Kind = "last_goal", Number = last.Number, PlayedAt = last.At });

        return list;
    }

    private static IEnumerable<StatPlayer> Distinct(StatMatch unit) =>
        unit.AllPlayers.GroupBy(p => p.PlayerId).Select(g => g.First());

    /// <summary>Colocação tipo competição (1, 2, 2, 4) pelo valor, entre quem está no dicionário.</summary>
    private static int Rank<T>(IReadOnlyDictionary<Guid, Agg> pool, Guid playerId, Func<Agg, T> value) where T : IComparable<T>
    {
        var mine = value(pool[playerId]);
        return 1 + pool.Values.Count(a => value(a).CompareTo(mine) > 0);
    }

    /// <summary>Posição numa taxa por jogo, entre quem tem o mínimo de jogos e algum valor.</summary>
    private static int? RateRank(IReadOnlyDictionary<Guid, Agg> rated, Guid playerId, Func<Agg, int> count)
    {
        if (!rated.TryGetValue(playerId, out var me) || count(me) == 0) return null;
        var mine = count(me) / (double)me.Games;
        return 1 + rated.Values.Count(a => count(a) / (double)a.Games > mine);
    }

    private static int LongestRun(IEnumerable<bool> seq)
    {
        int best = 0, cur = 0;
        foreach (var hit in seq)
        {
            cur = hit ? cur + 1 : 0;
            best = Math.Max(best, cur);
        }
        return best;
    }

    private static RetrospectiveNightDto? BestNight(IEnumerable<StatMatch> units, Func<StatMatch, int> count) => units
        .Select(u => (u.PlayedAt, Count: count(u)))
        .Where(x => x.Count > 0)
        .OrderByDescending(x => x.Count).ThenByDescending(x => x.PlayedAt)
        .Select(x => new RetrospectiveNightDto { Count = x.Count, PlayedAt = x.PlayedAt })
        .FirstOrDefault();

    private static RetrospectiveCountRefDto? TopCount(IEnumerable<Guid> ids, Func<Guid, string> nameOf) => ids
        .GroupBy(id => id)
        .OrderByDescending(g => g.Count())
        .ThenBy(g => nameOf(g.Key), StringComparer.OrdinalIgnoreCase)
        .Select(g => new RetrospectiveCountRefDto { PlayerId = g.Key, Name = nameOf(g.Key), Count = g.Count() })
        .FirstOrDefault();

    private static RetrospectivePartnerDto Partner(Guid id, Record r, Func<Guid, string> nameOf) => new()
    {
        PlayerId = id, Name = nameOf(id), Games = r.Games,
        Wins = r.Wins, Draws = r.Draws, Losses = r.Losses, Aproveitamento = Percent(r.Wins, r.Draws, r.Losses),
    };

    private static RetrospectiveRivalDto Rival(Guid id, Record r, Func<Guid, string> nameOf) => new()
    {
        PlayerId = id, Name = nameOf(id), Wins = r.Wins, Draws = r.Draws, Losses = r.Losses,
    };

    private static double Ratio(int wins, int draws, int losses)
    {
        var games = wins + draws + losses;
        return games == 0 ? 0 : (wins * 3 + draws) / (3.0 * games);
    }

    private static int Percent(int wins, int draws, int losses) =>
        (int)Math.Round(100 * Ratio(wins, draws, losses), MidpointRounding.AwayFromZero);

    private sealed class Agg
    {
        public int Games, GoalkeeperGames, Wins, Draws, Losses, Goals, Assists, OwnGoals, Mvps, Votes, Openers, WinningGoals;
        public int Results => Wins + Draws + Losses;
        public double Aproveitamento => Ratio(Wins, Draws, Losses);
    }

    private sealed class Record
    {
        public int Games, Wins, Draws, Losses;
        public int Results => Wins + Draws + Losses;
        public double Ratio => PlayerRetrospectiveCalculator.Ratio(Wins, Draws, Losses);

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
