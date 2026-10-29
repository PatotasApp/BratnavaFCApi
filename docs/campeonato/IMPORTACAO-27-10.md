# Importação do campeonato de 27/10/2026

Antes do Modo Campeonato, o campeonato de 27/10 foi registrado como **3 partidas comuns** na base oficial. Este documento mapeia essas partidas para um campeonato, para importar **primeiro no banco local** e depois em produção.

Leitura feita em 29/10/2026 na base oficial, **somente leitura** (`default_transaction_read_only=on`). Patota `3f401edf-d309-4bae-97d8-28eae0da7a8a`.

## O que existe hoje na base oficial
As 3 partidas estão com status **4 (Ended)**: encerradas, mas nunca finalizadas. Por isso **hoje não contam em nenhuma estatística**. Todas são de 27/10 às 20:30, no Boca Jrs, com placar mas **sem gols individuais e sem votos de MVP**.

| Jogo | Partida (id) | Placar | Última atualização |
|---|---|---|---|
| 1 | `a110819e-989d-425d-81ac-479cb7b6353e` | Laranja **4 x 3** Azul | 28/10 00:08 |
| 2 | `e8280212-2dfd-4055-ab3f-d7093a677928` | Preto **7 x 3** Azul | 28/10 00:30 |
| 3 | `83100247-218e-4ce7-8d8e-06479bb7b7ea` | Preto **5 x 4** Laranja | 28/10 00:54 |

A ordem segue a última atualização de cada partida (quando o placar foi fechado). A data de criação não serve: o jogo 3 foi criado em 21/10, como a partida normal do dia.

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
- **MVP:** do campeonato, **com votação aberta** (`MvpMode` 1). Importa em **pós-jogo** (`Status` 4, PostGame); os jogadores votam e o admin finaliza pelo app.
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

**Em produção**, só depois do deploy das migrações do campeonato. Mesmo roteiro, com backup antes.

## Pendente
- **Nome do campeonato:** ainda não definido (sugestão: "Campeonato 27/10").
