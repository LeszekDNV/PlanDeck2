<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Critical-Path Integrity Tests

- **Plan**: `context/changes/testing-critical-path-integrity/plan.md`
- **Scope**: Phase 1-4 of 4
- **Date**: 2026-07-21
- **Verdict**: NEEDS ATTENTION
- **Findings**: 0 critical, 7 warnings, 0 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | FAIL |
| Scope Discipline | WARNING |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | PASS |
| Success Criteria | PASS |

## Findings

### F1 — Reconnect E2E can reveal before disconnect is observed

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: `src/PlanDeck/Tests/PlanDeck.E2e.Tests/VotingRoomTests.cs:168`
- **Detail**: The test closes user B's page and immediately reveals. If the server has not processed the disconnect yet, the scenario can pass without exercising the planned offline/reconnect transition.
- **Fix**: Wait until user A observes user B as offline before revealing and reconnecting.
  - Strength: Proves the intended disconnect boundary instead of relying on transport timing.
  - Tradeoff: Requires a stable page-object locator for participant online state.
  - Confidence: HIGH — no current assertion synchronizes on server-side disconnect processing.
  - Blind spot: The current UI may need a dedicated `data-testid` for online state.
- **Decision**: FIXED

### F2 — Last-write-wins test performs serialized writes

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Plan Adherence
- **Location**: `src/PlanDeck/Tests/PlanDeck.Integration.Tests/Realtime/PlanningRoomHubTests.cs:586`
- **Detail**: `SelectEstimate_LastWriteWins_NoConcurrencyError` waits for the first invocation and broadcast before issuing the second write, so it proves sequential overwrite rather than the planned rapid/concurrent organizer behavior.
- **Fix**: Drive overlapping writes from separate hub connections and assert that persisted state and the final broadcast agree with the write that completes last.
  - Strength: Exercises the actual concurrency boundary named by the test and plan.
  - Tradeoff: Deterministic completion ordering may require controlled persistence timing.
  - Confidence: HIGH — the current calls are visibly serialized.
  - Blind spot: Existing repository abstractions may not expose a simple delay hook.
- **Decision**: FIXED

### F3 — Persist-first ordering is not observed at broadcast time

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Plan Adherence
- **Location**: `src/PlanDeck/Tests/PlanDeck.Integration.Tests/Realtime/PlanningRoomHubTests.cs:286`
- **Detail**: The test awaits the broadcast and checks the database afterward. It proves eventual agreement, but not that persistence completed before `RoomStateChanged` was emitted.
- **Fix**: Read the persisted estimate from the broadcast callback before releasing its synchronization gate, then assert it already equals the broadcast value.
  - Strength: Directly proves the persist-before-broadcast invariant at the observable boundary.
  - Tradeoff: Adds a database read inside test synchronization code.
  - Confidence: HIGH — the current assertion order cannot distinguish persist-first from broadcast-first.
  - Blind spot: None significant.
- **Decision**: FIXED

### F4 — Session persistence contract is incomplete

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Plan Adherence
- **Location**: `src/PlanDeck/Tests/PlanDeck.Integration.Tests/Persistence/SessionPersistenceTests.cs:178`
- **Detail**: The round-trip test omits the planned session-name assertion, and no failure-path test proves that a failed save leaves no partial session. Progress marks the persistence phase complete despite this gap.
- **Fix**: Assert the generated session name and add a deterministic failing-save scenario that verifies no session or tasks remain.
  - Strength: Completes both the shape and atomicity parts of the phase contract.
  - Tradeoff: Failure injection against the real test database needs careful isolation.
  - Confidence: HIGH — neither assertion exists in the persistence test fixture.
  - Blind spot: A suitable deterministic SQL failure mechanism has not yet been selected.
- **Decision**: FIXED

### F5 — Browser tests under-assert planned cross-user and ordering contracts

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Plan Adherence
- **Location**: `src/PlanDeck/Tests/PlanDeck.E2e.Tests/VotingRoomTests.cs:170`
- **Detail**: The reconnect test does not compare revealed values in both browsers, estimate persistence uses one voter instead of the planned two-user flow, and the configuration test checks task visibility but not order.
- **Fix**: Assert both browsers contain the same revealed values, drive the estimate scenario with two users, and compare task-list text in configured order.
  - Strength: Aligns the E2E evidence with the explicit contracts in Phase 3.
  - Tradeoff: Adds browser coordination and runtime to already expensive tests.
  - Confidence: HIGH — the missing assertions are directly visible in the three test bodies.
  - Blind spot: The task-list locator may need a small page-object extension for ordered enumeration.
- **Decision**: FIXED

### F6 — Remote E2E runs can leave persistent test data

- **Severity**: ⚠️ WARNING
- **Impact**: 🔬 HIGH — architectural stakes; think carefully before deciding
- **Dimension**: Safety & Quality
- **Location**: `src/PlanDeck/Tests/PlanDeck.E2e.Tests/AspireAppFixture.cs:26`
- **Detail**: `BaseUrl` explicitly permits test/prod targets, while the E2E scenarios create sessions and tasks without cleanup. A remote run can pollute a shared or production database.
- **Fix A ⭐ Recommended**: Reject production targets and require an explicit non-production E2E opt-in.
  - Strength: Prevents accidental writes to production with a small, centralized guard.
  - Tradeoff: Does not clean data from legitimate shared test environments.
  - Confidence: HIGH — target selection is centralized in `AspireAppFixture`.
  - Blind spot: The repository does not currently expose a canonical environment marker for remote URLs.
- **Fix B**: Track created sessions and delete them during teardown.
  - Strength: Keeps shared test environments clean.
  - Tradeoff: Requires a supported cleanup API and reliable teardown after partial failures.
  - Confidence: MEDIUM — no E2E cleanup path is currently established.
  - Blind spot: Cleanup authorization and behavior on failed test runs are unverified.
- **Decision**: FIXED via Fix A

### F7 — Unplanned bulk scope remains in `.github`

- **Severity**: ⚠️ WARNING
- **Impact**: 🔬 HIGH — architectural stakes; think carefully before deciding
- **Dimension**: Scope Discipline
- **Location**: `src/PlanDeck/.github/.10x-cli-manifest.json:1`
- **Detail**: Phase commits include extensive `.github` toolkit content unrelated to the four test/cookbook phases, increasing review and rollback blast radius.
- **Fix A ⭐ Recommended**: Move the unrelated `.github` changes into a separate change/PR.
  - Strength: Restores scope fidelity and independent reviewability.
  - Tradeoff: Requires history or branch restructuring.
  - Confidence: HIGH — the paths are unrelated to the plan's required files.
  - Blind spot: None significant.
- **Fix B**: Document an approved scope expansion in the plan.
  - Strength: Preserves the existing history.
  - Tradeoff: Accepts a broad, weakly related change boundary.
  - Confidence: MEDIUM — this is a process accommodation rather than technical isolation.
  - Blind spot: Stakeholder approval is not recorded.
- **Decision**: SKIPPED
