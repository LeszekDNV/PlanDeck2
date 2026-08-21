<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Microsoft Login Error Implementation Plan

- **Plan**: `context/changes/microsoft-login-error/plan.md`
- **Scope**: All 3 phases
- **Date**: 2026-07-29
- **Verdict**: APPROVED
- **Findings**: 0 critical, 1 warning, 3 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | PASS |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | PASS |
| Success Criteria | PASS |

## Findings

### F1 — A failed capability request remains cached for the client scope

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: `src/PlanDeck/Web/PlanDeck.Client/Services/AccountClientService.cs:131`
- **Detail**: The first capability task is cached before it completes. If the gRPC call faults or is canceled, the failed task remains cached and all later account pages keep Microsoft actions hidden for the lifetime of the Blazor client scope, even after a transient failure has recovered.
- **Fix**: Clear the cached in-flight task after failure or cancellation and retain the cache only after a successful reply.
- **Decision**: FIXED — failed and canceled capability requests now clear the cached in-flight task.

### F2 — Configured route tests do not exercise the OIDC challenge

- **Severity**: 🔎 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Success Criteria
- **Location**: `src/PlanDeck/Tests/PlanDeck.Integration.Tests/Account/EntraEndpointAvailabilityTests.cs:22`
- **Detail**: The configured-host test inspects endpoint metadata but does not resolve the OIDC scheme or invoke login/register without following redirects. A future route/scheme registration drift could therefore pass this test.
- **Fix**: Assert OIDC scheme registration and exercise configured login/register redirect responses while retaining the existing link-route authorization assertion.
- **Decision**: FIXED — configured-host coverage now resolves the OIDC scheme, executes login/register challenges without following redirects, and retains the link-route authorization assertion.

### F3 — Response-started behavior lacks a focused regression test

- **Severity**: 🔎 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Success Criteria
- **Location**: `src/PlanDeck/Tests/PlanDeck.Integration.Tests/ErrorHandling/GlobalExceptionHandlerTests.cs:23`
- **Detail**: The handler correctly returns `false` when the response has started, but no test locks that contract or proves it avoids rewriting and duplicate handler logging.
- **Fix**: Add a focused handler-level test for an already-started response.
- **Decision**: FIXED — a focused handler-level test now proves that an already-started response is not rewritten, logged, or passed to Problem Details.

### F4 — UI capability bindings rely on manual verification

- **Severity**: 🔎 OBSERVATION
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Success Criteria
- **Location**: `src/PlanDeck/Web/PlanDeck.Client/Pages/Account/Login.razor:40`
- **Detail**: Login, registration, and security visibility were manually confirmed, but no automated component or browser test can detect a future incorrect `ShowEntra` or link-form binding.
- **Fix**: Add focused component coverage if a supported component runner is introduced, or a local-only Playwright scenario using the existing Aspire fixture.
- **Decision**: FIXED — a local-only Playwright scenario now verifies available-state Microsoft actions on login, registration, and account security, including the link form.

## Verification Evidence

- Phase 1 configuration tests: 4 passed.
- Phase 1 route tests: 3 passed.
- Phase 1 server build: passed.
- Phase 2 capability tests: 2 passed.
- Phase 2 account regressions: 39 passed.
- Phase 2 client build: passed.
- Phase 3 exception tests: 2 passed.
- Full integration suite: 135 passed, 1 unrelated vault test skipped.
- Whole solution build: passed.
- All manual Progress rows were explicitly confirmed during implementation.

## Triage Summary

- **Fixed**: F1, F2, F3, F4.
- Entra endpoint and exception-handler regression tests: 6 passed.
- Microsoft authentication availability E2E scenario: 1 passed.
- Deliberate-break check: hiding the login-page Microsoft action caused the E2E scenario to fail on its risk-tied assertion; the break was reverted.
- Whole solution build after triage: passed.
