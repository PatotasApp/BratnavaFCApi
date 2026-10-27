# Modo Campeonato — ponto de partida

Pasta de passagem: qualquer pessoa ou IA que pegar este trabalho deve começar por aqui.

| Arquivo | O que tem |
|---|---|
| [DECISOES.md](DECISOES.md) | Tudo que o Luis decidiu. Não reabrir sem perguntar a ele. |
| [PLANO.md](PLANO.md) | Arquitetura e as fases 0 a 5. |
| [PROGRESSO.md](PROGRESSO.md) | O que já foi feito, o que falta, e como retomar. **Atualizar sempre.** |
| [PARALELO.md](PARALELO.md) | Trabalhos que outra IA pode fazer ao mesmo tempo sem conflito. |

## Contexto em 5 linhas
- Repositórios: `BratnavaFCApi` (C# .NET 8, EF Core, Postgres), `BratnavaFCApp` (Flutter, app principal) e `BratnavaFCFront` (React + Vite, cliente gerado em `src/api/generated`).
- Hoje uma partida (`MatchEntity`) tem sempre 2 times (`MatchPlayerEntity.Team` = 1 ou 2).
- Campeonato = entidade-mãe (`ChampionshipEntity`); cada jogo é uma `MatchEntity` com `ChampionshipId`.
- **Risco nº 1:** as estatísticas leem `Matches` finalizadas direto em vários lugares. Sem blindagem, 1 campeonato apareceria como 3 partidas. Por isso a Fase 0 existe.
- Branch de trabalho: `feature/campeonato-fase0-blindagem` (criada a partir de `main` em 27/10/2026). Nada vai para `main` sem o Luis pedir.

## Regras de convivência
- Nunca commitar `src/BratnavaFC.Api/appsettings*.json`: há alterações locais do Luis neles.
- Mensagens de commit e textos para o usuário em português.
- Duas IAs nunca editam os mesmos arquivos ao mesmo tempo (ver PARALELO.md).
