using BratnavaFC.Domain.Enums;

namespace BratnavaFC.Domain.Entities;

/// <summary>Participação de um jogador no campeonato do dia (presença, time, MVP).</summary>
public class ChampionshipPlayerEntity : BaseEntity
{
    private ChampionshipPlayerEntity() { } // EF

    public ChampionshipPlayerEntity(Guid championshipId, PlayerEntity player)
    {
        ArgumentNullException.ThrowIfNull(player);
        if (championshipId == Guid.Empty) throw new InvalidOperationException("ChampionshipId e obrigatorio.");

        ChampionshipId = championshipId;
        PlayerId = player.Id;
        Player = player;
        IsGoalkeeper = player.IsGoalkeeper;
    }

    public Guid ChampionshipId { get; private set; }
    public Guid PlayerId { get; private set; }
    public PlayerEntity? Player { get; private set; }

    public InviteResponse InviteResponse { get; private set; } = InviteResponse.None;
    public Guid? TeamId { get; private set; }
    public bool IsGoalkeeper { get; private set; }

    /// <summary>Confirmou e não apareceu: não entra em time nem em estatística.</summary>
    public bool DidNotPlay { get; private set; }

    /// <summary>MVP do campeonato (quando a regra é 1 MVP por campeonato).</summary>
    public bool IsMvp { get; private set; }

    internal void Accept() => InviteResponse = InviteResponse.Accepted;
    internal void Reject()
    {
        InviteResponse = InviteResponse.Rejected;
        TeamId = null;
    }

    internal void AssignTeam(Guid? teamId, bool isGoalkeeper)
    {
        TeamId = teamId;
        IsGoalkeeper = isGoalkeeper;
    }

    internal void SetMvp(bool value) => IsMvp = value;
}
