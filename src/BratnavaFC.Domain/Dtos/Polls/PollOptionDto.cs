namespace BratnavaFC.Domain.Dtos.Polls;
public sealed class PollOptionDto
{
    public Guid Id { get; set; }
    public string Text { get; set; } = "";
    public string? Description { get; set; }
    public string? ImageUrl { get; set; }
    public int SortOrder { get; set; }
    public int VoteCount { get; set; }
}
