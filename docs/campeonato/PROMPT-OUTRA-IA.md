# Prompt para a outra IA (motor de formatos e classificação)

Copie tudo abaixo da linha.

---

Você vai trabalhar no projeto **BratnavaFC**, um app de patotas de futebol. A API é C# .NET 8 + EF Core + Postgres e fica em `C:\Users\lhnme\source\repos\BratnavaFCApi`. O dono do projeto é o Luis. Fale com ele em português.

## Contexto
O Luis decidiu criar um **Modo Campeonato**: um campeonato é uma entidade-mãe com vários jogos de 2 times no mesmo dia. O formato é parametrizável (todos contra todos, fica quem ganha, "troca quem já ganhou duas", mata-mata, todos contra todos + final), e a classificação tem desempates configuráveis.

Outra IA (Claude) está implementando a **Fase 0** (blindagem das estatísticas: `ChampionshipId` nas partidas, filtros globais do EF, migração) na branch `feature/campeonato-fase0-blindagem`, **no checkout principal** `C:\Users\lhnme\source\repos\BratnavaFCApi`.

Toda a documentação está em `C:\Users\lhnme\source\repos\BratnavaFCApi\docs\campeonato\`. **Leia antes de começar:**
1. `README.md`: ponto de partida e regras de convivência
2. `DECISOES.md`: decisões do Luis; não reabra sem perguntar a ele
3. `PLANO.md`: arquitetura e fases
4. `PARALELO.md`: a sua tarefa é a **"A) Motor de formatos e classificação"**
5. `PROGRESSO.md`: estado da Fase 0 (só leitura para você)

## Regras de isolamento (obrigatórias)
- **Não edite nada** em `C:\Users\lhnme\source\repos\BratnavaFCApi`, **não troque de branch** lá e não rode `git checkout`/`switch`/`stash` lá. Esse checkout é da outra IA.
- Crie um worktree só seu:
  ```
  git -C C:\Users\lhnme\source\repos\BratnavaFCApi worktree add ..\BratnavaFCApi-campeonato-motor -b feature/campeonato-motor main
  ```
  e trabalhe **só** em `C:\Users\lhnme\source\repos\BratnavaFCApi-campeonato-motor`.
- Escreva código **só** em `src/BratnavaFC.Domain/Championships/` (pasta nova) e em `BranavaFCTests/ChampionshipEngine*Tests.cs`. Nada de entidades do EF, `AppDbContext`, `Configurations`, `Migrations` ou serviços: isso é da outra IA.
- Nunca commite `appsettings*.json`. Commits só na branch `feature/campeonato-motor`, em português, **sem push** e nunca em `main`.

## Tarefa
Lógica pura, sem banco: entradas e saídas só com `Guid` de time e placares `int`.
1. **Tipos de regras**, seguindo o `DECISOES.md`:
   - formato
   - opções do fica quem ganha: limite de vitórias seguidas (0 = sem limite, 2 = "troca quem já ganhou duas") e regra de empate (saem os dois / sai quem está há mais tempo em campo / pênaltis)
   - fim do jogo (tempo e/ou gols)
   - fim do campeonato (tempo total / nº de jogos / todos jogaram a mesma quantidade)
   - modo de classificação (pontos totais / pontos por jogo)
   - pontos de V/E e ordem dos desempates
2. **Todos contra todos:** gera a tabela para N times, 1 ou 2 turnos, com folga quando N é ímpar.
3. **Fica quem ganha:** dado o histórico de resultados, diz quem joga o próximo jogo, respeitando o limite de vitórias seguidas, a regra de empate e a fila de espera.
4. **Mata-mata** e **todos contra todos + final**.
5. **Classificação:** pontos → saldo → gols pró → confronto direto → (sorteio/pênaltis como entrada externa), com ordem configurável e modo pontos por jogo.

Siga o estilo do código existente: C# .NET 8, nullable ligado, comentários em português, classes `sealed` e imutáveis quando der. Veja `src/BratnavaFC.Domain/Entities/MatchEntity.cs` para o tom das mensagens de erro.

**Pronto quando:** `dotnet test BranavaFCTests/BranavaFC.Tests.csproj` passa no seu worktree, com testes para 3, 4 e 5 times, empates, limite de 2 vitórias seguidas, desempate por confronto direto e "todos jogaram a mesma quantidade".

## Acompanhamento (obrigatório; a outra IA vai ler)
Mantenha **atualizado a cada etapa** o arquivo:
`C:\Users\lhnme\source\repos\BratnavaFCApi-campeonato-motor\docs\campeonato-motor\PROGRESSO.md`

Ele deve ter:
- branch e último commit
- tabela de etapas (⬜ / ⏳ / ✅)
- decisões de design que você tomou e por quê
- dúvidas para o Luis
- o que falta

Antes de cada etapa, releia `C:\Users\lhnme\source\repos\BratnavaFCApi\docs\campeonato\PROGRESSO.md` e `DECISOES.md`: podem ter mudado. Se algo que você precisa conflitar com a Fase 0, **pare e pergunte ao Luis** em vez de mexer nos arquivos dela.
