<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Real-time Vote-Integrity Baseline (F-02)

- **Plan**: context/changes/realtime-vote-integrity/plan.md
- **Scope**: Phases 1-6 of 6
- **Date**: 2026-07-21
- **Verdict**: APPROVED
- **Findings**: 1 critical, 2 warnings, 1 observation

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | PASS |
| Safety & Quality | PASS |
| Architecture | PASS |
| Pattern Consistency | PASS |
| Success Criteria | PASS |

## Success Criteria Evidence

- `dotnet build PlanDeck.slnx`: PASS (one unrelated pre-existing MudBlazor analyzer warning in `Sessions.razor`)
- `dotnet test Tests/PlanDeck.Unit.Tests/PlanDeck.Unit.Tests.csproj --no-build`: PASS (109/109 after triage)
- `dotnet test Tests/PlanDeck.Integration.Tests/PlanDeck.Integration.Tests.csproj --no-build --filter "FullyQualifiedName~PlanningRoomHubTests"`: PASS (20/20 after triage)
- All Progress rows, including manual checks, are complete and backed by observable code or tests.

## Findings

### F1 — Concurrent reset can desynchronize persisted and in-memory task state

- **Severity**: ❌ CRITICAL
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: src/PlanDeck/Web/PlanDeck.Server/Hubs/PlanningRoomHub.cs:81
- **Detail**: `ResetRound` reads `CurrentTaskId`, awaits persistence for that task, then calls `planningRoomService.ResetRound(key)`, which resolves the active task again. A concurrent `SetActiveTask` can switch tasks during the await, causing the database clear to target the old task while memory resets the new task. This silently leaves persisted and in-memory agreed estimates inconsistent.
- **Fix**: Add `ResetRound(RoomKey key, Guid taskId)` and mutate the exact task whose persisted estimate was cleared.
  - Strength: Makes persistence and the in-memory mutation target the same task even when `SetActiveTask` interleaves.
  - Tradeoff: Requires a small interface/service change plus a focused concurrency regression test.
  - Confidence: HIGH — the two snapshots and intervening await are directly visible in the hub flow.
  - Blind spot: A broader per-room command serializer could solve related races but was not assessed.
- **Decision**: FIXED — `ResetRound` now targets the captured task id; regression coverage switches the active task before reset.

### F2 — Caller disconnect can cancel broadcasts intended for healthy participants

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: src/PlanDeck/Web/PlanDeck.Server/Hubs/PlanningRoomHub.cs:48
- **Detail**: Group broadcasts pass `Context.ConnectionAborted`, which represents only the invoking connection. If that caller disconnects while casting, revealing, resetting, or switching tasks, its token can cancel delivery to every other healthy connection in the group. `OnDisconnectedAsync` already broadcasts without this caller-owned token.
- **Fix**: Do not pass `Context.ConnectionAborted` to `Clients.Group(...).SendAsync`; reserve it for caller-scoped work.
- **Decision**: FIXED — group broadcasts no longer inherit the invoking connection's cancellation token.

### F3 — Active-session status is checked only when joining

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: src/PlanDeck/Core/PlanDeck.Application/Planning/VotingRoundService.cs:10
- **Detail**: `AuthorizeAndLoadSeedAsync` rejects non-Active sessions during join, but `IsAuthorizedParticipantAsync`, used by later member actions, checks only identity/membership. Guests bypass that method entirely after joining. A connection can therefore continue voting or persisting estimates if the session becomes Draft/Completed while it remains connected.
- **Fix A ⭐ Recommended**: Re-check `SessionStatus.Active` for every room mutation, including guest actions.
  - Strength: Keeps the persisted session lifecycle authoritative throughout a live connection.
  - Tradeoff: Adds a tenant-scoped repository read to each mutation unless status is cached with explicit invalidation.
  - Confidence: HIGH — current member and guest authorization branches omit the status check.
  - Blind spot: Product policy may intentionally allow an already joined round to finish after status changes.
- **Fix B**: Document "active at join time" as the intentional lifecycle policy.
  - Strength: Avoids additional hot-path database reads.
  - Tradeoff: Completed/inactive sessions can still accept live mutations until clients disconnect.
  - Confidence: MEDIUM — no explicit product requirement settles this lifecycle edge case.
  - Blind spot: Downstream session-completion UX was not reviewed.
- **Decision**: FIXED via Fix A — member and guest mutations now re-check Active status; unit and transport regressions added.

### F4 — In-memory rooms have no eviction path

- **Severity**: 🔭 OBSERVATION
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: src/PlanDeck/Core/PlanDeck.Application/Planning/PlanningRoomService.cs:9
- **Detail**: The singleton `_rooms` dictionary uses `GetOrAdd` and never removes entries. Every joined session retains tasks, participants, and votes until process restart. This matches the single-replica MVP but grows without bound on a long-running instance.
- **Fix A ⭐ Recommended**: Add TTL eviction for rooms with no online participants.
  - Strength: Bounds memory without coupling room cleanup to every session lifecycle path.
  - Tradeoff: Requires background cleanup and a clearly chosen inactivity window.
  - Confidence: HIGH — no removal path exists.
  - Blind spot: Expected production session volume and process restart cadence are unknown.
- **Fix B**: Add explicit removal on session completion/deletion.
  - Strength: Deterministic cleanup with no background worker.
  - Tradeoff: Must wire every lifecycle path and still leaves abandoned active rooms resident.
  - Confidence: MEDIUM — completion/deletion flows exist but were outside this plan.
  - Blind spot: Other lifecycle transitions may also need cleanup.
- **Decision**: FIXED via Fix A — rooms without online participants are evicted after 15 minutes by a TimeProvider-driven hosted cleanup service.
