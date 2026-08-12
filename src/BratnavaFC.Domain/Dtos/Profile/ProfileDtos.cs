using BratnavaFC.Domain.Dtos.Conquistas;

namespace BratnavaFC.Domain.Dtos.Profile;

public sealed class UserProfileDto
{
    public Guid UserId { get; set; }
    public string Name { get; set; } = "";
    public string UserName { get; set; } = "";
    public string? PhotoUrl { get; set; }
    public int? Age { get; set; }
    public string? Position { get; set; }
    public int Games { get; set; }
    public int Goals { get; set; }
    public int Assists { get; set; }
    public int Mvps { get; set; }
    public List<ConquistaDto> Marcos { get; set; } = [];
    public List<ConquistaDto> Feitos { get; set; } = [];
    public List<ConquistaDto> Titulos { get; set; } = [];
    public List<ConquistaDto> Destaques { get; set; } = [];
    public List<PatotaProfileDto> Patotas { get; set; } = [];
}

public sealed class PatotaProfileDto
{
    public Guid? GroupId { get; set; }
    public string GroupName { get; set; } = "";
    public int Games { get; set; }
    public int Goals { get; set; }
    public int Assists { get; set; }
    public int Mvps { get; set; }
    public PlayerConquistasDto Conquistas { get; set; } = new();
}

public sealed class ProfilePrivacyDto
{
    public string Visibility { get; set; } = "AuthenticatedUsers";
    public bool ShowPatotaNames { get; set; } = true;
    public bool ShowZoeiraAchievements { get; set; }
}

public sealed class UpdateProfilePrivacyDto
{
    public string Visibility { get; set; } = "AuthenticatedUsers";
    public bool ShowPatotaNames { get; set; } = true;
    public bool ShowZoeiraAchievements { get; set; }
}
