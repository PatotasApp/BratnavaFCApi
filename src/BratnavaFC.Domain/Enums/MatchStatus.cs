namespace BratnavaFC.Domain.Enums;

public enum MatchStatus : short
{
    Created = 0,        // partida criada (instante do create)
    Acceptation = 1,    // aceitar / recusar
    MatchMaking = 2,    // gerar times, cores, swap, setar times
    Started = 3,        // partida iniciada
    Ended = 4,          // partida encerrada
    PostGame = 5,       // votar MVP, setar placar/gols
    Finalized = 6       // finalizada (MVP + placar definitivo)
}
