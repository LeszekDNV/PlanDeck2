# Multi-tenant Persistence Baseline (F-01) Implementation Plan

## Overview

Establish the EF Core domain-persistence pattern and the central, reusable **tenant-scoping convention** for PlanDeck, plus **one verified migration** applied against the configured SQL Server. This is the foundation every data-backed slice (S-01…S-06, S-08) inherits: tenant isolation is fixed once, centrally, so no later slice re-derives it and risks a cross-tenant data leak. F-01 deliberately ships the *convention* + *one seed entity* + *one verified migration* — not the full domain model.

## Current State Analysis

- `PlanDeck.Infrastructure/Persistence/PlanDeckDbContext.cs` is an empty `DbContext` — only `modelBuilder.ApplyConfigurationsFromAssembly(...)`. No `DbSet`s, no entities, no `Migrations/` folder.
- DI and lifecycle are already wired in `PlanDeck.Server/Extensions/ServiceCollectionExtensions.cs`:
  - `AddSqlDatabase(IConfiguration)` registers `PlanDeckDbContext` against `DefaultConnection` with `EnableRetryOnFailure()`, managed-identity connection rewrite, and a `AddDbContextCheck<PlanDeckDbContext>("sql")` health check.
  - `ApplyMigrationsAsync(this WebApplication)` calls `dbContext.Database.MigrateAsync()`; invoked from `Program.cs` **only** in `IsDevelopment()`.
- Auth is Entra ID (`AddExternalServices`): OpenIdConnect + cookie, `MapInboundClaims = false` (so raw `oid` / `tid` claims are preserved), `GetClaimsFromUserInfoEndpoint = true`. `IHttpContextAccessor` is registered (`AddLocalServices`). Flat role model; multi-tenant.
- Aspire (`AppHost.cs`) provisions a real SQL Server container locally (`mssql/server:2025-latest`, db `PlanDeckDb`, host port 2140, persistent data volume) and `WithReference(sqlDatabase, "DefaultConnection")`; Azure SQL in publish mode. Container runtime is Podman (already required to run the app).
- Package baseline: `PlanDeck.Infrastructure` → `Microsoft.EntityFrameworkCore.SqlServer` (10.\*); `PlanDeck.Server` → `Microsoft.EntityFrameworkCore.Design` (10.\*, `PrivateAssets=all`) as the migration startup project. Project dependency direction: **Server → Application → Infrastructure**, and `PlanDeck.Infrastructure` already has a `ProjectReference` to `PlanDeck.Application` (Application is the inner layer).
- `PlanDeck.Integration.Tests` exists but is bare (NUnit only, no project references).

### Key Discoveries:

- `MapInboundClaims = false` means tenant/user identity is read from raw `tid` / `oid` claims — no JWT→.NET claim remapping. (`ServiceCollectionExtensions.cs:82`)
- `ApplyMigrationsAsync` already runs `MigrateAsync()` on startup in Development (`Program.cs:31`) — the migration produced here will be exercised every dev run with zero extra wiring.
- `PlanDeck.Infrastructure` → `PlanDeck.Application` reference already exists, so domain entities + the `ICurrentUserContext` abstraction can live in `PlanDeck.Application` and be consumed by the `DbContext` in Infrastructure without changing dependency direction (`PlanDeck.Infrastructure.csproj:14`).
- The roadmap scopes F-01 minimal and says "each consuming slice still adds and exercises its own entities through real behavior" (`roadmap.md:82`) — so exactly one seed entity belongs here.

## Desired End State

After this plan:

1. A **`ITenantScoped`** marker (a `Guid TenantId`) and a **`TenantEntity`** base exist in the domain layer; any entity implementing the marker is automatically (a) filtered to the current caller's tenant on every read and (b) stamped with the current tenant + audit timestamps on insert — **fail-closed**: a write with no resolvable tenant throws, and a read with no tenant context returns zero rows.
2. An **`ICurrentUserContext`** abstraction resolves `TenantId` (Entra `tid`) and `UserId` (Entra `oid`) from the authenticated principal; a host implementation reads them from `IHttpContextAccessor` and is registered in DI.
3. One seed entity — **`AppUser`** (the Entra identity record) — is mapped and demonstrates the convention.
4. A single EF Core migration **`InitialCreate`** lives in `PlanDeck.Infrastructure/Migrations/`, applies cleanly to SQL Server on startup, and creates the `AppUsers` table.
5. An **integration test** (real SQL Server via Testcontainers) proves: the migration applies; data written under tenant A is invisible to tenant B; an un-tenanted write is rejected.

Verify: `dotnet build PlanDeck.slnx` succeeds; `dotnet test` (unit + integration) is green; running the app via Aspire applies the migration and creates `AppUsers`.

## What We're NOT Doing

- **Not** building the full domain model — no `Team`, `TeamMember`, `Session`, `Task`, `Vote`, `Result`, or guest-voting entities. Those land in S-01…S-08, each adding its own entities through real behavior.
- **Not** building user-provisioning / login-sync (upserting `AppUser` on sign-in). F-01 maps the entity and proves the convention; the provisioning flow is a later slice's behavior. (`AppUser` rows in the verification test are inserted directly through the `DbContext`.)
- **Not** implementing membership / per-team sharing filters ("data shared with me"). The baseline boundary is the **tenant**; finer-grained membership filtering composes on top in S-01/S-04.
- **Not** using SQL Row-Level Security, soft-delete, or temporal tables.
- **Not** changing the auth pipeline, the localization setup, or `AddSqlDatabase`'s connection handling.

## Implementation Approach

Tenant isolation is enforced **centrally in `PlanDeckDbContext`**, not per-query, so it cannot be forgotten by a later slice:

- **Reads**: a global query filter (`HasQueryFilter`) is applied generically in `OnModelCreating` to every entity type implementing `ITenantScoped`, comparing `TenantId` against a context-resolved current tenant. When there is no authenticated tenant the resolved value is `Guid.Empty`, which matches no real row → **fail-closed reads**.
- **Writes**: `SaveChanges`/`SaveChangesAsync` is overridden to, for each added `ITenantScoped` entity, stamp `TenantId` from the current context (or validate an explicitly-set one matches), set audit timestamps, and **throw** if no tenant can be resolved → **fail-closed writes**. This prevents persisting un-tenanted or cross-tenant rows.
- The current tenant/user is supplied through an injected **`ICurrentUserContext`** so the `DbContext` stays free of ASP.NET types (layering); the host provides the `HttpContext`-backed implementation.

## Approach to the verified migration

The deliverable is a *verified* migration against real SqlServer DDL — quality-first, matching the project's container-based stack. Verification reuses the repo's existing Aspire testing approach (boot `Projects.PlanDeck_AppHost`, read the `PlanDeckDb` connection string) rather than introducing a parallel Testcontainers path, keeping the test infrastructure consistent and on the already-required Podman runtime.

> **Assumptions made autonomously** (user was unavailable during planning; revisit if any is wrong):
> 1. Baseline scope dimension is **tenant (`tid`)**, not owner/membership — membership filtering is layered later.
> 2. Seed entity is **`AppUser`** (foundational identity record), not a throwaway sample and not `Team` (which belongs to S-01).
> 3. Enforcement is **fail-closed** (throw on un-tenanted write; empty result on no-context read).
> 4. Verification uses the existing Aspire **`DistributedApplicationTestingBuilder`** pattern (as in `PlanDeck.E2e.Tests/AspireAppFixture.cs`) to provision real SQL Server through the same Podman runtime — no new Testcontainers dependency.
> 5. Domain entities + abstractions live in **`PlanDeck.Application`**; the `HttpContext` implementation lives in **`PlanDeck.Server`**.
> 6. Keys are **`Guid`**; `AppUser.Id` is the Entra `oid`, `AppUser.TenantId` is the Entra `tid`.

## Critical Implementation Details

- **Global query filter must re-evaluate per request.** The filter has to read the *current* tenant at query time, not a value captured when the model was built (the `DbContext`/model is effectively cached). Apply the filter generically over a context member that delegates to the injected `ICurrentUserContext` (e.g. a private `Guid CurrentTenantId => _currentUser.TenantId;`) so EF parameterizes it per query. A filter that closes over a one-time-captured `Guid` will silently scope every request to the first request's tenant — a cross-tenant leak.
- **`MapInboundClaims = false`** — read identity from the raw `"tid"` and `"oid"` claim types, not `ClaimTypes.*`. `oid`/`tid` are GUIDs.
- **Generic filter application** (the one non-obvious bit) — in `OnModelCreating`, iterate `modelBuilder.Model.GetEntityTypes()`, and for each whose CLR type implements `ITenantScoped`, invoke a generic helper via reflection that calls `entity.HasQueryFilter((T e) => e.TenantId == CurrentTenantId)`.

## Phase 1: Tenant-scoping convention & seed entity (code, no migration)

### Overview

Introduce the scoping abstractions, the `AppUser` seed entity, its EF configuration, the `DbContext` read-filter + write-stamping behavior, the current-user context implementation, and DI wiring. Everything compiles and the convention's logic is unit-tested with the relational SQLite provider (no SQL Server yet).

### Changes Required:

#### 1. Domain abstractions & seed entity

**File**: `src/PlanDeck/Core/PlanDeck.Application/Domain/ITenantScoped.cs`, `.../Domain/TenantEntity.cs`, `.../Domain/AppUser.cs`

**Intent**: Define the marker that drives scoping and the seed entity that anchors the migration. Remove the `Class1.cs` placeholder in `PlanDeck.Application`.

**Contract**:
- `ITenantScoped` — exposes `Guid TenantId { get; set; }`.
- `abstract TenantEntity : ITenantScoped` — `Guid Id`, `Guid TenantId`, `DateTimeOffset CreatedAtUtc`, `DateTimeOffset UpdatedAtUtc`.
- `AppUser : TenantEntity` — `string DisplayName`, `string Email`. `Id` carries the Entra `oid`; `TenantId` carries the Entra `tid`.

#### 2. Current-user context abstraction

**File**: `src/PlanDeck/Core/PlanDeck.Application/Abstractions/ICurrentUserContext.cs`

**Intent**: Give the persistence layer a way to learn the current tenant/user without referencing ASP.NET. Lives beside the existing `IAzureDevOpsWorkItemClient` abstraction.

**Contract**: `ICurrentUserContext` exposes `Guid TenantId { get; }` (returns `Guid.Empty` when unauthenticated), `Guid UserId { get; }`, `bool IsAuthenticated { get; }`.

#### 3. EF entity configuration

**File**: `src/PlanDeck/Core/PlanDeck.Infrastructure/Persistence/Configurations/AppUserConfiguration.cs`

**Intent**: Map `AppUser` via `IEntityTypeConfiguration<AppUser>` (auto-discovered by the existing `ApplyConfigurationsFromAssembly`).

**Contract**: PK `Id` (not store-generated — value comes from Entra `oid`); `DisplayName` required, bounded length; `Email` required, bounded length; index on `(TenantId)`. Table `AppUsers`.

#### 4. DbContext: query filter + write stamping

**File**: `src/PlanDeck/Core/PlanDeck.Infrastructure/Persistence/PlanDeckDbContext.cs`

**Intent**: Inject `ICurrentUserContext`; expose `DbSet<AppUser>`; apply the generic tenant query filter to all `ITenantScoped` types in `OnModelCreating`; override `SaveChanges`/`SaveChangesAsync` to stamp tenant + audit and reject un-tenanted writes (fail-closed).

**Contract**: Constructor `(DbContextOptions<PlanDeckDbContext>, ICurrentUserContext)`. `DbSet<AppUser> AppUsers`. Private `Guid CurrentTenantId => _currentUser.TenantId;` used by the filter. On insert of an `ITenantScoped`: if `TenantId == Guid.Empty` set it from `CurrentTenantId`; if that is also `Guid.Empty` throw `InvalidOperationException`; if a non-empty `TenantId` was set explicitly and differs from `CurrentTenantId` (when authenticated) throw. Stamp `CreatedAtUtc`/`UpdatedAtUtc` on add, `UpdatedAtUtc` on modify.

```csharp
// Generic filter application in OnModelCreating — the non-obvious part.
foreach (var entityType in modelBuilder.Model.GetEntityTypes())
{
    if (typeof(ITenantScoped).IsAssignableFrom(entityType.ClrType))
    {
        var method = typeof(PlanDeckDbContext)
            .GetMethod(nameof(ApplyTenantFilter), BindingFlags.NonPublic | BindingFlags.Instance)!
            .MakeGenericMethod(entityType.ClrType);
        method.Invoke(this, [modelBuilder]);
    }
}
// ApplyTenantFilter<T>(ModelBuilder b) => b.Entity<T>().HasQueryFilter(e => e.TenantId == CurrentTenantId);
```

#### 5. Host current-user implementation + DI

**File**: `src/PlanDeck/Web/PlanDeck.Server/Identity/HttpContextCurrentUserContext.cs`, and registration in `PlanDeck.Server/Extensions/ServiceCollectionExtensions.cs`

**Intent**: Read `tid`/`oid` from the authenticated principal via the already-registered `IHttpContextAccessor`; register `ICurrentUserContext` (scoped). When no authenticated user (startup migration, unauthenticated request) return `Guid.Empty`/`false`.

**Contract**: `HttpContextCurrentUserContext : ICurrentUserContext` reading claim types `"tid"` / `"oid"`. Registered in `AddLocalServices` (or `AddExternalServices`) as `services.AddScoped<ICurrentUserContext, HttpContextCurrentUserContext>()`. `AppUser`/`ITenantScoped` namespaces imported where needed.

#### 6. Unit tests for the convention

**File**: `src/PlanDeck/Tests/PlanDeck.Unit.Tests/Persistence/TenantScopingTests.cs` (+ project references to `PlanDeck.Infrastructure`/`PlanDeck.Application`, `Microsoft.EntityFrameworkCore.Sqlite`)

**Intent**: Prove the filter + stamping logic without SQL Server, using the relational **SQLite in-memory** provider and a fake `ICurrentUserContext`.

**Contract**: Tests — (a) read under tenant A excludes tenant-B rows; (b) insert auto-stamps `TenantId` + `CreatedAtUtc`; (c) insert with no tenant context throws `InvalidOperationException`; (d) `IgnoreQueryFilters()` returns cross-tenant rows (escape hatch intact).

#### 7. Design-time DbContext factory (enables migration generation)

**File**: `src/PlanDeck/Core/PlanDeck.Infrastructure/Persistence/PlanDeckDbContextFactory.cs`

**Intent**: Let `dotnet ef` instantiate `PlanDeckDbContext` at design time **without** booting the Server's DI. Booting Server DI fails because `AddSqlDatabase` throws when `DefaultConnection` is absent (it is Aspire-injected only at runtime, `ServiceCollectionExtensions.cs:22-25`), and the new `ICurrentUserContext` constructor arg would also need resolving. EF tools prefer an `IDesignTimeDbContextFactory<T>` over the startup project's service provider, so this class is used for `migrations add`/`script` and bypasses `Program.cs` entirely.

**Contract**: `PlanDeckDbContextFactory : IDesignTimeDbContextFactory<PlanDeckDbContext>` building `DbContextOptions<PlanDeckDbContext>` via `UseSqlServer(<placeholder connection string>)` (migrations scaffold the model and never open the connection) and passing a no-op `ICurrentUserContext` (returns `Guid.Empty` / `false`) into the constructor. Lives in Infrastructure; no DI registration (tooling-only). The same no-op context may be a small private nested type.

### Success Criteria:

#### Automated Verification:

- Solution builds: `dotnet build PlanDeck.slnx`
- Unit tests pass: `dotnet test Tests/PlanDeck.Unit.Tests/PlanDeck.Unit.Tests.csproj`
- Design-time factory resolves (smoke): `dotnet ef dbcontext info --project Core/PlanDeck.Infrastructure --startup-project Web/PlanDeck.Server` succeeds without a live database
- No new analyzer/nullable warnings introduced in changed files

#### Manual Verification:

- Code review confirms tenant isolation is enforced only in `PlanDeckDbContext` (no per-query `Where(TenantId==...)` scattered), and the `DbContext` references no ASP.NET types.

**Implementation Note**: After completing this phase and all automated verification passes, pause for manual confirmation before proceeding to Phase 2.

---

## Phase 2: Initial migration & verified persistence

### Overview

Generate the first EF Core migration, confirm it applies to real SQL Server on startup, and add an integration test (Testcontainers MS SQL) that proves migration-applies + tenant isolation + fail-closed writes end-to-end against a real database.

### Changes Required:

#### 1. Initial migration

**File**: `src/PlanDeck/Core/PlanDeck.Infrastructure/Migrations/*_InitialCreate.cs` (+ model snapshot)

**Intent**: Produce the first migration creating the `AppUsers` table. Generated, not hand-written.

**Contract**: Run from `src/PlanDeck`:
`dotnet ef migrations add InitialCreate --project Core/PlanDeck.Infrastructure --startup-project Web/PlanDeck.Server --output-dir Migrations`
(Requires the `dotnet-ef` tool; `Microsoft.EntityFrameworkCore.Design` is already referenced by the startup project. The `PlanDeckDbContextFactory` from Phase 1 #7 is what lets this command run with no live `DefaultConnection`.) Migration creates `AppUsers` with the columns/PK/index from `AppUserConfiguration` and no `TenantId` query-filter artifacts in DDL.

#### 2. Integration test: verified migration + isolation against real SQL Server

**File**: `src/PlanDeck/Tests/PlanDeck.Integration.Tests/Persistence/TenantPersistenceTests.cs` (+ a `[SetUpFixture]` mirroring `PlanDeck.E2e.Tests/AspireAppFixture.cs`)

**Intent**: Provision the real SQL Server the same way the rest of the repo does — via the Aspire AppHost — apply migrations via `Database.MigrateAsync()`, and exercise the convention against real DDL. Reuses the proven `DistributedApplicationTestingBuilder<Projects.PlanDeck_AppHost>` pattern (same Podman runtime) rather than introducing Testcontainers.

**Contract**: An NUnit `[SetUpFixture]` boots the AppHost (`DistributedApplicationTestingBuilder.CreateAsync<Projects.PlanDeck_AppHost>()` → `BuildAsync` → `StartAsync`), waits for the SQL resource to reach `KnownResourceStates.Running`, and exposes the `PlanDeckDb` connection string via `app.GetConnectionStringAsync("PlanDeckDb")`; `[OneTimeTearDown]` disposes the app. Each test builds a `PlanDeckDbContext` from that connection string plus a fake `ICurrentUserContext` for the tenant under test. Tests — (a) `MigrateAsync()` succeeds and `AppUsers` exists; (b) a row written under tenant A is not returned to a context bound to tenant B; (c) inserting with an empty tenant context throws.

#### 3. Register Integration test project references

**File**: `src/PlanDeck/Tests/PlanDeck.Integration.Tests/PlanDeck.Integration.Tests.csproj`

**Intent**: Add the references the test needs (the project is currently bare). Mirror the E2e project's Aspire wiring.

**Contract**: Add `PackageReference` `Aspire.Hosting.Testing` (match E2e's pinned version) and `Microsoft.EntityFrameworkCore.SqlServer`; add `ProjectReference` to `Aspire/PlanDeck.AppHost` (provides the `Projects.PlanDeck_AppHost` metadata type) and to `Core/PlanDeck.Infrastructure` (transitively brings `PlanDeck.Application`). No Testcontainers dependency.

### Success Criteria:

#### Automated Verification:

- Solution builds with the migration present: `dotnet build PlanDeck.slnx`
- Migration is up to date (no pending model changes): `dotnet ef migrations has-pending-model-changes --project Core/PlanDeck.Infrastructure --startup-project Web/PlanDeck.Server` reports none
- Integration tests pass: `dotnet test Tests/PlanDeck.Integration.Tests/PlanDeck.Integration.Tests.csproj` (container runtime running)
- Full suite passes: `dotnet test PlanDeck.slnx`

#### Manual Verification:

- Running the app via Aspire (`dotnet run --project Aspire/PlanDeck.AppHost`, Podman started) applies the migration on startup and the `AppUsers` table exists in `PlanDeckDb` (inspect via the Aspire dashboard / SQL).
- The `sql` health check reports healthy after startup.

**Implementation Note**: After completing this phase and all automated verification passes, pause for manual confirmation that the app boots, migrates, and the table exists.

---

## Testing Strategy

### Unit Tests (Phase 1, SQLite relational provider):

- Query filter excludes other tenants' rows.
- Insert auto-stamps `TenantId` + audit timestamps from the current context.
- Insert with no resolvable tenant throws (fail-closed write).
- `IgnoreQueryFilters()` escape hatch returns cross-tenant rows.

### Integration Tests (Phase 2, real SQL Server via the Aspire AppHost):

- `MigrateAsync()` applies the `InitialCreate` migration cleanly; `AppUsers` table created.
- Cross-tenant read isolation holds against real SQL Server.
- Fail-closed write rejected against real SQL Server.

### Manual Testing Steps:

1. Start Podman, run `dotnet run --project Aspire/PlanDeck.AppHost`.
2. Confirm startup logs show the migration applied and no exceptions.
3. Inspect `PlanDeckDb` and confirm the `AppUsers` table and its index exist.
4. Confirm the `sql` health check is healthy.

## Performance Considerations

Negligible at MVP scale. The global query filter adds a `TenantId` predicate (covered by the `(TenantId)` index). Reflection in `OnModelCreating` runs once at model build. No N+1 or hot-path concerns introduced.

## Migration Notes

- First migration for the database — no existing data to migrate.
- Migration applied automatically on startup in Development via the existing `ApplyMigrationsAsync` (`Program.cs`). Production/publish applies via the same path or deploy-time migration (unchanged by this plan).
- Rollback: `dotnet ef database update 0 ... ` drops the table, or delete the migration before it ships since nothing depends on it yet.

## References

- Roadmap slice F-01: `context/foundation/roadmap.md:72`
- Change identity: `context/changes/multitenant-persistence-baseline/change.md`
- PRD access control / tenancy: `context/foundation/prd.md:131`, guardrail `prd.md:53`
- Existing persistence wiring: `src/PlanDeck/Web/PlanDeck.Server/Extensions/ServiceCollectionExtensions.cs:17`, `src/PlanDeck/Web/PlanDeck.Server/Program.cs:31`
- Empty DbContext to fill: `src/PlanDeck/Core/PlanDeck.Infrastructure/Persistence/PlanDeckDbContext.cs`
- Entra claims config: `ServiceCollectionExtensions.cs:82` (`MapInboundClaims = false`)

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Tenant-scoping convention & seed entity

#### Automated

- [x] 1.1 Solution builds: `dotnet build PlanDeck.slnx`
- [x] 1.2 Unit tests pass: `dotnet test Tests/PlanDeck.Unit.Tests/PlanDeck.Unit.Tests.csproj`
- [x] 1.3 Design-time factory resolves: `dotnet ef dbcontext info` succeeds without a live database
- [x] 1.5 No new analyzer/nullable warnings introduced in changed files

#### Manual

- [x] 1.4 Code review confirms isolation lives only in `PlanDeckDbContext` and the DbContext references no ASP.NET types

### Phase 2: Initial migration & verified persistence

#### Automated

- [x] 2.1 Solution builds with the migration present: `dotnet build PlanDeck.slnx`
- [x] 2.2 No pending model changes: `dotnet ef migrations has-pending-model-changes ...`
- [x] 2.3 Integration tests pass: `dotnet test Tests/PlanDeck.Integration.Tests/PlanDeck.Integration.Tests.csproj`
- [x] 2.4 Full suite passes: `dotnet test PlanDeck.slnx`

#### Manual

- [x] 2.5 App boots via Aspire, applies the migration on startup, and `AppUsers` exists in `PlanDeckDb`
- [x] 2.6 The `sql` health check reports healthy after startup
