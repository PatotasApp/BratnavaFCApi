namespace BratnavaFC.Domain.Dtos;

public class CreateMatchDto
{
    public DateTime PlayedAt { get; set; }
    public string PlaceName { get; set; } = string.Empty;
}