# Prompt 4 para a outra IA (sorteio de N times para o campeonato)

Copie tudo abaixo da linha. Pode ser feito antes ou depois do Prompt 3 (front web): os dois não conflitam.

---

## Objetivo
No Modo Campeonato, o admin divide os confirmados do dia em **N times** (3, 4, 5...). Hoje a API só tem uma sugestão provisória (`ChampionshipService.DrawTeamsAsync`: um goleiro por time e o resto em "cobra" pela nota). As estratégias reais de sorteio da patota (`src/BratnavaFC.Application/TeamGeneration`: Manual, Random, Algorithm, GroupByWins, Profile) só sabem dividir em **2** times. Sua tarefa é criar o **sorteio de N times**, reaproveitando a força dos jogadores que essas estratégias já calculam. Eu (a outra IA) ligo o resultado no campeonato depois.

## Leia antes
1. `C:\Users\lhnme\source\repos\BratnavaFCApi\docs\campeonato\README.md`, `DECISOES.md` e `PROGRESSO.md`.
2. `src/BratnavaFC.Application/TeamGeneration/*` (factory, settings, estratégias) e `IPlayerStatsService.EnrichPlayersAsync`, que dá a força de cada jogador.
3. A memória da revisão do algoritmo está resumida no `PROGRESSO.md`. Pontos que valem aqui: o **goleiro é escolhido antes do sorteio e fica o jogo todo**; jogador **sem nota não pode valer 0** (use a média do grupo); **no máximo 1 goleiro por time**, com goleiro sobrando indo para a linha sem quebrar (3+ goleiros não pode lançar exceção).

## Isolamento (obrigatório)
- Worktree próprio a partir da branch do campeonato (é lá que o código vai ser usado):
  ```
  git -C C:\Users\lhnme\source\repos\BratnavaFCApi worktree add ..\BratnavaFCApi-campeonato-sorteio -b feature/campeonato-sorteio feature/campeonato-fase0-blindagem
  ```
  Trabalhe só em `C:\Users\lhnme\source\repos\BratnavaFCApi-campeonato-sorteio`.
- Escreva código **só** em `src/BratnavaFC.Application/TeamGeneration/MultiTeam/` (pasta nova) e em `BranavaFCTests/MultiTeamGeneration*Tests.cs`. **Não altere** as estratégias atuais, `PlayerStatsService`, `ChampionshipService`, entidades, migrações nem `appsettings`. A Fase 3 está mexendo no `PlayerStatsService` agora. Use só a **interface** `IPlayerStatsService`.
- Commits só na branch `feature/campeonato-sorteio`, sem push.

## O que fazer
1. `MultiTeamGenerationService` com algo como:
   ```csharp
   Task<Result<MultiTeamDrawResultDto>> GenerateAsync(
       List<PlayerRequestDto> players, int teamCount, MultiTeamStrategy strategy,
       IReadOnlyCollection<Guid> goalkeeperIds, int optionsCount = 3, CancellationToken ct = default);
   ```
   com estratégias **Aleatório** e **Equilibrado** (força via `EnrichPlayersAsync`, a mesma base do `AlgorithmStrategy`) e, se fizer sentido, **Perfil** (ataque/defesa/físico).
2. **Regras:**
   - Tamanhos de time diferindo em no máximo 1.
   - Um goleiro por time quando houver goleiros suficientes; goleiros sobrando vão para a linha.
   - Jogador sem nota/histórico recebe a média do grupo.
   - Determinístico quando recebe uma semente (`Random` injetável), para dar para testar.
3. **Equilíbrio:** minimizar a diferença entre o time mais forte e o mais fraco (ex.: busca local/trocas a partir de uma "cobra"). Com até ~30 jogadores e 3–6 times tem que rodar bem abaixo de 1 s. Devolva em cada opção a força de cada time e a diferença máx−mín.
4. DTO de saída pensado para virar o `DefineTeamsDto` do campeonato (lista de times com `playerIds` e `goalkeeperPlayerId`; nomes/cores ficam comigo).
5. Testes: 3, 4 e 5 times; ímpar de jogadores; 0, 1, N e N+2 goleiros; sem notas; Aleatório com semente; o Equilibrado nunca pior que a "cobra" simples; desempenho.

**Pronto quando:** `dotnet test BranavaFCTests/BranavaFC.Tests.csproj` passa no seu worktree (com o Docker ligado; os testes `Category=Postgres` usam Testcontainers).

## Acompanhamento (obrigatório)
Mantenha atualizado a cada etapa `C:\Users\lhnme\source\repos\BratnavaFCApi-campeonato-sorteio\docs\campeonato-sorteio\PROGRESSO.md` (branch, último commit, etapas, decisões de algoritmo e por quê, dúvidas para o Luis, o que falta).
