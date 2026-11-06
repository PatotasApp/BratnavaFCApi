namespace BratnavaFC.Domain.Dtos.Share;

/// <summary>
/// Vitrine pública da patota (link compartilhado, inclusive sem login). Só números gerais e
/// dados de apresentação: nada de partidas, integrantes, financeiro, enquetes ou presenças.
/// </summary>
public sealed class PublicGroupDto
{
    public Guid GroupId { get; set; }
    public string Name { get; set; } = "";
    public string? LogoUrl { get; set; }
    public int SinceYear { get; set; }
    public int MembersCount { get; set; }
    public int MatchesPlayed { get; set; }
    public int GoalsScored { get; set; }
    /// <summary>0 = domingo … 6 = sábado (System.DayOfWeek).</summary>
    public int? DayOfWeek { get; set; }
    /// <summary>"HH:mm".</summary>
    public string? KickoffTime { get; set; }
    public string? PlaceName { get; set; }
}

public sealed class PublicGroupResponseDto
{
    /// <summary>False quando o admin desligou a página pública: nenhum dado da patota sai.</summary>
    public bool Enabled { get; set; }
    /// <summary>Quem abriu é integrante (ou admin da plataforma): o cliente pode abrir a patota.</summary>
    public bool IsMember { get; set; }
    public PublicGroupDto? Group { get; set; }
}

public static class ShareKinds
{
    public const string Match = "match";
    public const string Mvp = "mvp";
    public const string Championship = "championship";
    public const string Poll = "poll";

    public static readonly IReadOnlySet<string> Internal =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Match, Mvp, Championship, Poll };
}

public static class ShareAccess
{
    /// <summary>Integrante: pode abrir o conteúdo.</summary>
    public const string Member = "member";
    /// <summary>Sem login: precisa entrar para saber se é integrante.</summary>
    public const string LoginRequired = "login_required";
    /// <summary>Logado, mas não é integrante da patota dona do conteúdo.</summary>
    public const string NotMember = "not_member";
}

/// <summary>
/// Resolução de um link interno (partida, votação de MVP, campeonato, enquete). Para quem não
/// é integrante só sai o mínimo: o tipo do conteúdo e, se a patota tem página pública, nome e
/// logo. Os detalhes continuam nas rotas internas, que checam integrante na API.
/// </summary>
public sealed class ShareTargetDto
{
    public string Kind { get; set; } = "";
    public Guid Id { get; set; }
    public string Access { get; set; } = ShareAccess.LoginRequired;
    /// <summary>Só para integrante ou quando a patota tem página pública.</summary>
    public Guid? GroupId { get; set; }
    public string? GroupName { get; set; }
    public string? GroupLogoUrl { get; set; }
    /// <summary>Só para integrante: dicas para montar a rota interna.</summary>
    public string? MatchStatus { get; set; }
    public string? PollType { get; set; }
}
