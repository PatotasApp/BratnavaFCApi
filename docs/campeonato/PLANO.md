# Plano do Modo Campeonato

## Arquitetura
**Campeonato = entidade-mãe. Jogos = `MatchEntity` filhas, invisíveis por padrão.**

Cada jogo continua sendo uma partida de verdade, para reaproveitar gols, assistências, gol contra, goleiro, cores, replays e votação de MVP. A marca `ChampionshipId` tira o jogo de toda consulta comum via **filtro global do EF**.

### Modelo de dados (alvo final)
| Entidade | Campos principais |
|---|---|
| `ChampionshipEntity` | GroupId, Nome, PlayedAt, Local, Status, Formato, `Rules` (tipo próprio, congelado ao iniciar) |
| `ChampionshipTeamEntity` | Nome, Cor, Posição final (gravada ao finalizar, como `SeasonTitleEntity`) |
| `ChampionshipPlayerEntity` | Jogador, Time, presença, goleiro, `DidNotPlay`, MVP do campeonato |
| `ChampionshipVoteEntity` | Voto de MVP do campeonato |
| `ChampionshipBetEntity` (+ seleções) | Aposta do campeonato; mesmo saldo de fichas (`UserBetBalanceEntity`) |
| `MatchEntity` (+ campos) | `ChampionshipId?`, `ChampionshipTeamAId/BId`, Rodada/Fase, Ordem |
| `PlayerMatchStatContributionEntity` (+ campos) | `ChampionshipId?`, `SourceKind` (Partida / Campeonato) |

### Onde a API lê partidas para estatística (todos têm que respeitar as regras)
- `MatchService.GetHistoryAsync`, `GetPlayerRecentMatchesAsync`, `GetPlayerHistoryAsync`
- `PlayerStatsService.GetVisualReportAsync`, `GetSpotlightReportAsync`, `EnrichPlayersAsync` (sorteio)
- `ConquistaProjectionService` (contribuições, projeções, títulos)
- `TeamBuilderService.GetStatsAsync` (sinergia)
- `BetService` (resolução de apostas)
- `MatchService.Create` (limite de partidas simultâneas) e `GetUpcomingAsync`/partida atual (não devem contar jogos de campeonato)

## Fases
### Fase 0: Blindagem das estatísticas ← EM ANDAMENTO
Ver PROGRESSO.md. Objetivo: um jogo de campeonato pode existir no banco sem aparecer em lugar nenhum, e as estatísticas atuais ficam idênticas.
1. Teste golden: snapshot das saídas de todos os leitores acima, com uma patota fictícia.
2. `ChampionshipEntity` mínima + `MatchEntity.ChampionshipId` (nulo) + migração que não altera linhas existentes.
3. Filtro global: `Matches` (`ChampionshipId == null`) e também `MatchPlayers`, `Goals`, `Votes` (o filtro de `Matches` não vale quando essas tabelas são consultadas direto).
4. Exceções que precisam ver tudo: exclusão de patota (`GroupService`), exclusão de jogador e de conta (`PlayerService`, `UserService`).
5. Teste de vazamento: campeonato com 3 jogos finalizados e gols → snapshot golden idêntico.
6. Teste de arquitetura: `IgnoreQueryFilters()` só em arquivos de uma lista permitida.

### Fase 1: Backend do campeonato
Entidades completas, CRUD, regras, e o fluxo:
**Criado → Presença → Sorteio → Em andamento → Pós-campeonato → Finalizado** (+ Cancelado).
- Presença: confirmação única do dia, reaproveitando ausências/auto-rejeição.
- Sorteio para N times.
- Geração de jogos conforme o formato (no fica quem ganha, cada jogo nasce na hora).
- Endpoints de jogo ao vivo (gol, goleiro, replay) precisam achar jogos de campeonato: usar `IgnoreQueryFilters` nos lookups por id, com allowlist.

### Fase 2: Classificação e encerramento
Tabela calculada a partir dos jogos, desempates configuráveis, posição final gravada, MVP com a `MvpTieRule` da patota.

### Fase 3: Estatísticas respeitando as regras
`BuildContributions` gera as linhas do campeonato conforme as regras:
- Consolidado: 1 linha por jogador, `Games = 1`, resultado pela colocação, gols conforme parâmetro.
- Jogos separados: 1 linha por jogo.
- Só no campeonato: nenhuma linha.

Os leitores passam a juntar partidas normais + entradas de campeonato. Ideal: todos lendo da tabela de contribuições.
**Teste obrigatório (matriz de regras):** nº de itens no histórico = `Games` da projeção = jogos no relatório visual = jogos no dashboard.

### Fase 4: App e web
Menu "Campeonatos", wizard de criação com resumo em linguagem simples, tela ao vivo (tempo real via `Realtime`), card 🏆 no histórico com selos das regras, detalhe com artilharia, e apostas na tela existente.

### Fase 5: Extras
Título de campeão como conquista, card do campeão (`MatchCardService`), notificações "seu time joga agora", prêmios automáticos (artilheiro, garçom, menos vazado).
