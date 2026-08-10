using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BratnavaFC.Domain.Dtos
{
    public sealed record MyPlayerDto(
        Guid PlayerId,
        Guid? UserId,
        Guid GroupId,
        string PlayerName,
        bool IsGoalkeeper,
        decimal SkillPoints,
        BratnavaFC.Domain.Enums.Status Status,
        string GroupName,
        bool IsGuest,
        string? PhotoUrl,
        string? GroupLogoUrl
    );
}
