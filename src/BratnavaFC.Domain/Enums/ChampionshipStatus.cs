namespace BratnavaFC.Domain.Enums;

public enum ChampionshipStatus : short
{
    Created = 0,     // campeonato criado, regras ainda editáveis
    Acceptation = 1, // confirmação de presença do dia
    TeamDraw = 2,    // sorteio/montagem dos times
    InProgress = 3,  // jogos acontecendo
    PostGame = 4,    // votação de MVP e conferência da tabela
    Finalized = 5,   // classificação e MVP definitivos
    Cancelled = 6
}
