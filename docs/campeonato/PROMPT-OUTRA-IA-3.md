# Prompt 3 para a outra IA (telas do campeonato no front web)

Copie tudo abaixo da linha. **Só mande depois de liberar espaço em disco**: um worktree novo do front precisa de `npm install` (centenas de MB).

---

Suas telas do Modo Campeonato no app Flutter foram revisadas e aprovadas. Rotas idênticas às do backend, enums com os valores certos, JSON com os nomes do contrato e a flag desligada em produção. A única divergência encontrada era do **backend**: `topScorers`/`topAssists` voltavam `{ playerId, count }` em vez de `goals`/`assists`. Já foi corrigido lá (commit `1f49cd1`), e agora existe um teste que trava as chaves do JSON no formato do contrato. Do seu lado não precisa mudar nada.

## Objetivo
Fazer as **mesmas telas no front web** (`C:\Users\lhnme\source\repos\BratnavaFCFront`, React + Vite + TypeScript), seguindo o mesmo contrato. O Luis decidiu que o Modo Campeonato vai nos dois clientes. O backend das Fases 1 e 2 já está implementado e testado, então o contrato v3 é o que a API responde de verdade.

## Leia antes de tudo
1. `C:\Users\lhnme\source\repos\BratnavaFCApi\docs\campeonato\CONTRATO-API.md` (v3): fonte da verdade.
2. `DECISOES.md` da mesma pasta.
3. A sua própria implementação Flutter em `C:\Users\lhnme\source\repos\BratnavaFCApp-campeonato` (mesmos fluxos, textos, presets e validações). Reaproveite as decisões de UI que você já tomou lá.

## Isolamento (obrigatório)
- O checkout `C:\Users\lhnme\source\repos\BratnavaFCFront` está numa branch de outra feature e tem alterações locais do Luis: **não mexa nele.** Crie um worktree a partir do `main`:
  ```
  git -C C:\Users\lhnme\source\repos\BratnavaFCFront worktree add ..\BratnavaFCFront-campeonato -b feature/campeonato-web main
  ```
  e trabalhe só em `C:\Users\lhnme\source\repos\BratnavaFCFront-campeonato`.
- Não mexa na API nem no app Flutter.
- Commits só na branch `feature/campeonato-web`, em português, **sem push**.

## O que fazer
Estude como o front organiza uma feature (`src/pages`, `src/domains`, `src/api`, `src/routes`, stores e componentes), o tema e os componentes já existentes, e siga o mesmo padrão.
1. **Tipos TypeScript** iguais ao contrato (enums como número) num arquivo próprio da feature. Não edite `src/api/generated` (é gerado). Escreva o cliente HTTP da feature à mão, com o mesmo `http`/axios que o resto do front usa.
2. **Datasource mock e real** atrás de uma flag de ambiente (`VITE_CHAMPIONSHIPS_ENABLED`, `VITE_CHAMPIONSHIPS_USE_MOCK`), desligada em produção, como você fez no Flutter.
3. Mesmas telas: **lista** com `ruleBadges`, **wizard de criação** (presets, "Vitória para todos" com a legenda, resumo em linguagem simples, validações do contrato), **detalhe/ao vivo** (presença, times, jogo atual, gols, fila, classificação, artilharia/assistências, ações do admin, `next-game`, pênaltis em empate, polling de 5 s só durante o jogo) e **votação de MVP**.
4. Testes com vitest no padrão do projeto: resumo das regras, validações do wizard e mapeamento JSON.

**Fora do escopo:** apostas, card no histórico e estatísticas (Fase 3, em andamento no backend).

**Pronto quando:** `npm run build` e `npm test` passam no seu worktree e dá para navegar por todas as telas com o mock.

## Acompanhamento (obrigatório)
Mantenha atualizado a cada etapa:
`C:\Users\lhnme\source\repos\BratnavaFCFront-campeonato\docs\campeonato-web\PROGRESSO.md`
(branch e último commit, tabela de etapas, decisões, dúvidas sobre o contrato sem inventar campos, o que falta). Antes de cada etapa, releia o `CONTRATO-API.md`: ele pode ganhar versão nova (veja o "Histórico").

## Tarefa pequena opcional, no Flutter (só depois do web)
No worktree `BratnavaFCApp-campeonato`, troque o campo de digitar o ID do convidado por um **seletor de jogadores da patota**, usando o endpoint de jogadores que o app já usa em outras telas. Registre no `PROGRESSO.md` de lá.
