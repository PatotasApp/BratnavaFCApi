# Importação do campeonato de 27/10/2026

Antes do Modo Campeonato, o campeonato de 27/10 foi registrado como **3 partidas comuns** na base oficial. Este documento mapeia essas partidas para um campeonato, para importar **primeiro no banco local** e depois em produção.

Leitura feita em 29/10/2026 na base oficial, **somente leitura** (`default_transaction_read_only=on`). Patota `3f401edf-d309-4bae-97d8-28eae0da7a8a`.

## O que existe hoje na base oficial
As 3 partidas estão com status **4 (Ended)**: encerradas, mas nunca finalizadas. Por isso **hoje não contam em nenhuma estatística**. Todas são de 27/10 às 20:30, no Boca Jrs, com placar mas **sem gols individuais e sem votos de MVP**.

| Jogo | Partida (id) | Placar | Início real (Brasília) |
|---|---|---|---|
| 1 | `a110819e-989d-425d-81ac-479cb7b6353e` | Laranja **4 x 3** Azul | 20:47 |
| 2 | `e8280212-2dfd-4055-ab3f-d7093a677928` | Preto **7 x 3** Azul | 21:09 |
| 3 | `83100247-218e-4ce7-8d8e-06479bb7b7ea` | Preto **5 x 4** Laranja | 21:31 |

A ordem está confirmada pelo início real (`ActualStartTime`). O jogo 3 é a partida normal do dia (criada em 21/10): é ele que tem a enquete vinculada e as 5 recusas de presença.

### Tudo o que está ligado às 3 partidas
| Dado | O que tem | No campeonato |
|---|---|---|
| Partidas | 3, status 4 (Ended), placar e cores, `ActualStartTime` | Viram os jogos 1 a 3 (mesmos ids), status 6 |
| Jogadores com time | 10 por jogo (5 x 5), todos com presença confirmada, 1 goleiro por time | 15 participantes confirmados, nos 3 times |
| Convites sem time | J1 e J2: 15 cada (sem resposta). J3: 10 sem resposta + 5 recusas | Ficam fora (só quem jogou entra) |
| Gols | **Nenhum** (só o placar) | Artilharia/assistência vazias |
| Votos de MVP / MVP | **Nenhum** | Votação aberta no campeonato |
| Apostas | **Nenhuma** | — |
| Replays (clipes) | **Nenhum** clipe gerado | — |
| Pedidos de replay | 66 na fila, todos **Pending**: gols por time (J1 4/3, J2 7/3, J3 5/4, batem com o placar) e 40 "Jogada" | Ficam onde estão (ligados ao id do jogo, que não muda) |
| Enquete | J3 tem a enquete "Churrasco Luís e Andrei" (evento, aberta) | Passa para o campeonato (`LinkedPollId` do campeonato) |
| Conquistas/estatística | Nenhuma linha (as partidas nunca foram finalizadas) | Contam ao finalizar o campeonato |
| Cores | Preto `#000000`, Azul `#00CBF8`, Laranja `#FA810A` (ativas) | Cores dos 3 times |

## Times (iguais nos 3 jogos)
| Time | Goleiro | Linha |
|---|---|---|
| **Preto** | Artur | Caio, Pagel, Patrick, Piccoli |
| **Azul** | Luis | Jonathan, Luska, Marlon, Pedro Roweder |
| **Laranja** | Ademir (convidado) | Andrei Salvador, Little Piccoli, Matheus, Mozart |

15 jogadores. Cada partida também tem 15 convites sem time (pessoas que não foram); eles **não** entram no campeonato.

## Classificação (todos contra todos, 1 turno, vitória 3 pontos)
| Pos. | Time | J | V | E | D | GP | GC | SG | Pts |
|---|---|---|---|---|---|---|---|---|---|
| 1º | **Preto** 🏆 | 2 | 2 | 0 | 0 | 12 | 7 | +5 | 6 |
| 2º | Laranja | 2 | 1 | 0 | 1 | 8 | 8 | 0 | 3 |
| 3º | Azul | 2 | 0 | 0 | 2 | 6 | 11 | −5 | 0 |

## Regras escolhidas pelo Luis (29/10)
- **Histórico:** conta como **uma única partida** (`HistoryMode` 1, Consolidated).
- **Resultado:** por colocação, **Preto vitória e os demais derrota** (`ResultMode` 2, `PlacementResults` [1 Win, 3 Loss]).
- **MVP:** do campeonato, **com votação aberta** (`MvpMode` 2, PerChampionship). Importa em **pós-jogo** (`Status` 4, PostGame); os jogadores votam e o admin finaliza pelo app.
- **Formato:** todos contra todos, 1 turno (`Format` 1).
- **Gols:** não existem gols individuais; `GoalsCountInGeneralStats` = false. Artilharia e assistências ficam vazias.
- Demais: padrão (`MvpCountsInGeneralStats` true, `IncludeInTeamDraw` false, mínimo de 1 jogo para a colocação).

Efeito nas estatísticas, depois de finalizado: cada um dos 15 ganha **1 partida**. Os 5 do Preto ficam com vitória e os outros 10 com derrota. O MVP votado ganha 1 MVP.

## Como importar (banco local primeiro)
A ideia é **transformar as 3 partidas existentes**, mantendo os mesmos ids, para que replays ou clipes ligados a elas continuem funcionando:
1. Copiar da base oficial para o local as 3 partidas e só os 15 `MatchPlayers` com time. O banco local é uma cópia anterior a 27/10, então elas ainda não estão lá. Os jogadores já existem no local.
2. Criar o campeonato no local com as regras acima: status PostGame, `PlayedAt` 27/10 20:30, local Boca Jrs, com os 3 times (cores Preto, Azul e Laranja da patota), os 15 participantes confirmados no time certo e os goleiros.
3. Ligar as 3 partidas ao campeonato: `ChampionshipId`, `ChampionshipTeamAId`/`BId` pela cor, `ChampionshipGameOrder` 1 a 3 e `Status` 6 (Finalized). Com isso elas saem das listas de partidas comuns, pelo filtro global.
4. Conferir no app e no site: classificação, card no histórico, votação de MVP. Finalizar pelo app.

**Em produção**, só depois que a `feature/campeonato-integracao-local` for para a `main` (o deploy aplica as migrações que faltam). Mesmo roteiro, com backup antes.

## Produção já tem o campeonato (deploy de 28/10)
Em 28/10, a pedido do Luis, a `main` foi para produção até o commit `6bdc626` (hoje a `origin/main` está em `8daffce`). O deploy (`fly-deploy.yml`) aplica as migrações antes de publicar. Por isso a produção já tem `AddChampionships` e `AddChampionshipGameEvents`. Antes do deploy foram feitos a tag `pre-campeonato-20261027` e o backup `BratnavaFC-db-backups\prod-antes-campeonato-20261027-201620.dump`.

As migrações mais novas (`AddShowChampionshipsInHistory`, `AddChampionshipLinkedPoll`, `AddReplayClipChampionship`, `AddChampionshipBets`) só estão na `feature/campeonato-integracao-local` e no banco local. Para importar em produção, essa branch precisa ir antes para a `main` (o deploy aplica as migrações).

## Importação no banco local ✅ (29/10/2026)
- Nome **"Campeonato 27/10"**, id `bac18efd-395e-537c-8dff-122e7e273c57` (fixo, gerado do nome do evento). Times: Preto `aad22248-…`, Laranja `89e79b46-…`, Azul `67d84b57-…` (ordem 1, 2, 3).
- Backup antes: `BratnavaFC-db-backups\dev-antes-import-27-10-20261029-092343.dump`.
- Feito numa transação: a J3 antiga do banco local (de antes do jogo) foi trocada pela versão da produção; J1 e J2 foram copiadas; as 3 viraram jogos 1 a 3 (status 6) com só os 10 jogadores de cada jogo; a enquete passou para o campeonato.
- Conferido no banco: 3 times de 5 com os goleiros certos, jogos na ordem, placares iguais à produção.
- Próximo: abrir no app/site, conferir a classificação (Preto 1º), votar o MVP e finalizar.

## Para produção (não é o mesmo script)
O script local **apaga e recria** as partidas. Em produção isso apagaria os 66 pedidos de replay ligados a elas. Lá o roteiro tem de **atualizar** as 3 partidas no lugar (ligar ao campeonato, status 6, tirar a enquete do jogo) e apagar só os `MatchPlayers` sem time, depois da `feature/campeonato-integracao-local` ir para a `main` e com backup antes.
