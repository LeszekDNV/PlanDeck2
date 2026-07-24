<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Lokalne konta użytkowników i logowanie przez Entra ID

- **Plan**: context/changes/create-local-account/plan.md
- **Scope**: Phases 1-7 of 7
- **Date**: 2026-07-25
- **Verdict**: REJECTED
- **Findings**: 1 critical, 4 warnings, 1 observation

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | FAIL |
| Scope Discipline | PASS |
| Safety & Quality | FAIL |
| Architecture | WARNING |
| Pattern Consistency | WARNING |
| Success Criteria | FAIL |

## Verification

- `dotnet build PlanDeck.slnx`: PASS (17 warnings, including vulnerable packages and one nullable warning)
- `dotnet test PlanDeck.slnx --no-build`: PASS (176 unit, 126 integration, 15 E2E; 1 integration test skipped)
- Phase 7 repository scan: FAIL (test-auth configuration, scenario references, and legacy auth routes remain)

## Findings

### F1 — Unrestricted configuration bypasses email confirmation

- **Severity**: ❌ CRITICAL
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: src/PlanDeck/Core/PlanDeck.Infrastructure/Identity/LocalAccountService.cs:41
- **Detail**: `Testing:E2e:AutoConfirmEmail` directly sets `ApplicationUser.EmailConfirmed` and suppresses confirmation mail without checking the host environment. Any deployment that receives this configuration can create fully confirmed accounts without proving mailbox ownership. The E2E fixture enables it and therefore does not exercise the planned public register → mailbox → confirm flow. This contradicts the explicit phase 7 rule that tests use the same public path and read confirmation links from a controlled mailbox.
- **Fix**: Remove `AutoConfirmEmail`; make E2E registration read the confirmation link from MailPit through `EmailInbox`, navigate to it, and only then log in.
  - Strength: Eliminates the production-reachable bypass and makes E2E validate the real security boundary.
  - Tradeoff: E2E setup becomes slower and must reliably correlate uniquely addressed messages.
  - Confidence: HIGH — MailPit and `EmailInbox` already exist in this change.
  - Blind spot: Remote E2E mailbox-provider configuration was not exercised locally.
- **Decision**: ACCEPTED — User accepts the email-confirmation bypass risk and chose not to fix it.

### F2 — Registration reports success after mail delivery failure and exposes internals

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: src/PlanDeck/Core/PlanDeck.Infrastructure/Identity/LocalAccountService.cs:163
- **Detail**: Registration catches every exception and returns `exception.Message` to the client. Confirmation-mail delivery is separately swallowed, logs the raw email address, and registration still returns success. This violates the plan's requirement to surface permanent delivery failure without a success-shaped fallback and the cross-cutting rule against PII in logs.
- **Fix**: Catch only expected persistence/Identity conflicts, map them to stable public codes, let unexpected failures propagate to centralized handling, and return an explicit retryable delivery status without logging the address.
  - Strength: Prevents implementation-detail disclosure and avoids stranding users behind a false success response.
  - Tradeoff: Requires extending the registration result/UI contract and updating tests.
  - Confidence: HIGH — lifecycle operations already expose explicit `SendFailed` results.
  - Blind spot: Operational logging policy may allow hashed or redacted identifiers, but none is implemented here.
- **Decision**: SKIPPED

### F3 — Removed test-auth infrastructure remains in deployment configuration

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Plan Adherence
- **Location**: .azuredevops/pipelines/azure-dev.yml:45
- **Detail**: The pipeline still filters for deleted `E2eScenarioEndpointTests` and sets `Authentication__UseTestScheme=true`; `.env.example`, `.azure/test/.env`, and `.github/copilot-instructions.md` retain test-scheme/scenario-token configuration. Phase 7 explicitly required removing all such flags, parameters, dependent checks, and documentation.
- **Fix**: Remove stale flags and scenario parameters from pipeline/environment files, replace the deleted-test filter with current auth tests, and update repository instructions to state that Testing uses real Identity.
  - Strength: Restores deployment configuration as the source of truth and prevents false confidence in a no-longer-existing auth mode.
  - Tradeoff: Testing deployment now requires valid SMTP and real account setup.
  - Confidence: HIGH — server registration for the old scheme has already been deleted.
  - Blind spot: External Azure DevOps variable groups were not inspected.
- **Decision**: FIXED

### F4 — Legacy GET auth routes and test-scheme logout fallback remain

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Plan Adherence
- **Location**: src/PlanDeck/Web/PlanDeck.Client/Services/AccountClientService.cs:66
- **Detail**: The client retains a broad-catch fallback to GET `auth/logout`, with comments and branching for deterministic test identities, while `Program.cs` still maps GET `/auth/login` and `/auth/logout` and several pages navigate to `/auth/login`. Phase 2 required logout mutation via antiforgery-protected POST, and phases 5/7 required eliminating old routes and test-auth fallbacks.
- **Fix**: Remove the member logout fallback and stale test-scheme logic, route account login directly to `/account/login`, and move guest sign-out to a dedicated protected POST endpoint.
  - Strength: Produces one explicit authentication lifecycle per identity type and removes broad exception-driven control flow.
  - Tradeoff: Guest UI/tests must submit antiforgery-aware POST logout.
  - Confidence: HIGH — `/account/logout` already implements the intended member path.
  - Blind spot: Guest-cookie CSRF impact is limited but was not threat-modeled separately.
- **Decision**: FIXED

### F5 — Newly introduced mail dependencies have known vulnerabilities

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: src/PlanDeck/Core/PlanDeck.Infrastructure/PlanDeck.Infrastructure.csproj:11
- **Detail**: The build reports moderate-severity advisories GHSA-9j88-vvj5-vhgr for MailKit 4.11.0 and GHSA-g7hc-96xr-gvvx for transitive MimeKit 4.11.0. SMTP processing is part of the new account security boundary.
- **Fix**: Upgrade MailKit to a supported non-vulnerable version that also resolves the transitive MimeKit advisory, then rerun mail adapter tests.
- **Decision**: FIXED

### F6 — Changed client code introduces a nullable warning

- **Severity**: 👁 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: src/PlanDeck/Web/PlanDeck.Client/Services/AccountClientService.cs:78
- **Detail**: The full build emits CS8604 because `result.Errors` may be null before `FirstOrDefault()`. Nullable is enabled repository-wide, so changed code should preserve a warning-clean contract even though compilation succeeds.
- **Fix**: Remove the obsolete fallback with F4; otherwise use a null-safe pattern over `result.Errors`.
- **Decision**: FIXED via F4

## Triage Summary

- **Fixed**: F3, F4, F5, F6
- **Accepted**: F1 — email-confirmation bypass risk accepted by the user
- **Skipped**: F2
- **Post-triage verification**:
  - `dotnet build PlanDeck.slnx`: PASS (0 warnings, 0 errors)
  - `dotnet test PlanDeck.slnx --no-build`: PASS (176 unit, 126 integration, 15 E2E; 1 integration test skipped)
  - `dotnet list Core\PlanDeck.Infrastructure\PlanDeck.Infrastructure.csproj package --vulnerable --include-transitive`: PASS (no vulnerable packages)
