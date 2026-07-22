<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Secure Project-Scoped Azure DevOps Endpoints Implementation Plan

- **Plan**: `context/changes/secure-ado-grpc-endpoints/plan.md`
- **Scope**: Full plan (Phases 1-6)
- **Date**: 2026-07-22
- **Verdict**: REJECTED
- **Findings**: 3 critical, 3 warnings, 1 observation

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | FAIL |
| Scope Discipline | WARNING |
| Safety & Quality | FAIL |
| Architecture | FAIL |
| Pattern Consistency | WARNING |
| Success Criteria | WARNING |

## Findings

### F1 — Session authorization is not project-scoped end-to-end

- **Severity**: ❌ CRITICAL
- **Impact**: 🔬 HIGH — architectural stakes; think carefully before deciding
- **Dimension**: Safety & Quality
- **Location**: `src/PlanDeck/Core/PlanDeck.Application/Services/SessionGrpcService.cs:25-84,105-111,133-217,280-454`; `src/PlanDeck/Core/PlanDeck.Infrastructure/Persistence/SessionRepository.cs:18-32`
- **Detail**: Phase 4 expected membership-based project/session authorization via resolvers. Current `SessionGrpcService` rejects guests but does not require `IProjectAccessResolver`/`ISessionAccessResolver` checks for member flows, and repository listing remains tenant-wide.
- **Fix**: Enforce resolver-based authorization for each session read/write path before data access.
  - Strength: Reuses abstractions already introduced in the same change (`SessionAccessResolver`, `ProjectAccessResolver`) and aligns with phase intent.
  - Tradeoff: Requires touching several service entry points and tests.
  - Confidence: HIGH — plan and existing abstractions clearly require this direction.
  - Blind spot: Full blast radius on UI flows was not replayed after tightening checks.
- **Decision**: FIXED

### F2 — Generic task APIs still trust client-supplied ADO metadata

- **Severity**: ❌ CRITICAL
- **Impact**: 🔬 HIGH — architectural stakes; think carefully before deciding
- **Dimension**: Plan Adherence
- **Location**: `src/PlanDeck/Core/PlanDeck.Core.Shared/Contracts/ISessionService.cs:135-157`; `src/PlanDeck/Core/PlanDeck.Application/Services/SessionGrpcService.cs:48-57,197-206,595-613`
- **Detail**: Phase 4 required server-authoritative persistence where browser sends only selected work-item IDs. `NewSessionTaskDto` still exposes ADO metadata fields and `MapNewTask` persists them directly in generic add/create APIs.
- **Fix A ⭐ Recommended**: Split DTOs/operations so generic create/add accept only ad-hoc task fields, while ADO task persistence can only happen through server re-fetch-by-ID operations.
  - Strength: Fully matches the approved boundary and removes metadata forgery class at the contract level.
  - Tradeoff: Contract migration touches client wrappers and tests.
  - Confidence: HIGH — this is explicitly required by Phase 4 contract text.
  - Blind spot: Existing clients relying on legacy fields need coordinated update.
- **Fix B**: Keep DTO shape but hard-ignore all incoming ADO fields in service layer and fail when present.
  - Strength: Smaller immediate diff.
  - Tradeoff: Leaves risky surface in shared contracts and invites future regressions.
  - Confidence: MEDIUM — behavior is safer, but interface remains misleading.
  - Blind spot: Future contributors may re-enable field usage accidentally.
- **Decision**: FIXED (Fix A)

### F3 — ADO target-lock transition exists but is never executed

- **Severity**: ❌ CRITICAL
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Architecture
- **Location**: `src/PlanDeck/Core/PlanDeck.Application/Abstractions/IProjectAzureDevOpsConnectionRepository.cs`; `src/PlanDeck/Core/PlanDeck.Infrastructure/Persistence/ProjectAzureDevOpsConnectionRepository.cs`; no call site in application services
- **Detail**: Plan state sequencing requires locking organization/project target on first persisted import in the same transaction. `LockTargetAsync` exists but is not called by import/write-back flows.
- **Fix**: Invoke `LockTargetAsync(projectId)` during the first successful persisted imported-task transaction.
  - Strength: Satisfies immutability guarantee without redesigning model.
  - Tradeoff: Requires careful transaction boundaries around import persistence.
  - Confidence: HIGH — the missing link is concrete and isolated.
  - Blind spot: Race behavior under concurrent first-import attempts needs explicit test coverage.
- **Decision**: FIXED

### F4 — Upstream error details risk leaking raw provider content

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: `src/PlanDeck/Core/PlanDeck.Infrastructure/AzureDevOps/AzureDevOpsWorkItemClient.cs`; `src/PlanDeck/Core/PlanDeck.Application/Services/SessionGrpcService.cs`
- **Detail**: `SendAsync` includes upstream response body in thrown exceptions, and downstream mapping can forward exception messages into gRPC details. This conflicts with the sanitized-error requirement.
- **Fix**: Replace raw-body exception text with categorized, sanitized messages and keep raw payload out of user-facing errors.
  - Strength: Aligns with security guardrail and keeps operational classification.
  - Tradeoff: Slightly weaker diagnostics unless server-side structured logs are expanded.
  - Confidence: HIGH — implementation path is straightforward.
  - Blind spot: Current log fields were not audited end-to-end for equivalent context.
- **Decision**: FIXED (Fix A)

### F5 — Deployment runbook still references retired global PAT config

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Scope Discipline
- **Location**: `src/PlanDeck/AZURE_DEPLOYMENT.md:67`
- **Detail**: Documentation still instructs storing `AzureDevOps__PersonalAccessToken`, which contradicts “do not retain global PAT configuration.”
- **Fix**: Remove that variable from deployment guidance and point to project-owned Key Vault connection flow only.
- **Decision**: FIXED

### F6 — Automated evidence for real-vault gate is environment-fragile

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Success Criteria
- **Location**: `src/PlanDeck/Tests/PlanDeck.Integration.Tests/AzureDevOps/RealKeyVaultProjectSecretStoreTests.cs`; `src/PlanDeck/Aspire/PlanDeck.AppHost/AppHost.cs`; `src/PlanDeck/azure.yaml`
- **Detail**: Full-suite run showed the real-vault integration test skipped in this environment; infra preview also required placeholder Entra inputs to execute non-interactively. Checkboxes are complete, but CI-grade reproducibility for this gate is not yet deterministic.
- **Fix A ⭐ Recommended**: Add explicit preflight checks/docs and a dedicated pipeline/test profile that provides required Entra params and Azure context for real-vault verification.
  - Strength: Makes pass/fail deterministic and auditable.
  - Tradeoff: Adds environment orchestration complexity.
  - Confidence: MEDIUM — direction is clear, exact pipeline wiring still to be implemented.
  - Blind spot: Cost/time of provisioning for each run not measured.
- **Fix B**: Reclassify this gate as manual-only and keep automated suite permissive.
  - Strength: Fastest path.
  - Tradeoff: Weakens security proof and regresses confidence in phase criteria.
  - Confidence: MEDIUM — operationally simple but strategically weaker.
  - Blind spot: Human checklist quality varies between runs.
- **Decision**: FIXED (Fix A)

### F7 — One phase commit bundled unrelated change metadata files

- **Severity**: 👁️ OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: commit `489b7d0` (includes multiple `context/changes/*/change.md` outside this change scope)
- **Detail**: Phase commit included unrelated change-folder metadata, reducing commit scope clarity.
- **Fix**: Keep phase commits limited to touched-file set from the active change folder and implementation files only.
- **Decision**: FIXED (staging scope constrained to active change files only)
