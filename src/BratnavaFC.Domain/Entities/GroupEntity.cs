using System;
using BratnavaFC.Domain.Enums;

namespace BratnavaFC.Domain.Entities;

public class GroupEntity : BaseEntity
{
    public string Name { get; set; }
    public DateTimeOffset? ScheduleMatchDate { get; set; }
    public List<PlayerEntity>? Players { get; set; }
    public List<GroupAdminEntity> Admins { get; set; } = [];
    public Status Status { get; set; }

    public void Activate() => Status = Status.Active;
    public void Deactivate() => Status = Status.Inactive;

    public void AddPlayer(PlayerEntity player)
    {
        ArgumentNullException.ThrowIfNull(player);

        if (Players!.Exists(x => x.Id == player.Id))
            throw new InvalidOperationException("Player already exists in the group.");

        player.GroupId = Id;
        Players.Add(player);
    }

    public void DeactivatePlayer(Guid playerId)
    {
        var player = Players!.FirstOrDefault(x => x.Id == playerId);

        if (player == null)
            throw new InvalidOperationException("Player does not exist in the group.");

        player.Status = Status.Inactive;
    }

    public void ActivatePlayer(Guid playerId)
    {
        var player = Players!.FirstOrDefault(x => x.Id == playerId);

        if (player == null)
            throw new InvalidOperationException("Player does not exist in the group.");

        player.Status = Status.Active;
    }

    public void RemovePlayer(Guid playerId)
    {
        var player = Players!.FirstOrDefault(x => x.Id == playerId);

        if (player == null)
            throw new InvalidOperationException("Player does not exist in the group.");

        Players!.Remove(player);
    }
}
