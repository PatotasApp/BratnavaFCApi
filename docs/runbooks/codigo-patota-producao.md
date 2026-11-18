# Código único de patota — preparação para produção

> Registrado em 13/11/2026 após um erro local `23505` na constraint
> `IX_Groups_Code`. Este documento é um plano operacional. **Nada abaixo foi
> executado em produção.**

## Contexto

A migração `20261109201431_AddDiscoveryProfilesAndMatchOpenings`, da frente de
descoberta de patotas, adiciona `Groups.Code` como `NOT NULL`, inicialmente com
valor padrão vazio, preenche os registros existentes e cria o índice único
`IX_Groups_Code`.

A API da branch `develop` que estava sendo usada localmente ainda não conhece
essa coluna. Ao inserir uma patota, o PostgreSQL usa o padrão vazio. A primeira
inserção pode gravar `Code = ''`; a seguinte falha com `23505`, retornando HTTP
500.

Na cópia local `bratnavafc_demo_main_20261113`, foi aplicado provisoriamente um
default de banco que gera códigos. Essa alteração local **não deve ser copiada
manualmente para produção**. Em produção, a correção deve ser versionada em uma
migração EF e entregue junto da API compatível.

## Decisão necessária antes do deploy

Confirmar se a funcionalidade de descoberta de patotas será publicada:

- Se sim, integrar a entidade, configuração EF, DTOs, serviço e migração da
  frente de descoberta. A API publicada deve preencher `GroupEntity.Code` ao
  criar uma patota.
- Se não, não levar as colunas da frente de descoberta para produção. Não
  remover colunas de uma base que já as possua sem antes auditar consumidores e
  dados existentes.

## Pré-validação somente leitura

Executar por uma pessoa autorizada, usando a connection string de produção sem
imprimi-la nos logs:

```sql
SELECT column_name, is_nullable, column_default
FROM information_schema.columns
WHERE table_schema = 'public'
  AND table_name = 'Groups'
  AND column_name = 'Code';

SELECT
  COUNT(*) AS total,
  COUNT(*) FILTER (WHERE "Code" IS NULL OR "Code" = '') AS sem_codigo,
  COUNT(*) - COUNT(DISTINCT "Code") AS duplicados
FROM "Groups";

SELECT "MigrationId"
FROM "__EFMigrationsHistory"
WHERE "MigrationId" LIKE '%DiscoveryProfilesAndMatchOpenings%';
```

Também confirmar:

1. snapshot/backup recente e restaurável;
2. janela de deploy e responsável pelo rollback;
3. ausência de outra versão da API escrevendo em `Groups` durante a mudança;
4. suporte a `gen_random_uuid()` no PostgreSQL, caso o fallback de banco use
   essa função.

## Alterações que devem ser versionadas

1. A API deve mapear `GroupEntity.Code`, gerar oito caracteres válidos e
   preencher o campo em toda criação de patota.
2. A configuração EF deve manter tamanho máximo de 8 e índice único.
3. A migração deve evitar `defaultValue: ""`. Usar uma função/default seguro de
   banco durante a transição, inclusive para proteger instâncias antigas da API.
4. A migração deve preencher códigos nulos/vazios antes de aplicar `NOT NULL` e
   o índice único.
5. O serviço deve tratar especificamente PostgreSQL `23505` na constraint
   `IX_Groups_Code`, gerar outro código e repetir a criação por um número
   limitado de tentativas. Outros erros de unicidade não devem ser mascarados.
6. Adicionar teste de integração que force uma colisão e confirme que a segunda
   tentativa cria a patota, o jogador do criador e os administradores uma única
   vez.

Se a migração de descoberta já tiver sido aplicada em produção, não editar a
migração histórica. Criar uma nova migração corretiva para:

- instalar o default seguro;
- preencher `NULL`/vazio;
- validar duplicidades;
- garantir `NOT NULL` e `IX_Groups_Code`.

## Ordem recomendada do deploy

1. Colocar a criação de patotas em manutenção curta ou garantir que só uma
   versão da API escreverá durante a transição.
2. Aplicar a migração corretiva com `dotnet ef database update` e
   **`--connection` explícito**. Revisar antes o banco apontado pela connection
   string. Nunca usar `DefaultConnection` implicitamente.
3. Publicar a API que modela e gera `GroupEntity.Code`.
4. Reexecutar as consultas de pré-validação; o esperado é zero código vazio e
   zero duplicidade.
5. Criar uma patota controlada por uma conta de teste autorizada e confirmar:
   HTTP 201, código com oito caracteres, jogador do criador e vínculo de admin.
6. Remover a patota de teste pelo fluxo suportado e monitorar por pelo menos um
   ciclo de deploy os eventos `23505`, `IX_Groups_Code` e `POST /api/Groups 500`.

## Rollback

- Se a API nova falhar, voltar a versão da aplicação. Manter a coluna, o índice
  e o default seguro; eles são compatíveis com a API anterior e evitam novos
  códigos vazios.
- Não apagar códigos nem remover o índice durante um rollback emergencial.
- Se a migração falhar antes do commit, corrigir e reaplicar. Se ela tiver sido
  confirmada e houver corrupção, interromper escritas e seguir o procedimento
  de restauração do snapshot.

## Critérios de conclusão

- criação de patota retorna 201;
- nenhum `Code` nulo ou vazio;
- nenhum código duplicado;
- nenhuma ocorrência nova de `23505 / IX_Groups_Code`;
- testes automatizados de criação e colisão aprovados;
- migração e versão da API registradas no histórico do deploy.
