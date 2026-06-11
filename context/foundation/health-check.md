---
project: "PlanDeck"
checked_at: 2026-06-11T01:20:52+02:00
health_status: needs-attention
context_type: brownfield
language_family: dotnet
stack_assessment_available: true
checks_run:
  - lockfile
  - dependency_audit
  - outdated_deps
  - test_runner
  - ci_cd
  - configuration
audit_findings:
  critical: 0
  high: 0
  moderate: 0
  low: 0
test_runner_detected: true
ci_provider: null
recommended_fixes: 7
---

## Dependency Health

### Lockfile

Status: missing
Package manager: dotnet / NuGet

No `packages.lock.json` files were found under `src\PlanDeck`. Dependency versions are not fully pinned, which weakens reproducibility and makes it harder for an AI assistant to reason about the exact dependency graph it is changing.

Fix: enable NuGet lock files and restore once:

```powershell
Set-Location C:\Projects\10xKurs\src\PlanDeck
dotnet restore PlanDeck.slnx --use-lock-file
```

Then commit the generated `packages.lock.json` files.

### Security Audit

Tool: `dotnet list PlanDeck.slnx package --vulnerable --include-transitive`
Summary: 0 CRITICAL, 0 HIGH, 0 MODERATE, 0 LOW
Direct vs transitive: no vulnerable direct or transitive packages reported by NuGet audit.

The audit completed successfully for all projects in `PlanDeck.slnx`:

- `PlanDeck.AppHost`
- `PlanDeck.ServiceDefaults`
- `PlanDeck.Application`
- `PlanDeck.Common`
- `PlanDeck.Core.Shared`
- `PlanDeck.Infrastructure`
- `PlanDeck.E2e.Tests`
- `PlanDeck.Integration.Tests`
- `PlanDeck.Unit.Tests`
- `PlanDeck.Client`
- `PlanDeck.Server`

### Outdated Dependencies

Packages with major version gaps: 0 direct packages

`dotnet list PlanDeck.slnx package --outdated` reported no direct package updates. A broader transitive scan (`--include-transitive`) reported many transitive major-version gaps, mostly flowing from upstream packages (for example SQL Server tooling, Microsoft.Build, and older support libraries), but those are not directly actionable until their parent packages move. Treat this as informational only.

## Test Suite

Test runner: NUnit 4 + Playwright
Tests found: 1 discoverable E2E test; no tests discovered in the unit or integration test projects
Test execution: discovery/build succeeded; test execution not attempted

Configuration:

- `Tests\PlanDeck.Unit.Tests\PlanDeck.Unit.Tests.csproj` references `NUnit`, `NUnit3TestAdapter`, `Microsoft.NET.Test.Sdk`, `NUnit.Analyzers`, and `coverlet.collector`.
- `Tests\PlanDeck.Integration.Tests\PlanDeck.Integration.Tests.csproj` uses the same NUnit test infrastructure.
- `Tests\PlanDeck.E2e.Tests\PlanDeck.E2e.Tests.csproj` references `Microsoft.Playwright.NUnit` and discovered `CallServerButton_ReturnsHelloWorld`.

Framework: NUnit 4.6.1, Microsoft.NET.Test.Sdk 18.6.0, Playwright NUnit 1.60.0.

Dry-run command:

```powershell
Set-Location C:\Projects\10xKurs\src\PlanDeck
dotnet test PlanDeck.slnx --list-tests --verbosity minimal
```

Result: the runner and build pipeline are usable, but test coverage is currently skeletal. The AI assistant can verify build/test infrastructure, but most domain changes will need new unit and integration tests before they are safe.

## CI/CD

Provider: not detected
Configuration: not found

| Stage      | Status | Notes              |
|------------|--------|--------------------|
| Lint       | ✗      | not configured     |
| Test       | ✗      | not configured     |
| Build      | ✗      | not configured     |
| Type check | ✗      | not configured     |
| Security   | ✗      | not configured     |

ℹ No CI/CD configuration detected. You'll set this up in the infrastructure and deployment lesson. For now, local test runner coverage is what matters for agent collaboration.

## Configuration

### High severity

- **NuGet lock files (`packages.lock.json`)** — missing. Without lock files, restores can drift and the AI assistant cannot rely on an exact package graph. Fix: run `dotnet restore PlanDeck.slnx --use-lock-file`.
- **`.gitignore`** — missing at both the repository root and `src\PlanDeck`. Without it, generated artifacts (`bin\`, `obj\`, Playwright traces, logs, local settings) are easy to commit accidentally. Fix: add a .NET-oriented `.gitignore` at `C:\Projects\10xKurs\.gitignore`.

### Medium severity

- **Stale `.proto` scaffold** — `Web\PlanDeck.Server\PlanDeck.Server.csproj` still contains `<Protobuf Include="Protos\greet.proto" GrpcServices="Server" />`. This contradicts the code-first gRPC convention documented for the project and reinforces the stack-assessment gap. Fix: remove the stale `Protos\greet.proto` file and its `<Protobuf>` item when you next touch gRPC setup.
- **No repo-level SDK pin (`global.json`)** — missing. The project targets .NET 10 and uses `LangVersion=latest`; without a pinned SDK, different machines may compile with different preview/patch behavior. Fix: add a `global.json` at `src\PlanDeck` or the repository root pinning the intended .NET 10 SDK.
- **No `.editorconfig`** — missing. Formatting and analyzer severity are not codified for editors or agents. Fix: add a .NET `.editorconfig` and keep formatting/analyzer expectations in one place.
- **Sparse test coverage** — unit and integration test projects exist, but `dotnet test --list-tests` discovered no tests in either project. Fix: add at least one meaningful unit test and one integration smoke test for the first domain slice.

### Low severity

- **`.env.example` / `.env.template`** — missing. Environment variables are not documented in a copyable form. This matters once Azure DevOps, Entra ID, notifications, and deployment secrets are wired. Fix: add a template file with variable names only and safe placeholder values.
- **Central package management (`Directory.Packages.props`)** — missing. Versions are repeated across test projects, which invites drift as the codebase grows. Fix: consider centralizing package versions after the initial dependency set stabilizes.

## Stack Assessment Cross-Reference

Stack assessment: `context/foundation/stack-assessment.md`
Agent readiness (from stack-assess): `ready-with-compensation`

| Quality Gate Gap | Health-Check Finding | Status |
|---|---|---|
| Code-first `protobuf-net.Grpc` is niche relative to `.proto`-first examples | `.github\copilot-instructions.md` already documents code-first gRPC clearly, but `PlanDeck.Server.csproj` still has a stale `<Protobuf>` item for `Protos\greet.proto`. | Partially mitigated; stale scaffold reinforces the wrong pattern |
| .NET Aspire 13.x is recent and under-represented in older training data | `.github\copilot-instructions.md` documents AppHost, ServiceDefaults, and the local run command; no CI exists yet to validate Aspire wiring automatically. | Mitigated locally; CI validation still upcoming |
| Missing repo-level tooling guardrails | `global.json`, `.editorconfig`, NuGet lock files, `.gitignore`, central package management, and CI are absent. | Reinforced |

## Recommended Fixes

### Fix before agent work (Category A)

### 1. Generate NuGet lock files

**Impact**: The assistant needs reproducible restores to reason about the exact package graph and avoid version drift between sessions.
**Severity**: high
**Effort**: quick (< 5 min)
**Fix**:

```powershell
Set-Location C:\Projects\10xKurs\src\PlanDeck
dotnet restore PlanDeck.slnx --use-lock-file
```

Commit the generated `packages.lock.json` files.

### 2. Add a repository `.gitignore`

**Impact**: Without a `.gitignore`, an assistant can accidentally include generated build outputs, logs, local settings, or Playwright artifacts in later commits.
**Severity**: high
**Effort**: quick (< 5 min)
**Fix**:

```powershell
Set-Location C:\Projects\10xKurs
dotnet new gitignore
```

Then review the generated file and add any project-specific local files that should never be committed.

### 3. Remove stale `.proto` gRPC scaffold

**Impact**: The stack assessment identified code-first `protobuf-net.Grpc` as the main place where agents need steering. The leftover `.proto` item gives the assistant a contradictory example and increases the chance of wrong `.proto`-first changes.
**Severity**: medium
**Effort**: quick (< 5 min)
**Fix**:

Remove this item from `Web\PlanDeck.Server\PlanDeck.Server.csproj`:

```xml
<Protobuf Include="Protos\greet.proto" GrpcServices="Server" />
```

Then delete `Web\PlanDeck.Server\Protos\greet.proto` if it is still present, and run:

```powershell
Set-Location C:\Projects\10xKurs\src\PlanDeck
dotnet build PlanDeck.slnx
```

### 4. Pin the .NET SDK

**Impact**: The project targets .NET 10 and uses `LangVersion=latest`; pinning the SDK prevents different machines or agents from compiling against subtly different SDK behavior.
**Severity**: medium
**Effort**: quick (< 5 min)
**Fix**:

```powershell
Set-Location C:\Projects\10xKurs\src\PlanDeck
dotnet new globaljson --sdk-version 10.0.301
```

Adjust the version if the installed team baseline differs.

### 5. Add `.editorconfig`

**Impact**: A shared formatting/analyzer baseline reduces noisy agent edits and makes reviews focus on behavior instead of style drift.
**Severity**: medium
**Effort**: moderate (15–30 min)
**Fix**:

```powershell
Set-Location C:\Projects\10xKurs
dotnet new editorconfig
```

Then add project-specific analyzer severities if needed.

### 6. Seed unit and integration tests for the first domain slice

**Impact**: The runner exists, but unit/integration projects currently discover no tests. The assistant will need executable checks around the first feature slice to safely iterate on domain behavior.
**Severity**: medium
**Effort**: moderate (15–30 min)
**Fix**:

Add at least:

- one unit test for a pure application/domain rule (for example, hidden-vote-then-reveal behavior once implemented),
- one integration smoke test for the first gRPC service contract once implemented.

Run:

```powershell
Set-Location C:\Projects\10xKurs\src\PlanDeck
dotnet test PlanDeck.slnx --list-tests
```

### 7. Add environment variable documentation

**Impact**: Future work will need Entra ID, Azure DevOps, and notification settings. A template prevents agents from guessing variable names or leaking real values into docs/code.
**Severity**: low
**Effort**: quick (< 5 min)
**Fix**:

Create `.env.example` or `docs\configuration.md` with variable names only and safe placeholders. Do not include secrets.

### Addressed in upcoming lessons (Category B)

### CI/CD pipeline not configured

**Lesson**: [Sprint Zero z Agentem: infrastruktura, walking skeleton i pierwszy deploy (M1L5)](https://platforma.przeprogramowani.pl/external/10xdevs-3/m1-l5)
**What you'll do there**: Add a pipeline that builds, tests, and eventually deploys the project. For now, local build/test coverage is sufficient for agent collaboration.

### Deployment configuration not configured

**Lesson**: [Sprint Zero z Agentem: infrastruktura, walking skeleton i pierwszy deploy (M1L5)](https://platforma.przeprogramowani.pl/external/10xdevs-3/m1-l5)
**What you'll do there**: Choose and configure the deployment target and production pipeline. No deployment manifest is expected at this point.

### Dedicated `AGENTS.md` not present

**Lesson**: [Agent Onboarding: Agents.md, AI Rules i feedback loops (M1L4)](https://platforma.przeprogramowani.pl/external/10xdevs-3/m1-l4)
**What you'll do there**: Convert the existing detailed `.github\copilot-instructions.md` guidance into the right onboarding artifact for future agents. Generating a stub now would be premature.

## Summary

Health status: needs-attention

PlanDeck is in good shape for an early scaffold: dependency audit is clean, direct packages are current, the .NET/NUnit/Playwright test infrastructure is discoverable, and the existing Copilot instructions already compensate for the main stack-assessment gap. The project still needs reproducibility and tooling guardrails before smooth agent-assisted development: NuGet lock files, `.gitignore`, SDK pinning, `.editorconfig`, and removal of stale `.proto` scaffolding. Unit and integration test projects also need real tests as soon as the first domain slice lands.

Next step: address the quick Category A fixes (lockfile, `.gitignore`, stale `.proto`, SDK pin), then proceed to agent onboarding once the foundation artifacts have been reviewed.
