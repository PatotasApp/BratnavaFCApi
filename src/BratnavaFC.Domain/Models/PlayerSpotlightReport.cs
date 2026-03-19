namespace BratnavaFC.Domain.Models;

public sealed class PlayerSpotlightReport
{
    public Guid                    GroupId { get; init; }
    public List<PlayerSpotlightItem> Players { get; init; } = new();
}
