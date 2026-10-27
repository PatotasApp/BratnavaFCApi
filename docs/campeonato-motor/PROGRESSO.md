# Progresso — motor de formatos e classificação

Branch: `feature/campeonato-motor`

Último commit: `docs: finaliza acompanhamento do motor` (o commit que contém este arquivo)

Commit de implementação: `c068138` (`feat: implementa motor de campeonatos`)

| # | Etapa | Status |
|---|---|---|
| 1 | Tipos de regras e contratos imutáveis | ✅ |
| 2 | Tabela de todos contra todos (3, 4 e 5 times; 1/2 turnos) | ✅ |
| 3 | Motor de fica quem ganha | ✅ |
| 4 | Mata-mata e todos contra todos + final | ✅ |
| 5 | Classificação e desempates configuráveis | ✅ |
| 6 | Regras de encerramento | ✅ |
| 7 | Testes completos e revisão de isolamento | ✅ |
| 8 | Commit final | ✅ |

## Decisões de design

- O motor será independente de EF e receberá apenas IDs de time, placares e regras.
- Resultados e agendas serão `sealed record`, para preservar imutabilidade estrutural e facilitar testes.
- Sorteio e pênaltis serão resolvidos por uma ordem externa de IDs, sem aleatoriedade interna, tornando o motor determinístico.
- A tabela de todos contra todos usará o método do círculo; em quantidade ímpar, uma vaga fictícia representará a folga.
- As regras validam os invariantes na construção e usam mensagens em português no mesmo tom de `MatchEntity`.
- No limite de vitórias seguidas, o vencedor que alcança o limite vai ao fim da fila, o último perdedor permanece e a sequência é zerada.
- Em `sai quem está há mais tempo`, a ordem inicial dos times também define qual dos dois entrou primeiro; cada substituição atualiza essa antiguidade.
- No mata-mata, a ordem recebida define os confrontos; quando há número ímpar, o último time avança automaticamente. Empates exigem vencedor externo.
- Desempates são aplicados por grupos ainda empatados. O confronto direto recalcula pontos somente entre os times do grupo; sem decisão externa suficiente, eles compartilham a posição.
- No modo pontos por jogo, tanto os pontos gerais quanto os pontos do confronto direto são divididos pelos jogos correspondentes.
- Quando tempo e limite de gols são configurados juntos, o jogo termina ao alcançar qualquer um deles. A regra de quantidades iguais exige que todos tenham jogado ao menos uma vez.

## Dúvidas para o Luis

- Nenhuma até aqui. As decisões atuais são suficientes para iniciar a implementação.

## O que falta

- Nada neste escopo. A integração com entidades e serviços fica para a Fase 1.

## Validação

- Testes do motor: 24 aprovados, 0 falhas.
- Suíte completa: 1554 aprovados, 0 falhas e 7 ignorados (já existentes).
- Arquivos de código limitados a `src/BratnavaFC.Domain/Championships/` e `BranavaFCTests/ChampionshipEngine*Tests.cs`.
