# Retrospectiva do jogador

Retrospectiva individual da temporada, estilo Wrapped: stories em tela cheia no app, página de rolagem no site.
Protótipo aprovado pelo Luis em 02/11/2026.

## Acesso

**Só o admin da patota** (GroupAdmins) e Admin/GodMode. Qualquer outro recebe 403, inclusive para a própria
retrospectiva (decisão do Luis: o que está sendo feito para o evento de 21/11/2026 é só do admin).
O admin escolhe de qual jogador ver; a resposta já traz a lista de quem jogou no período.

## Endpoint

`GET api/Retrospective/group/{groupId}?playerId=&year=&from=&to=`

- `playerId`: sem ele, o jogador do próprio admin na patota (ou o primeiro em ordem alfabética, se ele não joga).
- Período: `year` (ano de Brasília; padrão = o atual, ou o último com jogos) **ou** `from`/`to` (datas inclusivas, Brasília).
- Quando a branch `feature/temporadas` entrar, falta só aceitar `seasonId` e resolver pelo `ISeasonService.ResolvePeriodAsync`
  (o `StatPeriod.cs` é idêntico ao de lá, de propósito, para o merge não conflitar).

## Fonte e critérios (os mesmos das Estatísticas)

- Unidades: partidas finalizadas + campeonatos pelas regras de cada um (`ChampionshipStatsSource`); campeonato consolidado = 1 jogo.
- Presença pelo time escalado (time 1/2 e `!DidNotPlay`). V/E/D pelo placar. Gols = só os registrados com autor.
  Assistência em gol contra não conta.
- Colocação tipo competição (1, 2, 2, 4) em tudo.
- Rankings de taxa (aproveitamento, gols/assistências por jogo, decisivos por jogo) só com o mínimo de
  **20% dos jogos da patota (no mínimo 3)**.
- Nomes: primeiro nome; nome completo quando outro jogador da patota (convidado também) tem o mesmo primeiro nome.

## Blocos

| Bloco | Regra |
|---|---|
| Presença | jogos, % dos jogos da patota, posição, maior sequência de jogos da patota seguidos sem faltar |
| Resultados | V-E-D, aproveitamento (V=3, E=1), posição, mais vitórias seguidas, mais jogos sem perder (jogo sem placar não quebra), melhor mês (mín. 3 jogos com resultado) |
| Gols / assistências | total, posição, melhor noite (empate: a mais recente), pra quem mais passou e quem mais passou pra ele |
| Meses | só os meses em que a patota tem gol registrado; gols e assistências do jogador em cada um |
| Decisivos | abriu o placar (1º gol da partida) e gol da vitória (o gol que deixou o vencedor um à frente do placar final do perdedor). Só em confronto direto com **todos os gols registrados** (registro incompleto não diz a ordem). Gol contra não credita ninguém. Ordem: horário digitado e, no mesmo minuto, ordem de cadastro |
| Dupla de ouro | melhor aproveitamento no mesmo time, mín. 4 jogos com resultado; e quem mais jogou do lado |
| Freguês / carrasco | times opostos em confronto direto, mín. 4 confrontos; freguês só com retrospecto positivo (V > D), carrasco só com negativo (D > V) |
| Marcos | 1º gol, último gol (até agora, se o período não acabou), gols 100º/200º..., 1ª assistência e assistências 100ª/200ª... da patota no período |
| MVP | MVPs, votos recebidos, posição |
| Gol contra | quantos |
| Perfil | Goleiro (mais da metade dos jogos no gol) = O Paredão. Senão, o dado em que fica mais bem colocado: O Fiel (presença), O Matador (gols/jogo), O Garçom (assistências/jogo), O Pé-quente (aproveitamento), O Craque da Galera (MVPs), O Decisivo (aberturas + gols da vitória por jogo). Empate de posição: nessa ordem |

Um bloco nulo (ou zerado) = sem dado: os fronts não mostram o story/cartão.
