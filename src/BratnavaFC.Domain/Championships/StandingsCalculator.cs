namespace BratnavaFC.Domain.Championships;

public sealed record TeamStanding(
    int Position,
    Guid TeamId,
    int Played,
    int Wins,
    int Draws,
    int Losses,
    int GoalsFor,
    int GoalsAgainst,
    int GoalDifference,
    int Points,
    decimal PointsPerGame,
    decimal ClassificationPoints);

public static class StandingsCalculator
{
    public static IReadOnlyList<TeamStanding> Calculate(
        IEnumerable<Guid> teamIds,
        IEnumerable<ChampionshipMatch> matches,
        StandingsRules rules,
        IEnumerable<Guid>? externalDecisionOrder = null)
    {
        var teams = ChampionshipGuard.EnsureTeams(teamIds);
        ArgumentNullException.ThrowIfNull(matches);
        ArgumentNullException.ThrowIfNull(rules);
        var games = matches.ToArray();
        var teamSet = teams.ToHashSet();
        if (games.Any(game => !teamSet.Contains(game.TeamAId) || !teamSet.Contains(game.TeamBId)))
            throw new InvalidOperationException("A classificacao possui jogo de um time que nao participa do campeonato.");

        var externalOrder = BuildExternalOrder(externalDecisionOrder, teamSet);
        var stats = teams.Select((teamId, originalIndex) =>
                new MutableStanding(teamId, originalIndex))
            .ToDictionary(item => item.TeamId);

        foreach (var game in games)
            ApplyGame(stats[game.TeamAId], stats[game.TeamBId], game, rules);

        var rankedGroups = RankGroup(
            teams.Select(teamId => stats[teamId]).ToList(),
            criterionIndex: 0,
            rules,
            games,
            externalOrder);

        var result = new List<TeamStanding>(teams.Length);
        var teamsBeforeGroup = 0;
        foreach (var group in rankedGroups)
        {
            var position = teamsBeforeGroup + 1;
            foreach (var item in group.OrderBy(value => value.OriginalIndex))
            {
                var pointsPerGame = item.Played == 0 ? 0m : (decimal)item.Points / item.Played;
                result.Add(new TeamStanding(
                    position,
                    item.TeamId,
                    item.Played,
                    item.Wins,
                    item.Draws,
                    item.Losses,
                    item.GoalsFor,
                    item.GoalsAgainst,
                    item.GoalsFor - item.GoalsAgainst,
                    item.Points,
                    pointsPerGame,
                    rules.Mode == StandingMode.TotalPoints ? item.Points : pointsPerGame));
            }

            teamsBeforeGroup += group.Count;
        }

        return result.AsReadOnly();
    }

    private static List<List<MutableStanding>> RankGroup(
        List<MutableStanding> group,
        int criterionIndex,
        StandingsRules rules,
        IReadOnlyList<ChampionshipMatch> games,
        IReadOnlyDictionary<Guid, int> externalOrder)
    {
        if (group.Count <= 1 || criterionIndex >= rules.Tiebreakers.Count)
            return new List<List<MutableStanding>> { group };

        var criterion = rules.Tiebreakers[criterionIndex];
        var values = GetCriterionValues(group, criterion, rules, games, externalOrder);
        var separatedGroups = group
            .GroupBy(item => values[item.TeamId])
            .OrderByDescending(items => items.Key)
            .Select(items => items.OrderBy(item => item.OriginalIndex).ToList())
            .ToArray();

        var result = new List<List<MutableStanding>>();
        foreach (var separatedGroup in separatedGroups)
            result.AddRange(RankGroup(
                separatedGroup,
                criterionIndex + 1,
                rules,
                games,
                externalOrder));

        return result;
    }

    private static IReadOnlyDictionary<Guid, decimal> GetCriterionValues(
        IReadOnlyList<MutableStanding> group,
        TiebreakerCriteria criterion,
        StandingsRules rules,
        IReadOnlyList<ChampionshipMatch> games,
        IReadOnlyDictionary<Guid, int> externalOrder)
    {
        return criterion switch
        {
            TiebreakerCriteria.Points => group.ToDictionary(
                item => item.TeamId,
                item => rules.Mode == StandingMode.TotalPoints
                    ? item.Points
                    : item.Played == 0 ? 0m : (decimal)item.Points / item.Played),
            TiebreakerCriteria.GoalDifference => group.ToDictionary(
                item => item.TeamId,
                item => (decimal)(item.GoalsFor - item.GoalsAgainst)),
            TiebreakerCriteria.GoalsFor => group.ToDictionary(
                item => item.TeamId,
                item => (decimal)item.GoalsFor),
            TiebreakerCriteria.HeadToHead => GetHeadToHeadValues(group, rules, games),
            TiebreakerCriteria.ExternalDecision => group.ToDictionary(
                item => item.TeamId,
                item => externalOrder.TryGetValue(item.TeamId, out var index) ? (decimal)-index : decimal.MinValue),
            _ => throw new InvalidOperationException("Criterio de classificacao invalido.")
        };
    }

    private static IReadOnlyDictionary<Guid, decimal> GetHeadToHeadValues(
        IReadOnlyList<MutableStanding> group,
        StandingsRules rules,
        IReadOnlyList<ChampionshipMatch> games)
    {
        var tiedTeamIds = group.Select(item => item.TeamId).ToHashSet();
        var headToHead = group.ToDictionary(
            item => item.TeamId,
            item => new MutableStanding(item.TeamId, item.OriginalIndex));

        foreach (var game in games.Where(game =>
                     tiedTeamIds.Contains(game.TeamAId) && tiedTeamIds.Contains(game.TeamBId)))
            ApplyGame(headToHead[game.TeamAId], headToHead[game.TeamBId], game, rules);

        return headToHead.ToDictionary(
            item => item.Key,
            item => rules.Mode == StandingMode.TotalPoints
                ? (decimal)item.Value.Points
                : item.Value.Played == 0 ? 0m : (decimal)item.Value.Points / item.Value.Played);
    }

    private static Dictionary<Guid, int> BuildExternalOrder(
        IEnumerable<Guid>? externalDecisionOrder,
        IReadOnlySet<Guid> teamIds)
    {
        if (externalDecisionOrder is null)
            return new Dictionary<Guid, int>();

        var orderedTeams = externalDecisionOrder.ToArray();
        if (orderedTeams.Distinct().Count() != orderedTeams.Length)
            throw new InvalidOperationException("A ordem externa nao pode repetir times.");
        if (orderedTeams.Any(teamId => !teamIds.Contains(teamId)))
            throw new InvalidOperationException("A ordem externa possui um time que nao participa do campeonato.");

        return orderedTeams.Select((teamId, index) => (teamId, index))
            .ToDictionary(item => item.teamId, item => item.index);
    }

    private static void ApplyGame(
        MutableStanding teamA,
        MutableStanding teamB,
        ChampionshipMatch game,
        StandingsRules rules)
    {
        teamA.Played++;
        teamB.Played++;
        teamA.GoalsFor += game.TeamAGoals;
        teamA.GoalsAgainst += game.TeamBGoals;
        teamB.GoalsFor += game.TeamBGoals;
        teamB.GoalsAgainst += game.TeamAGoals;

        if (game.TeamAGoals > game.TeamBGoals)
        {
            teamA.Wins++;
            teamA.Points += rules.WinPoints;
            teamB.Losses++;
        }
        else if (game.TeamBGoals > game.TeamAGoals)
        {
            teamB.Wins++;
            teamB.Points += rules.WinPoints;
            teamA.Losses++;
        }
        else
        {
            teamA.Draws++;
            teamB.Draws++;
            teamA.Points += rules.DrawPoints;
            teamB.Points += rules.DrawPoints;
        }
    }

    private sealed class MutableStanding(Guid teamId, int originalIndex)
    {
        public Guid TeamId { get; } = teamId;
        public int OriginalIndex { get; } = originalIndex;
        public int Played { get; set; }
        public int Wins { get; set; }
        public int Draws { get; set; }
        public int Losses { get; set; }
        public int GoalsFor { get; set; }
        public int GoalsAgainst { get; set; }
        public int Points { get; set; }
    }
}
