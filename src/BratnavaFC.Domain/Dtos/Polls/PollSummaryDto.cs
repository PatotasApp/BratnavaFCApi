namespace BratnavaFC.Domain.Dtos.Polls;
public sealed class PollSummaryDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public bool AllowMultipleVotes { get; set; }
    public bool ShowVotes { get; set; }
    public string Status { get; set; } = "open";
    public string? DeadlineDate { get; set; }
    public string? DeadlineTime { get; set; }
    public string Type { get; set; } = "poll";
    public string? EventDate { get; set; }
    public string? EventTime { get; set; }
    public string? EventLocation { get; set; }
    public string? EventIcon { get; set; }
    public string? CostType { get; set; }
    public decimal? CostAmount { get; set; }
    public int OptionCount { get; set; }
    public int TotalVoters { get; set; }
    public bool HasVoted { get; set; }
    public DateTime CreateDate { get; set; }
    public Guid? LinkedMatchId { get; set; }
}
