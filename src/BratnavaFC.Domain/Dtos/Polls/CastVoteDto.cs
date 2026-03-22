namespace BratnavaFC.Domain.Dtos.Polls;
public sealed class CastVoteDto
{
    public List<Guid> OptionIds { get; set; } = new();
}
