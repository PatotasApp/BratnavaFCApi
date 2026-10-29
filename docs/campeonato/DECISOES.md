# Decisões tomadas (Luis, 27/10/2026)

## Produto
1. O campeonato é **separado das partidas**, com menu próprio. O admin cria escolhendo data e local, como num jogo normal.
2. **Vai nos dois clientes:** app Flutter e front web.
3. **Regras definidas na criação**, editáveis até o 1º jogo começar e travadas depois. Reabrir um campeonato finalizado é ação explícita do admin e reconstrói as projeções.
4. As regras **aparecem na listagem de partidas/histórico** como selos ("Conta como uma única partida", "1º V · 2º E · 3º D", "MVP do campeonato"...).
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
- Pagamento (29/10, revisável): campeão = apostado × (times − 1); demais = 3x; erro perde metade. Redefinir os times ou cancelar apaga as apostas abertas; voltar de Finalizado estorna.

## Justiça (sugestões aceitas como direção)
- Conquistas tipo hat-trick/clean sheet são **por jogo**, nunca somadas entre jogos.
- No consolidado, o campeonato conta como **1 data** nas sequências.
- Para receber o resultado da colocação, o jogador precisa ter jogado pelo menos 1 jogo (mínimo exato ainda a definir).
- ~~Quem chega atrasado entra num time sem mexer na classificação.~~ Substituído em 29/10/2026: depois que o campeonato começa, **os times ficam travados** (nome, cor, jogadores e goleiro). O app mostra os times só para leitura, em abas.

## Respostas do Luis (27/10/2026, tarde)
- **Fica quem ganha com limite:** vira parâmetro (`limitRule`): sai só o vencedor **ou** saem os dois. Com 3 times "saem os dois" é impossível e vira "sai só o vencedor" automaticamente.
- **Fim do campeonato:** sem regra automática. O admin clica em **"Encerrar campeonato"**; aí abre a votação do MVP do campeonato; depois o admin finaliza.
- **Mata-mata:** descartado ("mata-mata na verdade é um todos contra todos"). Formatos: fica quem ganha, todos contra todos, todos contra todos + final.
- **Desempate:** Pontos é sempre o 1º critério.
- **Mínimo de jogos para a colocação:** o admin escolhe na criação (padrão 1).
- **Estatística:** o campeonato conta como **uma partida** para fins de cálculo (confirmado).
- **Time de quem trocou de time:** o do último jogo (confirmado). **Team builder:** só jogos que contam como partida normal (confirmado).

## Fase 3: como cada regra conta nas estatísticas (implementado em 27/10/2026)
Só campeonatos **finalizados** contam. Tudo sai de uma fonte única (`ChampionshipStatsSource`), então histórico, relatório visual, spotlight e conquistas sempre batem entre si.

| Regra | Efeito |
|---|---|
| Consolidado | 1 item no histórico e **1 partida** para cada jogador que jogou ao menos 1 jogo. |
| Jogos separados | Cada jogo vira 1 partida (com selo do campeonato). |
| Só no campeonato | Não aparece em nenhum histórico nem estatística geral. |
| Resultado por jogo | V/E/D pelo placar (empate decidido nos pênaltis continua empate nas estatísticas). |
| Colocação | Cada jogador recebe o resultado da posição do time dele (no consolidado: 1 vez; nos jogos separados: em cada jogo). Abaixo do mínimo de jogos: só presença. |
| Vitória para todos | Vitória para todos que jogaram. |
| Só presença | Conta a partida, sem V/E/D; não avança nem quebra sequências (invencibilidade, vitórias seguidas). |
| Gols não contam | Gols, assistências, gols contra **e feitos** (hat-trick, clean sheet...) ficam fora; seguem visíveis só no campeonato. |
| MVP não conta | MVPs e votos ficam fora. |

**Decisões técnicas (revisáveis pelo Luis):**
- **Feitos são por jogo, nunca da soma.** 3 gols em 3 jogos diferentes não são hat-trick. No consolidado, cada feito conta no máximo 1 vez (o campeonato vale 1 partida).
- **MVP por jogo no consolidado conta no máximo 1** (vale 1 partida).
- **Time do jogador no consolidado:** o time do último jogo dele (quem trocou de time fica com o time final).
- **MVP do campeonato nos jogos separados:** fica no último jogo do jogador.
- **Sorteio de times:** jogos de campeonato só entram com "Entra no sorteio" ligado, e pelo placar real.
- **Team builder** (formações): só entram jogos que contam exatamente como partidas normais (jogos separados + V/E/D por jogo + gols e MVP contando). Nos outros modos o jogo não é uma partida normal entre 2 formações.
- **Clientes antigos:** o histórico só traz campeonatos com `includeChampionships=true`. App/site que ainda não conhecem campeonato continuam vendo só partidas. Nas estatísticas (relatório, conquistas) o campeonato conta para todos.
