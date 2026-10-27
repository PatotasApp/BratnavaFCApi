# Contrato da API do campeonato (Fases 1 e 2)

Status: **rascunho v1 (27/10/2026)**. O backend implementa isto na Fase 1/2 e os clientes (Flutter e web) constroem em cima.
Mudanças no contrato: só por quem faz o backend, anotadas no histórico no fim deste arquivo.

## Convenções (as mesmas da API atual)
- JSON em camelCase. **Enums trafegam como número** (a API não usa `JsonStringEnumConverter`).
- Datas em UTC ISO-8601.
- Respostas no envelope `ApiResponse` já usado pelos outros endpoints (o cliente Flutter já trata).
- Base: `api/championships/group/{groupId}`. Endpoints marcados **[admin]** exigem admin da patota; os outros exigem membro.

## Enums
| Enum | Valores |
|---|---|
| `ChampionshipStatus` | 0 Created · 1 Acceptation · 2 TeamDraw · 3 InProgress · 4 PostGame · 5 Finalized · 6 Cancelled |
| `HistoryMode` | 1 Consolidated (1 card, conta como 1 partida) · 2 SeparateGames · 3 ChampionshipOnly |
| `ResultMode` | 1 PerGame (só com SeparateGames) · 2 Placement · 3 WinForAll ("Vitória para todos") · 4 PresenceOnly |
| `PlacementResult` | 1 Win · 2 Draw · 3 Loss · 4 None |
| `MvpMode` | 1 PerGame · 2 PerChampionship · 3 None |
| `ChampionshipFormat` | 1 RoundRobin · 2 WinnerStays · 3 Knockout · 4 RoundRobinWithFinal |
| `WinnerStaysTieRule` | 1 BothLeave · 2 LongestOnFieldLeaves · 3 PenaltyShootout |
| `StandingMode` | 1 TotalPoints · 2 PointsPerGame |
| `TiebreakerCriteria` | 1 Points · 2 GoalDifference · 3 GoalsFor · 4 HeadToHead · 5 ExternalDecision |
| `ChampionshipEndMode` | 1 TotalTime · 2 NumberOfGames · 3 EqualGamesForAllTeams |
| `ChampionshipGameStatus` | 1 InProgress · 2 Finished |
| `InviteResponse` | 0 None · 1 Accepted · 2 Rejected (igual ao das partidas) |

## Regras — `ChampionshipRulesDto`
```json
{
  "historyMode": 1,
  "resultMode": 2,
  "placementResults": [1, 2, 3],
  "mvpMode": 2,
  "mvpCountsInGeneralStats": true,
  "goalsCountInGeneralStats": false,
  "includeInTeamDraw": false,
  "minGamesForPlacementResult": 1,
  "format": 2,
  "roundRobinTurns": 1,
  "winnerStays": { "consecutiveWinsLimit": 2, "tieRule": 2 },
  "gameEnd": { "timeLimitMinutes": 10, "goalLimit": 2 },
  "championshipEnd": { "mode": 1, "totalTimeMinutes": 120, "numberOfGames": null },
  "standings": { "mode": 2, "winPoints": 3, "drawPoints": 1, "tiebreakers": [1, 2, 3, 4, 5] }
}
```
- `placementResults[i]` = resultado de quem termina em `i+1`º. Posições além da lista usam o **último** valor. Só vale com `resultMode = 2`.
- `winnerStays` é obrigatório só com `format = 2`; `roundRobinTurns` só com 1 e 4.
- `gameEnd`: pelo menos um dos dois.
- `standings.tiebreakers`: sem repetição; precisa conter 1 (Points).
- Validações do servidor: `resultMode = 1` exige `historyMode = 2`.
- **Presets** (só no cliente, preenchem o formulário):
  - "Fica quem ganha clássico": format 2, limite 0, empate 2, pontos por jogo.
  - "Troca quem ganhou 2": format 2, limite 2, empate 2, pontos por jogo.
- O cliente mostra um **resumo em linguagem simples** antes de confirmar (ex.: "Conta como 1 partida · 1º vitória, 2º empate, 3º derrota · 1 MVP do campeonato"). Nome da regra 3: **"Vitória para todos"**, legenda *"Cada participante ganha 1 vitória nas estatísticas."*

## Endpoints

### Consulta
| Método | Rota | Retorno |
|---|---|---|
| GET | `/` | `ChampionshipSummaryDto[]` (mais recentes primeiro) |
| GET | `/{id}` | `ChampionshipDetailsDto` |
| GET | `/{id}/next-game` | `NextGameSuggestionDto` (sugestão do motor; null quando acabou) |

### Criação e regras [admin]
| Método | Rota | Corpo |
|---|---|---|
| POST | `/` | `CreateChampionshipDto` → `{ "id": "guid" }` |
| PUT | `/{id}` | `UpdateChampionshipDto` (só até o 1º jogo começar) |
| POST | `/{id}/cancel` | — (só antes de qualquer jogo finalizado) |

### Presença
| Método | Rota | Corpo |
|---|---|---|
| POST | `/{id}/acceptation/open` [admin] | — (adiciona todos os jogadores ativos da patota) |
| PATCH | `/{id}/my-invite/accept` | — |
| PATCH | `/{id}/my-invite/reject` | — |
| PATCH | `/{id}/invite/accept` [admin] | `{ "playerId": "guid" }` |
| PATCH | `/{id}/invite/reject` [admin] | `{ "playerId": "guid" }` |
| POST | `/{id}/players/guest` [admin] | `{ "playerId": "guid" }` (convidado já cadastrado) |

### Times [admin]
| Método | Rota | Corpo |
|---|---|---|
| POST | `/{id}/teams/draw` | `{ "teamCount": 3 }` → `DefineTeamsDto` sugerido (não salva) |
| PUT | `/{id}/teams` | `DefineTeamsDto` (vai para TeamDraw e salva) |
| PATCH | `/{id}/players/{playerId}/team` | `{ "teamId": "guid" }` (atrasado/troca; não muda jogos já feitos) |

### Jogos [admin]
| Método | Rota | Corpo |
|---|---|---|
| POST | `/{id}/start` | — (InProgress; trava regras e fecha apostas) |
| POST | `/{id}/games` | `{ "teamAId": "guid", "teamBId": "guid" }` → `{ "gameId": "guid" }` (já começa) |
| POST | `/{id}/games/{gameId}/goals` | `{ "scorerPlayerId", "assistPlayerId"?, "isOwnGoal", "timeSeconds"? }` |
| DELETE | `/{id}/games/{gameId}/goals/{goalId}` | — |
| PATCH | `/{id}/games/{gameId}/goalkeeper` | `{ "teamId", "playerId" }` |
| POST | `/{id}/games/{gameId}/end` | `{ "externalWinnerTeamId"? }` (pênaltis/decisão em empate) |
| POST | `/{id}/finish` | — (PostGame: abre votação de MVP) |

### Encerramento
| Método | Rota | Corpo |
|---|---|---|
| POST | `/{id}/vote` | `{ "votedPlayerId", "gameId"? }` (gameId só com MVP por jogo) |
| POST | `/{id}/finalize` [admin] | `{ "externalDecisionTeamOrder"?: ["guid"] }` (desempate final) |

## DTOs
```jsonc
// ChampionshipSummaryDto
{ "id", "name", "playedAt", "placeName", "status", "teamCount", "gameCount",
  "championTeamName": null, "championTeamColorHex": null, "ruleBadges": ["Conta como 1 partida", "1º V · 2º E · 3º D", "MVP do campeonato"] }

// ChampionshipDetailsDto
{ "id", "name", "playedAt", "placeName", "status", "rules": ChampionshipRulesDto,
  "rulesLocked": false, "ruleBadges": [...],
  "players": [ { "playerId", "name", "photoUrl", "isGuest", "isGoalkeeper", "inviteResponse", "teamId": null, "didNotPlay": false } ],
  "teams": [ { "id", "name", "colorId", "colorHex", "finalPosition": null, "playerIds": ["guid"] } ],
  "games": [ { "id", "order", "teamAId", "teamBId", "teamAGoals", "teamBGoals", "externalWinnerTeamId": null,
               "status", "startedAt", "goals": [ { "id", "scorerPlayerId", "assistPlayerId", "isOwnGoal", "timeSeconds" } ] } ],
  "standings": [ { "position", "teamId", "played", "wins", "draws", "losses", "goalsFor", "goalsAgainst",
                   "goalDifference", "points", "pointsPerGame" } ],
  "currentGameId": null,
  "waitingTeamIds": [],            // fila do fica quem ganha
  "topScorers": [ { "playerId", "goals" } ],
  "topAssists": [ { "playerId", "assists" } ],
  "mvpPlayerIds": [],
  "myInviteResponse": 0, "canVote": false, "myVotes": [ { "gameId": null, "votedPlayerId" } ] }

// CreateChampionshipDto / UpdateChampionshipDto
{ "name", "playedAt", "placeName", "rules": ChampionshipRulesDto }

// DefineTeamsDto
{ "teams": [ { "name": "Azul", "colorId": "guid|null", "playerIds": ["guid"], "goalkeeperPlayerId": "guid|null" } ] }

// NextGameSuggestionDto
{ "teamAId", "teamBId", "reason": "Fica quem ganha: Azul ganhou e fica; entra Verde", "waitingTeamIds": [] }
```

## Fora deste contrato (fases seguintes)
- Apostas do campeonato (mesma tela de apostas; categorias time campeão, menos vazado, artilheiro, maior assistente, MVP).
- Card 🏆 no histórico e estatísticas conforme as regras (Fase 3).
- Tempo real (SignalR) para a tela ao vivo: por enquanto o cliente faz polling de `GET /{id}` a cada ~5 s na tela ao vivo.

## Histórico
- v1 (27/10/2026): primeira versão.
