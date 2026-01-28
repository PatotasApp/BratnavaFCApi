namespace BratnavaFC.Domain.Dtos;

public class MatchDto
{
    public Guid? Id { get; set; }
    public DateTime PlayedAt { get; set; }
    public int HomeGoals { get; set; }
    public int AwayGoals { get; set; }
    public List<MatchPlayerDto> Players { get; set; } = new();
}