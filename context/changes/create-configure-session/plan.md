# Create & Configure Planning Session (S-04) Implementation Plan

## Overview

Deliver FR-005 + FR-006: a signed-in user can create a planning session from a set of selected tasks and configure it (task selection + voting scale only, deliberately minimal). This is the hinge of the product loop — voting (S-06) and write-back (S-08) attach to the session this slice creates. The work is a vertical slice following the established Team-slice pattern; auth, localization, and the multi-tenant persistence convention already exist and are reused unchanged.

## Current State Analysis

What exists today (verified in the codebase as of 2026-06-18):

- **Persistence convention (F-01, done):** `TenantEntity : ITenantScoped` gives every entity `Id`, `TenantId`, `CreatedAtUtc`, `UpdatedAtUtc`. `PlanDeckDbContext` auto-applies a per-request tenant query filter to every `ITenantScoped` type and fail-closed stamps tenant + audit on `SaveChanges`. Adding an entity = inherit `TenantEntity`, add an `IEntityTypeConfiguration`, expose a `DbSet`, generate a migration. (`Core/PlanDeck.Application/Domain/TenantEntity.cs`, `Core/PlanDeck.Infrastructure/Persistence/PlanDeckDbContext.cs`.)
- **Team slice (S-01, done):** a near-exact template for this slice — `Team`/`TeamMember` entities, `TeamConfiguration` (parent owns children via `HasMany().WithOne().HasForeignKey(...).OnDelete(Cascade)`), `ITeamRepository` (Application) → `TeamRepository` (Infrastructure), `ITeamService` code-first contract (`Core.Shared/Contracts`), `TeamGrpcService` (Application) mapping domain exceptions to `RpcException`, `ITeamClientService` + `TeamClientService` (Client), `Teams.razor` page (MudBlazor, `AuthorizeView`, `IStringLocalizer<SharedResource>`), and integration + E2E tests.
- **Task sources (S-02, S-03): NOT done.** There is **no persisted task entity**. The Azure DevOps integration (`AzureDevOpsWorkItemGrpcService` → `IAzureDevOpsWorkItemClient`) runs a **live WIQL query** and returns **transient** `AzureDevOpsWorkItemDto`s (`Id`, `Title`, `State`, `WorkItemType`, `Revision`, `Estimate`) — nothing is stored. `IAzureDevOpsClientService` already exposes this to the client.
- **No `Session`/`SessionTask` entity, service, or page exists.** `PlanningRoomService`/`PlanningRoomHub` are an unrelated in-memory realtime spike (S-06 territory) and are **not** touched here.
- **Wiring points:** server registers services in `Web/PlanDeck.Server/Extensions/ServiceCollectionExtensions.cs` (`AddLocalServices`) and maps gRPC endpoints in `Web/PlanDeck.Server/Program.cs` (`app.MapGrpcService<T>()`). Client registers services in `Web/PlanDeck.Client/Program.cs`. Nav lives in `Web/PlanDeck.Client/Layout/MainLayout.razor`. Localized strings are `IStringLocalizer<SharedResource>` keys (resx under `Web/PlanDeck.Client/Resources`).

### Key Discoveries:

- The session must **own its tasks as snapshot child entities** because no persisted task source exists (confirmed decision). Tasks are captured at creation/edit time from two sources: ad-hoc (typed title) and a live ADO import (existing WIQL gRPC call). `Core/PlanDeck.Application/Services/AzureDevOpsWorkItemGrpcService.cs:9`.
- Parent/child cascade pattern to copy: `Core/PlanDeck.Infrastructure/Persistence/Configurations/TeamConfiguration.cs:26` (`HasMany<TeamMember>().WithOne().HasForeignKey(m => m.TeamId).OnDelete(DeleteBehavior.Cascade)`).
- Tenant isolation is automatic via `TenantEntity` — every new entity inherits the filter + stamping; integration tests must still prove it (`Tests/PlanDeck.Integration.Tests/Persistence/TenantPersistenceTests.cs`, `TeamPersistenceTests.cs`).
- gRPC services are registered concretely (`services.AddScoped<TeamGrpcService>()`) and mapped by concrete type (`app.MapGrpcService<TeamGrpcService>()`); the repository is registered by interface. `ServiceCollectionExtensions.cs:117`, `Program.cs:93`.
- EF Core 10 maps a `List<string>` **primitive collection** to JSON automatically — the clean way to persist resolved voting-scale card values without a child table.

## Desired End State

A signed-in user opens `/sessions`, creates a planning session by naming it, optionally linking a team, adding tasks (typed ad-hoc and/or imported live from Azure DevOps), and choosing a voting scale (Fibonacci, T-shirt, or a custom value list). The session and its task snapshot are persisted, tenant-isolated. While the session is in **Draft**, the user can rename it, change the scale, and add/remove tasks; activating the session locks configuration. Every string is available in English and Polish. Proven by an integration test (real MSSQL: persistence, parent/child, tenant isolation) and a Playwright E2E (create a session with ad-hoc tasks + a chosen scale, see it listed).

Verification: `dotnet build PlanDeck.slnx` and `dotnet test PlanDeck.slnx` green; running the app applies the new migration on startup; manual UI walk-through of create → configure (Draft) → activate.

## What We're NOT Doing

- **Not** building a standalone persisted task source (S-02 / S-03). Sessions own snapshot tasks; a future S-02/S-03 can layer a reusable task store later without changing this contract.
- **Not** implementing voting, hidden-vote/reveal, who-voted, or estimate selection (S-06) — no vote storage, no per-task agreed estimate UI here.
- **Not** assigning/inviting members to a session (S-05) or guest-link voting (S-07). `TeamId` is stored but no membership/assignment logic is added.
- **Not** writing estimates back to Azure DevOps (S-08). `SessionTask` carries `AdoWorkItemId` + `AdoRevision` for that future slice, but nothing writes back now.
- **Not** adding computed/algorithmic estimate suggestions, notifications, or session history.
- **Not** changing auth, the localization infrastructure, the realtime spike, or any existing slice.

## Implementation Approach

Replicate the Team vertical slice for a parent-with-children aggregate. `PlanningSession : TenantEntity` owns a collection of `SessionTask : TenantEntity` via cascade delete. The voting scale is modeled as a `VotingScaleType` enum (`Fibonacci`, `TShirt`, `Custom`) plus a persisted ordered `List<string> ScaleValues` (the resolved card faces — presets resolved server-side, custom supplied by the user) so downstream voting reads one uniform field regardless of type. A `SessionStatus` enum (`Draft`, `Active`) gates editability. Business logic (validation, preset resolution, Draft-only edit enforcement, exception mapping) lives in the Application layer; data access lives in Infrastructure behind `ISessionRepository`. The client gets a typed `ISessionClientService` and a single `/sessions` MudBlazor page (list + create dialog + Draft config panel) mirroring `Teams.razor`. Tests reuse the existing Aspire-backed integration fixture and the Playwright E2E harness.

## Critical Implementation Details

- **ADO tasks are a snapshot, not a live link.** When a user imports an ADO work item into a session, copy `Id → AdoWorkItemId`, `Revision → AdoRevision`, plus `Title`/`WorkItemType`/`State` into a `SessionTask` row at that moment. The session does not re-query ADO afterward; this snapshot is what S-08 will later write back against (using the stored revision for optimistic concurrency).
- **Draft-only editing must be enforced server-side**, not just hidden in the UI. Reconfigure/add/remove operations on a non-Draft session must fail with `RpcException(FailedPrecondition)` — the UI gating is convenience, the server is the guard.
- **Preset scale values are resolved at the boundary**, not stored as a magic type alone: on create/update, if the type is `Fibonacci` or `TShirt`, the service fills `ScaleValues` from a canonical preset; if `Custom`, it validates and trims the user-supplied list (non-empty, deduped). This keeps the voting round (S-06) reading a single resolved list.

## Phase 1: Domain & Persistence

### Overview

Introduce the `PlanningSession` aggregate (session + task children + scale + status enums), its EF configuration and `DbSet`s, and a migration. Prove persistence, parent/child cascade, and tenant isolation with an integration test against real MSSQL.

### Changes Required:

#### 1. Domain entities & enums

**File**: `Core/PlanDeck.Application/Domain/PlanningSession.cs`

**Intent**: The session aggregate root the rest of the loop attaches to. Holds configuration (name, scale) and lifecycle state; owns its task snapshot.

**Contract**: `public sealed class PlanningSession : TenantEntity` with `Name` (required), `Guid? TeamId`, `Guid CreatedByUserId`, `SessionStatus Status`, `VotingScaleType ScaleType`, `List<string> ScaleValues` (ordered card faces), and a `List<SessionTask> Tasks` navigation.

**File**: `Core/PlanDeck.Application/Domain/SessionTask.cs`

**Intent**: A task snapshot owned by a session — captured from ad-hoc input or a live ADO import at the moment it is added.

**Contract**: `public sealed class SessionTask : TenantEntity` with `Guid SessionId`, `Title` (required), `TaskSource Source` (`AdHoc` / `AzureDevOps`), `int SortOrder`, and nullable ADO snapshot fields `int? AdoWorkItemId`, `int? AdoRevision`, `string? WorkItemType`, `string? State`. (`AdoWorkItemId`/`AdoRevision` are carried for S-08 write-back; unused this slice.)

**File**: `Core/PlanDeck.Application/Domain/SessionStatus.cs`, `VotingScaleType.cs`, `TaskSource.cs`

**Intent**: Enumerations backing lifecycle, scale kind, and task provenance.

**Contract**: `enum SessionStatus { Draft, Active }`, `enum VotingScaleType { Fibonacci, TShirt, Custom }`, `enum TaskSource { AdHoc, AzureDevOps }`.

#### 2. EF configurations

**File**: `Core/PlanDeck.Infrastructure/Persistence/Configurations/PlanningSessionConfiguration.cs`

**Intent**: Map the session table, constrain string lengths, store enums, persist the scale-values primitive collection, and own task children via cascade delete.

**Contract**: `IEntityTypeConfiguration<PlanningSession>` — table `Sessions`; `Id` `ValueGeneratedNever`; `Name` required, max 200; `Status`/`ScaleType` stored as int (or string) via `HasConversion<...>()`; `ScaleValues` as a primitive collection (EF Core JSON); `HasIndex(TenantId)`; `HasMany(s => s.Tasks).WithOne().HasForeignKey(t => t.SessionId).OnDelete(DeleteBehavior.Cascade)` mirroring `TeamConfiguration`.

**File**: `Core/PlanDeck.Infrastructure/Persistence/Configurations/SessionTaskConfiguration.cs`

**Intent**: Map the task table and its columns.

**Contract**: `IEntityTypeConfiguration<SessionTask>` — table `SessionTasks`; `Id` `ValueGeneratedNever`; `Title` required, max 500; `WorkItemType`/`State` max-length nullable; `Source` stored as int/string; `HasIndex(TenantId)` and an index on `SessionId`.

#### 3. DbContext DbSets

**File**: `Core/PlanDeck.Infrastructure/Persistence/PlanDeckDbContext.cs`

**Intent**: Expose the new aggregates so the global tenant filter + stamping apply automatically.

**Contract**: add `public DbSet<PlanningSession> Sessions => Set<PlanningSession>();` and `public DbSet<SessionTask> SessionTasks => Set<SessionTask>();`. No filter/stamping changes — inheriting `TenantEntity` is sufficient.

#### 4. Migration

**File**: `Core/PlanDeck.Infrastructure/Migrations/<timestamp>_AddPlanningSessions.cs` (generated)

**Intent**: Create `Sessions` and `SessionTasks` tables with the cascade FK and indexes; applied on startup by the existing `ApplyMigrationsAsync` hook.

**Contract**: generated via `dotnet ef migrations add AddPlanningSessions` against `PlanDeck.Infrastructure` using the existing design-time `PlanDeckDbContextFactory`. Review that the `ScaleValues` primitive collection maps to a single (JSON) column and the FK is `OnDelete: Cascade`.

#### 5. Integration test

**File**: `Tests/PlanDeck.Integration.Tests/Persistence/SessionPersistenceTests.cs`

**Intent**: Prove a session with tasks round-trips on real MSSQL, that deleting a session cascades its tasks, and that sessions are invisible across tenants.

**Contract**: NUnit fixture mirroring `TeamPersistenceTests` using the Aspire-backed `AspireAppFixture`; insert a session + tasks under tenant A, assert retrievable under A, invisible under B, and cascade on delete.

### Success Criteria:

#### Automated Verification:

- Solution builds: `dotnet build PlanDeck.slnx`
- Migration generates and the snapshot compiles (no pending-model-changes warning)
- Integration test passes: `dotnet test Tests/PlanDeck.Integration.Tests/PlanDeck.Integration.Tests.csproj --filter "FullyQualifiedName~SessionPersistenceTests"`

#### Manual Verification:

- Running the app (Aspire, Podman up) applies the migration and creates `Sessions` + `SessionTasks` tables
- `sql` health check is healthy after startup

**Implementation Note**: After completing this phase and all automated verification passes, pause for manual confirmation before proceeding.

---

## Phase 2: Backend — Contract, Service & Repository

### Overview

Expose session create/configure/list over code-first gRPC: the `ISessionService` contract + DTOs in `Core.Shared`, the `SessionGrpcService` implementation in Application (validation, preset resolution, Draft-only enforcement, exception mapping), `ISessionRepository` + `SessionRepository` for data access, and server DI + endpoint wiring.

### Changes Required:

#### 1. gRPC contract + DTOs

**File**: `Core/PlanDeck.Core.Shared/Contracts/ISessionService.cs`

**Intent**: The shared wire contract for the session lifecycle this slice supports. Client and server both reference it.

**Contract**: `[Service] interface ISessionService` with `[Operation]`s: `CreateSessionAsync` (name, optional `TeamId`, `VotingScaleType`, custom values, initial task list → `SessionDto`), `ListSessionsAsync` (→ list of `SessionDto`), `GetSessionAsync` (id → `SessionDto` with tasks), `UpdateSessionConfigAsync` (id, name, scale type, custom values — Draft only), `AddTaskAsync` (id, task payload — Draft only), `RemoveTaskAsync` (id, taskId — Draft only), `ActivateSessionAsync` (id → flips Draft→Active), `DeleteSessionAsync` (id). All request/reply/DTO types `[DataContract]` with `[DataMember(Order = n)]`. `SessionDto` carries `Id`, `Name`, `TeamId`, `Status`, `ScaleType`, `ScaleValues`, `CreatedAtUtc`, `Tasks`. `SessionTaskDto` carries `Id`, `Title`, `Source`, `SortOrder`, `AdoWorkItemId`, `AdoRevision`, `WorkItemType`, `State`. A `NewSessionTaskDto` (input) carries `Title`, `Source`, and optional ADO snapshot fields. Mirror the enum-as-int approach used by existing DTOs (define matching `[DataContract]` enums or `int`/string fields in `Core.Shared`).

#### 2. Repository abstraction

**File**: `Core/PlanDeck.Application/Abstractions/ISessionRepository.cs`

**Intent**: The Application-owned data-access seam, plus domain exceptions for not-found and illegal-state edits.

**Contract**: `interface ISessionRepository` with `CreateSessionAsync(PlanningSession session)`, `GetSessionsAsync()`, `GetSessionAsync(Guid id)` (includes tasks), `UpdateSessionAsync(PlanningSession session)`, `DeleteSessionAsync(Guid id)`. Add `SessionNotFoundException(Guid)`. The Application service performs Draft-state checks; a `SessionNotDraftException(Guid)` lives here for the service to throw.

#### 3. Repository implementation

**File**: `Core/PlanDeck.Infrastructure/Persistence/SessionRepository.cs`

**Intent**: EF Core access over `PlanDeckDbContext` + `ICurrentUserContext`, mirroring `TeamRepository`.

**Contract**: `sealed class SessionRepository(PlanDeckDbContext db, ICurrentUserContext currentUser) : ISessionRepository`. Create stamps `CreatedByUserId` from `currentUser.UserId` and adds the session with its tasks. Get-list is `AsNoTracking().OrderByDescending(CreatedAtUtc)`. Get-one `Include(s => s.Tasks)`. Update persists scalar + task-collection changes. Tenant scoping is automatic via the global filter — no explicit `TenantId` predicates.

#### 4. gRPC service implementation

**File**: `Core/PlanDeck.Application/Services/SessionGrpcService.cs`

**Intent**: Implement `ISessionService`: validate input, resolve preset scale values, enforce Draft-only edits, map domain exceptions to `RpcException`, and translate domain ↔ DTO.

**Contract**: `sealed class SessionGrpcService(ISessionRepository repository) : ISessionService`. Validation throws `RpcException(InvalidArgument)` for blank name / empty task title / empty custom-scale list. A private scale resolver returns canonical card faces for `Fibonacci`/`TShirt` and the trimmed/deduped user list for `Custom`. Edit operations load the session, throw `RpcException(FailedPrecondition)` if `Status != Draft` (via `SessionNotDraftException`), `RpcException(NotFound)` if missing. `Activate` flips status to `Active`. DTO mapping helpers mirror `TeamGrpcService.ToDto`.

#### 5. Server DI + endpoint mapping

**File**: `Web/PlanDeck.Server/Extensions/ServiceCollectionExtensions.cs`

**Intent**: Register the repository (by interface) and the gRPC service (concrete), following the Team registrations.

**Contract**: in `AddLocalServices`, add `services.AddScoped<ISessionRepository, SessionRepository>();` and `services.AddScoped<SessionGrpcService>();`.

**File**: `Web/PlanDeck.Server/Program.cs`

**Intent**: Expose the service over gRPC-Web.

**Contract**: add `app.MapGrpcService<SessionGrpcService>();` alongside the existing `MapGrpcService` calls.

#### 6. Service unit/behavior coverage

**File**: `Tests/PlanDeck.Unit.Tests/Sessions/SessionGrpcServiceTests.cs`

**Intent**: Cover the non-CRUD logic that isn't exercised by persistence tests: preset resolution, custom-scale validation, and Draft-only enforcement.

**Contract**: NUnit fixture with a fake/in-memory `ISessionRepository`; assert Fibonacci/T-shirt resolve to canonical values, blank/empty inputs throw `InvalidArgument`, and editing a non-Draft session throws `FailedPrecondition`.

### Success Criteria:

#### Automated Verification:

- Solution builds: `dotnet build PlanDeck.slnx`
- Unit tests pass: `dotnet test Tests/PlanDeck.Unit.Tests/PlanDeck.Unit.Tests.csproj --filter "FullyQualifiedName~SessionGrpcServiceTests"`
- Full test suite still green: `dotnet test PlanDeck.slnx`

#### Manual Verification:

- gRPC endpoint is reachable from the running app (create/list a session via the UI in Phase 3, or a temporary probe)
- Editing an activated session is rejected (FailedPrecondition surfaced as an error, not a silent no-op)

**Implementation Note**: After completing this phase and all automated verification passes, pause for manual confirmation before proceeding.

---

## Phase 3: Client — UI & Localization

### Overview

Add the typed client service and a single `/sessions` MudBlazor page (list + create dialog with ad-hoc + live-ADO task selection + scale picker + Draft config panel), a nav entry, client DI, and en/pl localized strings — mirroring the Team slice.

### Changes Required:

#### 1. Client service

**File**: `Web/PlanDeck.Client/Services/ISessionClientService.cs` + `SessionClientService.cs`

**Intent**: Wrap the gRPC `ISessionService` behind an interface injected into the page, calling it via the configured `GrpcChannel`.

**Contract**: interface with methods mirroring the contract operations (create, list, get, update config, add/remove task, activate, delete) returning the `Core.Shared` DTOs; implementation uses `channel.CreateGrpcService<ISessionService>()` exactly like `TeamClientService`.

#### 2. Client DI

**File**: `Web/PlanDeck.Client/Program.cs`

**Intent**: Register the client service by interface.

**Contract**: add `builder.Services.AddScoped<ISessionClientService, SessionClientService>();` next to the existing `ITeamClientService` registration.

#### 3. Sessions page

**File**: `Web/PlanDeck.Client/Pages/Sessions.razor`

**Intent**: The create+configure experience. List sessions; create via dialog (name, optional team, task list, scale); for a selected Draft session, a config panel allows rename, scale change, and add/remove tasks; an Activate action locks it.

**Contract**: `@page "/sessions"`, `AuthorizeView`-gated, `IStringLocalizer<SharedResource>`, MudBlazor components. The create dialog supports adding ad-hoc tasks (typed title) and importing ADO work items via the existing `IAzureDevOpsClientService` (live WIQL) with a multi-select to snapshot chosen items. Scale picker is a `MudSelect` over `VotingScaleType` with a values editor shown when `Custom`. Errors mapped from `RpcException` to localized snackbar messages, mirroring `Teams.razor`. No voting UI.

#### 4. Navigation entry

**File**: `Web/PlanDeck.Client/Layout/MainLayout.razor`

**Intent**: Add an authenticated nav link to `/sessions` next to Teams.

**Contract**: a `MudButton Href="/sessions"` inside the `Authorized` block using a localized `Nav_Sessions` key.

#### 5. Localized strings

**File**: `Web/PlanDeck.Client/Resources/SharedResource.*.resx` (en + pl)

**Intent**: Add every user-facing string the page uses; no hard-coded display text.

**Contract**: keys for `Nav_Sessions`, `Sessions_Title`, `Sessions_Create`, `Sessions_Name`, `Sessions_Team`, `Sessions_Empty`, `Sessions_Scale`, scale-type labels, `Sessions_AddAdHocTask`, `Sessions_ImportAdo`, `Sessions_Activate`, `Sessions_Draft`/`Sessions_Active`, `Sessions_CustomValues`, validation/error messages — added to both `en` and `pl` resx files.

### Success Criteria:

#### Automated Verification:

- Solution builds (Razor + client): `dotnet build PlanDeck.slnx`
- Full test suite still green: `dotnet test PlanDeck.slnx`

#### Manual Verification:

- A signed-in user creates a session at `/sessions` with ad-hoc tasks and a chosen scale; it appears in the list
- Importing ADO work items into a session snapshots the selected items (when ADO is configured)
- A Draft session can be reconfigured (rename, change scale, add/remove tasks); after Activate, config controls are disabled
- The page renders correctly in both English and Polish via the header language toggle

**Implementation Note**: After completing this phase and all automated verification passes, pause for manual confirmation before proceeding.

---

## Phase 4: End-to-End Test

### Overview

A Playwright E2E proving the core user loop of this slice via the Page Object Pattern, reusing the env-aware `AspireAppFixture` and the test-auth scheme.

### Changes Required:

#### 1. Page object

**File**: `Tests/PlanDeck.E2e.Tests/Pages/SessionsPage.cs`

**Intent**: Encapsulate locators + actions for the `/sessions` page, accounting for WASM boot by waiting for a known element before asserting (mirrors `TeamsPage`).

**Contract**: methods to navigate to `/sessions`, open the create dialog, fill name, add an ad-hoc task, pick a scale, submit, and read the session list.

#### 2. E2E test

**File**: `Tests/PlanDeck.E2e.Tests/SessionsTests.cs`

**Intent**: Drive create-session-with-ad-hoc-tasks + scale selection end-to-end and assert the session is listed.

**Contract**: `PageTest`-derived fixture overriding `ContextOptions()` for `IgnoreHTTPSErrors`, authenticating via the test-auth scheme, mirroring `TeamsTests`. ADO import is excluded (needs external config); ad-hoc path is asserted.

### Success Criteria:

#### Automated Verification:

- Build + Playwright browsers installed: `dotnet build PlanDeck.slnx` then `pwsh Tests/PlanDeck.E2e.Tests/bin/Debug/net10.0/playwright.ps1 install chromium`
- E2E test passes locally (Aspire boots via Podman): `dotnet test Tests/PlanDeck.E2e.Tests/PlanDeck.E2e.Tests.csproj --filter "FullyQualifiedName~SessionsTests"`
- Full suite green: `dotnet test PlanDeck.slnx`

#### Manual Verification:

- The E2E run boots Aspire, waits for the WASM app, creates a session, and the assertion is stable across reruns (no flaky WASM-timing failures)

**Implementation Note**: After completing this phase and all automated verification passes, pause for manual confirmation.

---

## Testing Strategy

### Unit Tests:

- Voting-scale preset resolution (Fibonacci, T-shirt) and custom-scale validation (empty/duplicate handling)
- Draft-only enforcement: editing a non-Draft session throws `FailedPrecondition`
- Input validation: blank name, empty task title, empty custom-scale list throw `InvalidArgument`

### Integration Tests:

- Session + tasks persist and round-trip on real MSSQL
- Deleting a session cascades its tasks
- Tenant isolation: a session created under tenant A is invisible under tenant B

### Manual Testing Steps:

1. Sign in, open `/sessions`, create a session with two ad-hoc tasks and the Fibonacci scale → it lists
2. Open the Draft session, rename it, switch to a custom scale with values, add and remove a task → changes persist
3. (ADO configured) Import work items into a session → selected items appear as snapshot tasks
4. Activate the session → config controls disable; attempting an edit is rejected
5. Toggle language to Polish → all session strings localize

## Performance Considerations

Volumes are tiny (a handful of sessions/tasks per tenant); no special indexing beyond the `TenantId` and `SessionId` indexes. `GetSessionAsync` uses a single `Include(s => s.Tasks)`; list queries are `AsNoTracking`.

## Migration Notes

A single additive `AddPlanningSessions` migration creates two new tables; no existing data is touched. Applied automatically on startup in Development by `ApplyMigrationsAsync`.

## References

- Roadmap slice: `context/foundation/roadmap.md` (S-04, lines 152–163)
- PRD: FR-005 / FR-006 (`context/foundation/prd.md` lines 88–90); voting-scale open question (line 150)
- Template slice: `context/archive/2026-06-18-team-and-members/plan.md`
- Persistence convention: `context/archive/2026-06-18-multitenant-persistence-baseline/plan-brief.md`
- Parent/child cascade: `Core/PlanDeck.Infrastructure/Persistence/Configurations/TeamConfiguration.cs:26`
- ADO live import: `Core/PlanDeck.Application/Services/AzureDevOpsWorkItemGrpcService.cs`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Domain & Persistence

#### Automated

- [x] 1.1 Solution builds: `dotnet build PlanDeck.slnx` — cd69bae
- [x] 1.2 Migration generates and the snapshot compiles (no pending-model-changes warning) — cd69bae
- [x] 1.3 Integration test passes: SessionPersistenceTests — cd69bae
#### Manual
- [x] 1.4 App startup applies the migration and creates `Sessions` + `SessionTasks` tables — cd69bae
- [x] 1.5 `sql` health check is healthy after startup — cd69bae

### Phase 2: Backend — Contract, Service & Repository

#### Automated

- [x] 2.1 Solution builds: `dotnet build PlanDeck.slnx` — 5f7f3a3
- [x] 2.2 Unit tests pass: SessionGrpcServiceTests — 5f7f3a3
- [x] 2.3 Full test suite still green: `dotnet test PlanDeck.slnx` — 5f7f3a3

#### Manual

- [x] 2.4 gRPC endpoint reachable from the running app
- [x] 2.5 Editing an activated session is rejected (FailedPrecondition surfaced, not silent)

### Phase 3: Client — UI & Localization

#### Automated

- [x] 3.1 Solution builds (Razor + client): `dotnet build PlanDeck.slnx` — 32ed00f
- [x] 3.2 Full test suite still green: `dotnet test PlanDeck.slnx` — 32ed00f

#### Manual

- [x] 3.3 Signed-in user creates a session with ad-hoc tasks + a scale; it appears in the list
- [x] 3.4 Importing ADO work items snapshots selected items (when ADO configured)
- [x] 3.5 Draft session reconfigurable; after Activate config controls disable
- [x] 3.6 Page renders correctly in English and Polish

### Phase 4: End-to-End Test

#### Automated

- [x] 4.1 Build + Playwright browsers installed
- [x] 4.2 E2E test passes locally: SessionsTests
- [x] 4.3 Full suite green: `dotnet test PlanDeck.slnx`

#### Manual

- [x] 4.4 E2E run boots Aspire, creates a session, assertion stable across reruns
