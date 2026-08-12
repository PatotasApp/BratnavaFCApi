namespace BratnavaFC.Domain.Models;

public sealed class PlayerSpotlightReport
{
    public Guid GroupId { get; set; }
    public List<PlayerSpotlightItem> Players { get; set; } = new();
}

public sealed class PlayerSpotlightItem
{
    public Guid PlayerId { get; set; }
    public Guid? UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsGoalkeeper { get; set; }
    public bool IsGuest { get; set; }
    public int GamesPlayed { get; set; }
    public int Wins { get; set; }
    public int Losses { get; set; }
    public int Ties { get; set; }
    public double WinRate { get; set; }
    public int Goals { get; set; }
    public int Assists { get; set; }
    public int Mvps { get; set; }
    public List<SpotlightRelation> BestPartners { get; set; } = new();
    public List<SpotlightRelation> WorstPartners { get; set; } = new();
    public List<SpotlightRelation> MostBeatenBy { get; set; } = new();
    public List<SpotlightRelation> LeastBeatenBy { get; set; } = new();
    public List<SpotlightRelation> MostAssistedBy { get; set; } = new();
    public List<SpotlightRelation> MostAssistedTo { get; set; } = new();
}

public sealed class SpotlightRelation
{
    public Guid PlayerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Count { get; set; }
    public double Rate { get; set; }
}
