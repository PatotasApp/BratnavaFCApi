namespace BratnavaFC.Domain.Dtos.Calendar;

public sealed class CalendarCategoryDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string? Color { get; set; }
    public string? Icon { get; set; }
    public bool IsSystem { get; set; }
}
