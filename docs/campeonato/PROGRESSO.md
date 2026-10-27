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
3. Próximo: ver a seção das Fases 1 e 2 abaixo.

## Fases 1 e 2 — Backend do campeonato ✅ (27/10/2026)
Motor mesclado nesta branch (`51048fa`). Contrato: [CONTRATO-API.md](CONTRATO-API.md) **v3** (implementado).

| # | Passo | Status |
|---|---|---|
| 1 | Merge do motor | ✅ |
| 2 | Domínio: `ChampionshipRuleSet`, times, participantes, votos, fluxo completo | ✅ |
| 3 | Persistência: 3 tabelas novas + colunas em `Matches`; migração única `AddChampionships` (substitui a da Fase 0, que nunca foi publicada) | ✅ |
| 4 | `ChampionshipService` (presença, sorteio provisório, times, jogos, próximo jogo pelo motor, classificação, MVP, finalização, cancelamento) | ✅ |
| 5 | `ChampionshipsController` em `api/championships/group/{groupId}` | ✅ |
| 6 | Testes de fluxo (`CampeonatoFluxoTests`): fica quem ganha completo, mata-mata 4 times, regras inválidas e cancelamento | ✅ passam no Postgres real (Testcontainers) |
| 7 | Suíte completa | ✅ 1562 aprovados, 0 falhas, 7 ignorados (já eram) |

### Como funciona (resumo para quem continuar)
- **Regras** (`Domain/Championships/ChampionshipRuleSet.cs`): um record que é o próprio DTO do contrato, guardado como `jsonb` em `Championships.RulesJson`. `Validate()` usa as opções do motor. `DefaultRules` = fica quem ganha, conta como 1 partida, 1º V · 2º E · 3º+ D, MVP do campeonato, fora do sorteio.
- **Fluxo** (`ChampionshipEntity`): Created → Acceptation (convida todos os ativos não convidados) → TeamDraw (`DefineTeams`) → InProgress (`Start`, trava as regras) → PostGame (`Finish`, votação) → Finalized (posições + MVP com a `MvpTieRule` da patota). `Cancel` só sem jogo encerrado.
- **Jogos**: `CreateGame` monta uma `MatchEntity` com `ChampionshipId`, times A/B, ordem, escalação atual (sem `DidNotPlay`), goleiros e cores, e já inicia. `EndGame` encerra e finaliza o placar na hora; MVP fica para o fim do campeonato. Um jogo por vez.
- **Próximo jogo**: motor conforme o formato (fica quem ganha, tabela, final, mata-mata). Se o admin criar jogos fora da ordem, a sugestão vira null e ele escolhe na mão.
- **Empate**: exige vencedor externo (pênaltis) no mata-mata, na final do "todos contra todos + final" e no fica quem ganha com regra de pênaltis.
- **Posição final**: mata-mata = campeão, vice, depois quem caiu mais tarde (empatados dividem); com final = final + classificação; demais = classificação.
- **Acesso aos jogos**: só `ChampionshipService.GamesQuery()` ignora o filtro global (na allowlist do teste de arquitetura).
- Ids das tabelas novas com `ValueGeneratedNever`: filho novo em agregado rastreado vira INSERT (não UPDATE). Gols novos são marcados como `Added` à mão.

### Pendências
- `photoUrl` dos participantes ainda vem null.
- Sorteio de N times é provisório (goleiros primeiro, depois "cobra" pela nota). O sorteio real está no [PROMPT-OUTRA-IA-4.md](PROMPT-OUTRA-IA-4.md) (outra IA, branch `feature/campeonato-sorteio`); depois eu ligo no `DrawTeamsAsync`.
- Cores dos times (pedido do Luis, 27/10): falta sortear cores aleatórias como nas partidas, avisar quando houver mais times que cores ativas, e permitir trocar nome/cor do time depois de iniciado. Fazer logo após a Fase 3.
- Sem notificações push nem tempo real (o cliente faz polling).
- Ausências (`UserAbsence`) ainda não recusam o convite do campeonato automaticamente.
- Respostas às 4 perguntas de regra do motor (seção abaixo).
## Fase 3 — Estatísticas respeitando as regras ✅ (27/10/2026)
Regras de contagem: [DECISOES.md](DECISOES.md), seção "Fase 3". Contrato do histórico: [CONTRATO-API.md](CONTRATO-API.md) v4.

| # | Passo | Status |
|---|---|---|
| 1 | Retrato golden das estatísticas sem campeonato, **antes** de refatorar (`Golden/estatisticas-sem-campeonato.txt`) | ✅ |
| 2 | Fonte única `StatMatch` + `ChampionshipStatsSource` (campeonatos finalizados → unidades pelas regras) | ✅ |
| 3 | Relatório visual e spotlight lendo da fonte única | ✅ golden idêntico |
| 4 | Conquistas/projeções com linhas de campeonato (`ForChampionship`, resultado "N" = só presença) | ✅ |
| 5 | Histórico geral, recentes e histórico do jogador com `?includeChampionships=true` | ✅ |
| 6 | Sorteio (`EnrichPlayersAsync`) só com `IncludeInTeamDraw`; team builder só com jogos que contam como partida normal | ✅ |
| 7 | Reconstrução das conquistas ao finalizar o campeonato | ✅ |
| 8 | Matriz `CampeonatoEstatisticasTests`: 8 combinações de regras + campeonato não finalizado; histórico = relatório = conquistas, jogador a jogador | ✅ |
| 9 | Suíte completa | ✅ 1575 aprovados, 0 falhas |

### Para quem continuar
- Arquivos: `src/BratnavaFC.Application/Statistics/` (`StatMatch`, `ChampionshipStatsSource`, `ChampionshipHistoryItems`).
- A linha de contribuição do consolidado fica presa ao **último jogo** do campeonato (FK para `Matches`). O Postgres pegou isso; o banco em memória não pegaria.
- O retrato golden só deve mudar de propósito: `UPDATE_GOLDEN=1 dotnet test --filter EstatisticasSemCampeonato`.
- **Os clientes (Flutter/web) ainda não usam `includeChampionships`**: falta o card 🏆 no histórico e o tratamento de `kind` nas listas do jogador.

### Próximos passos
- Cores dos times (pedido do Luis) e ligar o sorteio de N times da outra IA.
- Apostas do campeonato (mesma tela; time campeão, menos vazado, artilheiro, maior assistente, MVP).
- Clientes: card 🏆 no histórico + `kind` nas listas do jogador (bom para a outra IA, seguindo o contrato v4).

## Disco cheio (27/10, ~16h) — resolvido
O C: chegou a 0 GB livres no meio da Fase 3. `ChampionshipRuleSet.cs` foi truncado e restaurado do git; nenhum outro arquivo foi afetado. O Luis autorizou a limpeza de caches (NuGet, npm, Gradle) e de imagens/emuladores Android sem uso: ~49 GB liberados.

## Trabalho paralelo
- **Motor de formatos e classificação** ✅: outra IA, branch `feature/campeonato-motor` (commits `c068138` + `87033ce`), worktree `C:\Users\lhnme\source\repos\BratnavaFCApi-campeonato-motor`.
  - Validado em 27/10: 24 testes do motor e 1554 da suíte passando, só arquivos em `src/BratnavaFC.Domain/Championships/` e `BranavaFCTests/ChampionshipEngine*`, sem arquivo em comum com esta branch (merge limpo).
  - Revisão: código bom. Perguntas de produto para o Luis antes de ajustar:
    1. Fica quem ganha com limite: hoje **sai só o vencedor** e o perdedor fica. Com 4+ times, deveriam **sair os dois**?
    2. "Todos jogaram a mesma quantidade" encerra **no primeiro momento** em que as contagens empatam. O esperado não seria "acabou o tempo, joga até igualar"?
    3. Mata-mata com nº ímpar: quem passa direto é **o último da lista**. Deveria ser o melhor colocado/sorteado?
    4. Desempate: hoje "Pontos" pode ficar em qualquer posição da lista; travar como 1º critério?
- **Telas do campeonato no app Flutter** ✅: outra IA, branch `feature/campeonato-telas` (`515a2bd` + `33cd4a8`), worktree `C:\Users\lhnme\source\repos\BratnavaFCApp-campeonato`.
  - Validado em 27/10: só arquivos da feature + menu/rota/flag; flag ligada em dev e desligada em prod; rotas idênticas ao `ChampionshipsController`; enums e chaves JSON iguais ao contrato.
  - A revisão achou dois desvios **no backend**, já corrigidos em `1f49cd1`: `topScorers`/`topAssists` com `count` em vez de `goals`/`assists`, e `minimumTeams` vazando no JSON das regras. Novo `CampeonatoContratoTests` trava as chaves.
  - O `flutter test` não rodou aqui (sem memória/disco na máquina); vale o relatório dela: 115 aprovados.
  - Pendência dela: o convidado é adicionado digitando o ID (falta um seletor de jogadores).
- **Telas do campeonato no front web** ⬜: prompt em [PROMPT-OUTRA-IA-3.md](PROMPT-OUTRA-IA-3.md). Mandar só depois de liberar disco.
