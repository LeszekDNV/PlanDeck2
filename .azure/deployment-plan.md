# Azure Deployment Plan

> **Status:** Deployed

Generated: 2026-07-23T20:57:00+02:00

---

## 1. Project Overview

**Goal:** Restore `rg-test` as a publicly reachable manual-testing environment
and remove all remote E2E execution paths.

**Path:** Add Components (modify an existing Azure deployment)

## 2. Requirements

| Attribute | Value |
|-----------|-------|
| Classification | Development / manual testing |
| Scale | Small, single-replica pilot |
| Budget | Cost-optimized |
| Subscription | Visual Studio Professional Subscription (`0e4d9ffb-2b37-45c6-8702-1d1a6cc42d61`) |
| Location | `polandcentral` |
| Resource group | `rg-test` |

## 3. Components Detected

| Component | Type | Technology | Path |
|-----------|------|------------|------|
| PlanDeck web unit | Blazor UI + ASP.NET Core/gRPC API | .NET 10, Aspire, Blazor WASM | `src/PlanDeck/Web/` |
| App host | Deployment definition | .NET Aspire 13 | `src/PlanDeck/Aspire/PlanDeck.AppHost/` |
| E2E suite | Local browser tests | Playwright NUnit | `src/PlanDeck/Tests/PlanDeck.E2e.Tests/` |
| Deployment workflow | Azure deployment | GitHub Actions + azd | `.github/workflows/azure-dev.yml` |
| Legacy CI workflow | Build/test/deploy | Azure Pipelines | `.azuredevops/pipelines/azure-dev.yml` |

## 4. Recipe Selection

**Selected:** AZD with .NET Aspire

**Rationale:** The existing deployment is generated from the AppHost and already
uses `azure.yaml`; only configuration and ingress behavior need modification.

## 5. Architecture

**Stack:** Azure Container Apps + Azure SQL + existing supporting resources.

| Component | Azure Service | Change |
|-----------|---------------|--------|
| `plandeck-server` | Azure Container Apps | Make ingress external/public |
| Deterministic manual auth | Existing test authentication scheme | Keep for `rg-test` manual personas |
| E2E scenario endpoints | Application endpoint | Stop configuring them in published `rg-test` |
| Automated browser tests | Local Aspire + Playwright | Remove remote/deployed execution mode |

No resources are added, deleted, resized, or moved.

## 6. Provisioning Limit Checklist

| Resource Type | Number to Deploy | Total After Deployment | Limit/Quota | Notes |
|---------------|------------------|------------------------|-------------|-------|
| `Microsoft.App/containerApps` | 0 new | 1 existing | No new capacity required | `az quota` queried for `Microsoft.App` in `polandcentral`; no quota rows returned |
| `Microsoft.App/managedEnvironments` | 0 new | 1 existing | No new capacity required | Existing environment remains unchanged |
| `Microsoft.Sql/servers/databases` | 0 new | Existing unchanged | No new capacity required | No database provisioning change |

**Status:** All changes are in-place configuration updates; no additional quota
or regional capacity is required.

## 7. Execution Checklist

### Phase 1: Planning
- [x] Analyze workspace
- [x] Gather requirements
- [x] Confirm subscription and location with user
- [x] Prepare resource inventory
- [x] Fetch quotas and validate capacity
- [x] Scan codebase
- [x] Select recipe
- [x] Plan architecture
- [x] User approved this plan

### Phase 2: Execution
- [x] Make published Testing ingress public
- [x] Remove published E2E scenario configuration
- [x] Restrict Playwright E2E fixture to local Aspire
- [x] Remove remote E2E pipeline paths
- [x] Update repository instructions
- [x] Build and run targeted tests
- [x] Set status to `Ready for Validation`

### Phase 3: Validation
- [x] Invoke `azure-validate`
- [x] All validation checks pass
  - [x] AZD installation
  - [x] `azure.yaml` schema validation
  - [x] Existing `test` environment setup
  - [x] AZD authentication check
  - [x] Subscription and location check
  - [x] Aspire pre-provisioning checks (no Azure Functions detected)
  - [x] Provision preview
  - [x] Build verification
  - [x] Docker build-context validation (no Dockerfiles detected)
  - [x] Package validation
  - [x] Azure Policy validation
  - [x] Aspire deployment-variable checks
- [x] Validate Aspire Testing manifest
- [x] Validate solution build and relevant tests
- [x] Verify static role assignments
- [x] Record validation proof
- [x] Set status to `Validated`

### Phase 4: Deployment
- [x] Invoke `azure-deploy`
- [x] Preview provisioning changes
- [x] Deploy to `rg-test`
- [x] Verify public HTTPS endpoint and logout lifecycle
- [x] Set status to `Deployed`

## 8. Validation Proof

> Populated only by the `azure-validate` skill.

| Check | Command Run | Result | Timestamp |
|-------|-------------|--------|-----------|
| AZD and authentication | `azd version`; `azd auth login --check-status` | AZD 1.28.0; authenticated | 2026-07-23T21:14:18+02:00 |
| Deployment schema | Azure AZD `validate_azure_yaml` on `src/PlanDeck/azure.yaml` | Valid against stable schema | 2026-07-23T21:14:18+02:00 |
| Environment | `azd env list`; `azd env get-values`; `az account show` | `test`; approved subscription; `polandcentral`; `rg-test` | 2026-07-23T21:14:18+02:00 |
| Remote E2E configuration | `azd env set <E2E key> ""`; `azd env get-value <E2E key>` | Four stale remote-E2E values cleared | 2026-07-23T21:14:18+02:00 |
| Provisioning preview | `azd provision --preview --no-prompt` | Success; no new resources; existing ACA supporting resources only | 2026-07-23T21:14:18+02:00 |
| Package | `azd package --no-prompt` | Success | 2026-07-23T21:14:18+02:00 |
| Aspire Testing manifest | AppHost manifest publisher with `PLANDECK_PUBLISH_TARGET=Testing` | HTTP/HTTPS external; test auth enabled; no E2E scenario token | 2026-07-23T21:14:18+02:00 |
| Build | `dotnet build PlanDeck.slnx --configuration Release` | Success; 0 errors | 2026-07-23T21:14:18+02:00 |
| Integration tests | Targeted authentication, guest, Production configuration, and scenario endpoint tests | 17/17 passed | 2026-07-23T21:14:18+02:00 |
| Local E2E tests | Targeted logout, home, and role smoke tests | 4/4 passed | 2026-07-23T21:14:18+02:00 |
| Azure Policy | Policy assignments for `rg-test` scope | No deny policy conflicts with planned configuration | 2026-07-23T21:14:18+02:00 |

**Validated by:** `azure-validate`
**Validation timestamp:** 2026-07-23T21:14:18+02:00

## Role Assignment Verification

- **Status:** Verified
- **Identities checked:** Container Apps environment managed identity and
  `plandeck-server` managed identity
- **Roles confirmed:** Aspire-managed image-pull relationship remains unchanged;
  the Testing application uses Azure SQL plus deterministic fake external
  services and does not require the Production-only Key Vault role
- **Issues:** None introduced by this in-place ingress/configuration update

## Deployment Proof

| Check | Result | Timestamp |
|-------|--------|-----------|
| `azd provision --no-prompt` | Existing `rg-test` infrastructure updated successfully | 2026-07-23T21:25:28+02:00 |
| ACR pull authorization | `AcrPull` confirmed for the Container Apps environment identity | 2026-07-23T21:25:28+02:00 |
| `azd deploy --no-prompt` | Revision `plandeck-server--0000023` deployed and ready | 2026-07-23T21:25:28+02:00 |
| Public ingress | External ingress enabled; root returned HTTP 200 | 2026-07-23T21:25:28+02:00 |
| Remote E2E isolation | No E2E scenario environment variable; scenario endpoint returned HTTP 404 | 2026-07-23T21:25:28+02:00 |
| Manual authentication smoke | Logout remained anonymous after refresh; login restored Test Owner | 2026-07-23T21:25:28+02:00 |
| Database/runtime health | Projects loaded and revision logs showed successful SQL queries | 2026-07-23T21:25:28+02:00 |

### Live Role Verification

- **Identity:** Container Apps environment managed identity
- **Scope:** `acaenvacrade7omipejs3a` Azure Container Registry
- **Role:** `AcrPull`
- **Status:** Pass

## 9. Files to Modify

| File | Purpose |
|------|---------|
| `src/PlanDeck/Aspire/PlanDeck.AppHost/AppHost.cs` | Public manual Testing ingress; remove published E2E token wiring |
| `src/PlanDeck/Tests/PlanDeck.E2e.Tests/AspireAppFixture.cs` | Local-only E2E execution |
| `src/PlanDeck/Tests/PlanDeck.E2e.Tests/.runsettings` | Remove remote target parameters |
| `.github/workflows/azure-dev.yml` | Deploy manual Testing without E2E flags/secrets |
| `.azuredevops/pipelines/azure-dev.yml` | Remove private remote E2E jobs |
| `.github/copilot-instructions.md` | Record the environment contract |

## 10. Next Steps

> Current: deployed and verified

1. Keep automated E2E execution local through Aspire.
2. Use the public `rg-test` endpoint for manual testing only.
