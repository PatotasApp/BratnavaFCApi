# Temporadas

## Decisões (Luis, 02/11/2026)

- A temporada é um **período de datas** da patota, `[StartsAt, EndsAt)`. A partida (e o campeonato) pertence à temporada pela data do jogo (`PlayedAt`, horário de Brasília). Não há chave de temporada na partida: corrigir um corte recalcula tudo sozinho.
- Os períodos da patota ficam **encostados**, sem buraco nem sobreposição, e só um fica aberto (`EndsAt` nulo; índice único filtrado).
- **Ano da temporada** = ano (Brasília) em que ela começa. A pré-temporada aberta em 21/11/2026 é **2026/2**.
- **Nome**: o dado pelo admin; sem nome, `2026` enquanto o ano tiver uma temporada só, e `2026/1`, `2026/2` depois.
- **Pré-temporada** (flag): conta na carreira e no sorteio de times, mas sai dos filtros do ano (opção "incluir pré-temporadas") e não gera título.
- **Virada do ano**: automática só enquanto nenhum admin fechou temporada na patota (`ClosedByUserId` preenchido em alguma). Com admin no controle, a temporada aberta continua até ele abrir a próxima.
- **Títulos**: de temporada (selados no fechamento, fase 4) e do ano (somando as temporadas regulares). Ano com uma temporada só mostra uma vez. Ao fechar, aparece a **classificação real**, sem o "top 4%".

## Fase 1 (esta entrega)

API `api/seasons/group/{groupId}`:

| Rota | Quem | O quê |
|---|---|---|
| `GET /` | membro | Temporadas (antiga → nova), atual, jogos e campeonatos finalizados em cada, `manualMode`, `canUndoClose` |
| `GET /close-preview?cutAt=` | admin | Resumo do fechamento com esse corte, sem gravar |
| `POST /close` | admin | Fecha a aberta no corte (padrão agora; até 31 dias à frente) e abre a próxima (`nextName`, `nextIsPreSeason`) |
| `PUT /{id}` | admin | Nome e pré-temporada |
| `PUT /{id}/start` | admin | Move o corte com a anterior (fica no ano da temporada) |
| `POST /undo-close` | admin | Desfaz o último fechamento feito por admin |

- Migração `AddSeasons`: uma temporada por ano desde o primeiro jogo (partida ou campeonato) até o ano atual, aberta. Patota sem jogos: só o ano atual.
- Jogo lançado com data anterior à primeira temporada: os anos que faltam são criados na leitura.

Site: aba **Temporadas** em Configurações da patota.

## Próximas fases

2. Estatísticas e conquistas por temporada (seletor único no site; `year` antigo continua funcionando).
3. App Flutter.
4. Retrospectiva no fechamento: sela os títulos da temporada, mostra a classificação real, notificação e card para compartilhar.
