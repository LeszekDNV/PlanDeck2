<!-- PLAN-REVIEW-REPORT -->
# Plan Review: Real-time voting round — hidden vote, reveal, manual pick

- **Plan**: context/changes/realtime-voting-round/plan.md
- **Mode**: Deep
- **Date**: 2026-06-22
- **Verdict**: REVISE → SOUND (after triage)
- **Findings**: 0 critical, 3 warnings, 2 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| End-State Alignment | PASS |
| Lean Execution | PASS |
| Architectural Fitness | WARNING |
| Blind Spots | WARNING |
| Plan Completeness | WARNING |

## Grounding

Grounding: 12/12 paths ✓, symbols ✓ (email claim resolution, repo DI registrations), brief↔plan ✓. Progress↔Phase consistency ✓ (P1: 1.1–1.5, P2: 2.1–2.2, P3: 3.1–3.4, P4: 4.1–4.6, P5: 5.1–5.3; no stray checkboxes in phase blocks).

## Findings

### F1 — Join gate keys off the `email` claim only

- **Severity**: ⚠️ WARNING
- **Impact**: 🔬 HIGH — architectural stakes; think carefully before deciding
- **Dimension**: Blind Spots
- **Location**: Critical Implementation Details (auth gate) + Phase 3 hub contract
- **Detail**: The authorization gate resolves the caller's `email` claim only. The codebase already resolves email as `email ?? preferred_username` (`HttpContextCurrentUserContext.cs:20`), and the real OIDC scheme runs `MapInboundClaims=false` (`ServiceCollectionExtensions.cs:99`). Entra often emits `preferred_username` rather than `email`, so keying off `email` alone would lock out every assigned member on real auth — and the test scheme emits `email`, so tests would not catch it.
- **Fix**: Resolve the caller's email in the gate with the same `email ?? preferred_username` fallback (mirroring `HttpContextCurrentUserContext.Email`); note the rationale in Phase 3.
- **Decision**: FIXED — applied to Critical Implementation Details + Phase 3 hub contract.

### F2 — Repositories injected directly into the Server hub

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Architectural Fitness
- **Location**: Phase 3 — Application service + hub
- **Detail**: The plan injected `ISessionMemberRepository`/`ISessionRepository` directly into `PlanningRoomHub` (Server) for auth + seeding while routing persistence through a new `VotingRoundService`. No existing hub/controller injects repositories — repos are consumed only by Application gRPC services. This is inconsistent and violates "business logic out of `PlanDeck.Server`".
- **Fix**: Route ALL data access (membership check + room seed load + estimate write) through the application service (`VotingRoundService`); the hub injects only application services, never repositories.
- **Decision**: FIXED — VotingRoundService now owns `IsAssignedMemberAsync` + `LoadRoomSeedAsync` + `SelectEstimateAsync`; hub injects `IVotingRoundService` only.

### F3 — End state promises a persistence test no phase adds

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Completeness
- **Location**: Desired End State + Phase 1 success criteria
- **Detail**: Desired End State promises "a gRPC/persistence test for the estimate write," but no phase adds it — Phase 1 covered only build/migration/existing tests, and the estimate write was only transitively exercised by the Phase 3 hub test.
- **Fix**: Add an explicit Phase 1 automated test for `SetAgreedEstimateAsync` (write + re-read; cross-tenant returns false) with a matching `## Progress` row.
- **Decision**: FIXED — added Phase 1 change #5 + automated criterion + Progress row 1.4 (manual shifted to 1.5).

### F4 — Empty/non-Active/direct-URL voting state unspecified

- **Severity**: ⓘ OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Blind Spots
- **Location**: Phase 4 — VotingRoom.razor
- **Detail**: The route `/voting/{sessionId}` is directly reachable, but behavior for a not-found session, a non-Active/Draft session, a hub-rejected caller, or a session with no tasks was unspecified.
- **Fix**: Add safe empty/locked states to Phase 4 (localized "not available for voting" + back link; empty-state placeholder for no tasks; hub gate remains the trust boundary, client check is UX only).
- **Decision**: FIXED — added to Phase 4 VotingRoom.razor contract.

### F5 — Phase 2 understates blast radius

- **Severity**: ⓘ OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Completeness
- **Location**: Phase 2
- **Detail**: Extending `PlanningRoomState`/`Join` touches the construction site in `ToState()` plus 20+ existing F-02 Join calls/assertions in `PlanningRoomServiceTests.cs`. No SignalR contract-break risk (client + server ship as one hosted-WASM unit), but the migration of existing call sites was not noted.
- **Fix**: Note in Phase 2 that all existing F-02 Join call sites/assertions are migrated in-phase, and that single-unit deploy means no version skew.
- **Decision**: FIXED — added blast-radius note to Phase 2 unit-tests contract.
