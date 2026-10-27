# Progresso — sorteio de N times do campeonato

Branch: `feature/campeonato-sorteio`

Último commit de implementação: `f435cb9` (`Implementa sorteio equilibrado de múltiplos times`)

| # | Etapa | Status |
|---|---|---|
| 1 | Isolamento, leitura das regras e baseline | ✅ |
| 2 | Contratos e validações do sorteio de N times | ✅ |
| 3 | Estratégia aleatória determinística | ✅ |
| 4 | Estratégia equilibrada e busca local | ✅ |
| 5 | Testes funcionais e de desempenho | ✅ |
| 6 | Suíte completa, revisão de isolamento e commits | ✅ |

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
- Quando há mais goleiros que times, os primeiros na ordem dos jogadores confirmados são os titulares; os demais permanecem em `PlayerIds`, mas não em `GoalkeeperPlayerId`, portanto entram como linha.

## Dúvidas para o Luis

- Nenhuma no momento. A integração com `ChampionshipService` ficará para a outra IA, conforme o briefing.

## O que falta

- Nada neste escopo. A outra IA ainda precisa registrar/injetar o serviço e converter a opção escolhida em `DefineTeamsDto` no `ChampionshipService`.

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
- Isolamento revisado: código somente em `TeamGeneration/MultiTeam`, testes somente em `MultiTeamGeneration*Tests.cs` e este arquivo de acompanhamento.
