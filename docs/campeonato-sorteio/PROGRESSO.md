# Progresso — sorteio de N times do campeonato

Branch: `feature/campeonato-sorteio`

Último commit de implementação: `1864f1b` (`Integra sorteio múltiplo ao campeonato`)

| # | Etapa | Status |
|---|---|---|
| 1 | Isolamento, leitura das regras e baseline | ✅ |
| 2 | Contratos e validações do sorteio de N times | ✅ |
| 3 | Estratégia aleatória determinística | ✅ |
| 4 | Estratégia equilibrada e busca local | ✅ |
| 5 | Testes funcionais e de desempenho | ✅ |
| 6 | Suíte completa, revisão de isolamento e commits | ✅ |
| 7 | Mesclar as decisões e o contrato v6 | ✅ |
| 8 | Ligar o motor ao `ChampionshipService.DrawTeamsAsync` | ✅ |
| 9 | Preservar o aviso no envelope da rota de cores | ✅ |
| 10 | Suíte v6, revisão e commit da integração | ✅ |

## Decisões de algoritmo

- A implementação ficará autocontida em `TeamGeneration/MultiTeam` e dependerá somente de `IPlayerStatsService`.
- As primeiras estratégias serão `Random` e `Balanced`. Perfil multidimensional fica extensível, mas fora desta entrega por não haver pesos definidos para N times.
- As capacidades dos times serão calculadas antes do sorteio; cada time terá `floor(jogadores/times)` ou `ceil(jogadores/times)` jogadores.
- Os goleiros titulares serão alocados antes da linha. No máximo um será marcado como goleiro por time; excedentes entram no mesmo conjunto dos jogadores de linha.
- A força de quem não tiver estatística será a média das forças conhecidas do grupo, nunca zero.
- O equilibrado partirá de uma distribuição cobra e aceitará apenas trocas que reduzam a diferença entre o time mais forte e o mais fraco.
- A aleatoriedade será injetável por semente para permitir reprodução exata nos testes.
- O primeiro resultado equilibrado parte da cobra canônica; opções adicionais partem de distribuições semeadas e passam pela mesma busca local.
- A força conhecida segue a base do `AlgorithmStrategy`: `WinRate` com histórico suficiente ou `NeutralOverride` para neutros avaliados. Neutros sem avaliação recebem a média do grupo; se ninguém tiver avaliação, a média neutra é `0,50`.
- Quando há mais goleiros que times, os de maior força efetiva são os titulares; os demais permanecem em `PlayerIds`, mas não em `GoalkeeperPlayerId`, portanto entram como linha.
- `ChampionshipService.DrawTeamsAsync` pede a estratégia `Balanced`, converte a melhor opção em `DefineTeamsDto` e aplica nomes/cores depois do sorteio.
- O aviso de poucas cores continua no `message` tanto em `teams/draw` quanto em `teams/colors/draw`; a segunda rota usa um `Result<object?>` para impedir que o helper de respostas transforme o sucesso em 204.

## Dúvidas para o Luis

- Nenhuma no momento. A integração com `ChampionshipService` ficará para a outra IA, conforme o briefing.

## O que falta

- Nada. Motor e integração estão prontos nesta branch, sem push.

## Baseline

- `dotnet test BranavaFCTests/BranavaFC.Tests.csproj`: 1582 aprovados, 7 ignorados e 0 falhas.
- Docker/Testcontainers funcionando.
- Avisos preexistentes: nulabilidade, funções locais não usadas e vulnerabilidades conhecidas do `SixLabors.ImageSharp` 3.1.11.

## Validação parcial

- `dotnet build src/BratnavaFC.Application/BratnavaFC.Application.csproj`: aprovado.
- 16 testes novos aprovados em 309 ms.
- Cobertura nova: 3/4/5 times, quantidade ímpar, 0/1/N/N+2 goleiros, ausência parcial e total de notas, aleatório com semente, equilíbrio contra cobra, validações e 30 jogadores em 6 times abaixo de 1 segundo.

## Validação final

- `dotnet format --verify-no-changes` nos arquivos novos de aplicação e testes: aprovado.
- `dotnet test BranavaFCTests/BranavaFC.Tests.csproj`: 1598 aprovados, 7 ignorados e 0 falhas, incluindo os testes `Category=Postgres` no Docker.
- Na entrega isolada do motor, o código ficou somente em `TeamGeneration/MultiTeam` e os testes em `MultiTeamGeneration*Tests.cs`. A integração abaixo foi autorizada depois pelo Luis.

## Integração autorizada pelo Luis

- O ref `feature/campeonato-fase0-blindagem` avançou até `73b47ad`; foi mesclado nesta branch pelo commit de merge do contrato v6 antes da integração.
- `MultiTeamGenerationService` registrado como scoped e injetado no `ChampionshipService`.
- `POST /{id}/teams/draw` preserva `DefineTeamsDto` em `data` e o aviso de cores em `message`.
- `POST /{id}/teams/colors/draw` agora responde 200 com envelope e não perde a mensagem.
- Validação direcionada: 34 testes aprovados, incluindo dois testes novos de integração (controller e Postgres).
- Validação final no contrato v6: 1597 aprovados, 7 ignorados e 0 falhas.
- Formatação das alterações próprias verificada. O formatador ainda aponta somente trechos preexistentes trazidos pelo merge v6 em `ChampionshipService` e `CampeonatoFluxoTests`; eles não foram reformatados para evitar ruído fora da integração.
- Commits da continuação: `eca21c9` (merge v6) e `1864f1b` (integração).
