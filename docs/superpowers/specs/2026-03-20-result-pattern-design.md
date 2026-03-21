# Result Pattern — Design Spec
**Date:** 2026-03-20
**Scope:** BratnavaFC backend (BratnavaFCApi) + frontend (BratnavaFCFront)

---

## Overview

Introduce the Result Pattern across the full stack: services return `Result<T>` instead of throwing exceptions, controllers wrap responses in a consistent `ApiResponse<T>` envelope, and the frontend handles the envelope uniformly — showing toast notifications from backend messages.

---

## Decisions

| Question | Decision |
|---|---|
| Scope | Service layer + HTTP envelope (full stack) |
| Which endpoints | All endpoints (GET and mutations) |
| GET messages | `null` — no toast on read operations |
| Error format | `error: string` (main) + `errors: string[]` (validation details) |
| HTTP status codes | Maintained (400, 403, 404, 500) alongside envelope |
| Implementation | Custom `Result<T>` class, no external library |

---

## Backend

### 1. New Types — `BratnavaFC.Domain/Common/`

#### `Result.cs` — with `ResultBase` to avoid inheritance hiding

To avoid the `static new` hiding problem when `Result` inherits `Result<T>`, use a shared non-generic base:

```csharp
// Non-generic base carries all state
public abstract class ResultBase
{
    public bool Success { get; protected init; }
    public string? Message { get; protected init; }
    public string? Error { get; protected init; }
    public List<string> Errors { get; protected init; } = [];
    public ResultStatus Status { get; protected init; } = ResultStatus.Ok;
}

// Generic variant adds Data
public class Result<T> : ResultBase
{
    public T? Data { get; private init; }

    private Result() { }

    public static Result<T> Ok(T data, string? message = null,
        ResultStatus status = ResultStatus.Ok) => new()
    {
        Success = true, Data = data, Message = message, Status = status
    };

    public static Result<T> Fail(string error, ResultStatus status = ResultStatus.BadRequest,
        List<string>? errors = null) => new()
    {
        Success = false, Error = error, Status = status, Errors = errors ?? []
    };
}

// Void variant for operations with no data
public class Result : ResultBase
{
    private Result() { }

    public static Result Ok(string? message = null) => new()
    {
        Success = true, Message = message, Status = ResultStatus.Ok
    };

    public static Result Fail(string error, ResultStatus status = ResultStatus.BadRequest,
        List<string>? errors = null) => new()
    {
        Success = false, Error = error, Status = status, Errors = errors ?? []
    };
}

// Status enum drives HTTP status code — avoids fragile string parsing
public enum ResultStatus
{
    Ok          = 200,
    Created     = 201,
    BadRequest  = 400,
    Unauthorized = 401,
    Forbidden   = 403,
    NotFound    = 404,
}
```

#### `ApiResponse.cs`

```csharp
public record ApiResponse<T>(
    bool Success,
    T? Data,
    string? Message,
    string? Error,
    List<string> Errors
);
```

---

### 2. Base Controller — `BaseApiController`

A new `BaseApiController` is introduced so that **all** controllers (including `AuthenticationController` and `UsersController` which do not currently inherit `GroupAuthorizedController`) have access to `ToResponse`:

```csharp
// BratnavaFC.Api/Controllers/BaseApiController.cs
[ApiController]
public abstract class BaseApiController : ControllerBase
{
    protected IActionResult ToResponse<T>(Result<T> result, int? overrideSuccessStatus = null)
    {
        var response = new ApiResponse<T>(
            result.Success,
            result.Data,
            result.Message,
            result.Error,
            result.Errors
        );

        if (result.Success)
        {
            var status = overrideSuccessStatus ?? (int)result.Status;
            return StatusCode(status, response);
        }

        return StatusCode((int)result.Status, response);
    }

    protected IActionResult ToResponse(Result result, int? overrideSuccessStatus = null)
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

`GroupAuthorizedController` inherits `BaseApiController` instead of `ControllerBase`.
All other controllers also inherit `BaseApiController`.

**Envelope exceptions:** `Forbid()` and `Unauthorized()` short-circuits in `IsAuthorizedForGroupAsync` are **kept as-is** — they return ASP.NET's standard 403/401 without an envelope body. This is a documented exception to the envelope contract (authorization gates, not business logic).

**`PaymentController` catch blocks:** `PaymentController` currently catches `UnauthorizedAccessException` from the service and calls `Forbid(ex.Message)`. After migration, the service converts those throws to `Result.Fail(..., ResultStatus.Forbidden)`, so these `catch` blocks become dead code and **must be removed** from `PaymentController`.

**`ResultStatus.Unauthorized` (401):** This enum value is reserved exclusively for the JWT/authentication layer (middleware, token expiry). Services **never** set `ResultStatus.Unauthorized` — they use `ResultStatus.Forbidden` for business-rule access denials. The 401 responses originate from ASP.NET's `[Authorize]` attribute and the token refresh interceptor, not from service results.

---

### 3. Service Layer

All service interfaces in `BratnavaFC.Application/Abstractions/` return `Result<T>` or `Result`:

```csharp
// Before
Task<MatchEntity> Create(Guid groupId, MatchEntity match, CancellationToken ct);
Task UpdateAsync(Guid groupId, Guid matchId, UpdateMatchDto dto, CancellationToken ct);
Task DeleteAsync(Guid groupId, Guid matchId, CancellationToken ct);

// After
Task<Result<MatchEntity>> Create(Guid groupId, MatchEntity match, CancellationToken ct);
Task<Result> UpdateAsync(Guid groupId, Guid matchId, UpdateMatchDto dto, CancellationToken ct);
Task<Result> DeleteAsync(Guid groupId, Guid matchId, CancellationToken ct);
```

**Special case — `TeamGenerationService`:** This service has no interface and is injected as a concrete type. The service implementation is updated to return `Result<T>` directly; no new interface is created as part of this spec.

**Duplicate interface location:** Two interface locations exist in the repo:
- `src/BratnavaFC.Application/Abstractions/` — primary (11 interfaces, all updated here)
- `src/BratnavaFC.Application.Abstractions/` — legacy duplicate (3 files: `IMatchService`, `IPlayerStatsService`, `ITeamColorService`). These **must also be updated** to avoid compilation errors if still referenced.

**Exception → Result mapping in services:**

| Exception type | Converts to |
|---|---|
| `InvalidOperationException` (business rule) | `Result.Fail(ex.Message, ResultStatus.BadRequest)` |
| `ApplicationException` (auth/not found) | `Result.Fail(ex.Message, ResultStatus.BadRequest)` |
| `UnauthorizedAccessException` | `Result.Fail(ex.Message, ResultStatus.Forbidden)` |
| Infrastructure exceptions (EF Core, network) | Allowed to propagate → caught by global middleware |

**Rules for messages:**

| Operation | Success message | Status |
|---|---|---|
| GET (list/object) | `null` | `ResultStatus.Ok` |
| POST | `"[Entity] criado com sucesso."` | `ResultStatus.Created` |
| PUT/PATCH | `"[Entity] atualizado com sucesso."` | `ResultStatus.Ok` |
| DELETE | `"[Entity] removido com sucesso."` | `ResultStatus.Ok` |
| Not found | `"[Entity] não encontrado."` | `ResultStatus.NotFound` |
| Forbidden | `"Sem permissão para esta operação."` | `ResultStatus.Forbidden` |

---

### 4. Controller Layer — before/after

```csharp
// Before
try
{
    var entity = new MatchEntity(groupId, dto.PlayedAt, dto.PlaceName);
    var created = await _service.Create(groupId, entity, ct);
    return CreatedAtAction(nameof(Get), new { groupId, matchId = created.Id }, ToDto(created));
}
catch (InvalidOperationException ex)
{
    return BadRequest(new { error = ex.Message });
}

// After — service sets ResultStatus.Created via Ok(..., status: ResultStatus.Created)
var entity = new MatchEntity(groupId, dto.PlayedAt, dto.PlaceName);
var result = await _service.Create(groupId, entity, ct);
return ToResponse(result);
```

**Service side for POST (sets Created status):**
```csharp
// In MatchService.Create:
return Result<MatchEntity>.Ok(entity, "Partida criada com sucesso.", ResultStatus.Created);
```

All `try-catch` blocks for business logic are removed. Controllers only call the service and delegate to `ToResponse`.

---

### 5. Global Exception Middleware (`Program.cs`)

Catches unhandled infrastructure exceptions and returns a consistent `ApiResponse`:

```csharp
app.UseExceptionHandler(appError =>
{
    appError.Run(async context =>
    {
        context.Response.StatusCode = 500;
        context.Response.ContentType = "application/json";
        var response = new ApiResponse<object>(false, null, null, "Erro interno no servidor.", []);
        await context.Response.WriteAsJsonAsync(response);
    });
});
```

Added **before** `UseAuthentication()` in the middleware pipeline.

---

### 6. Tests (`BranavaFCTests/`)

All tests that assert thrown exceptions are rewritten to assert on `Result`:

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

Success tests access data via `result.Data`:

```csharp
// Before
var res = await sut.LoginAsync(request, CancellationToken.None);
res.Token.Should().NotBeNullOrEmpty();

// After
var result = await sut.LoginAsync(request, CancellationToken.None);
result.Success.Should().BeTrue();
result.Data!.Token.Should().NotBeNullOrEmpty();
result.Message.Should().NotBeNullOrEmpty();
```

Test method names: `_ShouldThrow` → `_ShouldReturnFailure` / `_ShouldReturnSuccess`

---

## Frontend

### 1. New type `ApiResponse<T>`

File: `src/api/apiResponse.ts` (new file)

```typescript
export interface ApiResponse<T = unknown> {
  success: boolean;
  data: T | null;
  message: string | null;
  error: string | null;
  errors: string[];
}
```

### 2. `getResponseMessage` helper

Same file `src/api/apiResponse.ts`. Imports `extractApiError` from its current location `../lib/apiError`:

```typescript
import { extractApiError } from '../lib/apiError';

export function getResponseMessage(e: unknown, fallback = "Ocorreu um erro inesperado."): string {
  const data = (e as any)?.response?.data as ApiResponse | undefined;
  // Envelope response from backend
  if (data && typeof data.success === 'boolean') {
    if (data.error) return data.error;
    if (data.errors?.length) return data.errors[0];
  }
  // Fallback for non-envelope errors (network, 500 before middleware, etc.)
  return extractApiError(e, fallback);
}
```

**Note:** `extractApiError` in `src/lib/apiError.ts` is **not modified or moved**. It handles legacy non-envelope error shapes (ASP.NET ModelState objects, plain strings) and remains the fallback.

### 3. `endpoints.ts` — typed return values

```typescript
// Before
create: (groupId: string, dto: CreateMatchDto) =>
    http.post(`/api/Matches/group/${groupId}`, dto),

// After
create: (groupId: string, dto: CreateMatchDto) =>
    http.post<ApiResponse<MatchDetailsDto>>(`/api/Matches/group/${groupId}`, dto),
```

All endpoint functions receive typed `ApiResponse<T>` generics.

### 4. Component usage pattern

```typescript
// Before
try {
    await MatchesApi.create(groupId, dto);
    toast.success("Partida criada com sucesso!");
} catch (e) {
    toast.error(extractApiError(e, "Erro ao criar partida."));
}

// After
import { getResponseMessage } from '../api/apiResponse';

try {
    const res = await MatchesApi.create(groupId, dto);
    if (res.data.message) toast.success(res.data.message);
} catch (e) {
    toast.error(getResponseMessage(e));
}
```

### 5. Toast rules

| Situation | Action |
|---|---|
| Mutation response with non-null `message` | `toast.success(res.data.message)` |
| GET response (message always null) | silent — no toast |
| Error catch with envelope `error` field | `toast.error(getResponseMessage(e))` |
| Error catch without envelope (500/network) | `toast.error(getResponseMessage(e))` — falls through to `extractApiError` |

---

## Files Affected

### Backend — new files
- `src/BratnavaFC.Domain/Common/Result.cs` (includes `ResultBase`, `Result<T>`, `Result`, `ResultStatus`)
- `src/BratnavaFC.Domain/Common/ApiResponse.cs`
- `src/BratnavaFC.Api/Controllers/BaseApiController.cs`

### Backend — modified files
- `src/BratnavaFC.Api/Controllers/GroupAuthorizedController.cs` — inherit `BaseApiController` instead of `ControllerBase`
- `src/BratnavaFC.Api/Controllers/AuthenticationController.cs` — inherit `BaseApiController`
- `src/BratnavaFC.Api/Controllers/UsersController.cs` — inherit `BaseApiController`
- `src/BratnavaFC.Api/Controllers/PlayersController.cs` — inherit `BaseApiController`
- `src/BratnavaFC.Api/Controllers/TeamColorController.cs` — inherit `BaseApiController`
- `src/BratnavaFC.Api/Controllers/TeamGenerationController.cs` — inherit `BaseApiController`
- `src/BratnavaFC.Api/Controllers/PaymentController.cs` — inherit `BaseApiController`; remove `catch (UnauthorizedAccessException)` blocks (now handled by service Result)
- `src/BratnavaFC.Api/Controllers/*.cs` — remaining controllers: remove try-catch, call `ToResponse()`
- `src/BratnavaFC.Application/Abstractions/I*Service.cs` — 11 interfaces: update return types
- `src/BratnavaFC.Application.Abstractions/*.cs` — 3 legacy duplicate interfaces: update return types
- `src/BratnavaFC.Application/Services/*.cs` — 11 services: replace throws with `Result.Fail()`, wrap returns in `Result.Ok()`
- `src/BratnavaFC.Application/Services/TeamGenerationService.cs` — concrete service (no interface), same changes
- `src/BratnavaFC.Api/Program.cs` — add global exception middleware
- `BranavaFCTests/**/*.cs` — rewrite all exception assertions to Result assertions

### Frontend — new files
- `src/api/apiResponse.ts` — `ApiResponse<T>` interface + `getResponseMessage()`

### Frontend — modified files
- `src/api/endpoints.ts` — add `ApiResponse<T>` generics to all endpoint functions
- `src/components/modals/*.tsx` — update error handling to use `getResponseMessage()`
- `src/pages/*.tsx` — update error handling to use `getResponseMessage()`, success toasts from `res.data.message`
- `src/domains/**/*.tsx` — same updates
- `src/lib/apiError.ts` — no changes (kept as fallback)
