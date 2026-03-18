namespace BratnavaFC.Domain.Dtos.Calendar;

public sealed class CreateCalendarEventDto
{
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public Guid? CategoryId { get; set; }
    public string Date { get; set; } = "";          // "YYYY-MM-DD"
    public string? Time { get; set; }               // "HH:mm" ou null
    public bool TimeTBD { get; set; }
}
