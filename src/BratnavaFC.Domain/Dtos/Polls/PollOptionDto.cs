namespace BratnavaFC.Domain.Dtos.Polls;

public sealed class PollOptionDto
{
    public Guid Id { get; set; }
    public string Text { get; set; } = "";
    public string? Description { get; set; }
    public List<string> Images { get; set; } = new();
    public int SortOrder { get; set; }
    public int VoteCount { get; set; }
}
