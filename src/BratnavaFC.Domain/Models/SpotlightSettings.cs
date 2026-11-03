using System.Text.Json.Serialization;

namespace BratnavaFC.Domain.Models;

/// <summary>
/// Configuração do Spotlight de uma patota (slideshow do telão). Fica em JSON no GroupSettings:
/// identidade (título, subtítulo, emoji, cor), capítulos escondidos, ritmo e os mesmos filtros
/// das Estatísticas (período, só mensalistas, campeonatos). O Spotlight é só dos admins da patota.
/// </summary>
public sealed class SpotlightSettings
{
    public const int TitleMaxLength = 80;
    public const int SubtitleMaxLength = 120;
    public const int EmojiMaxLength = 16;
    public const string DefaultColor = "#2f6df6";

    /// <summary>Capítulos que o admin pode esconder. Abertura e encerramento sempre aparecem.</summary>
    public static readonly IReadOnlyList<string> Chapters =
    [
        "numeros", "rankings", "medias", "marcos", "recordes", "sequencias", "decisivos", "duplas", "jogadores"
    ];

    /// <summary>Nulo = nome da patota.</summary>
    public string? Title { get; set; }
    public string? Subtitle { get; set; }
    public string Emoji { get; set; } = "⚽";
    /// <summary>Cor de destaque do fundo, "#rrggbb".</summary>
    public string Color { get; set; } = DefaultColor;
    public List<string> HiddenChapters { get; set; } = new();
    /// <summary>Multiplicador do tempo de cada slide (1 = normal).</summary>
    public double Pace { get; set; } = 1.0;

    public SpotlightPeriodMode PeriodMode { get; set; } = SpotlightPeriodMode.Season;
    /// <summary>Temporada (ano). Nulo = ano atual.</summary>
    public int? SeasonYear { get; set; }
    /// <summary>"Desde": data local (sem hora) a partir da qual os jogos contam.</summary>
    public DateOnly? SinceDate { get; set; }
    public int? LastGames { get; set; }
    public bool OnlyMonthly { get; set; } = true;
    public bool IncludeChampionships { get; set; } = true;
    /// <summary>Mínimo de jogos para médias e aproveitamento. Nulo = automático (1/4 dos jogos do período).</summary>
    public int? MinGames { get; set; }

    /// <summary>Normaliza o que veio do admin; lança InvalidOperationException no que não dá para salvar.</summary>
    public SpotlightSettings Normalized()
    {
        var title = string.IsNullOrWhiteSpace(Title) ? null : Title.Trim();
        var subtitle = string.IsNullOrWhiteSpace(Subtitle) ? null : Subtitle.Trim();
        var emoji = (Emoji ?? string.Empty).Trim();

        if (title?.Length > TitleMaxLength) throw new InvalidOperationException($"Título do Spotlight deve ter até {TitleMaxLength} caracteres.");
        if (subtitle?.Length > SubtitleMaxLength) throw new InvalidOperationException($"Subtítulo do Spotlight deve ter até {SubtitleMaxLength} caracteres.");
        if (emoji.Length > EmojiMaxLength) throw new InvalidOperationException("Emoji do Spotlight muito longo.");

        var color = (Color ?? string.Empty).Trim();
        if (!IsHexColor(color)) throw new InvalidOperationException("Cor do Spotlight deve estar no formato #rrggbb.");

        if (Pace is < 0.5 or > 3) throw new InvalidOperationException("Tempo dos slides do Spotlight fora do intervalo.");

        if (PeriodMode == SpotlightPeriodMode.Since && SinceDate is null)
            throw new InvalidOperationException("Informe a data inicial do período do Spotlight.");
        if (PeriodMode == SpotlightPeriodMode.LastGames && LastGames is null or < 1 or > 500)
            throw new InvalidOperationException("Quantidade de jogos do Spotlight deve ser entre 1 e 500.");
        if (SeasonYear is < 2000 or > 2100) throw new InvalidOperationException("Temporada do Spotlight inválida.");
        if (MinGames is < 1 or > 500) throw new InvalidOperationException("Mínimo de jogos do Spotlight deve ser entre 1 e 500.");

        return new SpotlightSettings
        {
            Title = title,
            Subtitle = subtitle,
            Emoji = emoji,
            Color = color.ToLowerInvariant(),
            HiddenChapters = (HiddenChapters ?? new()).Where(Chapters.Contains).Distinct().ToList(),
            Pace = Math.Round(Pace, 2),
            PeriodMode = PeriodMode,
            SeasonYear = SeasonYear,
            SinceDate = SinceDate,
            LastGames = LastGames,
            OnlyMonthly = OnlyMonthly,
            IncludeChampionships = IncludeChampionships,
            MinGames = MinGames,
        };
    }

    private static bool IsHexColor(string value) =>
        value.Length == 7 && value[0] == '#' && value.Skip(1).All(Uri.IsHexDigit);
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SpotlightPeriodMode
{
    /// <summary>Temporada (ano civil, como as conquistas).</summary>
    Season = 0,
    /// <summary>Desde uma data.</summary>
    Since = 1,
    /// <summary>Últimos N jogos.</summary>
    LastGames = 2,
    /// <summary>Todo o histórico.</summary>
    All = 3,
}
