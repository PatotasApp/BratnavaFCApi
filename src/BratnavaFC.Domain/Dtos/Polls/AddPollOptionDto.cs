namespace BratnavaFC.Domain.Dtos.Polls;
public sealed class AddPollOptionDto
{
    public string Text { get; set; } = "";
    public string? Description { get; set; }
    public string? ImageUrl { get; set; }
}
