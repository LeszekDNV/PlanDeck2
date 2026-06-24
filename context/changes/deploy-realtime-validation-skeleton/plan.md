# Deploy & Real-time-Stack Validation Skeleton — Implementation Plan

## Overview

Stand up a minimal, single-region pilot of PlanDeck on **Azure Container Apps + Azure SQL**, driven by a **GitHub Actions CI/CD pipeline** (provisioned with `azd pipeline config`, OIDC federated auth), and prove the exact runtime contract works in the cloud: hosted Blazor WASM load, gRPC-Web unary calls, a SignalR voting round staying connected through reveal, and Azure SQL access via managed identity. Validation is a **human-run manual runbook**, not automated tests. This is foundation F-03 — kept deliberately light (single replica, single region, no Azure SignalR backplane).

## Current State Analysis

The Azure deployment surface is already substantially scaffolded:

- **`Aspire/PlanDeck.AppHost/AppHost.cs`** has a publish-mode branch that wires `AddAzureContainerAppEnvironment("aca-env")`, `AddAzureSqlServer("sql-server").AddDatabase("PlanDeckDb")`, `AddAzureKeyVault("key-vault")`, and references them into `plandeck-server` with `DefaultConnection` + Key Vault. The server resource has `WithExternalHttpEndpoints()`.
- **`src/PlanDeck/azure.yaml`** exists (`azd` template, `host: containerapp`, service `plandeck-server` → the AppHost project).
- **`Aspire/PlanDeck.AppHost/PlanDeck.AppHost.csproj`** references `Aspire.Hosting.Azure.AppContainers`, `Aspire.Hosting.Azure.KeyVault`, `Aspire.Hosting.Azure.Sql` (all 13.4.6).
- **`Web/PlanDeck.Server/Extensions/ServiceCollectionExtensions.cs` → `AddSqlDatabase`** is already managed-identity-aware: when `AZURE_CLIENT_ID` is set it rewrites `Authentication="Active Directory Default"` → `Active Directory Managed Identity;User Id=<clientId>`, with `EnableRetryOnFailure()` and an `AddDbContextCheck<PlanDeckDbContext>("sql")` health check.
- **`Web/PlanDeck.Server/Program.cs`** wires gRPC-Web (`UseGrpcWeb(DefaultEnabled = true)`), the SignalR hub at `/hubs/planning-room`, Entra OIDC + guest-cookie auth, localization, static assets, and `MapDefaultEndpoints()` (health).
- **Test-auth support exists**: AppHost sets `Authentication__UseTestScheme=true` when env var `PLANDECK_E2E_TESTAUTH=true`; `AddExternalServices` permits the test scheme **only** in `Development` or `Testing` environments and, under test-auth, registers `FakeAzureDevOpsWorkItemClient` and skips Entra wiring entirely.

### Gaps this plan must close

1. **Migrations only run in Development.** `app.ApplyMigrationsAsync()` is inside `if (app.Environment.IsDevelopment())` in `Program.cs:33-37`, so a deployed (non-dev) environment never gets the schema. A pipeline migration step is required.
2. **No ACA scale/affinity config.** The publish-mode AppHost does not set `minReplicas=1` or session affinity. ACA defaults to scale-to-zero, which would drop the in-memory SignalR rooms (`PlanningRoomService` is a singleton, in-process — no backplane).
3. **No CI/CD pipeline.** There is no GitHub Actions workflow; `azd` has only been initialised (`azure.yaml`), not wired to a pipeline.
4. **No deployed-env auth posture.** For the pilot we will run with the **test-auth scheme** so SignalR/gRPC-Web can be validated without an Entra app registration — this requires the deployed app to run as `Testing` (so the test-scheme guard passes) and the test-scheme env vars to be set on the container.
5. **Azure SQL tier/region not pinned.** Default `AddDatabase` does not specify a serverless auto-pause SKU; region is chosen at provision time.

## Desired End State

- `git push` to `main` triggers a GitHub Actions workflow that runs `azd` (federated OIDC auth) to **provision + deploy** the pilot to ACA + Azure SQL in **West Europe**, then runs an **EF Core migration step** against the Azure SQL database.
- The deployed app runs as a single replica with session affinity, in test-auth mode, reachable over external HTTPS.
- A human follows `runbook.md` and confirms the four runtime-contract checks pass against the live URL.
- Azure SQL is a **serverless General Purpose, auto-pause** database; no application secrets are required (test-auth needs none; SQL uses managed identity).

### Key Discoveries

- Managed-identity SQL path is already implemented — `ServiceCollectionExtensions.cs:31-43`.
- Test-auth env propagation already exists — `AppHost.cs:43-49` and the guard in `ServiceCollectionExtensions.cs:56-61`.
- SignalR rooms are in-process singleton state — `AddLocalServices` registers `IPlanningRoomService` as a singleton (`ServiceCollectionExtensions.cs:140`), so `minReplicas=1` + affinity is mandatory for the pilot (no backplane per roadmap).
- The E2E harness can target a deployed URL via the `BaseUrl` run parameter (`AspireAppFixture`), but per the chosen validation approach we are **not** wiring that into the pipeline — the runbook is manual.

## What We're NOT Doing

- No Azure SignalR Service or Redis backplane (single replica only; documented scaling trigger lives in `infrastructure.md`).
- No multi-environment topology (no separate prod env, no manual approval gate) — one pilot environment, deploy on push to `main`.
- No automated E2E/integration tests in the pipeline — validation is a manual runbook.
- No real Entra OIDC sign-in flow in the pilot (test-auth scheme stands in).
- No Azure DevOps write-back validation, multi-region, HA/DR, or production hardening.
- No application secrets management beyond what test-auth + managed identity require (Key Vault stays minimal/empty).

## Implementation Approach

Three phases, each independently verifiable:

1. **Harden the AppHost publish-mode spec** so the generated ACA + Azure SQL manifest is correct for a real-time pilot (replica/affinity, test-auth env, SQL serverless SKU, region intent). Verified by inspecting the generated manifest (`azd`/Aspire publish) — no Azure subscription needed.
2. **Provision the GitHub Actions pipeline** with `azd pipeline config` (OIDC federated), trigger on push to `main`, and add an EF Core migration step that applies an idempotent script to Azure SQL using the pipeline's federated identity.
3. **Author the manual validation runbook** and execute it once against the deployed URL, capturing results and the ACA rollback command.

## Critical Implementation Details

- **Test-auth requires the deployed env to be `Testing`.** `AddExternalServices` throws if `Authentication:UseTestScheme` is true outside `Development`/`Testing` (`ServiceCollectionExtensions.cs:56-61`). The pilot container must therefore set `ASPNETCORE_ENVIRONMENT=Testing` **and** `Authentication__UseTestScheme=true`. Running as `Testing` (not `Development`) keeps HSTS/exception-handler on and WASM debugging off — but it also means `ApplyMigrationsAsync` does not run on startup, which is fine because migrations are a pipeline step.
- **The pipeline identity needs SQL access for the migration step.** Applying migrations against Azure SQL requires the `azd` federated service principal to be a database principal with `db_owner` (e.g. provisioned as the SQL AAD admin or a contained user). Prefer generating an idempotent script (`dotnet ef migrations script --idempotent`) and applying it with an AAD access token, so the step is rerunnable and does not couple to container startup.
- **SignalR state is in-process.** Do not raise `maxReplicas` above 1 for this pilot; horizontal scale silently breaks room state until a backplane is added.

## Phase 1: Harden the ACA publish-mode spec in the AppHost

### Overview

Make the publish-mode branch of `AppHost.cs` emit an ACA + Azure SQL specification suitable for a single-replica real-time pilot in test-auth mode, in West Europe.

### Changes Required:

#### 1. ACA replica + session affinity for the server

**File**: `src/PlanDeck/Aspire/PlanDeck.AppHost/AppHost.cs`

**Intent**: Ensure the deployed container app runs with `minReplicas=1`/`maxReplicas=1` and HTTP session affinity so the in-process SignalR rooms survive across requests and ACA does not scale the app to zero.

**Contract**: Customize the generated Container App on the `plandeck-server` resource in the publish-mode branch. Use the Aspire Azure App Containers customization hook to set scale + ingress sticky sessions, e.g.:

```csharp
planDeckServer.PublishAsAzureContainerApp((infra, app) =>
{
    app.Template.Scale.MinReplicas = 1;
    app.Template.Scale.MaxReplicas = 1;
    app.Configuration.Ingress.StickySessions = new ContainerAppStickySessions
    {
        Affinity = StickySessionAffinity.Sticky
    };
});
```

Verify the exact type names against the referenced `Aspire.Hosting.Azure.AppContainers` 13.4.6 model; the intent (min=max=1 + sticky affinity) is the contract.

#### 2. Pilot environment + test-auth env vars on the server

**File**: `src/PlanDeck/Aspire/PlanDeck.AppHost/AppHost.cs`

**Intent**: Run the deployed pilot in test-auth mode so the runtime contract (gRPC-Web + SignalR voting) is validatable without an Entra app registration.

**Contract**: In the publish-mode branch, set on `planDeckServer`: `ASPNETCORE_ENVIRONMENT=Testing` and `Authentication__UseTestScheme=true` (via `WithEnvironment`). These must be present on the container regardless of the local `PLANDECK_E2E_TESTAUTH` toggle (which is for local E2E only).

#### 3. Azure SQL serverless auto-pause SKU

**File**: `src/PlanDeck/Aspire/PlanDeck.AppHost/AppHost.cs`

**Intent**: Pin the pilot database to a serverless General Purpose tier with auto-pause to minimise cost, accepting cold-start latency.

**Contract**: Customize the generated `SqlDatabase` (serverless GP, e.g. `GP_S_Gen5_1`, with an auto-pause delay and a small min capacity) via the Azure provisioning customization hook on the database resource. Name/SKU is the contract; exact property surface is verified against the `Aspire.Hosting.Azure.Sql` provisioning model.

#### 4. Region selection

**File**: pipeline/azd configuration (no code) — documented in `runbook.md`

**Intent**: Provision everything in West Europe.

**Contract**: `AZURE_LOCATION=westeurope` is set as the azd environment value / GitHub Actions variable consumed by `azd provision`. No AppHost code change.

#### 5. Key Vault posture

**File**: `src/PlanDeck/Aspire/PlanDeck.AppHost/AppHost.cs`

**Intent**: Avoid carrying secrets the pilot does not need (test-auth needs none; SQL uses managed identity).

**Contract**: Leave the `AddAzureKeyVault` wiring in place but do not require any secret to be populated for the pilot to start. Document that no app secrets are needed. (Do not remove the reference — it is harmless and keeps the prod path intact.)

### Success Criteria:

#### Automated Verification:

- Solution builds: `dotnet build PlanDeck.slnx` (from `src/PlanDeck`)
- Publish manifest generates without error: `azd infra synth` (or `dotnet run --project Aspire/PlanDeck.AppHost -- --publisher manifest --output-path ./aspire-manifest.json`) and the generated ACA resource shows `minReplicas: 1`, sticky sessions, and the `Testing`/test-auth env vars.

#### Manual Verification:

- Generated Bicep/manifest shows the Azure SQL database on a serverless auto-pause SKU.
- The local `dotnet run --project Aspire/PlanDeck.AppHost` (non-publish) path is unchanged and still boots with the local SQL container + MailPit.

**Implementation Note**: After Phase 1 automated checks pass, pause for human confirmation of the generated manifest before moving on.

---

## Phase 2: GitHub Actions CI/CD pipeline (azd) + EF migration step

### Overview

Wire a GitHub Actions workflow that authenticates to Azure via OIDC federated credentials and runs `azd` to provision + deploy the pilot on push to `main`, then applies EF Core migrations to Azure SQL.

### Changes Required:

#### 1. Pipeline + federated auth via azd

**File**: `.github/workflows/azure-dev.yml` (generated), azd config under `src/PlanDeck/.azure/`

**Intent**: Create the GitHub Actions deploy workflow and the Azure federated identity so the pipeline can deploy without stored secrets.

**Contract**: Run `azd pipeline config --provider github` (from `src/PlanDeck`) to generate `azure-dev.yml`, create the app registration + federated credential, and set repo variables `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`, `AZURE_ENV_NAME`, `AZURE_LOCATION=westeurope`. Trigger is `push` to `main` (single pilot environment). The workflow runs `azd provision` + `azd deploy` (or `azd up`).

#### 2. EF Core migration step

**File**: `.github/workflows/azure-dev.yml` (post-deploy job/step) or an `azd` `postdeploy` hook in `azure.yaml`

**Intent**: Apply the EF Core schema to Azure SQL as an explicit, idempotent pipeline gate after the database is provisioned.

**Contract**: Generate an idempotent migration script and apply it to the provisioned Azure SQL database using the pipeline's federated identity (AAD token), e.g. `dotnet ef migrations script --idempotent --project Core/PlanDeck.Infrastructure --startup-project Web/PlanDeck.Server -o migrate.sql` then apply via `sqlcmd`/`Invoke-Sqlcmd` with an AAD access token. The pipeline service principal must be a SQL database principal with `db_owner` (provisioned as SQL AAD admin or contained user — see Critical Implementation Details). The connection target (server/db name) comes from azd outputs.

#### 3. Document the pilot deploy/rollback operations

**File**: `context/changes/deploy-realtime-validation-skeleton/runbook.md` (operations section; full runbook authored in Phase 3)

**Intent**: Capture the ACA revision rollback command and the migration policy so operators can recover the pilot.

**Contract**: Record `az containerapp ingress traffic set --name <app> --resource-group <rg> --revision-weight <revision>=100` for rollback and note that EF migrations do not roll back with revisions.

### Success Criteria:

#### Automated Verification:

- `.github/workflows/azure-dev.yml` exists and is valid YAML.
- The workflow run completes green on a push to `main`: provision + deploy + migration step all succeed (verified in the Actions run log).
- `az containerapp show --name <app> --resource-group <rg>` reports a `Running` provisioning state and an external FQDN.

#### Manual Verification:

- The Azure SQL database contains the EF Core tables after the migration step (e.g. `__EFMigrationsHistory` populated).
- No secrets are stored in the repo or GitHub Secrets for Azure auth (OIDC federated only).
- A second push to `main` redeploys cleanly and the migration step is a no-op (idempotent).

**Implementation Note**: Provisioning/deploying to a real subscription and any RBAC/SQL-admin changes require human execution/approval per `infrastructure.md`. The agent prepares config; a human runs the first `azd pipeline config` and approves the initial provision. Pause for human confirmation after the first green run.

---

## Phase 3: Manual validation runbook + first run

### Overview

Author a human-followable runbook that proves the four runtime-contract checks against the deployed URL, then execute it once and record the outcome.

### Changes Required:

#### 1. Validation runbook

**File**: `context/changes/deploy-realtime-validation-skeleton/runbook.md`

**Intent**: Give a human a precise, ordered checklist to validate the deployed pilot and capture pass/fail.

**Contract**: The runbook covers, against the live ACA URL: (a) warm up the serverless DB (hit a health/data endpoint, expect first-call cold-start latency); (b) hosted Blazor WASM loads (home page renders, no console errors); (c) a gRPC-Web unary call succeeds (e.g. create/list via an existing service through the UI); (d) a SignalR voting round — sign in via test-auth, open a session, cast hidden votes from two browser contexts (member + guest), confirm votes stay hidden until reveal and reveal is consistent; (e) Azure SQL via managed identity (data persists across reload). Include the health endpoint URL (`/health` via `MapDefaultEndpoints`), the test-auth login path, and the rollback command from Phase 2.

#### 2. Record results

**File**: `context/changes/deploy-realtime-validation-skeleton/runbook.md` (results section) and `change.md` notes

**Intent**: Capture the validated runtime contract and any gRPC-Web/SignalR surprises (the unknowns F-03 exists to de-risk).

**Contract**: A results table (each check: pass/fail + note) plus a short paragraph on whether gRPC-Web behaved identically through ACA ingress and whether SignalR survived a revision change (deploy a trivial change mid-session and observe reconnect).

### Success Criteria:

#### Automated Verification:

- `runbook.md` exists in the change folder.

#### Manual Verification:

- A human has executed the runbook against the deployed URL and all five checks pass (DB warmup, WASM load, gRPC-Web unary, SignalR hidden-vote/reveal round, SQL persistence).
- The SignalR room survives (or cleanly reconnects through) one ACA revision change while a session is active.
- Results recorded in `runbook.md` and summarized in `change.md`.

**Implementation Note**: This phase is human-executed against live Azure. The agent authors the runbook; a human runs it and reports results.

---

## Testing Strategy

### Unit Tests:

- None new — this is an infrastructure/deployment change. The existing suite (`dotnet test PlanDeck.slnx`) must remain green to confirm no regressions from AppHost/Program changes.

### Integration Tests:

- None new in the pipeline (validation is manual per decision). Existing integration/E2E tests continue to run locally via Aspire.

### Manual Testing Steps:

1. Inspect the generated ACA manifest (Phase 1) for replica=1, sticky sessions, `Testing`/test-auth env, serverless SQL SKU.
2. Push to `main`; watch the GitHub Actions run provision → deploy → migrate.
3. Execute `runbook.md` against the deployed URL (DB warmup, WASM, gRPC-Web, SignalR round, persistence).
4. Deploy a trivial change mid-session to observe SignalR behavior through an ACA revision change.

## Performance Considerations

- Serverless auto-pause SQL has cold-start latency on first query; the runbook warms the DB before timing-sensitive checks.
- Single replica caps throughput but is correct for a validation skeleton; scaling beyond one replica is explicitly deferred until a SignalR backplane exists.

## Migration Notes

- Migrations are applied by the pipeline (idempotent script), not on container startup. The pilot runs as `Testing`, so startup auto-migration (Development-only) does not fire — intentional.
- EF migrations do not roll back with ACA revisions; treat schema changes as a separate gate.

## References

- Roadmap slice: `context/foundation/roadmap.md` (F-03, lines 98-111)
- Infrastructure research + risk register: `context/foundation/infrastructure.md`
- AppHost publish-mode wiring: `src/PlanDeck/Aspire/PlanDeck.AppHost/AppHost.cs:7-22`
- Managed-identity SQL: `src/PlanDeck/Web/PlanDeck.Server/Extensions/ServiceCollectionExtensions.cs:31-49`
- Dev-only migrations: `src/PlanDeck/Web/PlanDeck.Server/Program.cs:33-37`
- Test-auth toggle + guard: `AppHost.cs:43-49`, `ServiceCollectionExtensions.cs:56-61`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles.

### Phase 1: Harden the ACA publish-mode spec in the AppHost

#### Automated

- [x] 1.1 Solution builds: `dotnet build PlanDeck.slnx` — d6c3778
- [x] 1.2 Publish manifest generates and shows minReplicas=1, sticky sessions, Testing/test-auth env — d6c3778

#### Manual

- [x] 1.3 Generated manifest shows Azure SQL on serverless auto-pause SKU — d6c3778
- [x] 1.4 Local non-publish AppHost run unchanged (SQL container + MailPit boot) — d6c3778

### Phase 2: GitHub Actions CI/CD pipeline (azd) + EF migration step

#### Automated

- [x] 2.1 `.github/workflows/azure-dev.yml` exists and is valid YAML
- [x] 2.2 Workflow run is green on push to main (provision + deploy + migrate)
- [x] 2.3 `az containerapp show` reports Running + external FQDN

#### Manual

- [x] 2.4 Azure SQL contains EF tables after migration step (__EFMigrationsHistory populated)
- [x] 2.5 No Azure auth secrets stored in repo/GitHub Secrets (OIDC federated only)
- [x] 2.6 Second push redeploys cleanly; migration step is a no-op

### Phase 3: Manual validation runbook + first run

#### Automated

- [ ] 3.1 `runbook.md` exists in the change folder

#### Manual

- [ ] 3.2 Human executed runbook; all five checks pass (DB warmup, WASM, gRPC-Web, SignalR round, persistence)
- [ ] 3.3 SignalR room survives/reconnects through one ACA revision change
- [ ] 3.4 Results recorded in runbook.md and summarized in change.md
