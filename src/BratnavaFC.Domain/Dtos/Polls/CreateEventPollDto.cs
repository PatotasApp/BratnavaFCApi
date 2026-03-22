namespace BratnavaFC.Domain.Dtos.Polls;
public sealed class CreateEventPollDto
{
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public bool ShowVotes { get; set; } = true;
    public string EventDate { get; set; } = ""; // yyyy-MM-dd required
    public string? EventTime { get; set; }       // HH:mm
    public string? EventLocation { get; set; }
    public string? EventIcon { get; set; }
    public string? CostType { get; set; }
    public decimal? CostAmount { get; set; }
    public string? DeadlineDate { get; set; }
    public string? DeadlineTime { get; set; }
}
