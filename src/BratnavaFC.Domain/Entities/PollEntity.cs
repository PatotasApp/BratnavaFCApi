using BratnavaFC.Domain.Time;

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
    /// <summary>Whether players who confirmed attendance ("Sim") may add guests.</summary>
    public bool AllowGuests { get; private set; }
    public List<PollOptionEntity> Options { get; private set; } = new();
    public List<PollVoteEntity>   Votes   { get; private set; } = new();
    public List<PollGuestEntity>  Guests  { get; private set; } = new();

    /// <summary>Partida vinculada a esta votação/evento (opcional). Null quando não há vínculo.</summary>
    public Guid? LinkedMatchId { get; private set; }
    public MatchEntity? LinkedMatch { get; private set; }

    public void SetLinkedMatch(Guid? matchId)
    {
        LinkedMatchId = matchId;
        UpdateDate    = DateTime.UtcNow;
    }

    private PollEntity() { }

    public PollEntity(Guid groupId, string title, string? description, bool allowMultipleVotes, bool showVotes, Guid? createdByUserId, DateOnly? deadlineDate = null, TimeOnly? deadlineTime = null, string type = "poll", DateOnly? eventDate = null, TimeOnly? eventTime = null, string? eventLocation = null, string? eventIcon = null, string? costType = null, decimal? costAmount = null, bool allowGuests = false)
    {
        if (groupId == Guid.Empty) throw new InvalidOperationException("GroupId é obrigatório.");
        if (string.IsNullOrWhiteSpace(title)) throw new InvalidOperationException("Título é obrigatório.");
        GroupId            = groupId;
        Title              = title.Trim();
        Description        = description?.Trim();
        AllowMultipleVotes = allowMultipleVotes;
        ShowVotes          = showVotes;
        CreatedByUserId    = createdByUserId;
        DeadlineDate       = deadlineDate;
        DeadlineTime       = deadlineTime;
        Type               = type;
        EventDate          = eventDate;
        EventTime          = eventTime;
        EventLocation      = eventLocation;
        EventIcon          = eventIcon;
        CostType           = costType;
        CostAmount         = costAmount;
        AllowGuests        = allowGuests;
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

    /// <summary>Substitui integralmente o prazo (incluindo null para remover).</summary>
    public void SetDeadline(DateOnly? deadlineDate, TimeOnly? deadlineTime)
    {
        DeadlineDate = deadlineDate;
        DeadlineTime = deadlineTime;
        UpdateDate   = DateTime.UtcNow;
    }

    public void SetAllowGuests(bool allow) { AllowGuests = allow; UpdateDate = DateTime.UtcNow; }

    /// <summary>
    /// Updates description and, for event polls, cost fields.
    /// Pass null to leave a field unchanged; pass "" to clear it.
    /// </summary>
    public void UpdateDetails(
        string? title,
        string? description,
        DateOnly? eventDate,
        bool clearEventDate,
        TimeOnly? eventTime,
        bool clearEventTime,
        string? eventLocation,
        string? eventIcon,
        decimal? costAmount,
        string? costType)
    {
        if (title is not null)
        {
            var trimmed = title.Trim();
            if (trimmed == string.Empty) throw new InvalidOperationException("Título é obrigatório.");
            Title = trimmed;
        }

        if (description is not null)
            Description = description.Trim() == string.Empty ? null : description.Trim();

        if (Type == "event")
        {
            if (clearEventDate) EventDate = null;
            else if (eventDate.HasValue) EventDate = eventDate.Value;

            if (clearEventTime) EventTime = null;
            else if (eventTime.HasValue) EventTime = eventTime.Value;

            if (eventLocation is not null)
                EventLocation = eventLocation.Trim() == string.Empty ? null : eventLocation.Trim();

            if (eventIcon is not null)
                EventIcon = eventIcon.Trim() == string.Empty ? null : eventIcon.Trim();
        }

        if (costAmount.HasValue)
            CostAmount = costAmount.Value <= 0 ? null : costAmount;

        if (costType is not null)
            CostType = costType.Trim() == string.Empty ? null : costType.Trim();

        UpdateDate = DateTime.UtcNow;
    }

    public void Close() { Status = "closed"; UpdateDate = DateTime.UtcNow; }
    public void Reopen() { Status = "open"; UpdateDate = DateTime.UtcNow; }

    // ── Domain rules ──────────────────────────────────────────────────────────

    /// <summary>Returns true when this poll was created as an event poll (Type == "event").</summary>
    public bool IsEventType() => Type == "event";

    /// <summary>Returns true when the voting deadline has already passed.</summary>
    public bool HasExpiredDeadline()
    {
        if (!DeadlineDate.HasValue) return false;
        var localDt = DeadlineDate.Value.ToDateTime(DeadlineTime ?? TimeOnly.MaxValue);
        var deadlineUtc = BratnavaDateTime.SaoPauloLocalToUtc(localDt);
        return DateTime.UtcNow > deadlineUtc;
    }

    /// <summary>Validates whether adding/removing a guest is currently allowed.</summary>
    public string? ValidateGuestChange()
    {
        if (!AllowGuests) return "Este evento não permite convidados.";
        if (Status == "closed") return "Este evento está encerrado.";
        if (HasExpiredDeadline()) return "O prazo deste evento já encerrou.";
        return null;
    }

    /// <summary>
    /// Validates whether a vote change (cast or remove) is currently allowed.
    /// Returns an error message, or null when the operation is valid.
    /// </summary>
    public string? ValidateVoteChange()
    {
        if (Status == "closed") return "Esta votação está encerrada.";
        if (HasExpiredDeadline()) return "O prazo desta votação já encerrou.";
        return null;
    }

    /// <summary>
    /// Validates a full vote submission (includes single/multiple-vote rule).
    /// Returns an error message, or null when the submission is valid.
    /// </summary>
    public string? ValidateVote(int optionCount)
    {
        var baseError = ValidateVoteChange();
        if (baseError is not null) return baseError;
        if (!AllowMultipleVotes && optionCount > 1) return "Esta votação permite apenas uma opção.";
        return null;
    }
}
