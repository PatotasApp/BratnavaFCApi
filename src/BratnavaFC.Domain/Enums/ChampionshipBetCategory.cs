namespace BratnavaFC.Domain.Enums;

/// <summary>Categorias de aposta do campeonato (sempre no campeonato inteiro, nunca por jogo).</summary>
public enum ChampionshipBetCategory : short
{
    Champion = 1,                // time campeão (obrigatória)
    LeastConcededGoalkeeper = 2, // goleiro menos vazado
    TopScorer = 3,               // artilheiro
    TopAssister = 4,             // maior assistente
    Mvp = 5                      // MVP (só se o campeonato tiver MVP)
}
