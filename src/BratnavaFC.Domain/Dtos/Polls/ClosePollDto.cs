namespace BratnavaFC.Domain.Dtos.Polls;

/// <summary>
/// Sent when an admin closes a poll. Optionally creates a calendar event.
/// </summary>
public sealed class ClosePollDto
{
    public bool CreateEvent { get; set; }
    public string? EventTitle { get; set; }
    public string? EventDescription { get; set; }
    public string? EventDate { get; set; }   // yyyy-MM-dd  (required when CreateEvent=true)
    public string? EventTime { get; set; }   // HH:mm
    public string? EventIcon { get; set; }
    public string? CategoryId { get; set; }
    public string? CostType { get; set; }    // "individual" | "group"
    public decimal? CostAmount { get; set; }
}
