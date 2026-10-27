# Decisões tomadas (Luis, 27/10/2026)

## Produto
1. O campeonato é **separado das partidas**, com menu próprio. O admin cria escolhendo data e local, como num jogo normal.
2. **Vai nos dois clientes:** app Flutter e front web.
3. **Regras definidas na criação**, editáveis até o 1º jogo começar e travadas depois. Reabrir um campeonato finalizado é ação explícita do admin e reconstrói as projeções.
4. As regras **aparecem na listagem de partidas/histórico** como selos ("Conta como 1 partida", "1º V · 2º E · 3º D", "MVP do campeonato"...).
5. **Estatísticas têm que obedecer as regras.** Se a regra diz "conta como 1 partida", não pode aparecer 3 partidas em lugar nenhum: histórico, dashboard, relatório visual, spotlight, conquistas, team builder, sorteio.

## Regras parametrizáveis
| Regra | Opções |
|---|---|
| Como aparece no histórico | **Consolidado** (1 card, conta como 1 partida) · **Jogos separados** (cada jogo é uma partida com selo) · **Só no campeonato** (fora do histórico e das estatísticas gerais) |
| Resultado | **Por jogo** (V/E/D de cada jogo; só com "jogos separados") · **Por colocação** (mapa editável; padrão 1º = V, 2º = E, 3º+ = D) · **Vitória para todos** · **Só presença** |
| MVP | Por jogo · Por campeonato · Nenhum. Também: se conta no total geral de MVPs |
| Gols/assistências nas estatísticas gerais | Sim / Não (padrão sugerido: Não; a artilharia do campeonato fica separada) |
| Entra no sorteio de times (`EnrichPlayersAsync`) | **Padrão: Não** (aprovado pelo Luis) |
| Formato | Todos contra todos (1 ou 2 turnos) · Fica quem ganha · Mata-mata · Todos contra todos + final. **Parametrizável** |
| Limite de vitórias seguidas (fica quem ganha) | 0 = sem limite · 2 = "troca quem já ganhou duas" · N |
| Empate no fica quem ganha | Saem os dois · sai quem está há mais tempo em campo · pênaltis |
| Fim de cada jogo | Tempo e/ou limite de gols |
| Fim do campeonato | Tempo total · nº de jogos · todos jogaram a mesma quantidade |
| Classificação | Pontos totais · pontos por jogo (padrão no fica quem ganha) |
| Pontuação e desempate | Pontos de V/E; ordem: pontos → saldo → gols pró → confronto direto → sorteio/pênaltis |

Presets prontos: "Fica quem ganha clássico", "Troca quem ganhou 2".

## Nome da regra "todos ganham"
- **"Vitória para todos"**, com a legenda: *"Cada participante ganha 1 vitória nas estatísticas."*
- Conta como **vitória de verdade** (win rate, sequências, conquistas). Não é "só presença".

## Apostas
- **Só no campeonato como um todo**, nunca por jogo. Os jogos do campeonato não aceitam aposta.
- **Na mesma tela de apostas que já existe:** o campeonato entra no seletor da aba "Aposta Atual" com 🏆. "Histórico" e "Ranking" são as mesmas abas, e as fichas usam o mesmo saldo.
- Categorias: **time campeão, goleiro menos vazado, artilheiro, maior assistente, MVP** (MVP só se o campeonato tiver MVP).
- Abrem após o sorteio dos times e fecham quando o 1º jogo começa.
- Empate numa categoria: quem apostou em qualquer um dos empatados acerta.
- Menos vazado: menor média de gols sofridos por jogo no gol, com mínimo de jogos no gol (ex.: metade dos jogos do time). Vale o `IsGoalkeeper` de cada jogo.
- MVP com regra "por jogo": vence quem teve mais MVPs.

## Justiça (sugestões aceitas como direção)
- Conquistas tipo hat-trick/clean sheet são **por jogo**, nunca somadas entre jogos.
- No consolidado, o campeonato conta como **1 data** nas sequências.
- Para receber o resultado da colocação, o jogador precisa ter jogado pelo menos 1 jogo (mínimo exato ainda a definir).
- Quem chega atrasado entra num time sem mexer na classificação.

## Ainda em aberto
- Mínimo de jogos para receber o resultado da colocação.
- Sorteio para N times: começar simples (cobra por nota) ou esperar o novo algoritmo "Jogo Parelho" (em discussão separada).
