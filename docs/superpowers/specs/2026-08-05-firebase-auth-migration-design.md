# Design: Migração da Autenticação para Firebase Auth — BratnavaFC

**Data:** 2026-08-05
**Status:** Aguardando revisão

---

## Visão Geral

A API deixa de gerenciar identidade. O front-end fala **direto com o SDK do Firebase**; o backend apenas valida o ID token e resolve a identidade interna. Não existe endpoint de login, cadastro, refresh, revoke nem reset de senha.

O princípio central é a separação entre **identidade interna** e **externa**:

| | Coluna | Papel |
|---|---|---|
| **Interna** | `Users.Id` (GUID, PK) | usada como FK em todas as tabelas de negócio. **Não muda.** |
| **Externa** | `Users.FirebaseUid` (varchar 128, único parcial) | UID do Firebase; único elo entre o token e a linha |

> **A coincidência `FirebaseUid == Id` existe SÓ para os usuários migrados em lote**, porque o script os importa forçando `Uid = GUID`. Usuário novo que entra por social login recebe um UID gerado pelo Firebase (alfanumérico de 28 chars). Nenhum código pode depender dessa igualdade, e `FirebaseUid` nunca é chave estrangeira.

O `UserName` permanece como identificador único no app, mas não autentica.

---

## Escopo

**Dentro:** validação do ID token, provisionamento de usuário no primeiro acesso, middleware de tradução de identidade, `GET`/`PUT /api/users/me`, migration das duas colunas novas.

**Fora — do front-end via SDK:** login, cadastro, renovação de token, logout, reset e troca de senha, verificação de e-mail. A API não expõe endpoint para nenhum deles e o código correspondente foi **deletado**.

**Fora — intocado:** a coluna `Users.Password` sobrevive com aviso no `SetPasswordHash`, e sai numa migration seguinte depois de o script de migração rodar.

---

## Configuração

`Firebase:ProjectId` é **opcional** — se ausente ou vazio, é derivado do `project_id` do service account JSON já usado pelo push (`FirebaseProjectId`). Não há mais `FirebaseAuth:ApiKey` nem `GoogleClientIds`: a Web API Key servia ao client REST (deletado) e a validação do token do Google agora é do próprio Firebase. **Essas duas configurações pertencem ao front-end.**

O bloco `Jwt` saiu dos três appsettings.

> A `Jwt:SecretKey` antiga e o service account JSON commitado em `appsettings.Production.json` permanecem no histórico do git. Ambos devem ser rotacionados.

---

## 1. Migration

`AddFirebaseUidAndAuthProvider`, sobre a tabela `Users`:

```csharp
AddColumn<string>("FirebaseUid", maxLength: 128, nullable: true);
AddColumn<short>("AuthProvider", nullable: false, defaultValue: (short)1);
CreateIndex("IX_Users_FirebaseUid", unique: true, filter: "\"FirebaseUid\" IS NOT NULL");
```

`Id` e FKs intocados. Nomes em PascalCase para casar com o schema existente (`"Users"`, `"UserName"`), não snake_case.

`AuthProvider` é **enum** (`Domain/Enums/AuthProvider.cs`) persistido como `smallint` via `HasConversion<short>()`, seguindo a convenção do `Status`. O conjunto do Firebase é aberto (existem `facebook.com`, `phone`, e provedores OIDC/SAML de nome arbitrário), daí o membro `Unknown`: provedor não previsto é registrado como desconhecido em vez de derrubar o provisionamento.

| Enum | `sign_in_provider` no token |
|---|---|
| `Email = 1` | `password` — **não** `email` |
| `Google = 2` | `google.com` |
| `Apple = 3` | `apple.com` |
| `Unknown = 99` | qualquer outro |

A migration anterior, `RemoveRefreshTokens`, dropa a tabela `RefreshTokens`.

---

## 2. Validação do ID Token

`AddJwtAuthentication(IConfiguration)`. Verificado: `https://securetoken.google.com/bratnavafc/.well-known/openid-configuration` responde 200 com `jwks_uri`, então o `Authority` sozinho faz o middleware baixar e rotacionar as chaves RS256.

```csharp
options.Authority = $"https://securetoken.google.com/{projectId}";
ValidIssuer = mesmo valor;  ValidAudience = projectId;
ValidateIssuer = ValidateAudience = ValidateLifetime = ValidateIssuerSigningKey = true;
RoleClaimType = "role";  NameClaimType = "name";
options.MapInboundClaims = false;
```

`MapInboundClaims = false` é obrigatório: com `true`, o handler renomeia `sub` para `NameIdentifier`, e **o `NameIdentifier` pertence à identidade interna**, escrita pelo middleware. Deixar o UID do Firebase cair ali faria os controllers lerem a identidade externa achando que é a interna. O UID externo continua acessível em `sub` / `user_id`.

O `OnMessageReceived` existente é preservado — `<video src>` e o cliente SignalR passam o token na query string (`?t=` em `/stream`, `?access_token=` em `/hubs/realtime`).

---

## 3. `IUserProvisioningService`

```csharp
Task<ProvisionedUser?> ResolveOrCreateAsync(string firebaseUid, ClaimsPrincipal principal, CancellationToken ct);
public sealed record ProvisionedUser(Guid Id, UserRole Role, bool IsActive);
```

Ordem de resolução:

1. **Lookup por `FirebaseUid`.** Achou → sincroniza o e-mail se divergir do token (o Firebase é a fonte da verdade) e retorna.
2. **Sem e-mail no token** → retorna `null`. A coluna é obrigatória e é o único identificador natural. Acontece em login por telefone ou anônimo.
3. **Lookup por e-mail — vínculo.** Existe linha com esse e-mail e `FirebaseUid` nulo: é um usuário anterior à migração que entrou pelo SDK antes do script em lote rodar. **Vincula** a linha existente. Sem esta etapa ele ganharia uma conta duplicada, órfã de todas as FKs de negócio. Se o e-mail já estiver vinculado a outro UID, recusa e loga como erro.
4. **Cria.** Novo GUID, `FirebaseUid`, e-mail, nome do claim `name`, `AuthProvider` do `sign_in_provider`, `UserName` gerado.

**Geração do `UserName`:** obrigatório e único, e nenhum provedor social fornece um. Deriva da parte local do e-mail, filtra para letras, dígitos, `.`, `_`, `-`, e desempata com sufixo numérico. O usuário troca em `PUT /api/users/me`.

**Leitura do `sign_in_provider`:** vive dentro do objeto `firebase` do payload, e o handler do JWT **não achata objetos aninhados** — a claim `firebase` chega como o JSON bruto, então há parse manual com `JsonDocument`.

**Custom claims:** grava `internal_id` e `role` via `SetCustomUserClaimsAsync` em todos os três ramos (encontrado, vinculado, criado). Ver a seção 4.1.

---

## 4. `FirebaseIdentityMiddleware`

Registrado **entre** `UseAuthentication` e `UseAuthorization`, por dois motivos: precisa do principal montado, e precisa injetar a claim de role **antes** de os `[Authorize(Roles = ...)]` serem avaliados.

- Lê o UID externo de `user_id` (usado pelo SDK) ou `sub` (padrão JWT).
- **Caminho rápido:** token já traz `internal_id` **e** `role` → usa direto, sem banco. Exigir as duas é essencial: com `internal_id` mas sem `role`, a autorização falharia adiante.
- **Caminho lento:** `ResolveOrCreateAsync`.
- Injeta o GUID interno em `ClaimTypes.NameIdentifier` (removendo qualquer valor anterior) e em `internal_id`.
- **Usuário inativo não recebe a claim de role**, e portanto não passa por nenhum `[Authorize(Roles = ...)]`. Isso substitui a checagem de status que existia no login antigo — sem ela, um usuário inativado manteria acesso, porque não há mais login onde barrá-lo.

A regra de negócio mora no service; o middleware só orquestra.

### Custo por request

| Situação | Custo |
|---|---|
| request anônima | checagem de flag, sai antes de tudo |
| token com `internal_id` + `role` | leitura de claims, nenhum I/O e nenhum serviço scoped resolvido |
| sem essas claims | um `SELECT` de uma linha por índice único |

Não há cache. Ele seria redundante: as custom claims são **permanentes no registro do Firebase** e o SDK renova o ID token a cada ~55 min, então a partir da primeira renovação todo token seguinte já as carrega. O caminho com banco é um evento **por usuário**, não um custo recorrente.

O `IUserProvisioningService` é resolvido de `context.RequestServices` **apenas no caminho lento**. Resolvê-lo por parâmetro do `InvokeAsync` instanciaria um `AppDbContext` scoped em toda request, inclusive nas que não consultam o banco.

> O front-end deve chamar `getIdToken(true)` logo após o primeiro sign-in. Sem isso, a janela até o refresh automático é de até ~1h; com isso, é uma request.

---

## 4.1. Quem escreve as custom claims, e quando

| Quem | Quando | O que grava |
|---|---|---|
| `UserProvisioningService` | toda passagem pelo caminho lento — ao criar, ao vincular, e também quando o usuário é encontrado | `internal_id` + `role` |
| `UserService.UpdateAsync` | quando um admin altera a role | `internal_id` + `role` |
| Script de migração em lote | uma vez por usuário existente | `internal_id` + `role` |

Reescrever as claims no caso "usuário encontrado" é o que torna o mecanismo **autocurável**. Chegar ao service significa que o token não trazia as claims — senão o middleware teria usado o caminho rápido. Sem essa reescrita, uma falha na primeira tentativa deixaria aquele usuário no caminho lento para sempre, sem retry.

A escrita é **best-effort**: falha é logada como warning e nunca bloqueia a request. O pior caso é o usuário continuar no caminho lento (um SELECT indexado por request) até uma tentativa passar.

> Uma troca de role só chega ao token no refresh seguinte. Pelo caminho lento o middleware leria a role do banco, mas o caminho rápido usa a do token — então um usuário com token válido mantém a role antiga por até ~1h, a menos que o front-end force `getIdToken(true)`.

### A claim `role` é o que impede 403 em massa

Cerca de 30 controllers usam `[Authorize(Roles = "User,Admin,GodMode")]` com `RoleClaimType = "role"`. Usuário recém-provisionado não tem essa custom claim no token, e custom claim só chega ao cliente **após refresh**. Por isso o middleware injeta a role lida do banco: funciona na primeira request, sem depender de refresh. Depender apenas de `SetCustomUserClaimsAsync` daria 403 em tudo até o cliente renovar o token.

---

## 5. Consumidores da identidade

Passaram a ler `ClaimTypes.NameIdentifier` (interna) em vez de `sub` (externa):

| Arquivo | O que quebraria |
|---|---|
| `Api/Realtime/RealtimeHub.cs:34` | fazia `Guid.TryParse(FindFirstValue("sub"))` e comparava com `p.UserId`. UID do Firebase não é GUID → `TryParse` falha → `CanAccessGroupAsync` retorna `false` e **todo não-admin perde acesso a grupo no realtime** |
| `Api/Middleware/AuditMiddleware.cs:23` | registraria o UID do Firebase, sem correspondência com as FKs — quebrando a rastreabilidade que o próprio comentário do arquivo descreve |

Os controllers que já leem `NameIdentifier ?? "sub"` (`GroupAuthorizedController`, `PlayersController`, `PushController`, `NotificationsController`) continuam corretos: o `NameIdentifier` agora carrega o GUID interno.

> Bug pré-existente, não corrigido aqui: `UseRateLimiter()` está antes de `UseAuthentication()` no pipeline, então o `FindFirstValue("sub")` da partição em `PresentationExtensions:106` já era sempre nulo.

---

## 6. Endpoints `/me`

`GET /api/users/me` → `MeDto(Id, Email, UserName, FirstName, LastName, Phone, AuthProvider, Role, Status)`, onde `Id` é a identidade **interna**.

`PUT /api/users/me` → `UpdateMeDto(UserName?, FirstName?, LastName?, Phone?)`. **Não existe campo de e-mail**, e um `"email"` no payload é ignorado pela desserialização — e-mail é read-only nesta fase. `UserName` é alterável com checagem de unicidade. Role e status não entram: são do fluxo administrativo.

Sincroniza o `DisplayName` no Firebase e faz rollback do SQL se a chamada falhar. O `PhoneNumber` só é enviado quando **já está em E.164**: o SDK valida e **lança** se o número não começar com `+`, e nossa coluna aceita formato local (`11999999999`) — sem essa guarda, toda edição de perfil de quem tem telefone local cairia no rollback. Não inferimos DDI, porque adivinhar `+55` gravaria número errado para quem não é do Brasil.

---

## 7. Removido

| Item | Motivo |
|---|---|
| `AuthenticationService`, `IAuthenticationService`, `AuthenticationController` | login/cadastro são do SDK |
| `FirebaseAuthRestClient`, `IFirebaseAuthRestClient`, `FirebaseAuthOptions` | o backend não valida senha nem emite token |
| `LoginDto`, `RegisterDto`, `GoogleLoginDto`, `TokenDto`, `RefreshTokenDto`, `ChangePasswordDto` | — |
| `POST /login`, `/register`, `/google`, `/refresh-token`, `/revoke`, `POST /api/users`, `PUT /api/users/{id}/password` | — |
| `RefreshTokenEntity` + `DbSet` | ciclo de vida do refresh token é do SDK |
| `UserService.CreateUserAsync`, `ChangePasswordAsync`, `NotifyPasswordChangedAsync` | — |
| `PasswordHasher<UserEntity>` e o registro no DI | nada valida hash |
| Bloco `Jwt` nos 3 appsettings | não há chave simétrica |

`FirebaseAdmin` fica em **2.4.0**: o `PushService` depende de `FirebaseAdmin.Messaging` e subir para 3.5.0 é risco não relacionado a esta migração.

---

## 8. Usuários existentes — reset forçado

Decisão: **os usuários atuais não preservam a senha.** Com o front-end falando direto com o Firebase, o backend nunca vê a senha em texto puro, então a migração preguiçosa no login (que dependia de interceptar `POST /login`) é impossível.

O script em lote, ainda a escrever, faz `ImportUsersAsync` com **perfil apenas** — sem `PasswordHash` — forçando `Uid = GUID` para cada usuário, grava `FirebaseUid = Id` no SQL, aplica a custom claim de role, e dispara o e-mail de redefinição do Firebase para todos.

Enquanto o script não rodar, um usuário antigo que se cadastrar pelo SDK com o mesmo e-mail é **vinculado** à linha existente pela etapa 3 do provisionamento, não duplicado.

---

## 9. Design-time factory

`Api/Design/AppDbContextFactory.cs` existe porque o `dotnet ef` sobe o host completo do ASP.NET, e o host agora exige credencial do Firebase para montar a autenticação. Sem ele, além de não ser possível gerar migration local, **os três steps de EF no `fly-deploy.yml` quebrariam** — nenhum deles tem `FIREBASE_SERVICE_ACCOUNT_JSON`. A connection string do factory é sobrescrita pelo `--connection` que o CI passa.

---

## 10. Riscos

**Provisionamento automático cria linha para qualquer token válido do projeto.** Equivale a um endpoint de cadastro aberto — o mesmo que existia antes — mas agora o controle de quem pode criar conta está inteiramente nas configurações de provedor do Firebase Console.

**Troca de role só reflete no token após refresh.** O `UserService.UpdateAsync` grava a custom claim, mas o cliente precisa de `getIdToken(true)` para vê-la. Pelo caminho lento o middleware pegaria a role do banco, mas o caminho rápido usa a do token — então um usuário com token válido mantém a role antiga até renovar.

**`UserName` único só na aplicação.** A coluna nunca teve índice único e continua sem; adicionar um exige checar duplicatas antes (`GROUP BY lower("UserName") HAVING count(*) > 1`), senão a migration falha no deploy.
