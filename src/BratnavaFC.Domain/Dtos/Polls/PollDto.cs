namespace BratnavaFC.Domain.Dtos.Polls;
public sealed class PollDto
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
    public DateTime CreateDate { get; set; }
    public List<PollOptionDto> Options { get; set; } = new();
    public List<PollVoteDto>? Votes { get; set; } // null if ShowVotes=false and not admin
    public List<Guid> MyVotedOptionIds { get; set; } = new();
    public int TotalVoters { get; set; }
    public List<PollMemberVoteDto>? Members { get; set; } // somente para admins: todos os membros com seus votos
    public Guid? LinkedMatchId { get; set; }
}
