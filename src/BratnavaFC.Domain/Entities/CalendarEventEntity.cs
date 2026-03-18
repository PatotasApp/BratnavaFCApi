namespace BratnavaFC.Domain.Entities;

public class CalendarEventEntity : BaseEntity
{
    public Guid GroupId { get; private set; }
    public Guid? CategoryId { get; private set; }
    public CalendarCategoryEntity? Category { get; private set; }
    public string Title { get; private set; } = null!;
    public string? Description { get; private set; }
    public DateOnly EventDate { get; private set; }
    public TimeOnly? EventTime { get; private set; }
    public bool TimeTBD { get; private set; }       // horário em aberto / a confirmar
    public Guid? CreatedByUserId { get; private set; }

    // EF
    private CalendarEventEntity() { }

    public CalendarEventEntity(
        Guid groupId, string title, string? description,
        Guid? categoryId, DateOnly eventDate, TimeOnly? eventTime,
        bool timeTbd, Guid? createdByUserId)
    {
        SetGroup(groupId);
        SetTitle(title);
        Description = description?.Trim();
        CategoryId = categoryId;
        EventDate = eventDate;
        EventTime = timeTbd ? null : eventTime;
        TimeTBD = timeTbd;
        CreatedByUserId = createdByUserId;
    }

    public void Update(
        string? title, string? description,
        Guid? categoryId, DateOnly? eventDate, TimeOnly? eventTime,
        bool? timeTbd)
    {
        if (title is not null) SetTitle(title);
        if (description is not null) Description = description.Trim();
        if (categoryId.HasValue) CategoryId = categoryId;
        if (eventDate.HasValue) EventDate = eventDate.Value;

        var newTimeTbd = timeTbd ?? TimeTBD;
        TimeTBD = newTimeTbd;
        EventTime = newTimeTbd ? null : (eventTime ?? EventTime);
    }

    private void SetTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new InvalidOperationException("Event title is required.");
        Title = title.Trim();
    }

    private void SetGroup(Guid groupId)
    {
        if (groupId == Guid.Empty)
            throw new InvalidOperationException("GroupId is required.");
        GroupId = groupId;
    }
}
