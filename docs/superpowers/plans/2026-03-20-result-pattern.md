# Result Pattern Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Introduce the Result Pattern across the full stack — services return `Result<T>` instead of throwing exceptions, all HTTP responses use a consistent `ApiResponse<T>` envelope, and the frontend shows backend messages via Sonner toasts.

**Architecture:** `ResultBase` → `Result<T>` / `Result` carry success/failure/message/status. A new `BaseApiController` exposes `ToResponse()` for all controllers. The frontend uses a new `ApiResponse<T>` TypeScript interface and `getResponseMessage()` helper to display toasts from backend messages on mutations.

**Tech Stack (backend):** .NET 8, ASP.NET Core, EF Core 8, xUnit + FluentAssertions + Moq, in-memory EF for tests.
**Tech Stack (frontend):** React 18, TypeScript, Axios, Sonner toasts.

**Spec:** `docs/superpowers/specs/2026-03-20-result-pattern-design.md`

**Parallelization:** Phase 1 (Foundation) must finish first. Then Phase 2 (Services) and Phase 3 (Frontend) can run fully in parallel — they touch different codebases. Within Phase 2, each service task is independent and can also be parallelized across agents.

---

## Phase 1 — Backend Foundation

> Must complete before Phase 2. Creates domain types and base controller that all services and controllers will reference.

---

### Task 1: Domain Types — Result + ApiResponse

**Files:**
- Create: `src/BratnavaFC.Domain/Common/Result.cs`
- Create: `src/BratnavaFC.Domain/Common/ApiResponse.cs`

- [ ] **Step 1: Create the Common folder and Result.cs**

```csharp
// src/BratnavaFC.Domain/Common/Result.cs
namespace BratnavaFC.Domain.Common;

public abstract class ResultBase
{
    public bool Success { get; protected init; }
    public string? Message { get; protected init; }
    public string? Error { get; protected init; }
    public List<string> Errors { get; protected init; } = [];
    public ResultStatus Status { get; protected init; } = ResultStatus.Ok;
}

public class Result<T> : ResultBase
{
    public T? Data { get; private init; }

    private Result() { }

    public static Result<T> Ok(T data, string? message = null,
        ResultStatus status = ResultStatus.Ok) => new()
    {
        Success = true, Data = data, Message = message, Status = status
    };

    public static Result<T> Fail(string error,
        ResultStatus status = ResultStatus.BadRequest,
        List<string>? errors = null) => new()
    {
        Success = false, Error = error, Status = status, Errors = errors ?? []
    };
}

public class Result : ResultBase
{
    private Result() { }

    public static Result Ok(string? message = null) => new()
    {
        Success = true, Message = message, Status = ResultStatus.Ok
    };

    public static Result Fail(string error,
        ResultStatus status = ResultStatus.BadRequest,
        List<string>? errors = null) => new()
    {
        Success = false, Error = error, Status = status, Errors = errors ?? []
    };
}

public enum ResultStatus
{
    Ok           = 200,
    Created      = 201,
    BadRequest   = 400,
    Unauthorized = 401,
    Forbidden    = 403,
    NotFound     = 404,
}
```

- [ ] **Step 2: Create ApiResponse.cs**

```csharp
// src/BratnavaFC.Domain/Common/ApiResponse.cs
namespace BratnavaFC.Domain.Common;

public record ApiResponse<T>(
    bool Success,
    T? Data,
    string? Message,
    string? Error,
    List<string> Errors
);
```

- [ ] **Step 3: Verify the project builds**

```bash
cd /c/github/BratnavaFCApi
dotnet build src/BratnavaFC.Domain/BratnavaFC.Domain.csproj
```
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add src/BratnavaFC.Domain/Common/
git commit -m "feat: add Result<T>, Result, ResultStatus and ApiResponse<T> domain types"
```

---

### Task 2: BaseApiController + GroupAuthorizedController update

**Files:**
- Create: `src/BratnavaFC.Api/Controllers/BaseApiController.cs`
- Modify: `src/BratnavaFC.Api/Controllers/GroupAuthorizedController.cs`

- [ ] **Step 1: Create BaseApiController.cs**

```csharp
// src/BratnavaFC.Api/Controllers/BaseApiController.cs
using BratnavaFC.Domain.Common;
using Microsoft.AspNetCore.Mvc;

namespace BratnavaFC.Api.Controllers;

[ApiController]
public abstract class BaseApiController : ControllerBase
{
    protected IActionResult ToResponse<T>(Result<T> result,
        int? overrideSuccessStatus = null)
    {
        var response = new ApiResponse<T>(
            result.Success,
            result.Data,
            result.Message,
            result.Error,
            result.Errors
        );
        var status = result.Success
            ? overrideSuccessStatus ?? (int)result.Status
            : (int)result.Status;
        return StatusCode(status, response);
    }

    protected IActionResult ToResponse(Result result,
        int? overrideSuccessStatus = null)
    {
        var response = new ApiResponse<object>(
            result.Success, null, result.Message, result.Error, result.Errors
        );
        var status = result.Success
            ? overrideSuccessStatus ?? (int)result.Status
            : (int)result.Status;
        return StatusCode(status, response);
    }
}
```

- [ ] **Step 2: Update GroupAuthorizedController to inherit BaseApiController**

Open `src/BratnavaFC.Api/Controllers/GroupAuthorizedController.cs`.
Change the class declaration from:
```csharp
public abstract class GroupAuthorizedController : ControllerBase
```
To:
```csharp
public abstract class GroupAuthorizedController : BaseApiController
```
Add `using BratnavaFC.Domain.Common;` to the using statements if not already present.

- [ ] **Step 3: Verify the project builds**

```bash
dotnet build src/BratnavaFC.Api/BratnavaFC.Api.csproj
```
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add src/BratnavaFC.Api/Controllers/BaseApiController.cs \
        src/BratnavaFC.Api/Controllers/GroupAuthorizedController.cs
git commit -m "feat: add BaseApiController with ToResponse helpers"
```

---

### Task 3: Global Exception Middleware

**Files:**
- Modify: `src/BratnavaFC.Api/Program.cs`

- [ ] **Step 1: Add global exception handler to Program.cs**

Open `src/BratnavaFC.Api/Program.cs`. Find the middleware pipeline section (around `app.UseSwagger()`). Add the following **before** `app.UseAuthentication()`:

```csharp
app.UseExceptionHandler(appError =>
{
    appError.Run(async context =>
    {
        context.Response.StatusCode = 500;
        context.Response.ContentType = "application/json";
        var response = new ApiResponse<object>(
            false, null, null, "Erro interno no servidor.", []);
        await context.Response.WriteAsJsonAsync(response);
    });
});
```

Add `using BratnavaFC.Domain.Common;` at the top of Program.cs if not already present.

- [ ] **Step 2: Verify build**

```bash
dotnet build src/BratnavaFC.Api/BratnavaFC.Api.csproj
```
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/BratnavaFC.Api/Program.cs
git commit -m "feat: add global exception middleware returning ApiResponse envelope"
```

---

## Phase 2 — Service Layer + Tests + Controllers

> Each task below is independent and can be executed in parallel by separate agents.
> Pattern per task: Interface → Tests (update to expect Result) → Service impl → Controller → Commit.
>
> **Exception mapping rules (apply consistently):**
> - `InvalidOperationException` → `Result.Fail(ex.Message, ResultStatus.BadRequest)`
> - `ApplicationException` → `Result.Fail(ex.Message, ResultStatus.BadRequest)`
> - `UnauthorizedAccessException` → `Result.Fail(ex.Message, ResultStatus.Forbidden)`
> - "not found" cases → `ResultStatus.NotFound`
> - Infrastructure exceptions (EF Core) → let propagate (caught by global middleware)
>
> **Message rules:**
> - GET list/object → `message: null`
> - POST → `"[Entity] criado com sucesso."`, status `ResultStatus.Created`
> - PUT/PATCH → `"[Entity] atualizado com sucesso."`, status `ResultStatus.Ok`
> - DELETE → `"[Entity] removido com sucesso."`, status `ResultStatus.Ok`
> - Not found → `"[Entity] não encontrado."`, status `ResultStatus.NotFound`
> - Forbidden → `"Sem permissão para esta operação."`, status `ResultStatus.Forbidden`

---

### Task 4: AuthenticationService

**Files:**
- Modify: `src/BratnavaFC.Application/Abstractions/IAuthenticationService.cs`
- Modify: `src/BratnavaFC.Application/Services/AuthenticationService.cs`
- Modify: `src/BratnavaFC.Api/Controllers/AuthenticationController.cs`
- Modify: `BranavaFCTests/AuthenticationServiceTests.cs`

**Current throws in AuthenticationService:**
- `ApplicationException("User not found")` — login: user not found
- `ApplicationException("Invalid user or password.")` — login: wrong password
- `ApplicationException("User is logged out, try again.")` — refresh: user inactive
- `ApplicationException("Refresh token expired.")` — refresh: token expired

- [ ] **Step 1: Update IAuthenticationService interface**

```csharp
using BratnavaFC.Domain.Common;

Task<Result<TokenDto>> LoginAsync(LoginDto request, CancellationToken cancellationToken);
Task<Result<TokenDto>> RefreshTokenAsync(RefreshTokenDto request, CancellationToken cancellationToken);
```

- [ ] **Step 2: Update AuthenticationServiceTests.cs — failure cases**

For each `.ThrowAsync<ApplicationException>()` assertion, replace with result assertion. Example:
```csharp
// Before
var act = async () => await sut.LoginAsync(request, CancellationToken.None);
await act.Should().ThrowAsync<ApplicationException>().WithMessage("User not found");

// After
var result = await sut.LoginAsync(request, CancellationToken.None);
result.Success.Should().BeFalse();
result.Error.Should().Be("User not found");
result.Status.Should().Be(ResultStatus.BadRequest);
```

Rename test methods: `_ShouldThrow` → `_ShouldReturnFailure`.

- [ ] **Step 3: Update AuthenticationServiceTests.cs — success cases**

```csharp
// Before
var res = await sut.LoginAsync(request, CancellationToken.None);
res.Token.Should().NotBeNullOrEmpty();

// After
var result = await sut.LoginAsync(request, CancellationToken.None);
result.Success.Should().BeTrue();
result.Data.Should().NotBeNull();
result.Data!.Token.Should().NotBeNullOrEmpty();
```

- [ ] **Step 4: Run tests — expect compile errors (interface not updated yet)**

```bash
dotnet test BranavaFCTests/BranavaFCTests.csproj --filter "FullyQualifiedName~AuthenticationServiceTests" 2>&1 | head -30
```
Expected: Compile error — `LoginAsync` return type mismatch.

- [ ] **Step 5: Update AuthenticationService implementation**

Replace all `throw new ApplicationException(...)` with `return Result<TokenDto>.Fail(message, ResultStatus.BadRequest)`.
Wrap success returns: `return Result<TokenDto>.Ok(new TokenDto(...), null)`.
Remove `try-catch` blocks that rethrow (since exceptions now return as Results).
Keep only catch blocks for infrastructure exceptions (EF, logging).

Key replacements:
```csharp
// Before
if (user is null) throw new ApplicationException("User not found");
// After
if (user is null) return Result<TokenDto>.Fail("User not found");

// Before
return new TokenDto(jwt, refreshToken.Token);
// After
return Result<TokenDto>.Ok(new TokenDto(jwt, refreshToken.Token));
```

- [ ] **Step 6: Update AuthenticationController**

Change class declaration:
```csharp
public class AuthenticationController : BaseApiController
```

Add `using BratnavaFC.Domain.Common;` to usings.

Replace controller action bodies:
```csharp
// Before
try { var token = await _service.LoginAsync(dto, ct); return Ok(token); }
catch (ApplicationException ex) { return BadRequest(new { error = ex.Message }); }

// After
var result = await _service.LoginAsync(dto, ct);
return ToResponse(result);
```

- [ ] **Step 7: Run tests — expect pass**

```bash
dotnet test BranavaFCTests/BranavaFCTests.csproj --filter "FullyQualifiedName~AuthenticationServiceTests" -v minimal
```
Expected: All pass.

- [ ] **Step 8: Full build check**

```bash
dotnet build src/BratnavaFC.Api/BratnavaFC.Api.csproj
```

- [ ] **Step 9: Commit**

```bash
git add src/BratnavaFC.Application/Abstractions/IAuthenticationService.cs \
        src/BratnavaFC.Application/Services/AuthenticationService.cs \
        src/BratnavaFC.Api/Controllers/AuthenticationController.cs \
        BranavaFCTests/AuthenticationServiceTests.cs
git commit -m "feat: migrate AuthenticationService to Result pattern"
```

---

### Task 5: UserService

**Files:**
- Modify: `src/BratnavaFC.Application/Abstractions/IUserService.cs`
- Modify: `src/BratnavaFC.Application/Services/UserService.cs`
- Modify: `src/BratnavaFC.Api/Controllers/UsersController.cs`
- Modify: `BranavaFCTests/UserServiceTests.cs`

**Current throws in UserService:**
- `ApplicationException("User already exists with the user name...")` → `Fail(..., BadRequest)`
- `ApplicationException("User already exists with the email...")` → `Fail(..., BadRequest)`
- `ApplicationException("User not found.")` → `Fail("Usuário não encontrado.", NotFound)`
- `ApplicationException("CurrentPassword is required.")` → `Fail(..., BadRequest)`
- `ApplicationException("NewPassword is required.")` → `Fail(..., BadRequest)`
- `ApplicationException("Current password is invalid.")` → `Fail(..., BadRequest)`
- `ApplicationException("Invalid role.")` → `Fail(..., BadRequest)`
- `ApplicationException("Invalid status.")` → `Fail(..., BadRequest)`

- [ ] **Step 1: Update IUserService interface** — change all void returns to `Result`, add `Result<T>` to typed returns

- [ ] **Step 2: Update UserServiceTests.cs** — replace `.ThrowAsync<ApplicationException>()` with result assertions (4 throw assertions)

- [ ] **Step 3: Update UserService implementation** — replace throws with `Result.Fail()`/`Result.Ok()`, wrap success returns

- [ ] **Step 4: Update UsersController** — inherit `BaseApiController`, remove try-catch, call `ToResponse(result)`

- [ ] **Step 5: Run tests**

```bash
dotnet test BranavaFCTests/BranavaFCTests.csproj --filter "FullyQualifiedName~UserServiceTests" -v minimal
```

- [ ] **Step 6: Commit**

```bash
git add src/BratnavaFC.Application/Abstractions/IUserService.cs \
        src/BratnavaFC.Application/Services/UserService.cs \
        src/BratnavaFC.Api/Controllers/UsersController.cs \
        BranavaFCTests/UserServiceTests.cs
git commit -m "feat: migrate UserService to Result pattern"
```

---

### Task 6: GroupService

**Files:**
- Modify: `src/BratnavaFC.Application/Abstractions/IGroupService.cs`
- Modify: `src/BratnavaFC.Application/Services/GroupService.cs`
- Modify: `src/BratnavaFC.Api/Controllers/GroupsController.cs`
- Modify: `BranavaFCTests/GroupServiceTests.cs`
- Modify: `BranavaFCTests/GroupServiceCreatorLeaveTests.cs`
- Modify: `BranavaFCTests/GroupInviteTests.cs`

**Current throws in GroupService:** ~16 distinct throws. Key mappings:
- `UnauthorizedAccessException("Requesting user is not an admin...")` → `Fail(..., Forbidden)`
- `UnauthorizedAccessException("Only the group creator can use this operation.")` → `Fail(..., Forbidden)`
- `ApplicationException("Group not found.")` → `Fail("Grupo não encontrado.", NotFound)`
- `ApplicationException("User not found.")` → `Fail("Usuário não encontrado.", NotFound)`
- `InvalidOperationException("User is already a member...")` → `Fail(..., BadRequest)`
- `InvalidOperationException("There is already a pending invite...")` → `Fail(..., BadRequest)`
- All other `InvalidOperationException` → `Fail(..., BadRequest)`

**Note on `GroupInviteTests.cs`:** This file has 14 throw assertions — verify whether they are testing GroupService methods or entity-level validation. Entity-level throws (on `GroupEntity`, `GroupInviteEntity`) should **not** be changed. Only service-level orchestration throws should become Result assertions.

- [ ] **Step 1: Update IGroupService interface** — all void returns → `Result`, `Guid` return → `Result<Guid>`, etc.

- [ ] **Step 2: Update GroupServiceTests.cs** — 8 throw assertions → result assertions

- [ ] **Step 3: Update GroupServiceCreatorLeaveTests.cs** — 5 throw assertions → result assertions

- [ ] **Step 4: Update GroupInviteTests.cs** — identify service-level assertions only, update those; leave entity-level alone

- [ ] **Step 5: Update GroupService implementation** — replace all throws with Result returns; wrap success returns

Example for `CreateAsync`:
```csharp
// Before
public async Task<Guid> CreateAsync(CreateGroupDto request, CancellationToken ct)
{
    // ... logic ...
    return group.Id;
}

// After
public async Task<Result<Guid>> CreateAsync(CreateGroupDto request, CancellationToken ct)
{
    // ... logic ...
    return Result<Guid>.Ok(group.Id, "Grupo criado com sucesso.", ResultStatus.Created);
}
```

- [ ] **Step 6: Update GroupsController** — already inherits `GroupAuthorizedController` (which inherits `BaseApiController`), remove any try-catch, call `ToResponse(result)`

- [ ] **Step 7: Run tests**

```bash
dotnet test BranavaFCTests/BranavaFCTests.csproj \
  --filter "FullyQualifiedName~GroupServiceTests|FullyQualifiedName~GroupServiceCreatorLeaveTests|FullyQualifiedName~GroupInviteTests" \
  -v minimal
```

- [ ] **Step 8: Commit**

```bash
git add src/BratnavaFC.Application/Abstractions/IGroupService.cs \
        src/BratnavaFC.Application/Services/GroupService.cs \
        src/BratnavaFC.Api/Controllers/GroupsController.cs \
        BranavaFCTests/GroupServiceTests.cs \
        BranavaFCTests/GroupServiceCreatorLeaveTests.cs \
        BranavaFCTests/GroupInviteTests.cs
git commit -m "feat: migrate GroupService to Result pattern"
```

---

### Task 7: MatchService

**Files:**
- Modify: `src/BratnavaFC.Application/Abstractions/IMatchService.cs`
- Modify: `src/BratnavaFC.Application.Abstractions/IMatchService.cs` ← legacy duplicate, update too
- Modify: `src/BratnavaFC.Application/Services/MatchService.cs`
- Modify: `src/BratnavaFC.Api/Controllers/MatchesController.cs`
- Modify: `BranavaFCTests/MatchServiceTests.cs`

**Note:** MatchService.cs is large (~51KB). Read it in chunks. Only known explicit throw: `InvalidOperationException("Partida nao encontrada.")`. Check for others inside the full file.

- [ ] **Step 1: Read MatchService.cs in full** — identify all `throw new` statements and map each to a Result return

- [ ] **Step 2: Update both IMatchService interface files** — primary at `src/BratnavaFC.Application/Abstractions/IMatchService.cs` AND legacy at `src/BratnavaFC.Application.Abstractions/IMatchService.cs`

- [ ] **Step 3: Update MatchServiceTests.cs** — 12 throw assertions → result assertions

- [ ] **Step 4: Update MatchService implementation** — replace throws, wrap returns
  - POST operations → `ResultStatus.Created` + success message
  - GET operations → `message: null`
  - DELETE → success message

- [ ] **Step 5: Update MatchesController** — remove try-catch blocks, call `ToResponse(result)`. Controller already inherits `GroupAuthorizedController`.

- [ ] **Step 6: Run tests**

```bash
dotnet test BranavaFCTests/BranavaFCTests.csproj --filter "FullyQualifiedName~MatchServiceTests" -v minimal
```

- [ ] **Step 7: Commit**

```bash
git add src/BratnavaFC.Application/Abstractions/IMatchService.cs \
        "src/BratnavaFC.Application.Abstractions/IMatchService.cs" \
        src/BratnavaFC.Application/Services/MatchService.cs \
        src/BratnavaFC.Api/Controllers/MatchesController.cs \
        BranavaFCTests/MatchServiceTests.cs
git commit -m "feat: migrate MatchService to Result pattern"
```

---

### Task 8: PlayerService

**Files:**
- Modify: `src/BratnavaFC.Application/Abstractions/IPlayerService.cs`
- Modify: `src/BratnavaFC.Application/Services/PlayerService.cs`
- Modify: `src/BratnavaFC.Api/Controllers/PlayersController.cs`
- Modify: `BranavaFCTests/PlayerServiceTests.cs`

**Current throws:**
- `ApplicationException("Group does not exist.")` → `Fail("Grupo não encontrado.", NotFound)`
- `ApplicationException("User does not exist.")` → `Fail("Usuário não encontrado.", NotFound)`
- `InvalidOperationException("Player already exists in the group.")` → `Fail(..., BadRequest)`
- `ApplicationException("PlayerEntity not found.")` → `Fail("Jogador não encontrado.", NotFound)`
- `InvalidOperationException("UserId is required.")` → `Fail(..., BadRequest)`
- `UnauthorizedAccessException("You can only leave your own player.")` → `Fail(..., Forbidden)`

- [ ] **Step 1: Update IPlayerService interface**

- [ ] **Step 2: Update PlayerServiceTests.cs** — 8 throw assertions → result assertions

- [ ] **Step 3: Update PlayerService implementation**

- [ ] **Step 4: Update PlayersController** — change inheritance to `BaseApiController`, call `ToResponse(result)` (no existing try-catch to remove)

- [ ] **Step 5: Run tests**

```bash
dotnet test BranavaFCTests/BranavaFCTests.csproj --filter "FullyQualifiedName~PlayerServiceTests" -v minimal
```

- [ ] **Step 6: Commit**

```bash
git add src/BratnavaFC.Application/Abstractions/IPlayerService.cs \
        src/BratnavaFC.Application/Services/PlayerService.cs \
        src/BratnavaFC.Api/Controllers/PlayersController.cs \
        BranavaFCTests/PlayerServiceTests.cs
git commit -m "feat: migrate PlayerService to Result pattern"
```

---

### Task 9: PaymentService

**Files:**
- Modify: `src/BratnavaFC.Application/Abstractions/IPaymentService.cs`
- Modify: `src/BratnavaFC.Application/Services/PaymentService.cs`
- Modify: `src/BratnavaFC.Api/Controllers/PaymentController.cs`

**Important:** `PaymentController` has `catch (UnauthorizedAccessException ex) { return Forbid(ex.Message); }` blocks. After migrating the service to return `Result.Fail(..., ResultStatus.Forbidden)`, **remove these catch blocks** from the controller — they become dead code.

PaymentService.cs is large (~26KB). Read it in full to identify all throws before starting.

- [ ] **Step 1: Read PaymentService.cs in full** — list all `throw new` statements

- [ ] **Step 2: Update IPaymentService interface**

- [ ] **Step 3: Update PaymentService implementation** — `UnauthorizedAccessException` → `Result.Fail(..., ResultStatus.Forbidden)`

- [ ] **Step 4: Update PaymentController**
  - Inherit `BaseApiController` (already inherits `GroupAuthorizedController` → already inherits `BaseApiController` after Task 2)
  - Remove `catch (UnauthorizedAccessException)` blocks
  - Replace remaining try-catch with `ToResponse(result)`

- [ ] **Step 5: Build check** (no PaymentService tests exist currently)

```bash
dotnet build src/BratnavaFC.Api/BratnavaFC.Api.csproj
```

- [ ] **Step 6: Commit**

```bash
git add src/BratnavaFC.Application/Abstractions/IPaymentService.cs \
        src/BratnavaFC.Application/Services/PaymentService.cs \
        src/BratnavaFC.Api/Controllers/PaymentController.cs
git commit -m "feat: migrate PaymentService to Result pattern"
```

---

### Task 10: CalendarService

**Files:**
- Modify: `src/BratnavaFC.Application/Abstractions/ICalendarService.cs`
- Modify: `src/BratnavaFC.Application/Services/CalendarService.cs`
- Modify: `src/BratnavaFC.Api/Controllers/CalendarController.cs`

**Current throws (all `InvalidOperationException`):**
- `"A data inicial não pode ser maior que a data final."` → `Fail(..., BadRequest)`
- `"Evento não encontrado."` → `Fail(..., NotFound)`
- `"Categoria não encontrada."` → `Fail(..., NotFound)`
- `"GroupId inválido."` → `Fail(..., BadRequest)`
- `"Grupo não encontrado."` → `Fail(..., NotFound)`
- `"Data inválida. Use o formato YYYY-MM-DD."` → `Fail(..., BadRequest)`
- `"Horário inválido. Use o formato HH:mm."` → `Fail(..., BadRequest)`
- `"Categorias do sistema não podem ser excluídas."` → `Fail(..., BadRequest)`

- [ ] **Step 1: Update ICalendarService interface**

- [ ] **Step 2: Update CalendarService implementation**

- [ ] **Step 3: Update CalendarController** — remove try-catch blocks, call `ToResponse(result)`

- [ ] **Step 4: Build check**

```bash
dotnet build src/BratnavaFC.Api/BratnavaFC.Api.csproj
```

- [ ] **Step 5: Commit**

```bash
git add src/BratnavaFC.Application/Abstractions/ICalendarService.cs \
        src/BratnavaFC.Application/Services/CalendarService.cs \
        src/BratnavaFC.Api/Controllers/CalendarController.cs
git commit -m "feat: migrate CalendarService to Result pattern"
```

---

### Task 11: TeamColorService

**Files:**
- Modify: `src/BratnavaFC.Application/Abstractions/ITeamColorService.cs`
- Modify: `src/BratnavaFC.Application.Abstractions/ITeamColorService.cs` ← legacy duplicate
- Modify: `src/BratnavaFC.Application/Services/TeamColorService.cs`
- Modify: `src/BratnavaFC.Api/Controllers/TeamColorController.cs`
- Modify: `BranavaFCTests/TeamColorServiceTests.cs`

**Current throws:**
- `InvalidOperationException("Cor do time nao encontrada para este grupo.")` → `Fail(..., NotFound)`
- `InvalidOperationException("Payload invalido.")` → `Fail(..., BadRequest)`
- `InvalidOperationException("GroupId e obrigatorio.")` → `Fail(..., BadRequest)`
- `InvalidOperationException("Group nao encontrado.")` → `Fail(..., NotFound)`

- [ ] **Step 1: Update both ITeamColorService interface files** (primary + legacy)

- [ ] **Step 2: Update TeamColorServiceTests.cs** — 3 throw assertions → result assertions

- [ ] **Step 3: Update TeamColorService implementation**

- [ ] **Step 4: Update TeamColorController** — change inheritance to `BaseApiController`, call `ToResponse(result)` (no existing try-catch)

- [ ] **Step 5: Run tests**

```bash
dotnet test BranavaFCTests/BranavaFCTests.csproj --filter "FullyQualifiedName~TeamColorServiceTests" -v minimal
```

- [ ] **Step 6: Commit**

```bash
git add src/BratnavaFC.Application/Abstractions/ITeamColorService.cs \
        "src/BratnavaFC.Application.Abstractions/ITeamColorService.cs" \
        src/BratnavaFC.Application/Services/TeamColorService.cs \
        src/BratnavaFC.Api/Controllers/TeamColorController.cs \
        BranavaFCTests/TeamColorServiceTests.cs
git commit -m "feat: migrate TeamColorService to Result pattern"
```

---

### Task 12: GroupSettingsService

**Files:**
- Modify: `src/BratnavaFC.Application/Abstractions/IGroupSettingsService.cs`
- Modify: `src/BratnavaFC.Application/Services/GroupSettingsService.cs`
- Modify: `src/BratnavaFC.Api/Controllers/GroupSettingsController.cs`
- Modify: `BranavaFCTests/GroupSettingsServiceTests.cs`

**Current throws:**
- `InvalidOperationException("GroupId e obrigatorio.")` → `Fail(..., BadRequest)`
- `InvalidOperationException("Group nao encontrado.")` → `Fail(..., NotFound)`

- [ ] **Step 1: Update IGroupSettingsService interface**

- [ ] **Step 2: Update GroupSettingsServiceTests.cs** — 1 throw assertion → result assertion

- [ ] **Step 3: Update GroupSettingsService implementation**

- [ ] **Step 4: Update GroupSettingsController** — remove try-catch, call `ToResponse(result)`

- [ ] **Step 5: Run tests**

```bash
dotnet test BranavaFCTests/BranavaFCTests.csproj --filter "FullyQualifiedName~GroupSettingsServiceTests" -v minimal
```

- [ ] **Step 6: Commit**

```bash
git add src/BratnavaFC.Application/Abstractions/IGroupSettingsService.cs \
        src/BratnavaFC.Application/Services/GroupSettingsService.cs \
        src/BratnavaFC.Api/Controllers/GroupSettingsController.cs \
        BranavaFCTests/GroupSettingsServiceTests.cs
git commit -m "feat: migrate GroupSettingsService to Result pattern"
```

---

### Task 13: TeamGenerationService

**Files:**
- Modify: `src/BratnavaFC.Application/Services/TeamGenerationService.cs`
- Modify: `src/BratnavaFC.Api/Controllers/TeamGenerationController.cs`
- Modify: `BranavaFCTests/TeamGenerationServiceTests.cs`

**Note:** No interface exists for TeamGenerationService — it is injected as a concrete type. Update the service directly.

- [ ] **Step 1: Read TeamGenerationService.cs** — identify all throws

- [ ] **Step 2: Update TeamGenerationServiceTests.cs** — 2 throw assertions → result assertions

- [ ] **Step 3: Update TeamGenerationService** — replace throws with Result returns, wrap success returns

- [ ] **Step 4: Update TeamGenerationController** — change inheritance to `BaseApiController`, call `ToResponse(result)`

- [ ] **Step 5: Run tests**

```bash
dotnet test BranavaFCTests/BranavaFCTests.csproj --filter "FullyQualifiedName~TeamGenerationServiceTests" -v minimal
```

- [ ] **Step 6: Commit**

```bash
git add src/BratnavaFC.Application/Services/TeamGenerationService.cs \
        src/BratnavaFC.Api/Controllers/TeamGenerationController.cs \
        BranavaFCTests/TeamGenerationServiceTests.cs
git commit -m "feat: migrate TeamGenerationService to Result pattern"
```

---

### Task 14: PlayerStatsService + HolidayService

**Files:**
- Modify: `src/BratnavaFC.Application/Abstractions/IPlayerStatsService.cs`
- Modify: `src/BratnavaFC.Application.Abstractions/IPlayerStatsService.cs` ← legacy duplicate
- Modify: `src/BratnavaFC.Application/Services/PlayerStatsService.cs`
- Modify: `src/BratnavaFC.Application/Abstractions/IHolidayService.cs`
- Modify: `src/BratnavaFC.Application/Services/HolidayService.cs`

**No dedicated controllers:** `IPlayerStatsService` is injected into `TeamGenerationController`, which is already updated in Task 13. `IHolidayService` has no controller — it is called internally by `CalendarService`. No additional controller files to change here.

**PlayerStatsService:** Only `ArgumentNullException` on constructor args — these should stay as constructor guards (not business logic). Wrap the `EnrichPlayersAsync` return in `Result<List<PlayerStats>>.Ok(...)`.

**HolidayService:** No explicit business throws. Wrap return in `Result<IReadOnlyList<HolidayDto>>.Ok(...)`.

- [ ] **Step 1: Update both IPlayerStatsService interface files** (primary + legacy)

- [ ] **Step 2: Update PlayerStatsService** — wrap return in Result, keep constructor ArgumentNullException guards as-is

- [ ] **Step 3: Update IHolidayService + HolidayService**

- [ ] **Step 4: Run PlayerStatsService tests**

```bash
dotnet test BranavaFCTests/BranavaFCTests.csproj --filter "FullyQualifiedName~PlayerStatsServiceTests" -v minimal
```
Expected: All pass (no throw assertions to migrate — only wrapping return values).

- [ ] **Step 5: Build check**

```bash
dotnet build src/BratnavaFC.Api/BratnavaFC.Api.csproj
```

- [ ] **Step 6: Commit**

```bash
git add src/BratnavaFC.Application/Abstractions/IPlayerStatsService.cs \
        "src/BratnavaFC.Application.Abstractions/IPlayerStatsService.cs" \
        src/BratnavaFC.Application/Services/PlayerStatsService.cs \
        src/BratnavaFC.Application/Abstractions/IHolidayService.cs \
        src/BratnavaFC.Application/Services/HolidayService.cs
git commit -m "feat: migrate PlayerStatsService and HolidayService to Result pattern"
```

---

### Task 15: Full test suite verification

- [ ] **Step 1: Run all tests**

```bash
dotnet test BranavaFCTests/BranavaFCTests.csproj -v minimal 2>&1 | tail -20
```
Expected: All tests pass. Zero failures.

- [ ] **Step 2: If any failures, investigate and fix**

Check if remaining entity tests (MatchEntityTests, PlayerEntityTests, etc.) pass — they should, since entity-level throws are unchanged.

- [ ] **Step 3: Full build**

```bash
dotnet build
```

---

## Phase 3 — Frontend

> Fully independent of Phase 2. Can start immediately after Phase 1 (which only defines the contract). Touches only `/c/github/BratnavaFCFront/`.

---

### Task 16: ApiResponse type + getResponseMessage helper

**Files:**
- Create: `src/api/apiResponse.ts`

- [ ] **Step 1: Create src/api/apiResponse.ts**

```typescript
import { extractApiError } from '../lib/apiError';

export interface ApiResponse<T = unknown> {
  success: boolean;
  data: T | null;
  message: string | null;
  error: string | null;
  errors: string[];
}

/**
 * Extracts a user-readable error message from an Axios error.
 * Prefers the ApiResponse envelope; falls back to extractApiError for
 * non-envelope responses (network errors, legacy endpoints, etc.).
 */
export function getResponseMessage(
  e: unknown,
  fallback = 'Ocorreu um erro inesperado.'
): string {
  const data = (e as any)?.response?.data as ApiResponse | undefined;
  // Detect envelope shape
  if (data && typeof data.success === 'boolean') {
    if (data.error) return data.error;
    if (data.errors?.length) return data.errors[0];
  }
  return extractApiError(e, fallback);
}
```

- [ ] **Step 2: Verify TypeScript compiles**

```bash
cd /c/github/BratnavaFCFront
npx tsc --noEmit 2>&1 | head -30
```
Expected: No new errors from this file.

- [ ] **Step 3: Commit**

```bash
git add src/api/apiResponse.ts
git commit -m "feat: add ApiResponse<T> interface and getResponseMessage helper"
```

---

### Task 17: Type all endpoints in endpoints.ts

**Files:**
- Modify: `src/api/endpoints.ts`

- [ ] **Step 1: Add ApiResponse import at the top of endpoints.ts**

```typescript
import type { ApiResponse } from './apiResponse';
```

- [ ] **Step 2: Add generic type to every endpoint function**

For each `http.get(...)`, `http.post(...)`, `http.put(...)`, `http.patch(...)`, `http.delete(...)` call, add the appropriate `ApiResponse<T>` generic. Use the DTO types already imported from `./generated/types`.

Example:
```typescript
// Before
list: (groupId: string) => http.get(`/api/Matches/group/${groupId}`),
create: (groupId: string, dto: CreateMatchDto) => http.post(`/api/Matches/group/${groupId}`, dto),

// After
list: (groupId: string) =>
  http.get<ApiResponse<MatchDetailsDto[]>>(`/api/Matches/group/${groupId}`),
create: (groupId: string, dto: CreateMatchDto) =>
  http.post<ApiResponse<MatchDetailsDto>>(`/api/Matches/group/${groupId}`, dto),
```

For endpoints that return no data (DELETE, some void mutations), use `ApiResponse<null>`.

- [ ] **Step 3: Verify TypeScript compiles**

```bash
npx tsc --noEmit 2>&1 | head -50
```
Expected: No type errors from endpoints.ts.

- [ ] **Step 4: Commit**

```bash
git add src/api/endpoints.ts
git commit -m "feat: add ApiResponse<T> generics to all endpoint functions"
```

---

### Task 18: Update pages and components — error handling + success toasts

**Files:**
- Modify: `src/pages/*.tsx` — all page files with API calls
- Modify: `src/components/modals/*.tsx` — all modal files with API calls
- Modify: `src/domains/**/*.tsx` — step components with API calls

**Rule:** Replace every occurrence of:
- `extractApiError(e, ...)` in catch blocks → `getResponseMessage(e)`
- `toast.success("hardcoded string")` after mutations → `if (res.data.message) toast.success(res.data.message)`

Do NOT remove the `extractApiError` import from `src/lib/apiError.ts` — it is still used by `getResponseMessage` internally.

- [ ] **Step 1: Update import in all modified files**

Add to each file that has API calls:
```typescript
import { getResponseMessage } from '../api/apiResponse'; // adjust path depth
```

- [ ] **Step 2: Update src/pages/LoginPage.tsx**

```typescript
// Before
toast.error(extractApiError(e, "Falha ao fazer login."));
// After
toast.error(getResponseMessage(e, "Falha ao fazer login."));
```

(LoginPage has no success toast to update — navigation replaces it.)

- [ ] **Step 3: Update src/pages/GroupsPage.tsx and GroupSettingsPage.tsx**

For each API mutation call:
- Error: `getResponseMessage(e)` instead of `extractApiError(e, ...)`
- Success: `if (res.data.message) toast.success(res.data.message)` instead of hardcoded string

- [ ] **Step 4: Update src/pages/PaymentsPage.tsx**

- [ ] **Step 5: Update src/pages/CalendarPage.tsx**

- [ ] **Step 6: Update src/pages/TeamColorsPage.tsx, HistoryPage.tsx, MatchesPage.tsx**

- [ ] **Step 7: Update src/components/modals/*.tsx** — all modals that make API calls

Example (CreateEditEventModal.tsx):
```typescript
// Before
toast.success("Evento atualizado!");
// After
if (res.data.message) toast.success(res.data.message);

// Before
toast.error(extractApiError(e, "Erro ao salvar evento."));
// After
toast.error(getResponseMessage(e));
```

- [ ] **Step 8: Update src/domains/**/*.tsx** — StepCreate, StepAccept, StepTeams, StepPlaying, StepPost, GoalTracker, etc.

- [ ] **Step 9: Verify TypeScript compiles**

```bash
npx tsc --noEmit 2>&1 | head -50
```
Expected: No type errors.

- [ ] **Step 10: Commit**

```bash
git add src/pages/ src/components/modals/ src/domains/
git commit -m "feat: update all components to use getResponseMessage and backend toast messages"
```

---

## Final Verification

- [ ] **Backend: Run full test suite**

```bash
cd /c/github/BratnavaFCApi
dotnet test BranavaFCTests/BranavaFCTests.csproj -v minimal 2>&1 | tail -10
```
Expected: All tests pass.

- [ ] **Backend: Final build**

```bash
dotnet build
```

- [ ] **Frontend: Final TypeScript check**

```bash
cd /c/github/BratnavaFCFront
npx tsc --noEmit
```

- [ ] **Search for any remaining raw extractApiError calls in component files**

```bash
grep -r "extractApiError" /c/github/BratnavaFCFront/src --include="*.tsx" --include="*.ts" \
  --exclude="apiResponse.ts" --exclude="apiError.ts"
```
Expected: Zero results (all migrated to `getResponseMessage`).

- [ ] **Search for hardcoded success toast strings that should come from backend**

```bash
grep -r 'toast\.success("' /c/github/BratnavaFCFront/src --include="*.tsx"
```
Review results — any remaining hardcoded strings should be intentional (validation feedback, not API mutation responses).
