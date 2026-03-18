namespace BratnavaFC.Domain.Dtos.Calendar;

public sealed class CalendarEventDto
{
    public Guid? Id { get; set; }
    public string Type { get; set; } = "";         // "manual" | "birthday" | "match"
    public string Title { get; set; } = "";
    public string Date { get; set; } = "";         // "YYYY-MM-DD"
    public string? Time { get; set; }              // "HH:mm" ou null
    public bool TimeTBD { get; set; }
    public Guid? CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public string? CategoryColor { get; set; }
    public string? CategoryIcon { get; set; }
    public Guid? SourceId { get; set; }            // matchId ou playerId para eventos automáticos
    public string? Description { get; set; }
}
