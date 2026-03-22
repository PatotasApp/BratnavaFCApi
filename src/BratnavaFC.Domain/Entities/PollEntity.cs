namespace BratnavaFC.Domain.Entities;
public class PollEntity : BaseEntity
{
    public Guid GroupId { get; private set; }
    public string Title { get; private set; } = null!;
    public string? Description { get; private set; }
    public bool AllowMultipleVotes { get; private set; }
    public bool ShowVotes { get; private set; }
    public string Status { get; private set; } = "open"; // open/closed
    public Guid? CreatedByUserId { get; private set; }
    public DateOnly? DeadlineDate { get; private set; }
    public TimeOnly? DeadlineTime { get; private set; }
    public string Type { get; private set; } = "poll"; // "poll" | "event"
    public DateOnly? EventDate { get; private set; }
    public TimeOnly? EventTime { get; private set; }
    public string? EventLocation { get; private set; }
    public string? EventIcon { get; private set; }
    public string? CostType { get; private set; }   // null | "individual" | "group"
    public decimal? CostAmount { get; private set; }
    public List<PollOptionEntity> Options { get; private set; } = new();
    public List<PollVoteEntity> Votes { get; private set; } = new();

    private PollEntity() { }

    public PollEntity(Guid groupId, string title, string? description, bool allowMultipleVotes, bool showVotes, Guid? createdByUserId, DateOnly? deadlineDate = null, TimeOnly? deadlineTime = null, string type = "poll", DateOnly? eventDate = null, TimeOnly? eventTime = null, string? eventLocation = null, string? eventIcon = null, string? costType = null, decimal? costAmount = null)
    {
        if (groupId == Guid.Empty) throw new InvalidOperationException("GroupId é obrigatório.");
        if (string.IsNullOrWhiteSpace(title)) throw new InvalidOperationException("Título é obrigatório.");
        GroupId = groupId;
        Title = title.Trim();
        Description = description?.Trim();
        AllowMultipleVotes = allowMultipleVotes;
        ShowVotes = showVotes;
        CreatedByUserId = createdByUserId;
        DeadlineDate = deadlineDate;
        DeadlineTime = deadlineTime;
        Type = type;
        EventDate = eventDate;
        EventTime = eventTime;
        EventLocation = eventLocation;
        EventIcon = eventIcon;
        CostType = costType;
        CostAmount = costAmount;
    }

    public void Update(string? title, string? description, bool? allowMultipleVotes, bool? showVotes, DateOnly? deadlineDate, TimeOnly? deadlineTime, bool clearDeadline = false)
    {
        if (title is not null) Title = title.Trim();
        if (description is not null) Description = description.Trim();
        if (allowMultipleVotes.HasValue) AllowMultipleVotes = allowMultipleVotes.Value;
        if (showVotes.HasValue) ShowVotes = showVotes.Value;
        if (clearDeadline) { DeadlineDate = null; DeadlineTime = null; }
        else { if (deadlineDate.HasValue) DeadlineDate = deadlineDate; if (deadlineTime.HasValue) DeadlineTime = deadlineTime; }
        UpdateDate = DateTime.UtcNow;
    }

    public void Close() { Status = "closed"; UpdateDate = DateTime.UtcNow; }
    public void Reopen() { Status = "open"; UpdateDate = DateTime.UtcNow; }
}
