namespace BratnavaFC.Domain.Dtos.Polls;
public sealed class AdminCastVoteDto
{
    public Guid PlayerId { get; set; }
    public List<Guid> OptionIds { get; set; } = new(); // lista vazia = remover voto
}
