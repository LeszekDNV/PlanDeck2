<!-- PLAN-REVIEW-REPORT -->
# Plan Review: Real-time vote-integrity baseline — hidden/reveal contract + reconnection

- **Plan**: context/changes/realtime-vote-integrity/plan.md
- **Mode**: Deep
- **Date**: 2026-06-22
- **Verdict**: REVISE (→ SOUND after triage)
- **Findings**: 0 critical, 3 warnings, 1 observation

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| End-State Alignment | PASS |
| Lean Execution | PASS |
| Architectural Fitness | WARNING |
| Blind Spots | WARNING |
| Plan Completeness | WARNING |

## Grounding

10/10 paths ✓, symbols verified ✓, brief↔plan ✓. Blast-radius sweep: only the 4 spike files (`PlanningRoomService`, `IPlanningRoomService`, `PlanningRoomHub`, `PlanningRoomClientService`/interface, `PlanningRoomState`) consume the changed symbols. No `lessons.md` or `contract-surfaces.md` present (skipped). Discovered `[Authorize]`/`Context.User` are used nowhere else in the codebase.

## Findings

### F1 — Phase 1 build criterion contradicts the hub compile-break

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Plan Completeness
- **Location**: Phase 1 — Success Criteria / Progress 1.5
- **Detail**: Phase 1 redesigns `IPlanningRoomService` (new `RoomKey`-based signatures), which breaks the hub and client service at compile time. Yet the Phase 1 success criterion asserted "no consumer outside `Planning/` references the old interface" and implied the solution still builds, while the actual call-site re-wiring was only described in Phase 2/3. An implementer running `dotnet build` at the end of Phase 1 would hit a broken build with no planned remedy in-phase.
- **Fix A ⭐ Recommended**: Fold minimal mechanical hub/client re-wiring into Phase 1
  - Strength: Solution compiles at every phase boundary; matches inward-out sequencing; behavior changes still deferred to Phase 2/3.
  - Tradeoff: Phase 1 touches the hub/client files for a no-behavior re-wire (identity stays client-supplied for now).
  - Confidence: HIGH — only 4 known call sites; signatures are explicit in the plan.
  - Blind spot: None significant.
- **Decision**: Fixed via Fix A — added Phase 1 change item #6 "Keep the solution compiling — minimal call-site re-wiring", revised Phase 1 Success Criteria + Implementation Note, updated Progress 1.5.

### F2 — "unauthenticated → 401" criterion unverifiable under always-on test scheme

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Blind Spots
- **Location**: Phase 2 — Manual Verification 2.3 / Phase 3 integration test
- **Detail**: `TestAuthenticationHandler` always returns `AuthenticateResult.Success` (fixed tid/oid) when `Authentication:UseTestScheme=true`, which the test/integration config uses. So both the manual "negotiate returns 401 unauthenticated" criterion and the Phase 3 assertion "an unauthenticated connection is rejected (negotiate 401)" can never be observed — an anonymous principal is unreachable. The criterion would either be silently skipped or block the implementer.
- **Fix A ⭐ Recommended**: Replace the 401 assertion with an `[Authorize]`-attribute/contract check + positive authenticated path
  - Strength: Asserts the auth gate's presence deterministically; positive join path proves the authenticated flow; honest about the test-scheme limitation.
  - Tradeoff: Real anonymous-rejection (OIDC) is verified only at deploy, not in the test suite.
  - Confidence: HIGH — `typeof(PlanningRoomHub)` attribute reflection is stable and scheme-independent.
  - Blind spot: None significant for MVP.
- **Fix B**: Override the host with a no-auth scheme in the test to force a real 401
  - Strength: Exercises the genuine rejection path.
  - Tradeoff: Requires a second WebApplicationFactory configuration / scheme swap; more test plumbing for a prod-only path.
  - Confidence: MEDIUM — interaction with the always-on test handler registration is fiddly.
  - Blind spot: Scheme-override wiring unverified against current `Program.cs`.
- **Decision**: Fixed via Fix A — revised Phase 2 manual bullet, Phase 3 test contract (assert `typeof(PlanningRoomHub)` carries `[Authorize]` + positive join), and Progress 2.3.

### F3 — reveal/reset trigger authorization unconstrained

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Blind Spots
- **Location**: Phase 2 — RevealVotes / ResetRound; What We're NOT Doing
- **Detail**: Any authenticated participant can invoke `RevealVotes`, which exposes every other participant's vote. The plan's core guarantee ("values not observable before reveal") implicitly assumes a controlled reveal, but the plan never states *who* may reveal/reset. Without an explicit boundary, an implementer might either over-build a role gate (scope creep) or ship an open reveal (integrity hole) — and reviewers later can't tell which was intended.
- **Fix A ⭐ Recommended**: Explicitly defer reveal/reset authorization to S-06's role model
  - Strength: Keeps F-02 to the integrity contract (hidden-until-reveal), names the gap, prevents accidental scope creep; S-06 already owns the facilitator/role model.
  - Tradeoff: F-02 ships with reveal callable by any participant (acceptable: rooms are tenant-scoped + authenticated, no UI yet).
  - Confidence: HIGH — aligns with existing scope split (S-06 owns voting screen + roles).
  - Blind spot: None significant.
- **Fix B**: Add a lightweight "facilitator = creator/first-joiner" gate now
  - Strength: Closes the hole immediately.
  - Tradeoff: Introduces a provisional role concept F-02 doesn't otherwise need; likely reworked by S-06.
  - Confidence: MEDIUM — provisional facilitator definition may not match S-06's model.
  - Blind spot: Interaction with S-06's eventual role model unverified.
- **Decision**: Fixed via Fix A — added a "What We're NOT Doing" bullet deferring reveal/reset authorization to S-06.

### F4 — `[Authorize]` on the hub is the first auth gate in the codebase

- **Severity**: 🔭 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Architectural Fitness
- **Location**: Phase 2 — Authorize and re-key the hub
- **Detail**: No `[Authorize]` attribute or `Context.User`-based gate exists anywhere in the codebase today; existing gRPC services and the current hub are anonymous, relying on the always-present principal plus client-side `AuthorizeView`. Adding `[Authorize]` to the hub is the first server-side authorization gate. Worth a one-line note so the implementer scopes it to the hub (not a global fallback policy) and a future reader understands the intent.
- **Fix**: Add a one-line note in Phase 2 that `[Authorize]` is intentionally hub-only, not a global policy, keeping the blast radius to F-02.
- **Decision**: Fixed — added the note under Phase 2 change item #1.
