# Prompt 2 para a outra IA (telas do campeonato no app Flutter)

Copie tudo abaixo da linha.

---

Olá de novo! Seu motor de campeonatos foi revisado e validado (1554 testes, merge limpo com a Fase 0). Obrigado. Agora vem a próxima tarefa, em outro repositório.

## Objetivo
Construir no **app Flutter** (`C:\Users\lhnme\source\repos\BratnavaFCApp`) as telas do **Modo Campeonato**, usando **dados falsos (mock)**, enquanto eu (a outra IA) faço o backend da Fase 1 na API. Quando a API ficar pronta, só se troca o mock pela chamada real. Por isso o contrato tem que ser seguido à risca.

## Leia antes de tudo
1. `C:\Users\lhnme\source\repos\BratnavaFCApi\docs\campeonato\CONTRATO-API.md`: **endpoints, DTOs e enums. É a sua fonte da verdade.**
2. `C:\Users\lhnme\source\repos\BratnavaFCApi\docs\campeonato\DECISOES.md`: decisões do Luis (nomes, regras, apostas).
3. `README.md` e `PLANO.md` da mesma pasta, para contexto.

Se esses arquivos não estiverem no disco, leia da branch: `git -C C:\Users\lhnme\source\repos\BratnavaFCApi show feature/campeonato-fase0-blindagem:docs/campeonato/CONTRATO-API.md`.

## Isolamento (obrigatório)
- O checkout `C:\Users\lhnme\source\repos\BratnavaFCApp` tem alterações locais do Luis: **não mexa nele e não troque de branch nele.** Crie um worktree:
  ```
  git -C C:\Users\lhnme\source\repos\BratnavaFCApp worktree add ..\BratnavaFCApp-campeonato -b feature/campeonato-telas main
  ```
  e trabalhe só em `C:\Users\lhnme\source\repos\BratnavaFCApp-campeonato`.
- Não mexa na API (`BratnavaFCApi`) nem no front web.
- Commits só na branch `feature/campeonato-telas`, em português, **sem push**.

## O que fazer
Antes de codar, estude como o app já organiza uma feature (por exemplo `lib/features/matches` e `lib/features/bet`: datasource, entities, providers, pages) e siga o mesmo padrão, o mesmo tema/cores (`AppColors`) e os mesmos componentes.

1. **Feature `lib/features/championships/`** com entidades e DTOs **iguais ao contrato** (enums como int) e um **datasource abstrato** com duas implementações: `Mock` (dados em memória, com um campeonato de exemplo em cada status) e `Api` (assinaturas prontas com as rotas do contrato, corpo com `UnimplementedError` ou a chamada HTTP real se for trivial). Um único lugar escolhe qual usar.
2. **Entrada no menu "Campeonatos"**, separada das partidas, **atrás de uma flag** desligada por padrão no build de produção.
3. **Lista de campeonatos**: card com nome, data, status, campeão (quando houver) e os **selos das regras** (`ruleBadges`).
4. **Wizard de criação (admin)**: data/local/nome → formato (com os presets "Fica quem ganha clássico" e "Troca quem ganhou 2") → regras de estatística → **resumo em linguagem simples** antes de confirmar. Use exatamente "Vitória para todos" com a legenda do DECISOES.md. Valide no cliente as mesmas regras listadas no contrato.
5. **Detalhe/ao vivo**: presença, times, jogo atual com placar e gols, fila de espera (fica quem ganha), **classificação**, artilharia/assistências, e as ações do admin (sortear/definir times, iniciar, criar próximo jogo usando `next-game`, registrar gol, encerrar jogo com pênaltis em empate, finalizar). Polling de 5 s só nessa tela.
6. **Votação de MVP** do campeonato ou por jogo, conforme `mvpMode`.
7. Testes de widget/unidade no padrão do projeto para: resumo das regras, validações do wizard e mapeamento JSON ↔ entidades.

**Fora do escopo agora:** apostas, card no histórico e estatísticas (dependem da Fase 3).

**Pronto quando:** `flutter analyze` sem erros novos e `flutter test` passando no seu worktree, e dá para navegar por todas as telas com o mock.

## Acompanhamento (obrigatório)
Mantenha atualizado a cada etapa:
`C:\Users\lhnme\source\repos\BratnavaFCApp-campeonato\docs\campeonato-telas\PROGRESSO.md`

Inclua: branch e último commit, tabela de etapas (⬜/⏳/✅), decisões de UI, **divergências ou dúvidas sobre o contrato** (não invente campo: anote a dúvida e siga com um mock razoável) e o que falta.

Antes de cada etapa, releia `CONTRATO-API.md` (pode ganhar uma versão nova; veja o "Histórico" no fim dele) e o `PROGRESSO.md` da API.
