# Real-time Voting Round (S-06) Implementation Plan

## Overview

Build the real-time voting screen — the heart of PlanDeck. Assigned members join an **Active** session, vote on each task in real time (values hidden, who-has-voted shown live), any member reveals the round so all votes appear together, and a member manually picks the agreed estimate, which is persisted on the task. This slice builds **on top of** the F-02 hidden-vote/reveal/reconnection contract (`realtime-vote-integrity`) — vote consistency and hidden-until-reveal are not re-derived here; they are extended with a per-task notion and an authoritative authorization gate.

## Current State Analysis

What exists today (delivered by F-01, F-02, S-04, S-05):

- **F-02 realtime contract** (in-memory, authoritative, tested): `PlanningRoomHub` (`Web/PlanDeck.Server/Hubs/PlanningRoomHub.cs`) with `[Authorize]`, claims-derived identity (`oid`/`tid`/`name`), `OnDisconnectedAsync`; `PlanningRoomService` (`Core/PlanDeck.Application/Planning/PlanningRoomService.cs`) keyed by `RoomKey(TenantId, SessionId)`; `PlanningRoomState` / `PlanningParticipantState` (`Core/PlanDeck.Core.Shared/Realtime/PlanningRoomState.cs`). Votes are **ephemeral** (never persisted), and the contract has **one round per room with no concept of a task**.
- **Session domain** (`Core/PlanDeck.Application/Domain/`): `PlanningSession` (Id, Name, TeamId, `CreatedByUserId`, `Status` Draft/Active, `ScaleType`, `ScaleValues: List<string>`, `Tasks`), `SessionTask` (Id, SessionId, Title, Source, SortOrder, Ado* fields — **no estimate field**), `SessionMember` (Id, SessionId, **Email**, DisplayName, AssignedByUserId). All derive `TenantEntity` with a global tenant query filter in `PlanDeckDbContext`.
- **Repositories** (`Core/PlanDeck.Application/Abstractions/`, impls in `Core/PlanDeck.Infrastructure/Persistence/`): `ISessionRepository` (`GetSessionAsync` eager-loads `Tasks`), `ISessionMemberRepository` (`GetMembersAsync(sessionId)`).
- **gRPC contracts** (`Core/PlanDeck.Core.Shared/Contracts/`): `ISessionService` (with `SessionDto` / `SessionTaskDto` — **no agreed estimate**), `ISessionMemberService`.
- **Client UI** (`Web/PlanDeck.Client/`): `Pages/Sessions.razor` (MudBlazor + gRPC, already injects `IPlanningRoomClientService`), `Services/PlanningRoomClientService.cs` (SignalR wrapper), localization via `IStringLocalizer<SharedResource>` (`Resources/SharedResource.resx` + `.pl.resx`), auth via cookie + `AuthorizeView`. **No voting page exists.**

### Key Discoveries:

- **Gap — no estimate storage**: `SessionTask` (`Domain/SessionTask.cs:1-20`) and `SessionTaskDto` (`Contracts/ISessionService.cs:86-111`) have no field for the agreed estimate. This is the primary persistence gap S-06 fills.
- **Gap — no task notion in the room**: `PlanningRoomState` (`Realtime/PlanningRoomState.cs`) carries only `SessionId`, `IsRevealed`, `Participants[]`. To vote per-task the room must track the task list, the active task, per-task votes, and per-task agreed estimates.
- **Scale lives on the session**: `PlanningSession.ScaleValues` (e.g. `["0","1","2","3","5","8","13","21","?","☕"]`) — the UI renders cards from this; the server validates a cast vote is a member of the scale.
- **Identity vs membership mismatch**: the hub identity is claims (`oid`/`tid`/`email`/`name`); `SessionMember` is keyed by **Email**. Join authorization matches the caller's `email` claim against `SessionMember.Email` (case-insensitive) within the tenant.
- **Server-authoritative pattern (F-02)**: the hub is the trust boundary; `PlanningRoomService` is pure/in-memory and unit-tested without a DB. To preserve that, the **hub** loads session + members from repositories and seeds the room with task list + scale; the service stays DB-free (data is passed in).
- **Repository tenant scoping** is automatic via the global query filter in `PlanDeckDbContext`; no manual `Where(TenantId==…)` needed.

## Desired End State

An assigned member opens an Active session, clicks "Join voting", and lands on `/voting/{sessionId}`. They see the session's task list, the active task, the voting-scale cards, and a live roster showing who has voted (not what). They cast a vote (hidden). Any member advances/selects the active task, reveals the round (all votes appear together), optionally resets to re-vote, and picks the agreed estimate — which persists on `SessionTask.AgreedEstimate` and is broadcast live to everyone, and survives reload. Verified by: unit tests on per-task round logic, an extended hub integration test covering the per-task flow + reveal + persisted pick, a gRPC/persistence test for the estimate write, and a two-browser Playwright E2E (vote → reveal → pick).

## What We're NOT Doing

- **No Azure DevOps write-back** (that is S-08; this slice only persists the agreed estimate locally).
- **No guest-link voting** (that is S-07; join is restricted to authenticated assigned members).
- **No automatic result/consensus computation** — the agreed estimate is picked manually in v1 (per PRD).
- **No persisted per-vote history** — individual votes stay in memory and are discarded at round reset/leave; only the agreed estimate is persisted.
- **No notifications.**
- **No task/scale editing from the voting screen** — task selection and scale configuration remain S-04's responsibility (the voting screen is read-only over the task list and scale).
- **No distinct facilitator concept** — any assigned member may drive the round (advance task, reveal, reset, pick).

## Implementation Approach

Extend, don't re-derive. The F-02 in-memory room gains a per-task dimension: the room is seeded (by the hub, from the DB) with the session's ordered task list and scale; it tracks an **active task**, **per-task votes**, **per-task reveal state**, and **per-task agreed estimate**. `CastVote`/`RevealVotes`/`ResetRound` operate on the active task. Two new hub operations — `SetActiveTask` and `SelectEstimate` — drive task navigation and the manual pick; `SelectEstimate` persists `AgreedEstimate` through an application service and broadcasts the updated room state so the pick is seen live. The hub remains the trust boundary: it authorizes every operation by confirming the caller's email is in the session's assigned-member list within the tenant. The client gets matching wrapper methods and a new `VotingRoom.razor` page; `Sessions.razor` gains a "Join voting" entry on Active sessions. Finally a two-browser Playwright E2E proves the realtime flow end to end.

## Critical Implementation Details

- **Room seeding & lifecycle**: the room must be seeded with the task list + scale **before** any vote is validated. The hub loads the session (eager-loaded `Tasks`) + members on `JoinRoom` and passes them into the service, which seeds the room **once** (idempotent — a late joiner does not reset votes/active task/estimates of an in-progress room). The active task defaults to the first task by `SortOrder` when the room is first seeded.
- **Authorization gate (trust boundary)**: every hub method resolves the caller's email and the `RoomKey`, loads the session's assigned members, and rejects (throws `HubException`) when the email is not a member of that tenant-scoped session. **Resolve the caller's email with the same fallback the app already uses — `email ?? preferred_username` (mirroring `HttpContextCurrentUserContext.Email`, `Infrastructure/.../HttpContextCurrentUserContext.cs:20`)** — because the real OIDC scheme runs with `MapInboundClaims=false` and Entra often emits the address in `preferred_username` rather than `email`; keying off `email` alone would lock out every assigned member on real auth. This is the gate F-02 deliberately deferred. Membership lookup should be efficient — load once per invocation; do not trust any client-supplied identity.
- **Vote validation**: a cast vote must be a member of the session's `ScaleValues`; reject otherwise. Votes remain hidden in every broadcast until the active task's reveal flag is set (reuse the F-02 `ToState` projection, now scoped to the active task).
- **SelectEstimate atomicity**: persist `AgreedEstimate` via the application service first (authoritative DB write through `ISessionRepository`), then update the in-memory room and broadcast. A persistence failure must not broadcast a false "picked" state.
- **Reset semantics**: `ResetRound` clears only the **active task's** votes and reveal flag; it does **not** clear an already-persisted `AgreedEstimate` (the estimate is replaced only when a new pick is made).

## Phase 1: Domain & Persistence — Agreed Estimate

### Overview

Add the persisted agreed-estimate field to the task domain, expose it over gRPC so the UI can render persisted estimates on load, and add the repository capability to set it.

### Changes Required:

#### 1. SessionTask entity

**File**: `src/PlanDeck/Core/PlanDeck.Application/Domain/SessionTask.cs`

**Intent**: Add a nullable agreed-estimate field so a task can carry the manually picked result.

**Contract**: New property `string? AgreedEstimate` on `SessionTask` (nullable; holds a scale value string such as `"5"` or `"XL"`).

#### 2. EF Core configuration + migration

**File**: `src/PlanDeck/Core/PlanDeck.Infrastructure/Persistence/Configurations/SessionTaskConfiguration.cs` and a new migration under `src/PlanDeck/Core/PlanDeck.Infrastructure/Migrations/`

**Intent**: Map `AgreedEstimate` as an optional column and generate the migration so the schema gains the column (applied on startup in Development).

**Contract**: `AgreedEstimate` configured optional with a sensible `MaxLength` (e.g. 32). New migration adds an `nvarchar(32) NULL` column to `SessionTasks`. Generate with `dotnet ef migrations add AddSessionTaskAgreedEstimate` against the existing `PlanDeckDbContext` (follow the convention of the prior migrations in that folder).

#### 3. gRPC DTO

**File**: `src/PlanDeck/Core/PlanDeck.Core.Shared/Contracts/ISessionService.cs`

**Intent**: Surface the agreed estimate on the read DTO so the voting screen shows persisted estimates after reload.

**Contract**: Add `[DataMember(Order = 9)] public string? AgreedEstimate { get; set; }` to `SessionTaskDto` (next free order). Update the DTO→entity mapping in `Core/PlanDeck.Application/Services/SessionGrpcService.cs` so reads include it.

#### 4. Repository — set agreed estimate

**File**: `src/PlanDeck/Core/PlanDeck.Application/Abstractions/ISessionRepository.cs` + impl `src/PlanDeck/Core/PlanDeck.Infrastructure/Persistence/SessionRepository.cs`

**Intent**: Provide an authoritative, tenant-scoped operation to persist the agreed estimate for a single task.

**Contract**: New method `Task<bool> SetAgreedEstimateAsync(Guid sessionId, Guid taskId, string? estimate, CancellationToken ct)` — loads the task within the session (tenant filter applies), sets `AgreedEstimate`, saves; returns false when the task/session is not found in the tenant.

#### 5. Persistence test for the estimate write

**File**: `src/PlanDeck/Tests/PlanDeck.Integration.Tests/Persistence/SessionRepositoryEstimateTests.cs` (or extend an existing repository fixture)

**Intent**: Honor the Desired End State's "gRPC/persistence test for the estimate write" with an explicit automated test rather than relying on the Phase 3 hub test to cover it transitively.

**Contract**: Against the test DB, seed a tenant-scoped session + task, call `SetAgreedEstimateAsync`, assert the column persists and re-reads carry it; assert it returns `false` for a task/session outside the tenant.

### Success Criteria:

#### Automated Verification:

- Solution builds: `dotnet build PlanDeck.slnx`
- Migration is generated and the model snapshot updates (no pending-model-changes warning)
- Unit/existing tests pass: `dotnet test Tests/PlanDeck.Unit.Tests/PlanDeck.Unit.Tests.csproj`
- Persistence test for `SetAgreedEstimateAsync` passes (write + re-read carries the estimate; cross-tenant returns false): `dotnet test Tests/PlanDeck.Integration.Tests/PlanDeck.Integration.Tests.csproj`

#### Manual Verification:

- On app start in Development the new migration applies cleanly against the configured SQL database (no startup error).

**Implementation Note**: After automated verification passes, pause for manual confirmation before Phase 2.

---

## Phase 2: Realtime Contract Extension — Per-Task Round

### Overview

Extend the F-02 in-memory contract and service with a per-task dimension: task list, scale, active task, per-task votes/reveal, and per-task agreed estimate — with the service staying DB-free and fully unit-testable.

### Changes Required:

#### 1. Wire state DTOs

**File**: `src/PlanDeck/Core/PlanDeck.Core.Shared/Realtime/PlanningRoomState.cs`

**Intent**: Carry everything the voting screen needs: the ordered task list with per-task estimate, which task is active, the scale to render, and participant votes scoped to the active task.

**Contract**: Extend `PlanningRoomState` with `CurrentTaskId` (`Guid?`), `IReadOnlyList<PlanningTaskState> Tasks`, and `IReadOnlyList<string> ScaleValues`. New record `PlanningTaskState(Guid TaskId, string Title, int SortOrder, string? AgreedEstimate)`. Keep `IsRevealed` + `Participants` meaning the **active task's** reveal state and per-active-task votes. `PlanningParticipantState` is unchanged (`HasVoted`/`Vote` now reflect the active task).

#### 2. Service — per-task room model

**File**: `src/PlanDeck/Core/PlanDeck.Application/Planning/PlanningRoomService.cs` and `IPlanningRoomService.cs`

**Intent**: Seed the room with tasks + scale, track the active task, scope votes/reveal/reset per task, and hold the picked estimate in memory for broadcast. Stay pure (no repository dependency) — the hub passes session data in.

**Contract**:
- A seed/initialize entry point (e.g. extend `Join` or add `EnsureSeeded(RoomKey, IReadOnlyList<(Guid TaskId, string Title, int SortOrder, string? AgreedEstimate)> tasks, IReadOnlyList<string> scaleValues)`) that populates the room **once** and sets the active task to the first by `SortOrder`. Idempotent for late joiners.
- Per-task vote storage (active-task-scoped `CastVote` validates the vote ∈ scale and rejects after the active task is revealed — reuse F-02 post-reveal rejection).
- `RevealVotes` / `ResetRound` operate on the active task; reset clears only the active task's votes + reveal flag and never the persisted estimate.
- `SetActiveTask(RoomKey, Guid taskId)` switches the active task (validates the task belongs to the room).
- `ApplyAgreedEstimate(RoomKey, Guid taskId, string? estimate)` updates the in-memory per-task estimate (called by the hub after the DB write) so `ToState` reflects it.
- `ToState` now projects active-task votes + the task list (each with its `AgreedEstimate`) + scale.

#### 3. Unit tests

**File**: `src/PlanDeck/Tests/PlanDeck.Unit.Tests/Planning/PlanningRoomServiceTests.cs`

**Intent**: Cover the new per-task invariants alongside the existing F-02 tests.

**Contract**: Tests for: seed sets first task active; late join does not reset active task/votes; cast rejected when vote ∉ scale; vote hidden until active-task reveal; reveal/reset scoped to active task (other tasks unaffected); `SetActiveTask` switches scope and carries independent votes; `ApplyAgreedEstimate` surfaces on the task in state; reset does not clear a set estimate.

**Blast-radius note**: `PlanningRoomState`/`PlanningParticipantState` are constructed in exactly one place — `PlanningRoomService.ToState()` — but extending `Join`/the wire contract touches the F-02 surface: this phase migrates **all** existing F-02 `Join` call sites and the ~20+ existing assertions in `PlanningRoomServiceTests.cs` (and any hub/integration references) to the new shape in-phase, so the suite stays green. No SignalR contract-break risk across deploy: client + server ship as a single hosted-WASM unit, so there's no version skew between the two halves of the contract.

### Success Criteria:

#### Automated Verification:

- Solution builds: `dotnet build PlanDeck.slnx`
- Unit tests pass: `dotnet test Tests/PlanDeck.Unit.Tests/PlanDeck.Unit.Tests.csproj`

#### Manual Verification:

- None (pure logic phase; covered by unit tests).

**Implementation Note**: After automated verification passes, pause for manual confirmation before Phase 3.

---

## Phase 3: Hub Authorization & Persistence Wiring

### Overview

Make the hub the authoritative trust boundary: load session + assigned members, gate every operation on assigned-member-by-email, seed the room from the DB, and wire the two new operations (`SetActiveTask`, `SelectEstimate`) — with the estimate persisted via an application service and broadcast live.

### Changes Required:

#### 1. Application service for the persisted pick

**File**: `src/PlanDeck/Core/PlanDeck.Application/Planning/` (new service, e.g. `VotingRoundService.cs` + interface)

**Intent**: Keep ALL data access for the room out of `PlanDeck.Server` (no existing hub/controller injects repositories — they're consumed only by Application services). This service owns the membership check, the room seed load, and the estimate write; the hub calls it and injects no repositories.

**Contract**:
- `Task<bool> SelectEstimateAsync(Guid sessionId, Guid taskId, string? estimate, CancellationToken ct)` delegating to `ISessionRepository.SetAgreedEstimateAsync`.
- `Task<bool> IsAssignedMemberAsync(Guid sessionId, string email, CancellationToken ct)` — loads the session's assigned members and matches by email (case-insensitive), for the hub gate.
- `Task<RoomSeed?> LoadRoomSeedAsync(Guid sessionId, CancellationToken ct)` returning the ordered tasks (+ existing estimates) and scale for the hub to seed the room.
- Injects `ISessionRepository` / `ISessionMemberRepository` (this is the only layer allowed to). Registered in DI (server `ServiceCollectionExtensions` `AddLocalServices`).

#### 2. Hub — authorization, seeding, new operations

**File**: `src/PlanDeck/Web/PlanDeck.Server/Hubs/PlanningRoomHub.cs`

**Intent**: Enforce assigned-member authorization, seed the room from the DB on join, and add task navigation + the persisted pick.

**Contract**:
- A private gate that resolves the caller's email — using the `email ?? preferred_username` fallback that mirrors `HttpContextCurrentUserContext.Email` (do **not** read the `email` claim alone; the real OIDC scheme runs `MapInboundClaims=false` and may emit only `preferred_username`) — plus the `RoomKey`, loads the session's assigned members (via the application service, see item 1), and throws `HubException` when the email ∉ members (case-insensitive). Applied to `JoinRoom`, `CastVote`, `RevealVotes`, `ResetRound`, `SetActiveTask`, `SelectEstimate`.
- `JoinRoom` additionally loads the seed (`VotingRoundService.LoadRoomSeedAsync`) and seeds the room before adding the participant.
- New `SetActiveTask(string sessionId, string taskId)` → delegate to service, broadcast.
- New `SelectEstimate(string sessionId, string taskId, string value)` → persist via the application service, then `ApplyAgreedEstimate` on the room, then broadcast the updated state. Reject the broadcast if persistence fails.
- Inject only the application service(s) (`IVotingRoundService`) into the hub constructor — **no repositories** (repos stay in the Application layer, matching every existing hub/gRPC service).

#### 3. Hub integration test

**File**: `src/PlanDeck/Tests/PlanDeck.Integration.Tests/Realtime/PlanningRoomHubTests.cs` (namespace `PlanDeck.Realtime.IntegrationTests`, outside `AspireAppFixture` scope)

**Intent**: Prove the per-task flow + authorization + persisted pick over the real transport.

**Contract**: Extend the existing `WebApplicationFactory<ServerEntryPoint>` test: seed a tenant-scoped session with tasks + members (via the test DB / repositories), connect as an assigned member, exercise join → set active task → cast → reveal (hidden-on-the-wire before reveal) → select estimate, and assert the estimate is broadcast and persisted; assert a non-member connection is rejected.

### Success Criteria:

#### Automated Verification:

- Solution builds: `dotnet build PlanDeck.slnx`
- Unit tests pass: `dotnet test Tests/PlanDeck.Unit.Tests/PlanDeck.Unit.Tests.csproj`
- Hub integration tests pass: `dotnet test Tests/PlanDeck.Integration.Tests/PlanDeck.Integration.Tests.csproj --filter "FullyQualifiedName~PlanningRoomHubTests"`

#### Manual Verification:

- A non-assigned authenticated user cannot join the room (rejected), confirmed via logs or a manual hub call.

**Implementation Note**: After automated verification passes, pause for manual confirmation before Phase 4.

---

## Phase 4: Client Service & Voting Screen

### Overview

Extend the client SignalR wrapper with the new operations and build the `VotingRoom.razor` page; add the "Join voting" entry on Active sessions; localize all new strings (en/pl).

### Changes Required:

#### 1. Client service wrapper

**File**: `src/PlanDeck/Web/PlanDeck.Client/Services/IPlanningRoomClientService.cs` + `PlanningRoomClientService.cs`

**Intent**: Mirror the new hub operations so the page can drive task navigation and the pick.

**Contract**: Add `Task SetActiveTaskAsync(string sessionId, string taskId)` and `Task SelectEstimateAsync(string sessionId, string taskId, string value)`, each invoking the matching hub method via the existing `HubConnection`. Keep the single-handler-registration + reconnect auto-rejoin pattern intact.

#### 2. Voting screen

**File**: `src/PlanDeck/Web/PlanDeck.Client/Pages/VotingRoom.razor` (new)

**Intent**: The real-time voting UI for one session.

**Contract**: `@page "/voting/{SessionId:guid}"`, wrapped in `<AuthorizeView>`. On init: load the session (via `ISessionClientService` for task titles/scale/persisted estimates), `ConnectAsync()` + `JoinRoomAsync(sessionId)`, subscribe to `RoomStateChanged`, `StateHasChanged` on updates, and `LeaveRoomAsync` + dispose on teardown. Renders: task list with active-task highlight + per-task agreed estimate (MudBlazor `MudList`/`MudCard`), scale cards from `ScaleValues` (`MudButton` per value, calling `CastVoteAsync`), a live roster showing who has voted (not what) until reveal, then revealed votes together; controls (any member) for `SetActiveTask`, `RevealVotes`, `ResetRound`, and a pick affordance calling `SelectEstimateAsync`. Errors via `RpcException`/`HubException` → `ISnackbar`. Follow the MudBlazor + injection patterns in `Pages/Sessions.razor`.

**Safe empty/locked states** (route is directly reachable, so handle these explicitly): if the session isn't found, isn't `Active`, or the caller is rejected by the hub gate (`HubException`), show a localized "session not available for voting" message + a back link instead of the board (don't attempt to connect/keep retrying). If the session is Active but has no tasks, render an empty-state placeholder ("no tasks to vote on") rather than a broken board. Server-side, the hub gate (Phase 3) is the authority — the client check is UX only, never the trust boundary.

#### 3. Entry point on Sessions

**File**: `src/PlanDeck/Web/PlanDeck.Client/Pages/Sessions.razor`

**Intent**: Let a user reach the voting screen from an Active session.

**Contract**: For sessions with `Status == Active`, add a "Join voting" `MudButton` that navigates to `/voting/{session.Id}` via `NavigationManager`.

#### 4. Localization

**File**: `src/PlanDeck/Web/PlanDeck.Client/Resources/SharedResource.resx` + `SharedResource.pl.resx`

**Intent**: All new user-facing strings are resource-driven in both languages.

**Contract**: Add `Voting_*` keys (title, your-vote, who-voted, reveal, reset, next/active task, pick-estimate, agreed, join-voting, not-voted, errors) to both `.resx` files following the `Feature_Element` naming convention.

### Success Criteria:

#### Automated Verification:

- Solution builds: `dotnet build PlanDeck.slnx`
- Unit + integration tests still pass: `dotnet test Tests/PlanDeck.Unit.Tests/PlanDeck.Unit.Tests.csproj` and the hub integration filter

#### Manual Verification:

- Two browser tabs (two assigned members) join an Active session; each sees the other appear and "has voted" update live without revealing values.
- Reveal shows all votes together; reset clears and allows re-vote on the active task.
- Picking an estimate updates both tabs live and the value persists after reloading `/voting/{sessionId}`.
- Switching the active task carries independent votes; UI strings render correctly in both `en` and `pl`.

**Implementation Note**: After automated verification passes, pause for manual confirmation before Phase 5.

---

## Phase 5: End-to-End Test (Playwright, two browsers)

### Overview

Prove the realtime voting flow end to end with two browser contexts: both join, one votes, reveal shows together, a member picks, and the estimate is shown/persisted.

### Changes Required:

#### 1. Page object

**File**: `src/PlanDeck/Tests/PlanDeck.E2e.Tests/Pages/VotingRoomPage.cs` (new)

**Intent**: Encapsulate locators + actions for the voting screen (Page Object Pattern, accounting for WASM boot).

**Contract**: A `VotingRoomPage` wrapping navigation to `/voting/{sessionId}`, cast/reveal/reset/pick actions, and accessors for the roster + revealed votes + agreed estimate; waits for a known element before asserting.

#### 2. E2E test

**File**: `src/PlanDeck/Tests/PlanDeck.E2e.Tests/` (new test class deriving from Playwright `PageTest`)

**Intent**: Two-browser-context realtime scenario.

**Contract**: Seed/prepare an Active session with tasks + two assigned members (consistent with the fixture's auth/test-data approach), open two browser contexts, join as both, cast votes, reveal, assert both contexts see votes together, pick an estimate, assert both see the agreed value and it persists on reload. Override `ContextOptions()` with `IgnoreHTTPSErrors = true`. Honors the existing `AspireAppFixture` (local boots Aspire/Podman; CI uses `BaseUrl`).

### Success Criteria:

#### Automated Verification:

- Solution builds: `dotnet build PlanDeck.slnx`
- E2E test passes locally: `dotnet test Tests/PlanDeck.E2e.Tests/PlanDeck.E2e.Tests.csproj --filter "FullyQualifiedName~VotingRoom"` (requires Podman running + Playwright browsers installed once)

#### Manual Verification:

- The E2E run is observed green locally (Aspire boots, two contexts complete the flow).

**Implementation Note**: Final phase — after automated verification passes, pause for manual confirmation, then close out the plan.

---

## Testing Strategy

### Unit Tests:

- Per-task round logic in `PlanningRoomService`: seeding/active-task default, idempotent late join, vote∈scale validation, hidden-until-reveal (active task), per-task reveal/reset isolation, `SetActiveTask` scope switch, `ApplyAgreedEstimate` surfacing, reset preserves persisted estimate.

### Integration Tests:

- Hub over real transport (`WebApplicationFactory<ServerEntryPoint>`): assigned-member gate (member allowed, non-member rejected), join→set-active→cast→reveal (hidden on the wire pre-reveal)→select-estimate (broadcast + persisted).

### Manual Testing Steps:

1. Create + configure a session (tasks + scale), assign two members, activate it (S-04/S-05 flows).
2. Open `/voting/{sessionId}` in two tabs as the two members; confirm live roster + "has voted" without value leakage.
3. Reveal → votes appear together; reset → re-vote works on the active task.
4. Pick an estimate → both tabs update live; reload → estimate persists.
5. Switch active task → independent votes; verify `en`/`pl` strings.

## Performance Considerations

In-memory room state with a per-room lock (F-02 pattern) is sufficient for MVP single-replica. The hub loads session + members per relevant invocation for authorization; acceptable at MVP scale. Multi-replica fan-out (a SignalR backplane) is explicitly out of scope and deferred (consistent with F-02).

## Migration Notes

One additive, nullable column (`SessionTasks.AgreedEstimate`) — backward compatible; existing rows get `NULL`. Applied on startup in Development per the existing migration convention.

## References

- Roadmap: `context/foundation/roadmap.md` (S-06, lines 177-188)
- Builds on F-02: `context/archive/` / `context/changes/realtime-vote-integrity/plan.md` (hidden-vote/reveal/reconnection contract)
- Realtime contract: `src/PlanDeck/Core/PlanDeck.Core.Shared/Realtime/PlanningRoomState.cs`, `src/PlanDeck/Core/PlanDeck.Application/Planning/PlanningRoomService.cs`, `src/PlanDeck/Web/PlanDeck.Server/Hubs/PlanningRoomHub.cs`
- Domain/persistence: `src/PlanDeck/Core/PlanDeck.Application/Domain/SessionTask.cs`, `src/PlanDeck/Core/PlanDeck.Infrastructure/Persistence/SessionRepository.cs`
- UI pattern: `src/PlanDeck/Web/PlanDeck.Client/Pages/Sessions.razor`, `src/PlanDeck/Web/PlanDeck.Client/Services/PlanningRoomClientService.cs`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Domain & Persistence — Agreed Estimate

#### Automated

- [x] 1.1 Solution builds: `dotnet build PlanDeck.slnx` — e255991
- [x] 1.2 Migration is generated and the model snapshot updates (no pending-model-changes warning) — e255991
- [x] 1.3 Unit/existing tests pass: `dotnet test Tests/PlanDeck.Unit.Tests/PlanDeck.Unit.Tests.csproj` — e255991
- [x] 1.4 Persistence test for `SetAgreedEstimateAsync` passes (write + re-read; cross-tenant returns false): `dotnet test Tests/PlanDeck.Integration.Tests/PlanDeck.Integration.Tests.csproj` — e255991

#### Manual

- [x] 1.5 On app start in Development the new migration applies cleanly against the configured SQL database — e255991

### Phase 2: Realtime Contract Extension — Per-Task Round

#### Automated

- [x] 2.1 Solution builds: `dotnet build PlanDeck.slnx` — 26adc7c
- [x] 2.2 Unit tests pass: `dotnet test Tests/PlanDeck.Unit.Tests/PlanDeck.Unit.Tests.csproj` — 26adc7c

### Phase 3: Hub Authorization & Persistence Wiring

#### Automated

- [x] 3.1 Solution builds: `dotnet build PlanDeck.slnx` — c7dbe62
- [x] 3.2 Unit tests pass: `dotnet test Tests/PlanDeck.Unit.Tests/PlanDeck.Unit.Tests.csproj` — c7dbe62
- [x] 3.3 Hub integration tests pass: `dotnet test Tests/PlanDeck.Integration.Tests/PlanDeck.Integration.Tests.csproj --filter "FullyQualifiedName~PlanningRoomHubTests"` — c7dbe62

#### Manual

- [x] 3.4 A non-assigned authenticated user cannot join the room (rejected) — c7dbe62

### Phase 4: Client Service & Voting Screen

#### Automated

- [x] 4.1 Solution builds: `dotnet build PlanDeck.slnx`
- [x] 4.2 Unit + integration tests still pass (unit project + hub integration filter)

#### Manual

- [x] 4.3 Two assigned members join an Active session; presence + "has voted" update live without value leakage
- [x] 4.4 Reveal shows votes together; reset clears and allows re-vote on the active task
- [x] 4.5 Picking an estimate updates both tabs live and persists after reloading `/voting/{sessionId}`
- [x] 4.6 Switching the active task carries independent votes; strings render in `en` and `pl`

### Phase 5: End-to-End Test (Playwright, two browsers)

#### Automated

- [ ] 5.1 Solution builds: `dotnet build PlanDeck.slnx`
- [ ] 5.2 E2E test passes locally: `dotnet test Tests/PlanDeck.E2e.Tests/PlanDeck.E2e.Tests.csproj --filter "FullyQualifiedName~VotingRoom"`

#### Manual

- [ ] 5.3 The E2E run is observed green locally (Aspire boots, two contexts complete the flow)
