<!-- PLAN-REVIEW-REPORT -->
# Plan Review: Reorganize Projects and Project-Owned Sessions

- **Plan**: context/changes/reorganize-project-and-sessions/plan.md
- **Mode**: Deep
- **Date**: 2026-07-22
- **Verdict**: SOUND (after triage fixes; pre-triage: REVISE)
- **Findings**: 0 critical, 2 warnings, 3 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| End-State Alignment | PASS |
| Lean Execution | PASS |
| Architectural Fitness | PASS |
| Blind Spots | WARNING |
| Plan Completeness | WARNING |

## Grounding

10/10 paths ✓, symbols ✓ (`ListSessionsRequest` empty, IDOR in `SessionMemberGrpcService` confirmed, `IProjectAccessResolver`/`SessionAccessResolver` exist, `ProjectRole = Member<Admin<Owner`, `RemoteEnvironment` already in fixture + .runsettings but missing in pipeline), brief↔plan ✓. Cascade claim verified against the full FK graph — no converging cascade paths; Restrict→Cascade is safe. Progress section mechanically valid.

## Findings

### F1 — Phase 3 automated criteria rely on a client test surface that doesn't exist

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Plan Completeness
- **Location**: Phase 3 — Success Criteria 3.2/3.3, Changes item 5
- **Detail**: Criterion 3.2 referenced an "existing client test surface", but no bUnit/component test project exists (only Unit, Integration, E2E). Criterion 3.3 (EN/PL resx parity) had no runnable command.
- **Fix A ⭐ Recommended**: Rewrite 3.2 to target `PlanDeck.Unit.Tests` (testable code-behind/helpers) and 3.3 as a concrete resx-parity NUnit test with a `dotnet test --filter` command.
- **Fix B**: Add a bUnit component test project as a Phase 3 deliverable.
- **Decision**: FIXED (Fix A) — Phase 3 item 5 and criteria 3.2/3.3 rewritten with concrete test files (`LocalizationResourceParityTests`, `SessionRoleUiTests`) and runnable commands; Progress 3.2 title aligned.

### F2 — Existing E2E suite breaks at Phase 3 but is only repaired in Phase 5

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Blind Spots
- **Location**: Phases 3–5 sequencing
- **Detail**: `SessionsPage.cs` navigates `/sessions`; Phase 3 removes the route but page objects are rebuilt only in Phase 5, so E2E `dotnet test` fails for two phases without acknowledgment.
- **Fix**: Note the expected break in Phase 3 + Migration Notes; `[Ignore]` affected E2E tests until Phase 5; exclude E2E from Phase 3/4 gates.
- **Decision**: FIXED — Known-break note added to Phase 3 manual verification and Migration Notes.

### F3 — Room invalidation before SQL commit can strand live rooms

- **Severity**: ℹ️ OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Blind Spots
- **Location**: Phase 2 — Critical Implementation Details
- **Detail**: Ordering Key Vault → invalidate rooms → SQL delete meant a failed SQL delete destroys rooms for still-existing Sessions.
- **Fix**: Invalidate rooms after successful SQL delete (best-effort, idempotent).
- **Decision**: FIXED — Timing & lifecycle, Phase 2 item 3 contract, and Phase 2 item 5 test contract reordered.

### F4 — "Atomic deployment" doesn't cover already-open WASM tabs

- **Severity**: ℹ️ OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Blind Spots
- **Location**: Migration Notes
- **Detail**: Open tabs keep running the cached old WASM client; their empty `ListSessionsRequest` gets `InvalidArgument` until reload.
- **Fix**: One sentence in Migration Notes marking this as expected transient behavior.
- **Decision**: FIXED — Migration Notes updated.

### F5 — Phase 2 orchestration should reuse the Phase 1 repository query

- **Severity**: ℹ️ OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Completeness
- **Location**: Phase 2 — item 2
- **Detail**: "Identifies owned Sessions" without saying how; `ISessionRepository` not listed among Phase 2 files.
- **Fix**: State that `ProjectGrpcService` injects `ISessionRepository` and uses the Phase 1 project-filtered query.
- **Decision**: FIXED — Phase 2 item 2 contract clarified.
