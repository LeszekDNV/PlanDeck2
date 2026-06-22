# Assign Session Members (S-05) Implementation Plan

## Overview

Add the ability to assign team members to a planning session (FR-007). A session keeps a persistent, email-keyed list of assigned members, managed from the session configuration UI. Members are entered ad-hoc by email (not bound to a team roster), assignment works in any session status, and the whole feature follows the existing Team/Session vertical-slice pattern end to end (domain → persistence → gRPC contract → application service → client wrapper → Blazor UI → localization → tests).

## Current State Analysis

- **No persistent session-membership concept exists.** `SessionMember` / `SessionParticipant` are absent from the domain and DB. The only "participant" notion is `PlanningParticipantState` in `Core.Shared/Realtime/PlanningRoomState.cs` — ephemeral SignalR state, unrelated to S-05.
- **`PlanningSession`** (`Core/PlanDeck.Application/Domain/PlanningSession.cs`) already has an optional `TeamId` (`Guid?`), `Status` (`Draft`/`Active`), and a cascade-deleted `Tasks` collection. It derives from `TenantEntity`.
- **Multi-tenant isolation is automatic** for any `TenantEntity`: `PlanDeckDbContext` (`Core/PlanDeck.Infrastructure/Persistence/PlanDeckDbContext.cs`) stamps `TenantId` + audit timestamps on save (fail-closed when no tenant) and applies a global query filter `e.TenantId == CurrentTenantId` to every `ITenantScoped` entity.
- **The Team-member slice is the exact template to mirror:**
  - Entity `TeamMember` (`Email` 320, `DisplayName` 256, `InvitedByUserId`) — `Core/PlanDeck.Application/Domain/TeamMember.cs`.
  - Config `TeamMemberConfiguration` with unique index `(TenantId, TeamId, Email)` — `.../Persistence/Configurations/TeamMemberConfiguration.cs`.
  - Repo `TeamRepository.AddMemberAsync` validates parent exists (`TeamNotFoundException`) and maps SQL unique-violation `2601/2627` → `DuplicateTeamMemberException` — `.../Persistence/TeamRepository.cs:41-69`.
  - Contract `ITeamService` + DTOs — `Core/PlanDeck.Core.Shared/Contracts/ITeamService.cs`.
  - Service `TeamGrpcService` validates email (`Contains('@')`) and maps exceptions to `RpcException` (`NotFound`, `AlreadyExists`) — `Core/PlanDeck.Application/Services/TeamGrpcService.cs`.
  - Client wrapper `TeamClientService : ITeamClientService` over `GrpcChannel.CreateGrpcService<ITeamService>()` — `Web/PlanDeck.Client/Services/TeamClientService.cs`.
- **DI + endpoint wiring**: `ServiceCollectionExtensions.AddLocalServices()` registers each repository-by-interface + the gRPC service class; `Program.cs` maps each service via `app.MapGrpcService<T>()`.
- **Session UI**: `Web/PlanDeck.Client/Pages/Sessions.razor` renders a config card for the selected session with a "Tasks" section (list + remove + add); `_isLocked` gates editing when `Status == Active`. It already injects `ITeamClientService` and loads `_teams`.
- **Validation messages** live in `Core/PlanDeck.Core.Shared/Validation/SessionValidationMessages.cs`; UI strings live in `Web/PlanDeck.Client/Resources/SharedResource.resx` (+ `.pl.resx`).
- **Test patterns**: integration `Tests/PlanDeck.Integration.Tests/Persistence/TeamPersistenceTests.cs` (tenant isolation both directions, duplicate throws, remove, fail-closed) with `FakeCurrentUserContext`; unit `Tests/PlanDeck.Unit.Tests/Sessions/SessionGrpcServiceTests.cs` with a `FakeSessionRepository`; E2E `Tests/PlanDeck.E2e.Tests/SessionsTests.cs` + page objects under `Pages/`.

## Desired End State

A user opening a session in `Sessions.razor` sees a "Members" section in the config card. They type an email (and optional display name) and assign it; the member appears in a list and persists. They can remove a member. Assignment works whether the session is `Draft` or `Active`. Duplicate emails for the same session are rejected with a clear message; invalid emails are rejected. All data is tenant-scoped. Verified by: integration tests (persistence/tenant/duplicate/cascade), unit tests (service validation + exception mapping), and an E2E test driving the UI assign/remove flow.

### Key Discoveries:

- Reuse `SessionNotFoundException` already defined in `Core/PlanDeck.Application/Abstractions/ISessionRepository.cs:18`.
- The duplicate-detection idiom is SQL error number `2601 or 2627` caught from `DbUpdateException` — `TeamRepository.cs:63`.
- Cascade-on-session-delete can be configured FK-only (no navigation collection on `PlanningSession`) to keep the aggregate clean, mirroring how `Tasks` cascade but without polluting the entity: `HasOne<PlanningSession>().WithMany().HasForeignKey(m => m.SessionId).OnDelete(DeleteBehavior.Cascade)`.
- Migrations are generated against the `PlanDeck.Infrastructure` project with `PlanDeck.Server` as startup project (that is where the `DbContext` and connection string are wired).

## What We're NOT Doing

- **No invitation/acceptance workflow** (no `Invited`/`Accepted`/`Declined` status). Assignment is a flat membership list.
- **No binding to the team roster** (`TeamMember`). Members are entered ad-hoc by email; we do not look up, validate against, or foreign-key to `TeamMember`. A session does not need a `TeamId` to have members.
- **No notifications** (email/MS Teams) — that is FR-011, out of scope.
- **No per-session roles** — the role model is flat per the PRD.
- **No SignalR/planning-room pre-population** from assigned members — that belongs to S-06.
- **No change to `SessionDto`** — members are fetched via a dedicated `ListSessionMembers` call.
- **No guest-link path** — that is S-07.

## Implementation Approach

Build a new vertical slice that mirrors the Team-member slice exactly, but anchored on `SessionId` + `Email` and exposed through its own contract `ISessionMemberService` (separate from `ISessionService`). Three phases, each independently testable: (1) domain + persistence + migration, (2) gRPC contract + application service + server wiring, (3) client + UI + localization + E2E.

## Phase 1: Domain & Persistence

### Overview

Introduce the `SessionMember` entity, its EF configuration, a migration, and a dedicated repository with parent-existence and duplicate handling. Prove tenant isolation and constraints with integration tests.

### Changes Required:

#### 1. Domain entity

**File**: `Core/PlanDeck.Application/Domain/SessionMember.cs` (new)

**Intent**: Persistent, tenant-scoped assignment of a person (by email) to a session.

**Contract**: `public sealed class SessionMember : TenantEntity` with `Guid SessionId`, `required string Email`, `string? DisplayName`, `Guid AssignedByUserId`. Mirrors `TeamMember` (swap `TeamId`→`SessionId`, `InvitedByUserId`→`AssignedByUserId`).

#### 2. EF configuration

**File**: `Core/PlanDeck.Infrastructure/Persistence/Configurations/SessionMemberConfiguration.cs` (new)

**Intent**: Map the table, lengths, indexes, uniqueness, and cascade.

**Contract**: `IEntityTypeConfiguration<SessionMember>` → `ToTable("SessionMembers")`; key `Id` `ValueGeneratedNever()`; `Email` required `HasMaxLength(320)`; `DisplayName` `HasMaxLength(256)`; `HasIndex(TenantId)`; unique `HasIndex(m => new { m.TenantId, m.SessionId, m.Email }).IsUnique()`; FK-only relationship to `PlanningSession` with `OnDelete(DeleteBehavior.Cascade)`.

#### 3. DbContext DbSet

**File**: `Core/PlanDeck.Infrastructure/Persistence/PlanDeckDbContext.cs`

**Intent**: Expose the new set so the global tenant filter and audit stamping apply.

**Contract**: add `public DbSet<SessionMember> SessionMembers => Set<SessionMember>();`. No `OnModelCreating` change needed (configurations auto-applied from assembly; tenant filter auto-applied to all `ITenantScoped`).

#### 4. Repository abstraction + exception

**File**: `Core/PlanDeck.Application/Abstractions/ISessionMemberRepository.cs` (new)

**Intent**: Define the persistence operations and the duplicate exception.

**Contract**: interface `ISessionMemberRepository` with `Task<SessionMember> AssignMemberAsync(Guid sessionId, string email, string? displayName, CancellationToken)`, `Task<bool> RemoveMemberAsync(Guid sessionId, Guid memberId, CancellationToken)`, `Task<IReadOnlyList<SessionMember>> GetMembersAsync(Guid sessionId, CancellationToken)`. Plus `public sealed class DuplicateSessionMemberException(Guid sessionId, string email) : Exception(...)` exposing `SessionId` and `Email`. Reuse the existing `SessionNotFoundException`.

#### 5. Repository implementation

**File**: `Core/PlanDeck.Infrastructure/Persistence/SessionMemberRepository.cs` (new)

**Intent**: Implement the operations following `TeamRepository`.

**Contract**: `SessionMemberRepository(PlanDeckDbContext db, ICurrentUserContext currentUser) : ISessionMemberRepository`. `AssignMemberAsync` checks `db.Sessions.AnyAsync(s => s.Id == sessionId)` → throw `SessionNotFoundException` if absent; create `SessionMember { SessionId, Email, DisplayName, AssignedByUserId = currentUser.UserId }`; `SaveChangesAsync` wrapped in the `catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })` → throw `DuplicateSessionMemberException`. `GetMembersAsync` returns `AsNoTracking().Where(m => m.SessionId == sessionId).OrderBy(m => m.Email)`. `RemoveMemberAsync` finds by `SessionId` + `Id`, removes, returns bool.

#### 6. Migration

**File**: `Core/PlanDeck.Infrastructure/Migrations/<timestamp>_AddSessionMembers.cs` (generated)

**Intent**: Create the `SessionMembers` table + indexes.

**Contract**: generated via `dotnet ef migrations add AddSessionMembers` (project `PlanDeck.Infrastructure`, startup `PlanDeck.Server`). Inspect output to confirm the unique index and cascade FK are present; do not hand-edit beyond verification.

#### 7. Integration tests

**File**: `Tests/PlanDeck.Integration.Tests/Persistence/SessionMemberPersistenceTests.cs` (new)

**Intent**: Prove scoping, stamping, uniqueness, removal, cascade, fail-closed.

**Contract**: mirror `TeamPersistenceTests` using `FakeCurrentUserContext`: assign stamps `TenantId`/audit/`AssignedByUserId`; members scoped per tenant (both directions); duplicate `(SessionId, Email)` throws `DuplicateSessionMemberException`; remove works; deleting the parent session cascades members; saving with no tenant context throws `InvalidOperationException`.

### Success Criteria:

#### Automated Verification:

- Solution builds: `dotnet build PlanDeck.slnx`
- Migration created and applies cleanly (verify via build + integration-test DB setup)
- Integration tests pass: `dotnet test Tests/PlanDeck.Integration.Tests/PlanDeck.Integration.Tests.csproj`

#### Manual Verification:

- The generated migration's up/down look correct (unique index `(TenantId, SessionId, Email)`, cascade FK to `Sessions`).

**Implementation Note**: After completing this phase and all automated verification passes, pause for manual confirmation before proceeding.

---

## Phase 2: gRPC Contract & Application Service

### Overview

Expose the feature over code-first gRPC via a new `ISessionMemberService`, implement it in the Application layer with validation and exception mapping, and wire DI + the endpoint. Cover with unit tests.

### Changes Required:

#### 1. Service contract + DTOs

**File**: `Core/PlanDeck.Core.Shared/Contracts/ISessionMemberService.cs` (new)

**Intent**: Define the wire contract for assign/remove/list.

**Contract**: `[Service] interface ISessionMemberService` with three `[Operation]`s: `AssignMemberAsync(AssignSessionMemberRequest)`, `RemoveMemberAsync(RemoveSessionMemberRequest)`, `ListMembersAsync(ListSessionMembersRequest)`. `[DataContract]` types (sequential `[DataMember(Order = n)]`): `SessionMemberDto { Id, SessionId, Email, DisplayName }`; `AssignSessionMemberRequest { SessionId, Email, DisplayName? }` + `AssignSessionMemberReply { SessionMemberDto Member }`; `RemoveSessionMemberRequest { SessionId, MemberId }` + `RemoveSessionMemberReply { bool Removed }`; `ListSessionMembersRequest { SessionId }` + `ListSessionMembersReply { List<SessionMemberDto> Members }`. Mirror `ITeamService.cs` shapes.

#### 2. Validation messages

**File**: `Core/PlanDeck.Core.Shared/Validation/SessionMemberValidationMessages.cs` (new)

**Intent**: Centralize service-side validation strings.

**Contract**: static class with `SessionIdRequired`, `EmailRequired` (valid email with `@`). Mirror `SessionValidationMessages`.

#### 3. Application service

**File**: `Core/PlanDeck.Application/Services/SessionMemberGrpcService.cs` (new)

**Intent**: Validate input, delegate to the repository, map domain exceptions to `RpcException`.

**Contract**: `SessionMemberGrpcService(ISessionMemberRepository repository) : ISessionMemberService`. `AssignMemberAsync`: reject empty `SessionId` (`InvalidArgument`), reject email missing/without `@` (`InvalidArgument`), trim `DisplayName`→null-if-blank; catch `SessionNotFoundException`→`NotFound`, `DuplicateSessionMemberException`→`AlreadyExists`. `RemoveMemberAsync`: reject empty ids; return `Removed`. `ListMembersAsync`: reject empty `SessionId`; map to DTOs. Mirror `TeamGrpcService`.

#### 4. Server DI registration

**File**: `Web/PlanDeck.Server/Extensions/ServiceCollectionExtensions.cs`

**Intent**: Register the new repository (by interface) and the gRPC service.

**Contract**: in `AddLocalServices()` add `services.AddScoped<ISessionMemberRepository, SessionMemberRepository>();` and `services.AddScoped<SessionMemberGrpcService>();`.

#### 5. Endpoint mapping

**File**: `Web/PlanDeck.Server/Program.cs`

**Intent**: Serve the contract over gRPC-Web.

**Contract**: add `app.MapGrpcService<SessionMemberGrpcService>();` alongside the existing `MapGrpcService` calls.

#### 6. Unit tests

**File**: `Tests/PlanDeck.Unit.Tests/Sessions/SessionMemberGrpcServiceTests.cs` (new)

**Intent**: Verify validation and exception-to-status mapping with a fake repository.

**Contract**: fake `ISessionMemberRepository`; assert empty `SessionId`→`InvalidArgument`, missing/`@`-less email→`InvalidArgument`, repository `SessionNotFoundException`→`NotFound`, `DuplicateSessionMemberException`→`AlreadyExists`, happy-path returns mapped DTO, list maps all members.

### Success Criteria:

#### Automated Verification:

- Solution builds: `dotnet build PlanDeck.slnx`
- Unit tests pass: `dotnet test Tests/PlanDeck.Unit.Tests/PlanDeck.Unit.Tests.csproj`

#### Manual Verification:

- (none beyond automated for this phase)

**Implementation Note**: After completing this phase and all automated verification passes, pause for manual confirmation before proceeding.

---

## Phase 3: Client, UI, Localization & E2E

### Overview

Add the client service wrapper, a "Members" section in the session config card, localized strings, and an E2E test exercising the assign/remove flow.

### Changes Required:

#### 1. Client service wrapper + interface

**Files**: `Web/PlanDeck.Client/Services/ISessionMemberClientService.cs` (new), `Web/PlanDeck.Client/Services/SessionMemberClientService.cs` (new)

**Intent**: Provide an injectable, interface-based wrapper over the gRPC contract.

**Contract**: `ISessionMemberClientService` with `Task<IReadOnlyList<SessionMemberDto>> GetMembersAsync(Guid sessionId)`, `Task<SessionMemberDto> AssignMemberAsync(Guid sessionId, string email, string? displayName)`, `Task<bool> RemoveMemberAsync(Guid sessionId, Guid memberId)`. Implementation calls `channel.CreateGrpcService<ISessionMemberService>()`. Mirror `SessionClientService`/`TeamClientService`.

#### 2. Client DI registration

**File**: `Web/PlanDeck.Client/Program.cs`

**Intent**: Register the wrapper by interface.

**Contract**: add `builder.Services.AddScoped<ISessionMemberClientService, SessionMemberClientService>();` next to the existing client-service registrations.

#### 3. Session config "Members" UI

**File**: `Web/PlanDeck.Client/Pages/Sessions.razor`

**Intent**: Add a Members section to the selected-session config card: list assigned members (with remove button), plus email + optional display-name inputs and an Assign button. Available regardless of `_isLocked` (assignment allowed in any status). Load members on session select.

**Contract**: inject `ISessionMemberClientService`; add `_members` list + input fields + `AssignMemberAsync`/`RemoveMemberAsync` handlers + a `LoadMembersAsync` called from `SelectAsync`. Reuse MudBlazor components from the Tasks section (`MudList`, `MudListItem`, `MudTextField`, `MudButton`, `MudIconButton`). Surface duplicate/invalid errors via the page's existing error/snackbar mechanism. All visible strings via `@L[...]`. Stable selectors/`data-testid`-style attributes or `aria-label`s for E2E.

#### 4. Localization

**Files**: `Web/PlanDeck.Client/Resources/SharedResource.resx`, `Web/PlanDeck.Client/Resources/SharedResource.pl.resx`

**Intent**: Add member-section strings in `en` and `pl`.

**Contract**: keys e.g. `Sessions_Members`, `Sessions_NoMembers`, `Sessions_MemberEmail`, `Sessions_MemberDisplayName`, `Sessions_AssignMember`, `Sessions_RemoveMember`, and any user-facing error string (duplicate/invalid). Add matching entries to both files.

#### 5. E2E test + page object

**Files**: `Tests/PlanDeck.E2e.Tests/SessionMembersTests.cs` (new) and the relevant page object under `Tests/PlanDeck.E2e.Tests/Pages/` (new or extended)

**Intent**: Drive the UI: create/select a session, assign a member by email, see it listed, remove it.

**Contract**: derive from `PageTest`, override `ContextOptions()` for `IgnoreHTTPSErrors = true`, use the Page Object Pattern, wait for WASM-rendered elements before asserting. Relies on `AspireAppFixture` (local Aspire boot; Podman running).

### Success Criteria:

#### Automated Verification:

- Solution builds: `dotnet build PlanDeck.slnx`
- E2E test passes locally: `dotnet test Tests/PlanDeck.E2e.Tests/PlanDeck.E2e.Tests.csproj` (Podman running; Playwright chromium installed)

#### Manual Verification:

- Assigning a valid email adds the member to the list and persists across reload.
- Assigning a duplicate email shows a clear error; invalid email is rejected.
- Removing a member works.
- Assignment works while the session is both Draft and Active.
- UI strings render correctly in `en` and `pl`.

**Implementation Note**: After completing this phase and all automated verification passes, pause for manual confirmation.

---

## Testing Strategy

### Unit Tests:

- `SessionMemberGrpcService` validation (empty SessionId, missing/`@`-less email) → `InvalidArgument`.
- Exception mapping: `SessionNotFoundException`→`NotFound`, `DuplicateSessionMemberException`→`AlreadyExists`.
- Happy-path assign returns mapped DTO; list maps all members.

### Integration Tests:

- Tenant isolation (both directions) for `SessionMembers`.
- `TenantId`/audit/`AssignedByUserId` stamping on assign.
- Duplicate `(SessionId, Email)` throws.
- Remove persists; cascade delete with parent session.
- Fail-closed when no tenant context.

### Manual Testing Steps:

1. Create a session, select it, assign a member by email — confirm it appears and persists after reload.
2. Try to assign the same email again — confirm rejection message.
3. Try an email without `@` — confirm rejection.
4. Remove a member — confirm it disappears.
5. Activate the session and confirm assignment/removal still works.
6. Switch culture to `pl` and confirm strings.

## Performance Considerations

Negligible: small per-session member lists, indexed queries by `SessionId`/`TenantId`. No new hot paths.

## Migration Notes

Single additive migration `AddSessionMembers` (new table + indexes, no data backfill). Applied automatically on startup in Development via `ApplyMigrationsAsync`.

## References

- Change identity: `context/changes/assign-session-members/change.md`
- Roadmap slice: `context/foundation/roadmap.md` (S-05), PRD FR-007
- Mirror slice (team members): `Core/PlanDeck.Application/Domain/TeamMember.cs`, `.../Persistence/Configurations/TeamMemberConfiguration.cs`, `.../Persistence/TeamRepository.cs:41-84`, `Core/PlanDeck.Core.Shared/Contracts/ITeamService.cs`, `Core/PlanDeck.Application/Services/TeamGrpcService.cs`, `Web/PlanDeck.Client/Services/TeamClientService.cs`
- Tenant infrastructure: `Core/PlanDeck.Infrastructure/Persistence/PlanDeckDbContext.cs:27-145`
- Reused exception: `Core/PlanDeck.Application/Abstractions/ISessionRepository.cs:18`
- Test patterns: `Tests/PlanDeck.Integration.Tests/Persistence/TeamPersistenceTests.cs`, `Tests/PlanDeck.Unit.Tests/Sessions/SessionGrpcServiceTests.cs`, `Tests/PlanDeck.E2e.Tests/SessionsTests.cs`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Domain & Persistence

#### Automated

- [x] 1.1 Solution builds: `dotnet build PlanDeck.slnx` — c2ab2aa
- [x] 1.2 Migration created and applies cleanly — c2ab2aa
- [x] 1.3 Integration tests pass: `dotnet test Tests/PlanDeck.Integration.Tests/PlanDeck.Integration.Tests.csproj` — c2ab2aa

#### Manual

- [x] 1.4 Generated migration up/down look correct (unique index, cascade FK) — c2ab2aa

### Phase 2: gRPC Contract & Application Service

#### Automated

- [x] 2.1 Solution builds: `dotnet build PlanDeck.slnx`
- [x] 2.2 Unit tests pass: `dotnet test Tests/PlanDeck.Unit.Tests/PlanDeck.Unit.Tests.csproj`

### Phase 3: Client, UI, Localization & E2E

#### Automated

- [ ] 3.1 Solution builds: `dotnet build PlanDeck.slnx`
- [ ] 3.2 E2E test passes locally: `dotnet test Tests/PlanDeck.E2e.Tests/PlanDeck.E2e.Tests.csproj`

#### Manual

- [ ] 3.3 Assigning a valid email adds the member and persists across reload
- [ ] 3.4 Duplicate email shows a clear error; invalid email rejected
- [ ] 3.5 Removing a member works
- [ ] 3.6 Assignment works in both Draft and Active status
- [ ] 3.7 UI strings render correctly in en and pl
