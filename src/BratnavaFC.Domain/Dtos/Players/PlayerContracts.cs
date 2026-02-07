using System;
using BratnavaFC.Domain.Enums;

namespace BratnavaFC.Domain.Dtos.Players;

public class PlayerContracts
{
    public record CreatePlayerRequest(string Name, Guid UserId, Guid GroupId, decimal SkillPoints, Status Status);
    public record UpdatePlayerRequest(string Name, decimal SkillPoints, Status Status);
    public record DeletePlayerRequest(Guid PlayerId);
    public record GetResponse(Guid Id, string Name, Guid UserId, Guid GroupId, decimal SkillPoints, Status Status);
}
