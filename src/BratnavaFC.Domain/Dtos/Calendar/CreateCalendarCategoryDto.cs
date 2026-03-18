namespace BratnavaFC.Domain.Dtos.Calendar;

public sealed class CreateCalendarCategoryDto
{
    public string Name { get; set; } = "";
    public string? Color { get; set; }
    public string? Icon { get; set; }
}
