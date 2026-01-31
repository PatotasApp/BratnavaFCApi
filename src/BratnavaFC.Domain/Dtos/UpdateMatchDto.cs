namespace BratnavaFC.Domain.Dtos;

public class UpdateMatchDto
{
    public Guid? Id { get; set; }
    public DateTime PlayedAt { get; set; }
    public string PlaceName { get; set; } = string.Empty;

}