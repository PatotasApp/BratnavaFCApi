using BratnavaFC.Domain.Dtos;

public sealed class MatchDetailsDto
{
    public Guid MatchId { get; set; }
    public Guid GroupId { get; set; }
    public string GroupName { get; set; } = "";
    public DateTime PlayedAt { get; set; }
    public string PlaceName { get; set; } = "";

    public short Status { get; set; }
    public string StatusName { get; set; } = "";

    public int? TeamAGoals { get; set; }
    public int? TeamBGoals { get; set; }

    public TeamColorDto? TeamAColor { get; set; }
    public TeamColorDto? TeamBColor { get; set; }

    public List<MatchMvpDto> ComputedMvps { get; set; } = [];

    public List<PlayerInMatchDto> TeamAPlayers { get; set; } = [];
    public List<PlayerInMatchDto> TeamBPlayers { get; set; } = [];
    public List<PlayerInMatchDto> UnassignedPlayers { get; set; } = [];

    public List<VoteDto> Votes { get; set; } = [];
    public List<VoteCountDto> VoteCounts { get; set; } = [];

    public List<GoalDto> Goals { get; set; } = [];


}

public sealed class TeamColorDto
{
    public Guid Id { get; set; }
    public Guid GroupId { get; set; }
    public bool IsActive { get; set; }
    public string Name { get; set; } = string.Empty;
    public string HexValue { get; set; } = string.Empty;
}


public sealed class MatchMvpDto
{
    public Guid MatchPlayerId { get; set; }   // MVP e um MatchPlayer (pelo teu metodo)
    public Guid PlayerId { get; set; }
    public string PlayerName { get; set; } = "";
    public short Team { get; set; }
}

public sealed class PlayerInMatchDto
{
    public Guid MatchPlayerId { get; set; }
    public Guid PlayerId { get; set; }
    public string PlayerName { get; set; } = "";
    public bool IsGoalkeeper { get; set; }
    public bool IsGuest { get; set; }
    public short Team { get; set; }
    public short InviteResponse { get; set; } // enum convertido (Accepted/Rejected/None)
    public bool IsMvp { get; set; }

    /// <summary>Preenchido apenas quando a rejeição foi automática (BackgroundService). Null = rejeição manual.</summary>
    public int? AbsenceType { get; set; }
    /// <summary>Ex: "Viagem - Férias em SP" ou "Viagem". Null quando rejeição manual.</summary>
    public string? AbsenceDescription { get; set; }
}

public sealed class VoteDto
{
    public Guid VoteId { get; set; }
    public Guid VoterMatchPlayerId { get; set; }
    public Guid VotedForMatchPlayerId { get; set; }

    public string VoterName { get; set; } = "";
    public string VotedForName { get; set; } = "";
}

public sealed class VoteCountDto
{
    public Guid VotedForMatchPlayerId { get; set; }
    public string VotedForName { get; set; } = "";
    public int Count { get; set; }
}
