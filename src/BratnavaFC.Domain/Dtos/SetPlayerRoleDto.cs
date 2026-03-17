namespace BratnavaFC.Domain.Dtos;

public sealed class SetPlayerRoleDto
{
    /// <summary>true = goleiro | false = linha</summary>
    public bool IsGoalkeeper { get; set; }
}
