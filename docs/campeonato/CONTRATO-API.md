# Contrato da API do campeonato (Fases 1 e 2)

Status: **v7 (28/10/2026) — implementado no backend (Fases 1, 2 e 3 + cores + respostas do Luis + voltar etapa e lances)**. O backend implementa isto na Fase 1/2 e os clientes (Flutter e web) constroem em cima.
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
| `ChampionshipFormat` | 1 RoundRobin · 2 WinnerStays · 4 RoundRobinWithFinal (o 3, mata-mata, foi **removido**) |
| `WinnerStaysTieRule` | 1 BothLeave · 2 LongestOnFieldLeaves · 3 PenaltyShootout |
| `WinnerStaysLimitRule` | 1 WinnerLeaves (sai só o vencedor) · 2 BothLeave (saem os dois; **com 3 times vira 1 automaticamente**) |
| `StandingMode` | 1 TotalPoints · 2 PointsPerGame |
| `TiebreakerCriteria` | 1 Points · 2 GoalDifference · 3 GoalsFor · 4 HeadToHead · 5 ExternalDecision |
| `ChampionshipGameStatus` | 1 InProgress · 2 Finished |
| `InviteResponse` | 1 None · 2 Rejected · 3 Accepted (igual ao das partidas) |

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
  "winnerStays": { "consecutiveWinsLimit": 2, "tieRule": 2, "limitRule": 1 },
  "gameEnd": { "timeLimitMinutes": 10, "goalLimit": 2 },
  "standings": { "mode": 2, "winPoints": 3, "drawPoints": 1, "tiebreakers": [1, 2, 3, 4, 5] }
}
```
- `placementResults[i]` = resultado de quem termina em `i+1`º. Posições além da lista usam o **último** valor. Só vale com `resultMode = 2`.
- `winnerStays` é obrigatório só com `format = 2`; `roundRobinTurns` só com 1 e 4.
- `gameEnd`: pelo menos um dos dois.
- `standings.tiebreakers`: sem repetição; **o 1º tem de ser 1 (Points)**; o admin ordena só os demais.
- `minGamesForPlacementResult`: o admin escolhe no wizard (padrão 1). Quem jogou menos que isso conta só presença.
- **Não há fim automático**: o campeonato termina quando o admin clica em **"Encerrar campeonato"** (`POST /{id}/finish`). Em seguida aparece a votação do MVP e depois o admin finaliza (`POST /{id}/finalize`).
- Validações do servidor: `resultMode = 1` exige `historyMode = 2`.
- **Presets** (só no cliente, preenchem o formulário):
  - "Fica quem ganha clássico": format 2, limite 0, empate 2, pontos por jogo.
  - "Troca quem ganhou 2": format 2, limite 2, empate 2, `limitRule` 1, pontos por jogo.
  - Com `limitRule` 2 e só 3 times, o cliente avisa: "Com 3 times não dá para saírem os dois; sai só o vencedor."
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
| POST | `/{id}/teams/draw` | `{ "teamCount": 3 }` → `DefineTeamsDto` sugerido (não salva); cores sorteadas entre as ativas da patota; `message` avisa se houver menos cores que times |
| PUT | `/{id}/teams` | `DefineTeamsDto` (vai para TeamDraw e salva) |
| PATCH | `/{id}/players/{playerId}/team` | `{ "teamId": "guid" }` (atrasado/troca; não muda jogos já feitos) |
| POST | `/{id}/teams/colors/draw` | — (sorteia cores ativas da patota, sem repetir; `message` traz o aviso se faltarem cores) |
| PATCH | `/{id}/teams/{teamId}` | `{ "name": "Azul", "colorId": "guid\|null" }` (renomear/trocar cor até finalizar; vale para os próximos jogos) |

### Jogos [admin]
| Método | Rota | Corpo |
|---|---|---|
| POST | `/{id}/start` | — (InProgress; trava regras e fecha apostas) |
| POST | `/{id}/games` | `{ "teamAId": "guid", "teamBId": "guid" }` → `{ "gameId": "guid" }` (já começa) |
| POST | `/{id}/games/{gameId}/goals` | `{ "scorerPlayerId", "assistPlayerId"?, "isOwnGoal", "timeSeconds"? }` |
| DELETE | `/{id}/games/{gameId}/goals/{goalId}` | — |
| PATCH | `/{id}/goalkeeper` | `{ "teamId", "playerId" }` (goleiro do time nos **próximos** jogos; o jogo em andamento não muda) |
| POST | `/{id}/games/{gameId}/end` | `{ "externalWinnerTeamId"? }` (pênaltis/decisão em empate) |
| POST | `/{id}/finish` | — botão **"Encerrar campeonato"** (PostGame: abre a votação de MVP) |
| POST | `/{id}/games/{gameId}/events` | `{ "type": "GolTimeA" \| "GolTimeB" \| "Jogada", "eventTime"?: "ISO-8601", "secondsBeforeStart"?: 15, "durationSeconds"?: 15 }` → `{ "id", "queueId", "teamId", "eventTime", "elapsedSeconds" }` (201). Lance para as câmeras; só com o jogo em andamento. `type` vai como **texto** (igual ao `POST api/Matches/.../events`). GolTimeA/B = times A/B **do jogo**. |

### Voltar etapa [admin]
| Método | Rota | Corpo |
|---|---|---|
| POST | `/{id}/step-back` | — volta para a etapa anterior (204; recusa 400 `{ error }`) |

- Acceptation → Created: apaga a lista de presença (abrir a presença recria).
- TeamDraw → Acceptation: desfaz os times; as presenças continuam.
- InProgress → TeamDraw: **só antes do 1º jogo**; destrava as regras.
- PostGame → InProgress: descarta os votos de MVP; dá para criar mais jogos.
- Finalized → PostGame: tira colocação e MVP; o campeonato deixa de contar nas estatísticas até finalizar de novo.
- Created / Cancelled: "Nao ha etapa anterior."

### Lances para as câmeras
- O pedido de recorte vai pelo mesmo publicador das partidas, com `matchId` = **id do jogo** (o jogo do campeonato é uma linha de `Matches`, então o worker das câmeras não muda).
- Cada lance fica em `ChampionshipGameEvents`: campeonato, jogo, tipo, time do gol, horário (UTC), segundos desde o início do jogo, `queueId` e quem marcou.

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
  "players": [ { "playerId", "name", "photoUrl" /* por enquanto sempre null */, "isGuest", "isGoalkeeper", "inviteResponse", "teamId": null, "didNotPlay": false } ],
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
  "myInviteResponse": 1 /* null se o usuário não participa */, "canVote": false, "myVotes": [ { "gameId": null, "votedPlayerId" } ] }

// CreateChampionshipDto / UpdateChampionshipDto
{ "name", "playedAt", "placeName", "rules": ChampionshipRulesDto }

// DefineTeamsDto
{ "teams": [ { "name": "Azul", "colorId": "guid|null", "playerIds": ["guid"], "goalkeeperPlayerId": "guid|null" } ] }

// NextGameSuggestionDto
{ "teamAId", "teamBId", "reason": "Fica quem ganha: Azul ganhou e fica; entra Verde", "waitingTeamIds": [] }
```

## Histórico com campeonatos (Fase 3)
Os três endpoints de histórico aceitam `?includeChampionships=true` (padrão `false`, para clientes antigos não receberem itens que não sabem abrir):
- `GET api/Matches/group/{groupId}/history`
- `GET api/Matches/group/{groupId}/player-recent`
- `GET api/Matches/group/{groupId}/player-history`

Com o parâmetro, os itens de campeonato vêm misturados às partidas, por data, com campos extras (só presentes nesses itens):

```jsonc
// MatchHistoryItemDto (history)
{ /* campos de sempre */, "kind": "championship" | "championshipGame", "championshipId", "championshipName",
  "championTeamName", "championTeamColorHex", "ruleBadges": [...] }

// PlayerRecentMatchDto (player-recent / player-history)
{ /* campos de sempre */, "kind": "championship" | "championshipGame", "championshipId", "championshipName",
  "playerResult": "W" | "D" | "L" | "N" }
```
- `kind = "championship"` (consolidado): `id`/`matchId` é o **id do campeonato**; abrir pela tela do campeonato. Placar vem 0x0 e não deve ser mostrado; no item do jogador, `teamAColorHex`/`teamAColorName` são a cor e o nome do time dele, e o resultado está em `playerResult` (pela regra, não pelo placar).
- `kind = "championshipGame"`: `id`/`matchId` é o **id do jogo**; placar e cores são do jogo, mas abrir pela tela do campeonato (`championshipId`), porque o detalhe de partida não enxerga jogos de campeonato.
- `playerResult = "N"`: só presença (sem V/E/D).
- Gols/assistências/MVP do jogador nesses itens já seguem a regra do campeonato (se não contam, vêm 0/false).

## Fora deste contrato (fases seguintes)
- Apostas do campeonato (mesma tela de apostas; categorias time campeão, menos vazado, artilheiro, maior assistente, MVP).
- Tempo real (SignalR) para a tela ao vivo: por enquanto o cliente faz polling de `GET /{id}` a cada ~5 s na tela ao vivo.

## Histórico
- v1 (27/10/2026): primeira versão.
- v2 (27/10/2026): **corrigido `InviteResponse`** (1 None · 2 Rejected · 3 Accepted, como no resto da API).
- v3 (27/10/2026): implementado. Mudanças: goleiro passou para `PATCH /{id}/goalkeeper` (por time, vale para os próximos jogos); `myInviteResponse` é null para quem não participa; `photoUrl` ainda vem null; `POST /` responde `{ id }` com 201; `teams/draw` é provisório (goleiros primeiro, depois "cobra" pela nota).
- v4 (27/10/2026): Fase 3. Histórico com `includeChampionships` e campos `kind`, `championshipId`, `championshipName`, `championTeamName`, `championTeamColorHex`, `ruleBadges`, `playerResult`. Nada mudou nos endpoints do campeonato.
- v5 (27/10/2026): cores dos times. `POST /{id}/teams/colors/draw` e `PATCH /{id}/teams/{teamId}`; `teams/draw` sorteia as cores. Só cores **ativas da própria patota** são aceitas (em `PUT /teams` também). Quando há menos cores que times, a resposta tem `success: true` e o aviso vem em `message` (ex.: "Ha so 2 cor(es) ativa(s) para 3 times: 1 time(s) ficaram sem cor. Cadastre mais cores em Cores dos times."). O cliente deve mostrar esse `message` ao admin.
- v6 (27/10/2026): respostas do Luis. **Removidos** o formato mata-mata (3) e `championshipEnd` (o fim é sempre o botão "Encerrar campeonato"). **Novo** `winnerStays.limitRule` (1 sai só o vencedor, 2 saem os dois; com 3 times o 2 vira 1). `standings.tiebreakers[0]` tem de ser Points. `minGamesForPlacementResult` vai para o wizard.
- v7 (28/10/2026): **novo** `POST /{id}/step-back` (voltar etapa) e `POST /{id}/games/{gameId}/events` (lances para as câmeras, gravados em `ChampionshipGameEvents`; migration `AddChampionshipGameEvents`, só cria tabela).
