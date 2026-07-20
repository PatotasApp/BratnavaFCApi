using BratnavaFC.Domain.Enums;

namespace BratnavaFC.Domain.Entities;

public class MatchPlayerEntity : BaseEntity
{
    private MatchPlayerEntity() { }

    public MatchPlayerEntity(Guid playerId)
    {
        if (playerId == Guid.Empty) throw new InvalidOperationException("PlayerId e obrigatorio.");
        PlayerId = playerId;
        Team = 0; 
    }

    public Guid PlayerId { get; private set; }
    public PlayerEntity? Player { get; private set; }

    public Guid GroupId { get; private set; }
    public GroupEntity? Group { get; private set; }

    public bool? IsMvp { get; private set; }

    public Guid MatchId { get; private set; }
    public MatchEntity? Match { get; private set; }

    public short Team { get; private set; }

    public List<VoteEntity> ReceivedVotes { get; private set; } = new();
    public Guid? VotedForId { get; private set; }

    public InviteResponse InviteResponse { get; private set; } = InviteResponse.None;
    public DateTime? InviteRespondedAt { get; private set; }

    public void AcceptInvite()
    {
        InviteResponse = InviteResponse.Accepted;
        InviteRespondedAt = DateTime.UtcNow;
    }

    public void RejectInvite()
    {
        InviteResponse = InviteResponse.Rejected;
        InviteRespondedAt = DateTime.UtcNow;
    }

    /// <summary>Restaura uma resposta salva (usado ao recriar MatchPlayers em re-sync/rewind).</summary>
    public void RestoreInviteResponse(InviteResponse response, DateTime? respondedAt = null)
    {
        InviteResponse = response;
        InviteRespondedAt = response == InviteResponse.None ? null : respondedAt;
    }

    public Guid? AutoRejectedByAbsenceId { get; private set; }
    public UserAbsenceEntity? AutoRejectedByAbsence { get; private set; }

    public void AutoRejectByAbsence(Guid absenceId, DateTime? respondedAt = null)
    {
        if (absenceId == Guid.Empty) throw new InvalidOperationException("AbsenceId é obrigatório.");
        InviteResponse = InviteResponse.Rejected;
        InviteRespondedAt = respondedAt ?? DateTime.UtcNow;
        AutoRejectedByAbsenceId = absenceId;
    }

    /// <summary>Reverte uma auto-rejeição por ausência, voltando o convite para pendente.</summary>
    public void ClearAutoRejection()
    {
        AutoRejectedByAbsenceId = null;
        AutoRejectedByAbsence   = null;
        InviteResponse          = InviteResponse.None;
        InviteRespondedAt       = null;
    }

    public List<GoalEntity> GoalsScored { get; private set; } = new();
    public List<GoalEntity> GoalsAssisted { get; private set; } = new();

    public void SetMvp() => IsMvp = true;
    public void RevokeMvp() => IsMvp = null;

    public void AssignToMatch(MatchEntity match)
    {
        Match = match ?? throw new ArgumentNullException(nameof(match));
        MatchId = match.Id;

        AssignGroup(match.GroupId);
    }

    public bool IsGoalkeeper { get; private set; }
    public void SetIsGoalkeeper(bool isGoalkeeper) => IsGoalkeeper = isGoalkeeper;

    public void AssignToPlayer(PlayerEntity player)
    {
        Player = player ?? throw new ArgumentNullException(nameof(player));
        PlayerId = player.Id;
        IsGoalkeeper = player.IsGoalkeeper;

        if (GroupId != Guid.Empty && player.GroupId != GroupId)
            throw new InvalidOperationException("Player nao pertence ao Group do MatchPlayer.");
    }

    public void AssignGroup(Guid groupId)
    {
        if (groupId == Guid.Empty)
            throw new InvalidOperationException("GroupId e obrigatorio.");

        GroupId = groupId;
    }

    public void SetTeam(short team)
    {
        if (team != 0 && team != 1 && team != 2)
            throw new ArgumentOutOfRangeException(nameof(team), "Team deve ser 0 (Unassigned), 1 (Time A) ou 2 (Time B).");

        Team = team;
    }

    public void AddReceivedVote(VoteEntity vote)
    {
        ArgumentNullException.ThrowIfNull(vote);

        if (!ReceivedVotes.Exists(v => v.Id == vote.Id))
            ReceivedVotes.Add(vote);
    }

    public void RemoveReceivedVote(Guid voteId)
    {
        var idx = ReceivedVotes.FindIndex(v => v.Id == voteId);
        if (idx >= 0) ReceivedVotes.RemoveAt(idx);
    }

    public void SetVotedFor(Guid? votedForId) => VotedForId = votedForId;
    public void ClearVotedFor() => VotedForId = null;

    /// <summary>
    /// Jogador confirmou presença mas não apareceu (ou foi removido pelo admin).
    /// Não conta para estatísticas: vitória/derrota/empate, presença, gols, MVP.
    /// </summary>
    public bool DidNotPlay { get; private set; }
    public void SetDidNotPlay(bool value) => DidNotPlay = value;
}
