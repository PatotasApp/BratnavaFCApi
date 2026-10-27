# Progresso

## Fase 0 — Blindagem das estatísticas ✅ concluída (27/10/2026)
Branch: `feature/campeonato-fase0-blindagem` (a partir de `main` @ 9863c15). Sem push.

| # | Passo | Status |
|---|---|---|
| 1 | Teste de vazamento das estatísticas (Postgres real) | ✅ |
| 2 | `ChampionshipEntity` mínima + `MatchEntity.ChampionshipId` + migração `AddChampionships` | ✅ |
| 3 | Filtros globais em `Matches`, `MatchPlayers`, `Goals`, `Votes` | ✅ |
| 4 | Exceções: exclusão de patota (`GroupService.DeleteManyAsync`) e de jogador (`PlayerService.DeleteAsync`) | ✅ |
| 5 | Testes de vazamento, limite de partidas e exclusões | ✅ |
| 6 | Teste de arquitetura (`IgnoreQueryFilters` só em allowlist) | ✅ |
| 7 | Build + todos os testes | ✅ 1535 aprovados, 0 falhas, 7 ignorados (já eram) |

### O que foi feito
- **Domínio:** `ChampionshipEntity` (GroupId, Nome, PlayedAt, Local, Status, Matches), enum `ChampionshipStatus`, e em `MatchEntity` os campos `ChampionshipId`/`Championship` + `AssignToChampionship(id)` (só com a partida em `Created`).
- **Banco:** migração `AddChampionships`: tabela `Championships` (FK para Groups com Restrict) e coluna `Matches.ChampionshipId` nula (FK Restrict + índice). Não altera nenhuma linha existente.
- **Filtros globais:** `ChampionshipId == null` em `Matches`, e `Match.ChampionshipId == null` em `MatchPlayers`, `Goals` e `Votes` (essas tabelas são consultadas direto por apostas, sinergia, jobs e controllers).
- **Exceções com `IgnoreQueryFilters`:**
  - `GroupService.DeleteManyAsync`: apaga também os jogos de campeonato e os campeonatos.
  - `PlayerService.DeleteAsync`: jogo de campeonato conta como histórico (jogador só pode ser desativado).
- **Testes** (`BranavaFCTests/CampeonatoBlindagemTests.cs`):
  - `JogosDeCampeonato_NaoAlteramNenhumaEstatisticaNemListagem`: tira um retrato de 28 saídas (histórico geral e por jogador, recentes, histórico do jogador, próximas partidas, relatório visual com e sem convidados, spotlight, sorteio, team builder, contribuições, projeções e conquistas), cria um campeonato com 3 jogos finalizados + 1 aberto e exige retrato idêntico. **Antes dos filtros, as 28 mudavam.**
  - `JogosDeCampeonatoAbertos_NaoContamNoLimiteDePartidasSimultaneas`
  - `ExcluirPatota_ApagaTambemCampeonatosESeusJogos`
  - `ExcluirJogador_ComHistoricoSoEmCampeonato_ERecusado`
  - `IgnoreQueryFilters_SoAparecemEmArquivosRevisados` (arquitetura)
- **Infra de teste:** `Testcontainers.PostgreSql` **3.10.0** em `BranavaFCTests`. A 4.x exige Docker API 1.44 e o Docker local do Luis é 20.10 (API 1.41). No CI (ubuntu-latest) o Docker é novo, então funciona também.

### Pontos de atenção para a Fase 1
- Os endpoints de jogo ao vivo (`MatchService` por id, `_repository.GetByIdAsync`, gols, votos, replay em `IncrementReplayGoal`) **não enxergam jogos de campeonato** por causa do filtro. A Fase 1 precisa de lookups explícitos com `IgnoreQueryFilters` (e incluir os arquivos na allowlist do teste de arquitetura) ou de um serviço próprio do campeonato.
- `RepositoryBase.GetByIdIncludingInactiveAsync` já usava `IgnoreQueryFilters` (anterior à Fase 0) e está na allowlist; hoje só é chamado para patota, jogador e usuário. Não usar com `MatchEntity`.
- `MatchBets` e `ReplayClips` não têm filtro: jogos de campeonato não aceitam aposta (decisão), e replay não é estatística. Reavaliar se a Fase 1 expuser replays de campeonato no feed.
- Teste de seed contra Postgres: monte a partida inteira em memória e salve uma vez. Filhos com Guid já preenchido adicionados a uma entidade rastreada viram UPDATE (o InMemory esconde isso).

### Como retomar
1. `git switch feature/campeonato-fase0-blindagem` dentro de `BratnavaFCApi`.
2. Docker ligado; `dotnet test ./BranavaFCTests/BranavaFC.Tests.csproj`.
3. Próximo: **Fase 1** (ver PLANO.md).

## Trabalho paralelo
- **Motor de formatos e classificação:** outra IA, branch `feature/campeonato-motor`, worktree `C:\Users\lhnme\source\repos\BratnavaFCApi-campeonato-motor`. Progresso dela em `docs\campeonato-motor\PROGRESSO.md` daquele worktree.
