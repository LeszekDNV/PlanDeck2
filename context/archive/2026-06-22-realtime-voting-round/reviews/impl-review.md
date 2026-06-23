<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Real-time Voting Round (S-06)

- **Plan**: context/changes/realtime-voting-round/plan.md
- **Scope**: Phases 1–5 of 5 (full plan)
- **Date**: 2026-06-23
- **Verdict**: NEEDS ATTENTION (no blockers; review fixes applied during triage)
- **Findings**: 0 critical · 2 warnings · 4 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | PASS |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | PASS |
| Success Criteria | PASS |

Automated success criteria re-verified post-fix: `dotnet build PlanDeck.slnx` (0/0), Unit 47/47, Integration 30/30 (hub + persistence), E2E VotingRoom 1/1.

## Findings

### F1 — SelectEstimate persists an unvalidated estimate value

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality / Data safety
- **Location**: PlanningRoomHub.cs:78-92, VotingRoundService.cs
- **Detail**: CastVote validates the vote ∈ scale, but SelectEstimate persisted any string (≤32 chars) as AgreedEstimate without checking ScaleValues. Only an authorized member can do this — data-quality gap, not a security hole.
- **Fix**: Added `IPlanningRoomService.IsValidEstimate(key, estimate)` (null allowed for clear; non-null must be in the seeded scale, fail-closed on unseeded). Hub `SelectEstimate` rejects with HubException before persisting.
- **Decision**: FIXED via Fix now

### F2 — Test auth scheme is config-gated but not environment-gated

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; production hardening
- **Dimension**: Safety & Quality
- **Location**: ServiceCollectionExtensions.cs:52-67, TestAuthenticationHandler.cs
- **Detail**: AddExternalServices enabled the deterministic TestAuthenticationHandler purely on the `Authentication:UseTestScheme` config flag, with no environment guard — a stray production appsettings could log in fixed test identities.
- **Fix**: `AddExternalServices` now takes `IHostEnvironment` and throws at startup when `UseTestScheme` is true outside the Development or Testing environments (fail-fast). Program.cs passes `builder.Environment`. Testing (hub integration tests) and Development (E2E) remain permitted.
- **Decision**: FIXED via Fix now

### F3 — ResetRound mutates in-memory state before persisting the clear

- **Severity**: ℹ️ OBSERVATION
- **Impact**: 🏃 LOW
- **Dimension**: Reliability
- **Location**: PlanningRoomHub.cs:52-69
- **Detail**: SelectEstimate persists-then-broadcasts (plan's atomicity rule); ResetRound did the reverse (memory-first, then persist, throw on failure), so a DB failure could desync memory and DB (estimate reappears on reload).
- **Fix**: Reordered to read the active task via GetState, persist the null clear first, then reset in-memory state and broadcast — consistent with SelectEstimate.
- **Decision**: FIXED via Fix now

### F4 — LeaveRoom is not behind the authorization gate

- **Severity**: ℹ️ OBSERVATION
- **Impact**: 🏃 LOW
- **Dimension**: Safety
- **Location**: PlanningRoomHub.cs:30-36
- **Detail**: LeaveRoom skips AuthorizeAsync, but is connection-scoped by deliberate F-02 design (commit f431c5d): it removes only the caller's own connection and broadcasts to a group the caller isn't in — no data leak. Pre-existing F-02 surface, not introduced by S-06.
- **Fix**: N/A
- **Decision**: SKIPPED (deliberate F-02 design)

### F5 — In-memory rooms/participants are never evicted (no TTL)

- **Severity**: ℹ️ OBSERVATION
- **Impact**: 🏃 LOW
- **Dimension**: Reliability / Performance
- **Location**: PlanningRoomService.cs (in-memory room model)
- **Detail**: Disconnected participants and empty rooms accumulate without eviction. The plan explicitly defers scale/multi-replica concerns as out of scope for the MVP (Performance Considerations). Pre-existing F-02 design.
- **Fix**: N/A
- **Decision**: SKIPPED (deferred beyond MVP per plan)

### F6 — JoinRoom loads the session twice per join

- **Severity**: ℹ️ OBSERVATION
- **Impact**: 🏃 LOW
- **Dimension**: Performance
- **Location**: PlanningRoomHub.cs (JoinRoom), VotingRoundService.cs
- **Detail**: AuthorizeAsync → IsAuthorizedParticipantAsync loaded the session, then LoadRoomSeedAsync loaded it again on every JoinRoom.
- **Fix**: Replaced `LoadRoomSeedAsync` with `AuthorizeAndLoadSeedAsync` (single session load shared by authorization + seeding via a private `IsAuthorizedAsync(session, …)` helper). JoinRoom now hits the DB once; null result = missing session or non-member → HubException.
- **Decision**: FIXED via Fix now
