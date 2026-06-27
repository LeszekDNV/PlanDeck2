# Critical-Path Integrity Tests — Implementation Plan

## Overview

Write integration + targeted e2e tests covering test-plan Phase 1 risks: #1 (session disconnect/reveal consistency), #2 (estimate save/write-back reliability), and #5 (session configuration correctness). Tests follow existing patterns in `PlanningRoomHubTests`, `SessionGrpcServiceTests`, and `VotingRoomTests`.

## Current State Analysis

The codebase already has:
- A `PlanningRoomHubTests` fixture with `WebApplicationFactory`, in-memory DB, `CreateConnection()`, `SeedSession()`, `WaitForBroadcastAsync()` — ready to extend
- A `VotingRoomTests` class with a two-browser-context vote→reveal→pick e2e test (happy path only)
- Unit tests covering reveal idempotency, vote hiding, partial turnout, scale validation in `PlanningRoomServiceTests`
- Unit tests covering session validation, scale resolution in `SessionGrpcServiceTests`
- `FakeAzureDevOpsWorkItemClient` that can be configured to return success/failure
- `VotingRoomPage` and `SessionsPage` page objects with `data-testid` locators

### Key Discoveries:

- Hub integration tests use `HttpTransportType.LongPolling` + `SemaphoreSlim` for broadcast coordination (`PlanningRoomHubTests.cs:243-258`)
- Disconnect can be simulated by disposing a `HubConnection` (triggers `OnDisconnectedAsync` server-side)
- ADO write-back is testable at gRPC level via `SessionGrpcService.WriteTaskEstimateToAdoAsync` with the existing `FakeAzureDevOpsWorkItemClient`
- E2E multi-user uses `Browser.NewContextAsync()` + cookie `e2e-user=b` (`VotingRoomTests.cs:56-68`)
- `PlanningRoomHub.SelectEstimate` validates estimate is in scale before persist — testable via hub invocation

## Desired End State

After this plan is complete:
- Hub integration tests prove: reconnect yields consistent state, full disconnect clears vote by design, reveal is atomic, persist-first estimate never broadcasts on failure, last-write-wins is clean, ADO write-back signals errors explicitly
- A config-to-voting integration test proves: scale values propagate from session config into voting room validation
- Three e2e tests prove in real browsers: two-user vote/reveal consistency, estimate persistence round-trip, and session config feeding into a voting round
- Test-plan §6.1 and §6.4 cookbook entries are filled with patterns from this phase

Verification: `dotnet test PlanDeck.slnx` passes with all new tests green.

## What We're NOT Doing

- Not testing ADO contract/payload mapping (that's Phase 2)
- Not testing authorization/tenant isolation (that's Phase 3)
- Not adding new test infrastructure (fixtures, base classes) — extending existing
- Not testing UI-only concerns (styling, layout, localization)
- Not testing multi-replica/distributed scenarios (MVP is single-replica)
- Not testing reconnect with WebSocket transport (tests use LongPolling for reliability)

## Implementation Approach

Extend existing test classes rather than creating new ones. Hub integration tests (Phase 1-2) use the `PlanningRoomHubTests` fixture. E2E tests (Phase 3) extend `VotingRoomTests` or add a sibling class. Each test method is named to document the invariant it proves.

## Phase 1: Hub Integration Tests (Risk #1 + #2)

### Overview

Add integration test methods to `PlanningRoomHubTests` covering disconnect/reconnect, reveal consistency, persist-first estimate guarantee, last-write-wins behavior, and ADO write-back error signaling.

### Changes Required:

#### 1. Extend Hub Integration Tests

**File**: `Tests/PlanDeck.Integration.Tests/Realtime/PlanningRoomHubTests.cs`

**Intent**: Add test methods proving the critical-path invariants for risks #1 and #2. Each test uses the existing fixture infrastructure (factory, `CreateConnection()`, `SeedSession()`, `WaitForBroadcastAsync()`).

**Contract**: New `[Test]` methods covering these scenarios:

**Risk #1 — Disconnect/Reveal Consistency:**
- `Reconnect_AfterBriefDisconnect_ReceivesCurrentRoomState` — participant joins, votes, connection drops briefly, reconnects via new `JoinRoom` call, receives current state (votes hidden or revealed depending on round state)
- `FullDisconnect_ClearsVote_ByDesign` — participant votes, all connections disposed, second participant sees `HasVoted` flag cleared for the disconnected participant
- `Reveal_WithPartialTurnout_ExposesOnlySubmittedVotes` — two participants joined, only one votes, reveal shows one vote value + one null; no error
- `Reveal_IsIdempotent_SecondCallReturnsSameState` — reveal called twice via hub invocation, second broadcast matches first exactly
- `CastVote_AfterReveal_ThrowsHubException` — attempt to vote after reveal produces `HubException`

**Risk #2 — Estimate Save Reliability:**
- `SelectEstimate_PersistsToDatabase_BeforeBroadcast` — after `SelectEstimate`, verify DB has `AgreedEstimate` set AND the broadcast contains the value
- `SelectEstimate_OnDbFailure_DoesNotBroadcast` — configure test to make `SetAgreedEstimateAsync` fail (task not found scenario via bad taskId); verify HubException thrown and no `RoomStateChanged` emitted with an agreed estimate
- `SelectEstimate_LastWriteWins_NoConcurrencyError` — two rapid `SelectEstimate` calls with different values; second value persists cleanly, broadcast reflects the latest
- `WriteEstimateToAdo_OnConcurrencyConflict_ReturnsAborted` — configure `FakeAzureDevOpsWorkItemClient` to throw `AzureDevOpsConcurrencyException`; verify gRPC call returns `StatusCode.Aborted`
- `WriteEstimateToAdo_OnRateLimit_ReturnsResourceExhausted` — configure fake to throw `AzureDevOpsRateLimitException`; verify `StatusCode.ResourceExhausted`
- `WriteEstimateToAdo_OnSuccess_UpdatesRevision` — configure fake to return success with new revision; verify response contains updated revision and DB is updated

#### 2. ADO Write-Back gRPC Tests

**File**: `Tests/PlanDeck.Unit.Tests/Sessions/SessionGrpcServiceTests.cs`

**Intent**: Add unit test methods for `WriteTaskEstimateToAdoAsync` error paths using the existing `FakeAzureDevOpsWorkItemClient`. These complement the hub integration tests by covering validation preconditions (non-numeric estimate, non-ADO task, missing work item ID).

**Contract**: New `[Test]` methods:
- `WriteTaskEstimateToAdo_NonNumericEstimate_ThrowsFailedPrecondition`
- `WriteTaskEstimateToAdo_NonAdoTask_ThrowsFailedPrecondition`
- `WriteTaskEstimateToAdo_MissingAgreedEstimate_ThrowsFailedPrecondition`
- `WriteTaskEstimateToAdo_GuestUser_ThrowsPermissionDenied`

### Success Criteria:

#### Automated Verification:

- All new hub integration tests pass: `dotnet test Tests/PlanDeck.Integration.Tests --filter "FullyQualifiedName~PlanningRoomHubTests"`
- All new unit tests pass: `dotnet test Tests/PlanDeck.Unit.Tests --filter "FullyQualifiedName~SessionGrpcServiceTests"`
- Existing tests still pass (no regressions): `dotnet test PlanDeck.slnx`
- Build succeeds: `dotnet build PlanDeck.slnx`

#### Manual Verification:

- Confirm test names clearly document the invariant they prove (readable as specification)

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 2: Config-to-Voting Integration Tests (Risk #5)

### Overview

Add integration tests proving that session configuration (task selection, voting scale) persists correctly and propagates into the voting room's runtime state.

### Changes Required:

#### 1. Extend Hub Integration Tests for Config Pipeline

**File**: `Tests/PlanDeck.Integration.Tests/Realtime/PlanningRoomHubTests.cs`

**Intent**: Add test methods proving the config→seed→vote-validation pipeline. These tests seed a session with specific configuration (custom scale, multiple tasks), join the room, and verify the room's runtime state matches the persisted config.

**Contract**: New `[Test]` methods:
- `JoinRoom_WithCustomScale_VoteOutsideScaleRejected` — seed session with custom scale `["S","M","L"]`, activate, join, attempt vote with `"5"` (not in scale) → `HubException`
- `JoinRoom_WithMultipleTasks_TaskSelectionPreserved` — seed session with 3 tasks in specific sort order, join room, verify state contains all 3 tasks in correct order
- `ConfigRoundTrip_CreateAndJoin_ScaleMatchesConfiguration` — seed session with Fibonacci scale, activate, join, verify broadcasted state's `ScaleValues` matches Fibonacci canonical set

#### 2. Session Persistence Integration Tests

**File**: `Tests/PlanDeck.Integration.Tests/Persistence/SessionPersistenceTests.cs`

**Intent**: Add tests proving atomic creation and config round-trip at the DB level. Extend existing persistence tests that already use `FakeCurrentUserContext` + real DbContext.

**Contract**: New `[Test]` methods:
- `CreateSession_WithTasksAndCustomScale_AllPersistedAtomically` — create session with 3 tasks and custom scale, read back, verify all fields match (name, scale type, scale values, task titles, sort order)
- `CreateSession_DbFailureMidway_NoPartialSessionExists` — verify no partial session left in DB if exception occurs during save (this may already be covered; verify and add only if gap exists)

### Success Criteria:

#### Automated Verification:

- All new config pipeline tests pass: `dotnet test Tests/PlanDeck.Integration.Tests --filter "FullyQualifiedName~PlanningRoomHubTests.JoinRoom" | "FullyQualifiedName~PlanningRoomHubTests.Config"`
- All new persistence tests pass: `dotnet test Tests/PlanDeck.Integration.Tests --filter "FullyQualifiedName~SessionPersistenceTests"`
- Full solution build succeeds: `dotnet build PlanDeck.slnx`
- No regressions: `dotnet test PlanDeck.slnx`

#### Manual Verification:

- Confirm config-to-voting tests exercise the real pipeline (session seed → hub join → state broadcast) without mocking intermediate layers

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 3: Targeted E2E Tests

### Overview

Add 3 comprehensive Playwright e2e tests proving critical paths in real browsers: vote/reveal consistency (two users), estimate persistence round-trip, and session config feeding into a voting round.

### Changes Required:

#### 1. E2E Tests for Critical Path

**File**: `Tests/PlanDeck.E2e.Tests/VotingRoomTests.cs`

**Intent**: Add 3 new test methods following the existing two-browser-context pattern. Each test covers one risk's critical path end-to-end.

**Contract**: New `[Test]` methods using existing `VotingRoomPage`, `SessionsPage`, `SessionMembersPage` page objects:

- `TwoMembers_DisconnectAndReconnect_RevealShowsConsistentState` (Risk #1) — User A and B join, both vote, User B navigates away (simulating disconnect), User A reveals, User B navigates back to voting room, both see identical revealed votes. Proves reconnect/reveal consistency in real browser.

- `EstimateSelect_PersistsAcrossPageReload` (Risk #2) — Two users vote, reveal, organizer picks estimate, page reloads, agreed estimate still visible on the task. Proves estimate persistence is durable (not just in-memory).

- `SessionConfig_ScaleAndTasks_FeedIntoVotingRound` (Risk #5) — Create session with T-Shirt scale and 2 specific tasks, activate, join voting room, verify scale buttons match T-Shirt values and task list shows both tasks in order. Proves config→voting pipeline works end-to-end in browser.

#### 2. Page Object Extensions (if needed)

**File**: `Tests/PlanDeck.E2e.Tests/Pages/VotingRoomPage.cs`

**Intent**: Add helper methods if the existing page object lacks locators for agreed-estimate display or scale button enumeration. Only add what's missing for the new tests.

**Contract**: Possible additions:
- `AgreedEstimateText` locator (for verifying persisted estimate after reload)
- `ScaleButtons` locator (for enumerating available vote options)
- `TaskListItems` locator (for verifying task names in voting room)

### Success Criteria:

#### Automated Verification:

- All new e2e tests pass locally (Podman running): `dotnet test Tests/PlanDeck.E2e.Tests --filter "FullyQualifiedName~VotingRoomTests"`
- Full solution build succeeds: `dotnet build PlanDeck.slnx`
- No regressions in existing e2e: `dotnet test Tests/PlanDeck.E2e.Tests`

#### Manual Verification:

- Watch one e2e test run in headed mode (`HEADED=1`) to confirm the flow matches real user behavior
- Confirm tests are deterministic (run 3x, all pass)

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 4: Cookbook Update (§6)

### Overview

Fill in test-plan.md §6.1 (integration test for session/voting flow) and §6.4 (e2e test for critical flow) with concrete patterns established in phases 1-3.

### Changes Required:

#### 1. Update Test Plan Cookbook

**File**: `context/foundation/test-plan.md`

**Intent**: Replace TBD entries in §6.1 and §6.4 with actionable recipes: where to add a test, naming convention, fixture to use, assertion pattern, and a reference test to copy from.

**Contract**: Update sections 6.1 and 6.4 with:
- Location (file path)
- Naming convention (e.g., `Scenario_Condition_ExpectedResult`)
- Fixture setup reference (which `[OneTimeSetUp]` to reuse)
- Key helper methods available
- Reference test to copy-from (the first test written in this phase)
- Run command

### Success Criteria:

#### Automated Verification:

- File exists and is valid markdown: `context/foundation/test-plan.md`
- Build still passes (no source changes): `dotnet build PlanDeck.slnx`

#### Manual Verification:

- Cookbook entries are actionable — a developer reading §6.1 can add a new hub integration test without reading the full test file first

---

## Testing Strategy

### Integration Tests (Phases 1-2):

- Hub tests: vote lifecycle, disconnect/reconnect, reveal atomicity, persist-first estimate, ADO error mapping
- Config pipeline: scale propagation, task ordering, round-trip persistence
- Edge cases: vote after reveal (rejected), non-numeric estimate write-back (rejected), last-write-wins (clean)

### E2E Tests (Phase 3):

- Two-user vote→reveal consistency with disconnect/reconnect
- Estimate persistence survives page reload
- Config (scale + tasks) visible in voting room

### What's NOT tested here:

- ADO payload structure/contract (Phase 2 of test rollout)
- Authorization/tenant isolation (Phase 3 of test rollout)
- UI styling/layout regressions

## Performance Considerations

- Hub integration tests use in-memory DB → fast (~1-5s each)
- E2E tests require full Aspire startup → first test ~60s (boot), subsequent ~30-60s each
- Total new test runtime estimate: ~5 minutes locally (integration: ~30s, e2e: ~4 min)

## References

- Related research: `context/changes/testing-critical-path-integrity/research.md`
- Existing hub tests: `Tests/PlanDeck.Integration.Tests/Realtime/PlanningRoomHubTests.cs`
- Existing e2e tests: `Tests/PlanDeck.E2e.Tests/VotingRoomTests.cs`
- Test-plan strategy: `context/foundation/test-plan.md` §1-§3
- Archived voting plan: `context/archive/2026-06-22-realtime-voting-round/plan.md`
- Archived ADO write-back plan: `context/archive/2026-06-24-ado-estimate-writeback/plan.md`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Hub Integration Tests (Risk #1 + #2)

#### Automated

- [x] 1.1 All new hub integration tests pass — 8bb7f9b
- [x] 1.2 All new ADO write-back unit tests pass — 8bb7f9b
- [x] 1.3 No regressions in existing test suite — 8bb7f9b
- [x] 1.4 Solution builds cleanly — 8bb7f9b

#### Manual

- [x] 1.5 Test names clearly document invariants (readable as specification) — 8bb7f9b

### Phase 2: Config-to-Voting Integration Tests (Risk #5)

#### Automated

- [x] 2.1 Config pipeline hub tests pass — ce58965
- [x] 2.2 Session persistence tests pass — ce58965
- [x] 2.3 Solution builds cleanly — ce58965
- [x] 2.4 No regressions — ce58965

#### Manual

- [x] 2.5 Config-to-voting tests exercise real pipeline without mocking intermediate layers — ce58965

### Phase 3: Targeted E2E Tests

#### Automated

- [x] 3.1 All 3 new e2e tests pass locally (Podman running) — 7df807f
- [x] 3.2 Solution builds cleanly — 7df807f
- [x] 3.3 No regressions in existing e2e tests — 7df807f

#### Manual

- [x] 3.4 Watched one e2e test in headed mode — flow matches real user behavior — 7df807f
- [x] 3.5 Tests are deterministic (3 consecutive runs pass) — 7df807f

### Phase 4: Cookbook Update (§6)

#### Automated

- [x] 4.1 test-plan.md is valid markdown with §6.1 and §6.4 filled

#### Manual

- [x] 4.2 Cookbook entries are actionable for a developer adding new tests
