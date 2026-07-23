<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Reorganize Projects and Project-Owned Sessions

- **Plan**: context/changes/reorganize-project-and-sessions/plan.md
- **Scope**: Phase 5 of 5
- **Date**: 2026-07-23
- **Verdict**: NEEDS ATTENTION
- **Findings**: 0 critical, 9 warnings, 1 observation

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | FAIL |
| Scope Discipline | PASS |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | WARNING |
| Success Criteria | FAIL |

## Verification

| Criterion | Result |
|-----------|--------|
| `dotnet test Tests\PlanDeck.Unit.Tests\PlanDeck.Unit.Tests.csproj` | PASS — 167 passed |
| `dotnet test Tests\PlanDeck.Integration.Tests\PlanDeck.Integration.Tests.csproj` | PASS — 97 passed, 1 skipped |
| `dotnet test Tests\PlanDeck.E2e.Tests\PlanDeck.E2e.Tests.csproj` | PASS — 15 passed |
| Protected remote Test E2E | NOT VERIFIED — no `BaseUrl` or test token available |
| `dotnet build PlanDeck.slnx` | PASS — 0 warnings, 0 errors |

## Findings

### F1 — Role-matrix E2E covers only Session creation

- **Severity**: ⚠️ WARNING
- **Impact**: 🔬 HIGH — architectural stakes; think carefully before deciding
- **Dimension**: Plan Adherence
- **Location**: src/PlanDeck/Tests/PlanDeck.E2e.Tests/SessionAuthorizationMatrixTests.cs:15
- **Detail**: The plan requires Owner/Admin/Member coverage for twelve Session and participant mutations, persistence checks after reload, and a forced Member denial proving unchanged state. The implementation checks Owner/Admin Session creation and hides only three Member controls. Configuration, task operations, ADO import/write-back, activation, participant operations, deletion, reload assertions, and forced denial are missing. `SessionsTests` and `SessionMembersTests` also do not use the planned seeded `runId` lifecycle.
- **Fix A ⭐ Recommended**: Complete the parameterized role matrix using seeded scenarios, fresh role contexts, reload assertions, forced Member requests, and `finally` cleanup.
  - Strength: Implements the approved contract and closes the largest authorization regression gap.
  - Tradeoff: Substantial E2E work across several mutation paths and test-data capabilities.
  - Confidence: HIGH — the missing cases are enumerated explicitly in Phase 5.
  - Blind spot: Some ADO cases may require additional deterministic fake data in the scenario harness.
- **Fix B**: Narrow the plan and rename the test to document that browser coverage is only a UI smoke test, relying on unit/integration tests for the complete authorization matrix.
  - Strength: Keeps the E2E suite smaller and faster.
  - Tradeoff: Abandons the approved browser-level persistence and forced-denial guarantees.
  - Confidence: MEDIUM — existing lower-level tests cover much of the service authorization, but not all browser wiring.
  - Blind spot: A full operation-by-operation lower-level coverage audit has not been completed.
- **Decision**: FIXED via Fix B — narrowed Phase 5 to a browser smoke test and renamed the test accordingly.

### F2 — Test authentication grants identities without validating the scenario token

- **Severity**: ⚠️ WARNING
- **Impact**: 🔬 HIGH — architectural stakes; think carefully before deciding
- **Dimension**: Safety & Quality
- **Location**: src/PlanDeck/Web/PlanDeck.Server/Identity/TestAuthenticationHandler.cs:44
- **Detail**: In `Testing`, any request can become Owner by omitting `e2e-user`, select another deterministic identity with an unsigned cookie, or become a guest with an unprotected header/cookie. `E2E_SCENARIO_TOKEN` protects only scenario endpoints, not the application, gRPC, or SignalR authentication boundary. Safety therefore depends entirely on an external deployment restriction that is not enforced or evidenced in this change.
- **Fix A ⭐ Recommended**: Add a token-validated bootstrap that issues a short-lived protected test-auth cookie, require it for every deterministic identity, and remove the implicit Owner default.
  - Strength: Makes the application fail closed even if the Testing endpoint is accidentally exposed.
  - Tradeoff: Requires coordinated fixture, handler, and deployment changes.
  - Confidence: HIGH — the current handler performs no integrity or token check.
  - Blind spot: The exact production-like cookie protection mechanism needs design against the remote test topology.
- **Fix B**: Keep the handler but enforce and document private ingress/access control for the entire Testing deployment, and at minimum remove implicit Owner authentication.
  - Strength: Avoids coupling the browser auth flow to the scenario token.
  - Tradeoff: A perimeter misconfiguration still exposes a complete authentication bypass.
  - Confidence: MEDIUM — no verifiable ingress policy is present in this repository.
  - Blind spot: External platform configuration may already provide protection, but it was not available for review.
- **Decision**: SKIPPED

### F3 — CI runs local E2E unconditionally and remote E2E incompletely

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Success Criteria
- **Location**: .azuredevops/pipelines/azure-dev.yml:45
- **Detail**: `dotnet test PlanDeck.slnx` includes the E2E project even when `runE2E` is false, causing CI to attempt local Aspire/Podman execution. When enabled, the remote E2E project runs a second time, Chromium is not installed explicitly, its TRX is not written to the published results directory, and the pipeline deploys to `plandeck-dev` after the test stage rather than testing a dedicated deployed `Testing` target.
- **Fix**: Run unit/integration projects explicitly in the normal test step, install Chromium, deploy or target the protected Testing environment before the gated E2E step, and emit its TRX into `$(Agent.TempDirectory)\test-results`.
  - Strength: Makes `runE2E` meaningful and aligns execution, deployment order, and result publication with the plan.
  - Tradeoff: Requires restructuring the pipeline stages and environment dependencies.
  - Confidence: HIGH — the current commands and stage order are explicit in the YAML.
  - Blind spot: The organization may provide a pre-existing external Testing deployment not represented in this repository.
- **Decision**: FIXED — normal CI now runs only unit/integration tests; gated remote E2E installs Chromium and publishes TRX results. The supplied BaseUrl remains the protected Testing target.

### F4 — Project deletion E2E does not prove the complete contract

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Plan Adherence
- **Location**: src/PlanDeck/Tests/PlanDeck.E2e.Tests/ProjectsTests.cs:15
- **Detail**: The test deletes a Project with one Session/task and confirms both routes are unavailable, but it never asserts the destructive warning text, participant/other child deletion, voting-room invalidation, or that the preserved shared Team remains usable. It checks only that the Team button remains visible and leaves that Team behind.
- **Fix**: Seed the complete owned graph, assert the warning text, exercise the active room, verify child and room unavailability, perform a real operation with the preserved Team, and clean up the Team.
  - Strength: Directly proves the approved destructive workflow and avoids residual E2E data.
  - Tradeoff: Adds setup and cross-page assertions to one test.
  - Confidence: HIGH — each missing assertion is named in the Phase 5 contract.
  - Blind spot: Participant deletion may need an observable test-only query or user-facing route.
- **Decision**: FIXED — the test now seeds memberships and child data, verifies the warning and invalidated room, proves the shared Team remains usable, and cleans the seeded Project graph.

### F5 — Successful guest join route is not tested

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Plan Adherence
- **Location**: src/PlanDeck/Tests/PlanDeck.E2e.Tests/GuestVotingTests.cs:16
- **Detail**: The successful guest test creates a guest context already scoped to `sessionId` and navigates directly to `/voting/{sessionId}`. The only `/join/{code}` test uses an invalid code, so the required successful `/join/{code}` to `/voting/{sessionId}` regression remains unverified.
- **Fix**: Obtain the active Session join code, enter a guest name through `/join/{code}`, and assert navigation plus voting behavior in the resulting guest context.
  - Strength: Covers the exact route transition the plan promises to preserve.
  - Tradeoff: The page object or scenario response may need to expose the join code.
  - Confidence: HIGH — the successful test bypasses the join route entirely.
  - Blind spot: The current scenario API response shape was not designed around this flow.
- **Decision**: FIXED — added a deterministic active-session test for `/join/{code}` to `/voting/{sessionId}` with scenario cleanup.

### F6 — Project deletion can leave a live Project without its ADO secret

- **Severity**: ⚠️ WARNING
- **Impact**: 🔬 HIGH — architectural stakes; think carefully before deciding
- **Dimension**: Safety & Quality
- **Location**: src/PlanDeck/Core/PlanDeck.Application/Services/ProjectGrpcService.cs:206
- **Detail**: The approved sequence soft-deletes the Key Vault secret before SQL deletion. If `repository.DeleteAsync` then fails, the Project graph remains while its ADO credential is gone. The plan intentionally rejects a cross-resource transaction but does not provide compensation for this second-step failure.
- **Fix A ⭐ Recommended**: Add a compensating recovery path that restores the soft-deleted secret when SQL deletion fails, and test both recovery success and failure.
  - Strength: Preserves the requirement that secret-cleanup failure blocks SQL deletion while repairing the opposite partial-failure case.
  - Tradeoff: Extends the secret-store abstraction and introduces another external operation that can fail.
  - Confidence: MEDIUM — Azure Key Vault supports recoverable soft deletion, but repository abstractions and retention settings need confirmation.
  - Blind spot: Recovery is impossible after purge or if vault soft-delete retention is misconfigured.
- **Fix B**: Commit SQL deletion first and move secret cleanup to a retryable outbox/job.
  - Strength: Keeps the relational graph authoritative and makes external cleanup retryable.
  - Tradeoff: Changes the approved guarantee because a failed secret cleanup would no longer block Project deletion.
  - Confidence: MEDIUM — operationally robust, but it requires a product decision and new infrastructure.
  - Blind spot: Secret retention duration and compliance requirements are unknown.
- **Decision**: FIXED via Fix A — SQL persistence failure now recovers the soft-deleted Key Vault secret, with success and recovery-failure tests.

### F7 — Completed Progress entries lack observable evidence

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Success Criteria
- **Location**: context/changes/reorganize-project-and-sessions/plan.md:641
- **Detail**: Phase 5 automated and manual entries are marked complete without commit SHAs despite the plan convention, the protected remote E2E run cannot be reproduced from available inputs, and the reviewed E2E tests do not demonstrate the claimed real EN/PL role workflows. Earlier phases also retain unchecked manual criteria while the change was marked implemented.
- **Fix**: Reset unsupported checkboxes to pending and append commit/run evidence only after each criterion is demonstrably completed.
- **Decision**: FIXED — locally verified checks now reference this review; remote and manual checks were reset to pending.

### F8 — E2E locators violate the phase's locator rules

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: src/PlanDeck/Tests/PlanDeck.E2e.Tests/SessionsTests.cs:50
- **Detail**: `Locator("strong")` is a CSS/structural selector explicitly prohibited by the plan. Positional `.First`/`.Last` choices in page objects also select by structure where a stable accessible name or a justified test ID should identify the intended element.
- **Fix**: Replace the CSS and positional locators with exact role/label/text locators, using a dedicated test ID only where accessibility attributes are genuinely ambiguous.
- **Decision**: FIXED — removed CSS and positional locators, using accessible locators or dedicated test IDs for ambiguous repeated rows.

### F9 — Project deletion loads full task graphs only to collect Session IDs

- **Severity**: 🔎 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: src/PlanDeck/Core/PlanDeck.Infrastructure/Persistence/SessionRepository.cs:18
- **Detail**: `ProjectGrpcService` needs only Session IDs before deletion, but `GetSessionsAsync` includes every Session task and materializes complete aggregates. Deletion cost therefore scales with all task data in the Project.
- **Fix**: Add a project-scoped IDs-only query or projection for deletion orchestration while retaining the aggregate query for UI listing.
- **Decision**: FIXED — project deletion now uses an IDs-only repository projection without loading task aggregates.

### F10 — Mobile navigation and Session headers overflow the viewport

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — existing mobile usability regressed
- **Dimension**: Success Criteria
- **Location**: src/PlanDeck/Web/PlanDeck.Client/Layout/MainLayout.razor; src/PlanDeck/Web/PlanDeck.Client/Pages/Sessions.razor
- **Detail**: At 375×812, the top navigation is compressed and clipped, the logout/status area is cut off, and long Project or Session names overflow instead of wrapping or truncating safely. Manual criterion 3.7 therefore fails, and criterion 5.7 remains incomplete.
- **Fix**: Introduce a responsive mobile navigation arrangement and constrain long labels/status content so all primary actions remain reachable without horizontal clipping.
- **Decision**: SKIPPED — mobile layout remains a known usability regression; criteria 3.7 and 5.7 stay incomplete.

## Local Manual Verification

Executed against local Aspire on 2026-07-23 with deterministic Owner, Admin, Member, and guest identities.

| Criterion | Result | Evidence |
|-----------|--------|----------|
| 1.4 | PASS | Member listed Sessions and voted; Session administration controls were absent. |
| 1.5 | PASS | Unknown and inaccessible Projects presented the same localized not-found/no-access state. |
| 2.4 | PASS | The single dialog enumerated permanent deletion of the Project-owned graph. |
| 2.5 | PARTIAL | Deletion invalidated the active Voting Room and preserved the shared Team; an inactive Session was not included in the manual fixture. |
| 3.4 | PASS | `/` redirected to `/projects`; Project dashboard and project-scoped Sessions required no Project selector. |
| 3.5 | PASS | `/sessions` displayed the normal not-found page. |
| 3.6 | PASS | Owner/Admin/Member controls matched their roles; authenticated return navigation and guest voting deep links worked. The successful join-code transition is additionally covered by E2E. |
| 3.7 | FAIL | At 375×812, navigation and long Project/Session labels were clipped or overflowed. See F10. |
| 4.4 | PASS | Three isolated browser contexts were simultaneously recognized as Owner, Admin, and Member. |
| 4.5 | NOT VERIFIED | Requires the protected remote Testing deployment and credentials. |
| 4.6 | NOT VERIFIED | Local Testing-mode execution cannot establish Production endpoint absence. |
| 5.6 | PARTIAL | Main Project and Session views were exercised in EN/PL, but every real role was not repeated in both languages. |
| 5.7 | FAIL | Routing, cascade deletion, direct links, and guest voting passed; mobile layout failed. |
| 5.8 | NOT VERIFIED | Requires an actual pipeline execution with layer-specific failures/results. |

Manual test data was removed through the UI and idempotent scenario cleanup. The local-only concealment Project was also deleted.

## Triage Summary

- **Fixed**: F1 (Fix B), F3, F4, F5, F6 (Fix A), F7, F8, F9 — 8
- **Skipped**: F2, F10 — 2
- **Accepted as rule**: none
