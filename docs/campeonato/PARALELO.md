# Trabalho em paralelo

Enquanto a Fase 0 está em andamento na branch `feature/campeonato-fase0-blindagem`, ela mexe em:
- `src/BratnavaFC.Domain/Entities/MatchEntity.cs`, `MatchPlayerEntity.cs` e o novo `ChampionshipEntity.cs`
- `src/BratnavaFC.Infrastructure/Data/AppDbContext.cs`, `Data/Configurations/*` e `Migrations/*`
- `src/BratnavaFC.Application/Services/GroupService.cs`, `PlayerService.cs`, `UserService.cs`
- testes novos em `BranavaFCTests/Campeonato*` e `docs/campeonato/PROGRESSO.md`

**Não tocar nesses arquivos em paralelo.** E não trabalhar no mesmo checkout: crie um worktree próprio
(`git worktree add ../BratnavaFCApi-campeonato-motor -b feature/campeonato-motor main`), porque trocar de branch no
checkout compartilhado quebra o trabalho da outra IA.

## A) Motor de formatos e classificação — PODE começar já ✅
Lógica pura, sem banco e sem entidades do EF, então não conflita com a Fase 0.

- **Onde:** `src/BratnavaFC.Domain/Championships/` (pasta nova) + testes em `BranavaFCTests/ChampionshipEngine*Tests.cs`.
- **O que:**
  - Tipos de regras (`ChampionshipFormat`, `WinnerStaysOptions`, `TiebreakerCriteria`, `StandingMode`...) seguindo DECISOES.md.
  - Gerador de tabela de todos contra todos (1 ou 2 turnos, N times, rodada com folga quando N é ímpar).
  - Simulador do fica quem ganha: dado o resultado de cada jogo, diz quem entra no próximo, respeitando limite de vitórias seguidas, regra de empate e fila de espera.
  - Mata-mata e "todos contra todos + final".
  - Classificação: pontos, saldo, gols pró, confronto direto, pontos por jogo; posição final com desempate configurável.
- **Entradas/saídas:** só ids de time (`Guid`) e placares (`int`). Nada de `MatchEntity`.
- **Pronto quando:** testes cobrindo 3, 4 e 5 times, empates, limite de 2 vitórias, desempate por confronto direto, e "todos jogaram a mesma quantidade".

## B) Telas com dados falsos (Flutter e web) — pode começar, com cuidado ⚠️
Repositórios diferentes, então sem conflito de arquivo. O risco é o contrato da API ainda não existir.
- Construir o **wizard de criação** e a **tela de detalhe/classificação** com dados mockados, atrás de uma flag, seguindo DECISOES.md.
- Isolar os DTOs num arquivo só, para trocar pelo contrato real na Fase 1.
- Não mexer na tela de apostas nem no histórico ainda: dependem da Fase 3.

## C) Sorteio para N times — NÃO começar ainda ⛔
Depende da decisão sobre o novo algoritmo ("Jogo Parelho"), em discussão separada.

## Acompanhamento
A outra IA mantém o próprio progresso em
`C:\Users\lhnme\source\repos\BratnavaFCApi-campeonato-motor\docs\campeonato-motor\PROGRESSO.md`
e não edita nada neste checkout. O prompt que ela recebeu está em [PROMPT-OUTRA-IA.md](PROMPT-OUTRA-IA.md).
