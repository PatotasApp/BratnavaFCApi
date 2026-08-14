namespace BratnavaFC.Domain.Dtos.Conquistas;

/// <summary>Conquistas de todos os jogadores de um grupo (raridade calibrada pela patota).</summary>
public sealed class GroupConquistasDto
{
    public Guid                        GroupId  { get; set; }
    public int                         Season   { get; set; }   // temporada atual (ano)
    public List<PlayerConquistasDto>   Players  { get; set; } = [];
}

public sealed class PlayerConquistasDto
{
    public Guid   PlayerId     { get; set; }
    public Guid?  UserId       { get; set; }
    public string PlayerName   { get; set; } = "";
    public bool   IsGoalkeeper { get; set; }

    /// <summary>Vitalícias por marco (limite fixo, permanente) — o nível atual de cada trilha.</summary>
    public List<ConquistaDto>      Marcos    { get; set; } = [];
    /// <summary>Vitalícias contáveis (×N, tier sobe com a contagem).</summary>
    public List<ConquistaDto>      Eventos   { get; set; } = [];
    /// <summary>Títulos de temporadas ENCERRADAS (pódio carimbado).</summary>
    public List<ConquistaDto>      Titulos   { get; set; } = [];
    /// <summary>Posição em percentil na temporada atual (ainda não vira título).</summary>
    public List<SeasonStandingDto> Temporada { get; set; } = [];
}

public sealed class ConquistaDto
{
    public string  Id           { get; set; } = "";
    public string  Nome         { get; set; } = "";
    public string  Descricao    { get; set; } = "";
    public string  Categoria    { get; set; } = "";   // Ataque, Criacao, Vitorias, MVP, Presenca, Zoeira
    public string  Icone        { get; set; } = "";   // emoji
    public string  Raridade     { get; set; } = "";   // Comum, Rara, Epica, Lendaria
    public double  PctPatota    { get; set; }          // fração 0..1 de quem tem
    public bool    Desbloqueada { get; set; }
    public int?    Count        { get; set; }          // eventos contáveis (×N)
    public int?    Valor        { get; set; }          // valor atual (ex.: jogos, gols)
    public int?    Meta         { get; set; }          // próxima meta (null se maxado)
    public int?    Nivel        { get; set; }          // marco: nível atual (quantos já passou)
    public int?    TotalNiveis  { get; set; }          // marco: total de níveis da trilha
    public string? ProximoNome  { get; set; }          // marco: nome do próximo nível (null se maxado)
    public int?    Ano          { get; set; }          // títulos de temporada
    public int?    Posicao      { get; set; }          // pódio 1..3 (títulos)
    public List<ConquistaEtapaDto> Etapas { get; set; } = [];
}

public sealed class ConquistaEtapaDto
{
    public string Nome { get; set; } = "";
    public string Descricao { get; set; } = "";
    public int Meta { get; set; }
    public bool Desbloqueada { get; set; }
}

/// <summary>Posição do jogador numa categoria da temporada atual (percentil vivo, muda jogo a jogo).</summary>
public sealed class SeasonStandingDto
{
    public string Categoria { get; set; } = "";   // Gols, Assistencias, MVPs, Presenca, Aproveitamento
    public string Nome      { get; set; } = "";
    public string Icone     { get; set; } = "";
    public int    Valor     { get; set; }          // valor do jogador na temporada
    public int    Posicao   { get; set; }          // 1-based
    public int    Total     { get; set; }          // jogadores elegíveis
    public double Percentil { get; set; }          // 0..1 (menor = melhor, "top X%")
    public string Raridade  { get; set; } = "";    // pela faixa de percentil
}
