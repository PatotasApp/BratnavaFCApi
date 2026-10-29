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
- **Validado pelo Luis no ambiente local (29/10/2026).**

## Plano para produção (29/10/2026)
### O que falta subir
| Parte | Branch | Situação |
|---|---|---|
| API | `feature/campeonato-integracao-local` | 12 commits à frente da `main`, sem conflito (contém a `main` inteira). O deploy aplica 4 migrações, **todas só acrescentam**: `GroupSettings.ShowChampionshipsInHistory` (padrão false), `Championships.LinkedPollId`, `ReplayClips.ChampionshipId` e as tabelas de apostas. |
| Site | `feature/campeonato-web` | 12 commits à frente da `main`. O `.env.production` já está com `VITE_CHAMPIONSHIPS_ENABLED=true`: o campeonato aparece assim que o site sobe. |
| App | `feature/campeonato-telas` | 21 commits à frente da `main`. `config/prod.json` está com `CHAMPIONSHIPS_ENABLED: false`: precisa ligar e publicar nova versão nas lojas. |

### Ordem
1. Backup da produção (`pg_dump`), além do `prod-antes-campeonato-20261027-201620.dump` que já existe.
2. API na `main` → deploy aplica as migrações. Conferir que a API antiga dos apps continua funcionando.
3. Site na `main` (já liga o campeonato).
4. **Ensaio:** cópia nova da patota para o local (`scripts/copiar-patota-para-dev.ps1`), rodar o script de produção no local e validar.
5. Importação em produção com o mesmo script, numa transação.
6. Ligar "Mostrar campeonatos no histórico" na patota (a migração cria desligado).
7. App: ligar a flag em `config/prod.json`, gerar e publicar nas lojas. Até sair, o app atual só não mostra o campeonato; as estatísticas já contam para todos.

### Script de produção: cópia, sem tocar nas partidas originais
Decisão do Luis (29/10): em produção **nenhuma das 3 partidas é alterada ou apagada**. O script `scripts/importar-campeonato-27-10.sql`:
- cria o campeonato, os 3 times, os 15 participantes e **3 jogos novos copiados** das originais (mesmo placar, cores, datas e início), com os 30 jogadores que tinham time;
- liga a enquete do dia também ao campeonato (ela continua na J3 original);
- **só faz INSERT**. Ids fixos: pode rodar de novo sem duplicar;
- **trava de entrada:** se placar, cores ou status das originais mudaram, aborta sem gravar nada;
- **trava de saída:** confere 15 participantes, 5 por time com 1 goleiro, 3 jogos, 30 jogadores e as originais intactas; senão desfaz tudo.

As originais estão como Ended (4) e nunca foram finalizadas, então **não contam em estatística**: nada fica em dobro. Elas continuam visíveis na lista de partidas até serem apagadas.

**Replays (decisão do Luis, 29/10):** na primeira tentativa de importar (29/10, ~15:20), a trava de entrada abortou sem gravar nada: o processo das câmeras tinha voltado às 15:14 e estava gerando os 66 replays nas partidas originais. Esperamos terminar (16:36: 66 `Published`, 66 clipes — J1 25, J2 24, J3 17) e o script mudou:
- **nenhum pedido de replay novo** e os originais ficam como estão (`Published`): nada é gerado em dobro;
- os **66 clipes** (`ReplayClips`) passam para o jogo do campeonato correspondente (`MatchId` do jogo novo e `ChampionshipId`). O vídeo no R2, as curtidas e os favoritos não mudam. É a única alteração fora dos INSERTs;
- os 66 lances do campeonato (tipo, time do gol, minuto) apontam no `QueueId` para o pedido original;
- **travas:** entrada exige os 66 pedidos `Published` e 66 clipes; saída exige os 66 clipes nos jogos novos, nenhum nas originais e nenhum pedido de replay novo.

`ReplayClips` não tem chave estrangeira para `Matches`: apagar as originais depois não afeta os clipes. **Atenção:** o `ClipCleanupJob` apaga clipes com mais de 7 dias sem curtida nem favorito; os de 27/10 entram nessa regra a partir de 03/11.

**Ignorados (decisão do Luis):** lembretes (9), notificações (136), convites sem time e recusas. Os convites e recusas não são presença real: são os 10 convidados avulsos chamados automaticamente e, em cada jogo, os jogadores do time que estava de fora (as 5 "recusas" da J3 são o time Azul, que não jogou o jogo 3). Tudo isso fica só nas partidas originais.

**Depois de consolidado** (campeonato finalizado e conferido), apagar as 3 originais num passo separado.

### Ensaio (29/10/2026, versão com clipes) ✅
Backup da produção das 16:38 (`prod-antes-import-27-10-v2-20261029-163803.dump`) restaurado em `bratnavafc_ensaio_v2`. Contagem e hash das 46 tabelas (em `ReplayClips`, sem as colunas que a importação muda):
- 1ª execução: só **+1 campeonato, +3 times, +15 participantes, +3 jogos, +30 jogadores e +66 lances**; todo o resto idêntico, inclusive os pedidos de replay e os clipes (fora partida/campeonato).
- Clipes por jogo 25/24/17, todos com o campeonato; clipes de gol = lances de gol = placar (7, 10, 9). Nenhum clipe sobrou nas originais.
- 2ª execução: **nada mudou**.

### Produção ✅ (29/10/2026, ~16:45)
`importar-campeonato-27-10.sql` rodou em produção. Conferido em somente leitura: "Campeonato 27/10" com status 4 (votação do MVP), 15 participantes, 66 lances; jogos 1/2/3 com 25/24/17 clipes (gols 7/10/9); as 3 originais com status 4, fora do campeonato, sem clipes; pedidos de replay todos `Published`.

Falta: ligar "Mostrar campeonatos no histórico" na patota, votar o MVP e finalizar; depois apagar as 3 originais.

### Ensaio anterior (29/10/2026, versão com cópia dos pedidos — substituída)
Banco separado `bratnavafc_dev_ensaio`, restaurado do backup local e com os dados **atuais da produção** de tudo o que é ligado às 3 partidas (lidos em modo somente leitura). Comparação por contagem e hash de cada tabela:
- 1ª execução: só **+1 campeonato, +3 jogos, +30 jogadores, +66 pedidos de replay e +66 lances**; os 66 pedidos originais viraram `Transferred` (resto da linha idêntico). Partidas originais (linha inteira), 75 jogadores, 9 lembretes, 136 notificações e a enquete: **idênticos**.
- Pedidos copiados: conteúdo aponta para o jogo novo, sem o id antigo; gols marcados por time batem com o placar de cada jogo (J1 4/3, J2 7/3, J3 5/4).
- 2ª execução: **nada mudou**.
- Travas de entrada: com um placar alterado, ou com um pedido original já enviado às câmeras, abortou e **nada foi gravado**. Um erro no meio (teste) também desfez tudo.
