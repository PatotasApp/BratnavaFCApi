# Hangfire Bi-Weekly Clip Cleanup Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Run a Hangfire background job every two weeks that deletes from Cloudflare R2 (and the database) every `ReplayClipEntity` that has no associated likes or favorites.

**Architecture:** Add Hangfire with `Hangfire.PostgreSql` storage (reusing the existing Neon PostgreSQL connection string). The job logic lives in `Application/Services` with its interface in `Application/Abstractions` — no Hangfire dependency leaks into the Application layer. Hangfire is wired up in the API layer (`Program.cs`), which registers the server, dashboard, and recurring job schedule.

**Tech Stack:** Hangfire 1.8.x, Hangfire.PostgreSql 1.20.x, EF Core 8 (InMemory for tests), xUnit, FluentAssertions, Moq, NullLogger

---

## File Map

| Action | Path | Responsibility |
|--------|------|----------------|
| **Modify** | `src/BratnavaFC.Api/BratnavaFC.Api.csproj` | Add Hangfire NuGet references |
| **Create** | `src/BratnavaFC.Application/Abstractions/IClipCleanupJob.cs` | Job interface (no Hangfire dep) |
| **Create** | `src/BratnavaFC.Application/Services/ClipCleanupJob.cs` | Job implementation |
| **Modify** | `src/BratnavaFC.Api/Program.cs` | Hangfire DI config, dashboard, recurring job |
| **Create** | `BranavaFCTests/ClipCleanupJobTests.cs` | Unit tests (TDD) |

---

## Task 1: Add Hangfire NuGet Packages

**Files:**
- Modify: `src/BratnavaFC.Api/BratnavaFC.Api.csproj`

- [ ] **Step 1: Add package references**

  Open `src/BratnavaFC.Api/BratnavaFC.Api.csproj` and add inside the existing `<ItemGroup>` that holds the other `<PackageReference>` entries:

  ```xml
  <PackageReference Include="Hangfire.AspNetCore" Version="1.8.14" />
  <PackageReference Include="Hangfire.PostgreSql" Version="1.20.9" />
  ```

  Verify the versions are still the latest stable releases at [nuget.org/packages/Hangfire.AspNetCore](https://www.nuget.org/packages/Hangfire.AspNetCore) and [nuget.org/packages/Hangfire.PostgreSql](https://www.nuget.org/packages/Hangfire.PostgreSql) before committing.

- [ ] **Step 2: Restore packages**

  ```
  dotnet restore src/BratnavaFC.Api/BratnavaFC.Api.csproj
  ```

  Expected: `Restore completed` with no errors.

- [ ] **Step 3: Commit**

  ```
  git add src/BratnavaFC.Api/BratnavaFC.Api.csproj
  git commit -m "chore: add Hangfire.AspNetCore and Hangfire.PostgreSql packages"
  ```

---

## Task 2: Create IClipCleanupJob Interface

**Files:**
- Create: `src/BratnavaFC.Application/Abstractions/IClipCleanupJob.cs`

- [ ] **Step 1: Create the interface file**

  ```csharp
  namespace BratnavaFC.Application.Abstractions;

  public interface IClipCleanupJob
  {
      Task ExecuteAsync(CancellationToken ct);
  }
  ```

- [ ] **Step 2: Verify it compiles**

  ```
  dotnet build src/BratnavaFC.Application/BratnavaFC.Application.csproj
  ```

  Expected: `Build succeeded`.

---

## Task 3: Write Failing Tests for ClipCleanupJob

**Files:**
- Create: `BranavaFCTests/ClipCleanupJobTests.cs`

- [ ] **Step 1: Create the test file**

  ```csharp
  using BratnavaFC.Application.Abstractions;
  using BratnavaFC.Application.Services;
  using BratnavaFC.Domain.Entities;
  using BratnavaFC.Domain.Enums;
  using FluentAssertions;
  using Microsoft.EntityFrameworkCore;
  using Microsoft.Extensions.Logging.Abstractions;
  using Moq;

  namespace BranavaFC.Tests;

  public class ClipCleanupJobTests
  {
      private static ClipCleanupJob CreateSut(
          BratnavaFC.Infrastructure.Data.AppDbContext db,
          IReplayUrlService? r2 = null) =>
          new(db, r2 ?? Mock.Of<IReplayUrlService>(), NullLogger<ClipCleanupJob>.Instance);

      private static ReplayClipEntity MakeClip(string objectKey = "bucket/clip.mp4") =>
          new(Guid.NewGuid(), Guid.NewGuid(), "goal-replays", objectKey,
              "video/mp4", "etag", DateTimeOffset.UtcNow, MatchEventType.Gol);

      // ── 1. No clips ─────────────────────────────────────────────────────────

      [Fact]
      public async Task ExecuteAsync_WhenNoClips_ShouldNotCallR2()
      {
          // Arrange
          await using var db = DbContextFactory.Create(nameof(ExecuteAsync_WhenNoClips_ShouldNotCallR2));
          var r2 = new Mock<IReplayUrlService>();
          var sut = CreateSut(db, r2.Object);

          // Act
          await sut.ExecuteAsync(CancellationToken.None);

          // Assert
          r2.Verify(x => x.DeleteObjectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
      }

      // ── 2. Orphan clip (no likes, no favorites) ──────────────────────────────

      [Fact]
      public async Task ExecuteAsync_WhenClipHasNoLikesOrFavorites_ShouldDeleteFromR2AndDb()
      {
          // Arrange
          await using var db = DbContextFactory.Create(nameof(ExecuteAsync_WhenClipHasNoLikesOrFavorites_ShouldDeleteFromR2AndDb));
          var clip = MakeClip("bucket/orphan.mp4");
          db.ReplayClips.Add(clip);
          await db.SaveChangesAsync();

          var r2 = new Mock<IReplayUrlService>();
          r2.Setup(x => x.DeleteObjectAsync("bucket/orphan.mp4", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
          var sut = CreateSut(db, r2.Object);

          // Act
          await sut.ExecuteAsync(CancellationToken.None);

          // Assert
          r2.Verify(x => x.DeleteObjectAsync("bucket/orphan.mp4", It.IsAny<CancellationToken>()), Times.Once);
          var remaining = await db.ReplayClips.ToListAsync();
          remaining.Should().BeEmpty();
      }

      // ── 3. Liked clip ────────────────────────────────────────────────────────

      [Fact]
      public async Task ExecuteAsync_WhenClipHasLike_ShouldNotDelete()
      {
          // Arrange
          await using var db = DbContextFactory.Create(nameof(ExecuteAsync_WhenClipHasLike_ShouldNotDelete));
          var clip = MakeClip("bucket/liked.mp4");
          db.ReplayClips.Add(clip);
          db.ReplayLikes.Add(new ReplayLikeEntity(clip.Id, Guid.NewGuid()));
          await db.SaveChangesAsync();

          var r2 = new Mock<IReplayUrlService>();
          var sut = CreateSut(db, r2.Object);

          // Act
          await sut.ExecuteAsync(CancellationToken.None);

          // Assert
          r2.Verify(x => x.DeleteObjectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
          var remaining = await db.ReplayClips.ToListAsync();
          remaining.Should().HaveCount(1);
      }

      // ── 4. Favorited clip ────────────────────────────────────────────────────

      [Fact]
      public async Task ExecuteAsync_WhenClipHasFavorite_ShouldNotDelete()
      {
          // Arrange
          await using var db = DbContextFactory.Create(nameof(ExecuteAsync_WhenClipHasFavorite_ShouldNotDelete));
          var clip = MakeClip("bucket/favorited.mp4");
          db.ReplayClips.Add(clip);
          db.ReplayFavorites.Add(new ReplayFavoriteEntity(clip.Id, Guid.NewGuid()));
          await db.SaveChangesAsync();

          var r2 = new Mock<IReplayUrlService>();
          var sut = CreateSut(db, r2.Object);

          // Act
          await sut.ExecuteAsync(CancellationToken.None);

          // Assert
          r2.Verify(x => x.DeleteObjectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
          var remaining = await db.ReplayClips.ToListAsync();
          remaining.Should().HaveCount(1);
      }

      // ── 5. R2 throws → DB record must be preserved ───────────────────────────

      [Fact]
      public async Task ExecuteAsync_WhenR2DeleteThrows_ShouldPreserveDbRecord()
      {
          // Arrange
          await using var db = DbContextFactory.Create(nameof(ExecuteAsync_WhenR2DeleteThrows_ShouldPreserveDbRecord));
          var clip = MakeClip("bucket/failing.mp4");
          db.ReplayClips.Add(clip);
          await db.SaveChangesAsync();

          var r2 = new Mock<IReplayUrlService>();
          r2.Setup(x => x.DeleteObjectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("R2 unavailable"));
          var sut = CreateSut(db, r2.Object);

          // Act
          await sut.ExecuteAsync(CancellationToken.None);

          // Assert — DB record must NOT be deleted if R2 removal failed
          var remaining = await db.ReplayClips.ToListAsync();
          remaining.Should().HaveCount(1);
      }

      // ── 6. Mixed bag ─────────────────────────────────────────────────────────

      [Fact]
      public async Task ExecuteAsync_WithMixedClips_ShouldOnlyDeleteEligible()
      {
          // Arrange
          await using var db = DbContextFactory.Create(nameof(ExecuteAsync_WithMixedClips_ShouldOnlyDeleteEligible));

          var eligible    = MakeClip("bucket/eligible.mp4");
          var likedClip   = MakeClip("bucket/liked.mp4");
          var favClip     = MakeClip("bucket/favorited.mp4");

          db.ReplayClips.AddRange(eligible, likedClip, favClip);
          db.ReplayLikes.Add(new ReplayLikeEntity(likedClip.Id, Guid.NewGuid()));
          db.ReplayFavorites.Add(new ReplayFavoriteEntity(favClip.Id, Guid.NewGuid()));
          await db.SaveChangesAsync();

          var r2 = new Mock<IReplayUrlService>();
          r2.Setup(x => x.DeleteObjectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
          var sut = CreateSut(db, r2.Object);

          // Act
          await sut.ExecuteAsync(CancellationToken.None);

          // Assert
          r2.Verify(x => x.DeleteObjectAsync("bucket/eligible.mp4",  It.IsAny<CancellationToken>()), Times.Once);
          r2.Verify(x => x.DeleteObjectAsync("bucket/liked.mp4",     It.IsAny<CancellationToken>()), Times.Never);
          r2.Verify(x => x.DeleteObjectAsync("bucket/favorited.mp4", It.IsAny<CancellationToken>()), Times.Never);

          var remaining = await db.ReplayClips.ToListAsync();
          remaining.Should().HaveCount(2);
          remaining.Should().NotContain(c => c.ObjectKey == "bucket/eligible.mp4");
      }
  }
  ```

- [ ] **Step 2: Run the tests — expect compile failure** (ClipCleanupJob doesn't exist yet)

  ```
  dotnet test BranavaFCTests/BranavaFC.Tests.csproj --filter "FullyQualifiedName~ClipCleanupJobTests"
  ```

  Expected: build error `The type or namespace name 'ClipCleanupJob' could not be found`.

---

## Task 4: Implement ClipCleanupJob

**Files:**
- Create: `src/BratnavaFC.Application/Services/ClipCleanupJob.cs`

- [ ] **Step 1: Create the implementation**

  ```csharp
  using BratnavaFC.Application.Abstractions;
  using BratnavaFC.Domain.Entities;
  using BratnavaFC.Infrastructure.Data;
  using Microsoft.EntityFrameworkCore;
  using Microsoft.Extensions.Logging;

  namespace BratnavaFC.Application.Services;

  public sealed class ClipCleanupJob : IClipCleanupJob
  {
      private readonly AppDbContext _db;
      private readonly IReplayUrlService _r2;
      private readonly ILogger<ClipCleanupJob> _logger;

      public ClipCleanupJob(
          AppDbContext db,
          IReplayUrlService r2,
          ILogger<ClipCleanupJob> logger)
      {
          _db     = db;
          _r2     = r2;
          _logger = logger;
      }

      public async Task ExecuteAsync(CancellationToken ct)
      {
          var candidates = await _db.ReplayClips
              .Where(c =>
                  !_db.ReplayLikes.Any(l => l.ClipId == c.Id) &&
                  !_db.ReplayFavorites.Any(f => f.ClipId == c.Id))
              .ToListAsync(ct);

          _logger.LogInformation(
              "ClipCleanupJob: {Count} clip(s) eligible for deletion", candidates.Count);

          var deleted = new List<ReplayClipEntity>(candidates.Count);

          foreach (var clip in candidates)
          {
              try
              {
                  await _r2.DeleteObjectAsync(clip.ObjectKey, ct);
                  deleted.Add(clip);
              }
              catch (Exception ex)
              {
                  _logger.LogError(ex,
                      "ClipCleanupJob: failed to delete R2 object for clip {ClipId} ({ObjectKey})",
                      clip.Id, clip.ObjectKey);
              }
          }

          if (deleted.Count > 0)
          {
              _db.ReplayClips.RemoveRange(deleted);
              await _db.SaveChangesAsync(ct);
          }

          _logger.LogInformation(
              "ClipCleanupJob: completed — deleted {Deleted}/{Total} clip(s)",
              deleted.Count, candidates.Count);
      }
  }
  ```

  **Why only remove from DB after confirmed R2 deletion:** If R2 deletion throws, we skip adding that clip to `deleted`. Only clips whose R2 object was successfully removed are then purged from the database. This prevents a state where the DB record is gone but the R2 file lingers (wasted storage) or the file is gone but the DB still references it (broken URLs for users).

- [ ] **Step 2: Run tests — expect all 6 to pass**

  ```
  dotnet test BranavaFCTests/BranavaFC.Tests.csproj --filter "FullyQualifiedName~ClipCleanupJobTests"
  ```

  Expected:
  ```
  Passed! - Failed: 0, Passed: 6, Skipped: 0
  ```

- [ ] **Step 3: Commit**

  ```
  git add src/BratnavaFC.Application/Abstractions/IClipCleanupJob.cs \
          src/BratnavaFC.Application/Services/ClipCleanupJob.cs \
          BranavaFCTests/ClipCleanupJobTests.cs
  git commit -m "feat: add ClipCleanupJob — deletes unloved R2 clips"
  ```

---

## Task 5: Configure Hangfire in Program.cs

**Files:**
- Modify: `src/BratnavaFC.Api/Program.cs`

> Hangfire.PostgreSql automatically creates its own schema and tables on first startup (`PrepareSchemaIfNecessary = true` by default). **No EF Core migration is needed.**

- [ ] **Step 1: Add using directives**

  At the top of `Program.cs`, alongside the existing `using` statements, add:

  ```csharp
  using Hangfire;
  using Hangfire.PostgreSql;
  ```

- [ ] **Step 2: Register Hangfire services (after the DATABASE section, ~line 142)**

  Place this block immediately after `builder.Services.AddDbContext<AppDbContext>(...)`:

  ```csharp
  // =====================
  // HANGFIRE
  // =====================
  builder.Services.AddHangfire(config => config
      .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
      .UseSimpleAssemblyNameTypeSerializer()
      .UseRecommendedSerializerSettings()
      .UsePostgreSqlStorage(options =>
          options.UseNpgsqlConnection(connectionString!)));

  builder.Services.AddHangfireServer(options =>
  {
      options.WorkerCount = 2;
      options.Queues      = ["default"];
  });
  ```

- [ ] **Step 3: Register ClipCleanupJob in DI (inside the DEPENDENCY INJECTION section, after line ~165)**

  Add alongside the other `AddScoped` calls:

  ```csharp
  builder.Services.AddScoped<IClipCleanupJob, ClipCleanupJob>();
  ```

- [ ] **Step 4: Mount Hangfire dashboard and schedule the recurring job (after `app.MapControllers()`, before `app.Run()`)**

  ```csharp
  // =====================
  // HANGFIRE DASHBOARD + JOBS
  // =====================
  if (app.Environment.IsDevelopment())
  {
      app.UseHangfireDashboard("/hangfire");
  }

  // AddOrUpdate is intentionally called on every startup.
  // On first deploy it creates the job; on subsequent deploys it updates the cron
  // or method if they changed and leaves it untouched otherwise.
  // This is the standard Hangfire pattern — it is safe and idempotent.
  RecurringJob.AddOrUpdate<IClipCleanupJob>(
      recurringJobId: "clip-r2-cleanup",
      methodCall: job => job.ExecuteAsync(CancellationToken.None),
      cronExpression: "0 3 1,15 * *",        // 03:00 UTC on 1st and 15th of every month (~biweekly)
      options: new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });
  ```

  > **Cron breakdown:** `0 3 1,15 * *` = minute 0, hour 3, day-of-month 1 or 15, any month, any weekday.  
  > This fires twice per month which approximates "every 2 weeks" without the edge cases of `*/14` (which skips February 29 and misaligns across months).  
  > **Deploy-time idempotency:** `AddOrUpdate` uses `recurringJobId` as the primary key in Hangfire's storage. Every app start (including restarts and re-deploys) calls this line — Hangfire will create the entry if absent, update it if the cron or method changed, and do nothing otherwise. You never need to manually seed or migrate the job.  
  > The dashboard is only exposed in Development to avoid leaking job history without auth. If you want it in production, add a proper `IDashboardAuthorizationFilter`.

- [ ] **Step 5: Build the full solution**

  ```
  dotnet build BratnavaFC.sln
  ```

  Expected: `Build succeeded — 0 Error(s)`.

- [ ] **Step 6: Run all tests**

  ```
  dotnet test BranavaFCTests/BranavaFC.Tests.csproj
  ```

  Expected: all existing tests still pass alongside the 6 new ones.

- [ ] **Step 7: Commit**

  ```
  git add src/BratnavaFC.Api/Program.cs
  git commit -m "feat: configure Hangfire with PostgreSQL storage and biweekly clip-cleanup job"
  ```

---

## Task 6: Smoke-Test in Development

- [ ] **Step 1: Start the API locally**

  ```
  dotnet run --project src/BratnavaFC.Api/BratnavaFC.Api.csproj
  ```

- [ ] **Step 2: Open the Hangfire dashboard**

  Navigate to `http://localhost:<port>/hangfire`.

  Expected:
  - Dashboard loads.
  - Under **Recurring Jobs**, a job named `clip-r2-cleanup` is listed with cron `0 3 1,15 * *`.
  - Hangfire tables (`hangfire.*`) have been created in the Neon PostgreSQL database (check via psql or your DB client).

- [ ] **Step 3: Trigger the job manually to verify end-to-end**

  In the Hangfire dashboard, click **Trigger now** on `clip-r2-cleanup`.

  Expected: the job runs, application logs show `ClipCleanupJob: found X clip(s) eligible for deletion` and `ClipCleanupJob: completed`.

---

## Notes & Edge Cases

| Concern | Decision |
|---------|----------|
| **Soft-deleted clips** | The query respects EF Core global query filters (status-based). Soft-deleted clips are therefore excluded from cleanup — if your `DeleteReplayAsync` already removes the R2 object on soft-delete, this is correct. If not, add `.IgnoreQueryFilters()` to the `_db.ReplayClips` query in `ClipCleanupJob.ExecuteAsync`. |
| **Hangfire tables in DB** | Hangfire creates them automatically in the `public` schema. To isolate, add `new PostgreSqlStorageOptions { SchemaName = "hangfire" }` as second argument to `UsePostgreSqlStorage`. |
| **Production dashboard auth** | The current plan only exposes `/hangfire` in Development. For production access, implement `IDashboardAuthorizationFilter` that checks for a `GodMode` JWT claim. |
| **Partial R2 failure** | Only clips whose R2 deletion succeeded are removed from the DB. A failed clip will be retried at the next scheduled run. |
| **Hangfire retry policy** | By default Hangfire retries a failed job 10 times with exponential backoff. Add `[AutomaticRetry(Attempts = 0)]` on `ClipCleanupJob` if you want a single-attempt run (no retry). |
