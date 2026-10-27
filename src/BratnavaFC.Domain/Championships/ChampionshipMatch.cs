namespace BratnavaFC.Domain.Championships;

public sealed record ChampionshipMatch
{
    public ChampionshipMatch(
        Guid teamAId,
        Guid teamBId,
        int teamAGoals,
        int teamBGoals,
        Guid? externalWinnerId = null)
    {
        ChampionshipGuard.EnsureDifferentTeams(teamAId, teamBId);
        if (teamAGoals < 0 || teamBGoals < 0)
            throw new InvalidOperationException("Placar nao pode ser negativo.");
        if (externalWinnerId.HasValue &&
            externalWinnerId != teamAId && externalWinnerId != teamBId)
            throw new InvalidOperationException("O vencedor externo deve ser um dos times do jogo.");
        if (teamAGoals != teamBGoals && externalWinnerId.HasValue)
            throw new InvalidOperationException("O vencedor externo so pode ser informado em caso de empate.");

        TeamAId = teamAId;
        TeamBId = teamBId;
        TeamAGoals = teamAGoals;
        TeamBGoals = teamBGoals;
        ExternalWinnerId = externalWinnerId;
    }

    public Guid TeamAId { get; }
    public Guid TeamBId { get; }
    public int TeamAGoals { get; }
    public int TeamBGoals { get; }
    public Guid? ExternalWinnerId { get; }
    public bool IsDraw => TeamAGoals == TeamBGoals;

    public Guid? WinnerId => TeamAGoals > TeamBGoals
        ? TeamAId
        : TeamBGoals > TeamAGoals
            ? TeamBId
            : ExternalWinnerId;

    public Guid? LoserId => WinnerId is null
        ? null
        : WinnerId == TeamAId ? TeamBId : TeamAId;
}

internal static class ChampionshipGuard
{
    public static Guid[] EnsureTeams(IEnumerable<Guid> teamIds, int minimumCount = 2)
    {
        ArgumentNullException.ThrowIfNull(teamIds);
        var teams = teamIds.ToArray();
        if (teams.Length < minimumCount)
            throw new InvalidOperationException($"Informe ao menos {minimumCount} times.");
        if (teams.Any(id => id == Guid.Empty))
            throw new InvalidOperationException("O Id do time e obrigatorio.");
        if (teams.Distinct().Count() != teams.Length)
            throw new InvalidOperationException("Os times nao podem se repetir.");

        return teams;
    }

    public static void EnsureDifferentTeams(Guid teamAId, Guid teamBId)
    {
        if (teamAId == Guid.Empty || teamBId == Guid.Empty)
            throw new InvalidOperationException("O Id do time e obrigatorio.");
        if (teamAId == teamBId)
            throw new InvalidOperationException("Um time nao pode jogar contra ele mesmo.");
    }
}
