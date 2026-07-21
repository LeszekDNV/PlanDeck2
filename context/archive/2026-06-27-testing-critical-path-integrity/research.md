---
date: 2026-06-27T10:20:32+02:00
researcher: AI
git_commit: 102e64d991da66c7cdb359cafeba1f3c8282da88
branch: main
repository: LeszekDNV/PlanDeck2
topic: "Critical-path integrity: grounding risks #1, #2, #5 for Phase 1 test rollout"
tags: [research, testing, voting-round, estimate-writeback, session-config, critical-path]
status: complete
last_updated: 2026-06-27
last_updated_by: AI
---

# Research: Critical-path integrity — grounding risks #1, #2, #5

**Date**: 2026-06-27T10:20:32+02:00
**Researcher**: AI
**Git Commit**: 102e64d991da66c7cdb359cafeba1f3c8282da88
**Branch**: main
**Repository**: LeszekDNV/PlanDeck2

## Research Question

Ground test-plan risks #1 (session disconnect/reveal consistency), #2 (estimate save/write-back reliability), and #5 (session configuration correctness) in the actual codebase: identify boundaries, state machines, concurrency rules, error-signaling patterns, and testable invariants that Phase 1 integration + targeted e2e tests must verify.

## Summary

The three risks map to well-defined subsystems with clear boundaries:

1. **Risk #1** — Real-time voting lives in `PlanningRoomService` (in-memory, per-room lock) + `PlanningRoomHub` (SignalR). Votes are ephemeral, last-write-wins before reveal, blocked after reveal. Reconnection auto-rejoins but **does not recover a lost vote** if the participant was fully disconnected (all connections closed → vote cleared). Reveal is idempotent and atomically exposes all votes via a single broadcast.

2. **Risk #2** — Local estimate persistence (`SelectEstimate`) uses persist-first pattern: DB write succeeds before in-memory state change + broadcast. ADO write-back is a **separate, user-initiated** operation with explicit error mapping (concurrency → `Aborted`, rate-limit → `ResourceExhausted`, generic → `Unavailable`) surfaced as localized snackbar toasts. No silent drops exist by design.

3. **Risk #5** — Session creation is atomic (single `SaveChangesAsync`). Voting-scale values are resolved at boundary (preset or custom-validated), persisted as JSON primitive collection. Draft-only enforcement is server-side. Validation rejects blank names, empty tasks, empty custom scales with `InvalidArgument`. Scale + tasks survive round-trip and are seeded into the voting room on first join.

## Detailed Findings

### Risk #1: Session Disconnect / Reveal Consistency

#### Round State Machine

- **States per task**: voting → revealed → reset (cyclic)
- **Gate**: `RoomTask.IsRevealed` — when `true`, `CastVote` is rejected
- **Reveal**: sets `IsRevealed = true`; idempotent (safe to call multiple times)
- **Reset**: clears `IsRevealed`, `Votes`, `AgreedEstimate` for active task only
- **File**: `Core/PlanDeck.Application/Planning/PlanningRoomService.cs:189-215`

#### Vote Submission

- SignalR-only (no gRPC for casting)
- `task.Votes[participantId] = vote` — last write wins, no idempotency key
- Validated against `room.ScaleValues` membership
- Participant must be joined; active task must exist
- **File**: `PlanningRoomService.cs:164-184`

#### Reconnection Flow

- Client: `WithAutomaticReconnect()` + `Reconnected` handler re-invokes `JoinRoom(sessionId)`
- Server `JoinRoom`: calls `AuthorizeAndLoadSeedAsync()` → fresh session load → `EnsureSeeded()` (idempotent) → broadcasts current state
- **File (client)**: `Web/PlanDeck.Client/Services/PlanningRoomClientService.cs:9-30`
- **File (server)**: `Web/PlanDeck.Server/Hubs/PlanningRoomHub.cs:25-39`

#### Disconnect Semantics

- Server `OnDisconnectedAsync`: removes connection from `participant.Connections`
- If **last connection** removed → participant fully leaves, **vote cleared**
- Multi-connection support: participant stays if any connection active
- **File**: `PlanningRoomHub.cs:116-131`

#### Vote Visibility (reveal projection)

```csharp
var vote = isRevealed && votes.TryGetValue(participant.Key, out var cast) ? cast : null;
```
- Before reveal: `Vote = null` for all (only `HasVoted` flag visible)
- After reveal: all votes exposed atomically in single broadcast
- **File**: `PlanningRoomService.cs:300-302`

#### Concurrency Protection

- `ConcurrentDictionary<RoomKey, PlanningRoom>` + `lock (room)` on every mutation
- Single broadcast per state change — no intermediate states leak
- **File**: `PlanningRoomService.cs:9, 21, 103, 164, 192, 205, 220, 235, 254, 263`

#### Testable Invariants for Risk #1

| Invariant | What to assert |
|-----------|---------------|
| Reconnect yields current state | After rejoin, client receives `RoomStateChanged` with all existing votes (if revealed) or `HasVoted` flags (if not) |
| Vote loss on full disconnect | If participant's last connection drops, their vote is cleared from active task |
| Reveal is atomic + idempotent | Calling reveal twice produces identical state; all participants see all votes in one broadcast |
| No vote after reveal | `CastVote` after `IsRevealed=true` throws `InvalidOperationException` |
| Partial turnout reveal | Reveal works with subset of participants having voted |
| Reset scoping | Reset clears only active task's votes; other tasks' agreed estimates preserved |

---

### Risk #2: Estimate Save / Write-Back Reliability

#### Local Persistence (SelectEstimate)

- **Persist-first pattern**: DB write via `IVotingRoundService.SelectEstimateAsync()` → success → in-memory update → broadcast
- If DB write fails → `HubException("The agreed estimate could not be saved.")` → no in-memory change, no broadcast
- **File**: `PlanningRoomHub.cs:94-114`

#### Repository Method

```csharp
public async Task<bool> SetAgreedEstimateAsync(Guid sessionId, Guid taskId, string? estimate, CancellationToken ct)
```
- Returns `false` if task not found (→ HubException)
- No optimistic concurrency on local save (last write wins)
- **File**: `Core/PlanDeck.Infrastructure/Persistence/SessionRepository.cs:53-65`

#### ADO Write-Back (Separate, User-Initiated)

- Triggered by explicit user click ("Write to ADO" button)
- **gRPC operation**: `ISessionService.WriteTaskEstimateToAdoAsync(WriteTaskEstimateRequest)`
- **Preconditions validated server-side** (SessionGrpcService:254-284):
  - Task must be `TaskSource.AzureDevOps`
  - `AdoWorkItemId` must be non-null
  - `AgreedEstimate` must be non-null and numeric (`double.TryParse`)
- **HTTP call**: PATCH with JSON-Patch (test `/rev` for optimistic concurrency + add `/fields/{EstimateField}`)
- **On success**: persists new revision via `SetAdoRevisionAsync` → returns `WriteTaskEstimateReply(WorkItemId, Revision, Session)`
- **File**: `SessionGrpcService.cs:254-315`, `AzureDevOpsWorkItemClient.cs:80-106`

#### Error Signaling (No Silent Drops)

| Failure | gRPC Status | Client Snackbar Key |
|---------|-------------|---------------------|
| ADO 409/412 (concurrency) | `Aborted` | `Sessions_WriteEstimateConflict` |
| ADO 429 (rate limit) | `ResourceExhausted` | `Sessions_WriteEstimateRateLimited` |
| Generic ADO/HTTP failure | `Unavailable` | `Sessions_WriteEstimateFailed` |
| Local save failure | `HubException` | (not ADO — direct hub error) |

- **Client handler**: `Sessions.razor.cs:544-572` — catches `RpcException`, maps `StatusCode` to localized toast
- **No retry logic** — user must re-attempt (or re-import for concurrency conflict)

#### Boundary: Local vs External

- Local persistence and ADO write-back are **independent operations** with separate failure modes
- Local save can succeed while ADO write-back hasn't been attempted yet (or fails)
- ADO write-back only available for numeric estimates from ADO-sourced tasks

#### Testable Invariants for Risk #2

| Invariant | What to assert |
|-----------|---------------|
| Persist-first guarantee | If DB write fails, in-memory state unchanged + no broadcast sent |
| Explicit success signaling | Successful `SelectEstimate` → client receives updated state with `AgreedEstimate` set |
| Explicit failure signaling (local) | DB failure → `HubException` propagated to calling client |
| ADO write-back success | Returns `WriteTaskEstimateReply` with updated revision; local `AdoRevision` updated |
| ADO concurrency conflict | 409 → `RpcException(Aborted)` → client shows conflict toast |
| ADO rate limit | 429 → `RpcException(ResourceExhausted)` → client shows rate-limited toast |
| No silent drop | Every failure path produces either an exception or a `false` return that is handled |
| Non-numeric gating | T-Shirt/`?`/`☕` estimates → `FailedPrecondition` (server-side) or action hidden (client-side) |

---

### Risk #5: Session Configuration Correctness

#### Configuration Shape (Persisted)

```
PlanningSession: Name, TeamId, Status(Draft|Active), ScaleType, ScaleValues(JSON), ShareCode
  └── SessionTask[]: Title, Source, SortOrder, AdoWorkItemId, AdoRevision, WorkItemType, State, AgreedEstimate
```
- **File**: `Core/PlanDeck.Application/Domain/PlanningSession.cs`, `SessionTask.cs`
- **Cascade delete**: removing session removes all tasks
- **Unique constraint**: `(SessionId, AdoWorkItemId)` where non-null — no duplicate ADO items per session

#### Voting Scale Resolution

- `Fibonacci` → canonical `["0","1","2","3","5","8","13","21","?","☕"]`
- `TShirt` → canonical `["XS","S","M","L","XL","?","☕"]`
- `Custom` → user-provided, trimmed, deduped (case-insensitive), must have ≥1 value
- Resolved **at boundary** (create/update), stored in `ScaleValues` — voting room reads single resolved list
- **File**: `SessionGrpcService.cs:421-445`

#### Validation Rules (Server-Side)

| Rule | gRPC Status |
|------|-------------|
| Session name required (non-blank) | `InvalidArgument` |
| Custom scale ≥1 value after trim/dedup | `InvalidArgument` |
| Unknown scale type | `InvalidArgument` |
| Task title required (non-blank) | `InvalidArgument` |
| Edit non-Draft session | `FailedPrecondition` |
| Duplicate ADO work items | Silently skipped (graceful) |

#### Atomicity

- `CreateSessionAsync`: single `db.Sessions.Add(session); await db.SaveChangesAsync()` — all-or-nothing
- Member assignment failure (duplicate) is caught and logged — session still created
- **File**: `SessionRepository.cs:14`, `SessionGrpcService.cs:59-76`

#### Configuration Update Contract

- `UpdateSessionConfigAsync(UpdateSessionConfigRequest)` — updates name, scale type, custom values, team
- **Only works on Draft sessions** (`LoadDraftAsync` throws `SessionNotDraftException` → `FailedPrecondition`)
- Does NOT modify tasks (separate add/remove operations)
- **File**: `SessionGrpcService.cs:116-133`

#### Scale + Tasks in Voting Room

- On `JoinRoom`, hub calls `AuthorizeAndLoadSeedAsync()` → loads session with tasks eager-loaded
- `EnsureSeeded()` initializes room with `ScaleValues` + task list (idempotent — late joiner doesn't reset)
- Vote validation checks against seeded `room.ScaleValues`
- **File**: `PlanningRoomHub.cs:25-39`, `PlanningRoomService.cs:179-182`

#### Testable Invariants for Risk #5

| Invariant | What to assert |
|-----------|---------------|
| Task selection persists | Created session contains all provided tasks with correct titles, sort order, and source |
| Scale resolution correctness | Fibonacci → canonical list; TShirt → canonical list; Custom → trimmed/deduped |
| Scale persists for voting | Scale values from session config are used in voting room (vote against non-scale value rejected) |
| Draft-only enforcement | Attempt to update Active session → `FailedPrecondition` |
| Validation rejects invalid | Blank name, empty custom scale, blank task title → `InvalidArgument` |
| Atomic creation | If creation fails mid-way, no partial session exists in DB |
| Config survives round-trip | Create → Get → all fields match (name, scale, tasks, team) |

---

## Code References

- `Core/PlanDeck.Application/Planning/PlanningRoomService.cs` — In-memory voting round state machine (lock, vote, reveal, reset)
- `Web/PlanDeck.Server/Hubs/PlanningRoomHub.cs` — SignalR hub: authorization, seeding, persist-first estimate, disconnect handling
- `Web/PlanDeck.Client/Services/PlanningRoomClientService.cs:9-30` — Client reconnection + auto-rejoin
- `Core/PlanDeck.Infrastructure/Persistence/SessionRepository.cs:53-65` — `SetAgreedEstimateAsync` (local persistence)
- `Core/PlanDeck.Application/Services/SessionGrpcService.cs:254-315` — ADO write-back orchestration + error mapping
- `Core/PlanDeck.Infrastructure/AzureDevOps/AzureDevOpsWorkItemClient.cs:80-106` — HTTP PATCH to ADO REST API
- `Web/PlanDeck.Client/Pages/Sessions.razor.cs:544-572` — Client-side ADO error handler with localized toasts
- `Core/PlanDeck.Application/Services/SessionGrpcService.cs:24-133` — Session create/update with validation + scale resolution
- `Core/PlanDeck.Application/Domain/PlanningSession.cs` — Session aggregate root
- `Core/PlanDeck.Application/Domain/SessionTask.cs` — Task entity (AgreedEstimate, AdoWorkItemId, AdoRevision)
- `Core/PlanDeck.Core.Shared/Contracts/ISessionService.cs` — gRPC contracts (CreateSession, UpdateConfig, WriteTaskEstimate)
- `Core/PlanDeck.Core.Shared/Realtime/PlanningRoomState.cs:3-23` — Room state DTO (per-task votes, reveal flag, scale)
- `Tests/PlanDeck.Unit.Tests/Planning/PlanningRoomServiceTests.cs` — Existing unit tests (reveal idempotency, partial turnout)
- `Tests/PlanDeck.Integration.Tests/Realtime/PlanningRoomHubTests.cs` — Existing hub integration tests
- `Tests/PlanDeck.Unit.Tests/Sessions/SessionGrpcServiceTests.cs` — Existing validation + scale tests
- `Tests/PlanDeck.Integration.Tests/Persistence/SessionPersistenceTests.cs` — Existing atomic persistence tests

## Architecture Insights

1. **Trust boundary pattern**: The SignalR hub is the trust boundary. `PlanningRoomService` is pure in-memory (no DB) and unit-testable. The hub handles authorization, DB seeding, and persist-first orchestration.

2. **Persist-first pattern**: All state-changing operations (estimate selection, round reset) write to DB first. Only on DB success does in-memory state mutate and broadcast fire. This prevents phantom state divergence.

3. **Ephemeral vs persistent data**: Votes are ephemeral (in-memory only, lost on server restart or full disconnect). Agreed estimates are persistent (DB). This is intentional — votes are transient per-round artifacts; only the outcome matters.

4. **Single-replica assumption**: Per-room `lock` is sufficient for MVP (single server instance). No distributed locking or event sourcing. Scaling to multiple replicas would require sticky sessions or a shared state store.

5. **Separation of local + external persistence**: Estimate write-back to ADO is an explicit, separate user action — never automatic. This isolates external failures from the core voting flow.

6. **Boundary-resolved configuration**: Scale values resolved at create/update boundary, stored as final list. Downstream consumers (voting room) never interpret scale type — they read the resolved list directly.

## Historical Context (from prior changes)

- `context/archive/2026-06-22-realtime-voting-round/plan.md` — Established per-task round architecture, ephemeral votes, persist-first estimate pattern, idempotent room seeding, hub-as-trust-boundary design. Tests designed: unit (per-task logic), integration (hub over transport), E2E (two-browser coordination).

- `context/archive/2026-06-24-ado-estimate-writeback/plan.md` — Established typed ADO exceptions, explicit error-mapping to gRPC statuses, numeric-only gating, no-retry policy, revision tracking for optimistic concurrency. Key principle: "success or failure must be surfaced explicitly — never silently dropped."

- `context/archive/2026-06-18-create-configure-session/plan.md` — Established ADO tasks as snapshots (not live links), Draft-only enforcement server-side, preset scale resolution at boundary, atomic creation (single SaveChangesAsync), cascade delete pattern. Testing: unit (validation+scale), integration (real DB+tenant isolation), E2E (create flow).

## Open Questions

1. **Vote recovery on reconnect**: Currently a full disconnect (last connection) clears the participant's vote. Should Phase 1 tests assert this as *correct behavior* (documenting the design) or flag it as a gap? The archived plan treats votes as ephemeral, suggesting this is intentional.

2. **Concurrent organizer actions**: No optimistic concurrency on local estimate save (`SetAgreedEstimateAsync`). Two organizers calling `SelectEstimate` simultaneously → last write wins. Is this a risk worth testing, or is single-organizer the assumed model?

3. **Scale values immutability during active session**: Once a session is activated, scale values are seeded into the room. But `UpdateSessionConfig` only works on Draft sessions. Should tests verify that scale values used in voting match what was configured at activation time (not modified later)?
