namespace BratnavaFC.Domain.Dtos;

public class MatchPlayerDto
{
    public Guid? Id { get; set; }
    public string Name { get; set; } = null!;
    public bool? IsMvp { get; set; }
}