# PlanDeck Pilot — Operations & Validation Runbook

> Change: `deploy-realtime-validation-skeleton` (F-03)
> Target: Azure Container Apps + Azure SQL, single region (**Poland Central**), single replica, test-auth mode.
> Resource group: `rg-test` · azd env: `test` · App URL: `https://plandeck-server.wittymeadow-96369440.polandcentral.azurecontainerapps.io/`
> Pipeline: `.github/workflows/azure-dev.yml` (deploy on push to `main`).

## One-time setup (human, requires an Azure subscription)

The pilot reuses the existing `rg-test` infrastructure (Poland Central) and azd env `test`. The
pipeline authenticates with OIDC federated credentials — **no long-lived secrets are stored**.

1. **Entra app registration + federated credential** for GitHub OIDC
   (`plandeck-pipeline-oidc`, subject `repo:LeszekDNV/PlanDeck2:ref:refs/heads/main`).
2. **RBAC for the pipeline service principal** (**human-approved**):
   - `Contributor` + `User Access Administrator` at **subscription** scope — azd (Aspire) runs a
     subscription-scoped deployment that also creates role assignments, so both are required.
   - `AcrPush` on the deployment's container registry.
   - `db_owner` contained user in `PlanDeckDb` (created `FROM EXTERNAL PROVIDER`) for the migration
     step.
3. **GitHub repo variables** (Settings → Variables, not Secrets):
   `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`, `AZURE_ENV_NAME=test`,
   `AZURE_LOCATION=polandcentral`, `SQL_SERVER_FQDN=<server>.database.windows.net`.
   The workflow also sets `AZURE_RESOURCE_GROUP=rg-${{ vars.AZURE_ENV_NAME }}` so azd targets the
   existing resource group.

The pilot needs no application secrets (test-auth needs none; SQL uses the container app's managed
identity), so Key Vault stays empty.

## Deploy

- Automatic: push to `main` runs the workflow (provision → migrate → deploy).
- Manual from a workstation (same `azd`): from `src/PlanDeck`, `azd provision` then `azd deploy`
  (or `azd up`), then apply migrations (see the workflow's migration step).

## Rollback

- **App code**: ACA keeps revisions. Shift 100% traffic back to the previous known-good revision:
  ```powershell
  az containerapp revision list --name <app> --resource-group <rg> -o table
  az containerapp ingress traffic set --name <app> --resource-group <rg> --revision-weight <revision>=100
  ```
- **Database**: EF Core migrations do **not** roll back with ACA revisions. Schema changes are a
  separate, human-approved gate; risky migrations need their own rollback script.

## Validation

Five runtime-contract checks against the live ACA URL. App URL and key paths:

- App: `https://plandeck-server.wittymeadow-96369440.polandcentral.azurecontainerapps.io/`
- Health (DB-backed): `/health` · Liveness: `/alive` (via `MapDefaultEndpoints`)
- Test-auth: in `Testing` mode the app auto-signs the browser in as **Test User**
  (`test.user@plandeck.local`); no interactive login is needed.
- Guest path: an active session exposes a `…/join/<code>` link to join as a named guest.

### Procedure

1. **(a) Warm up the serverless DB.** `GET /health` and expect HTTP 200. The first call after an
   auto-pause incurs cold-start latency (DB resume); subsequent calls are fast.
   ```powershell
   Measure-Command { Invoke-WebRequest -UseBasicParsing `
     "https://plandeck-server.wittymeadow-96369440.polandcentral.azurecontainerapps.io/health" }
   ```
2. **(b) Hosted Blazor WASM loads.** Open the app root. The MudBlazor shell renders (`PlanDeck`
   toolbar, `Teams`/`Sessions` nav, the signed-in `Test User`). No console errors except a benign
   `favicon.ico` 404.
3. **(c) gRPC-Web unary call.** On the home page click **Call server**; a `Hello World!` heading
   appears — a `protobuf-net.Grpc` unary call over gRPC-Web through ACA ingress.
4. **(d) SignalR hidden-vote / reveal round.**
   - Go to **Sessions → Create session**, give it a name, add a task, **Save**.
   - **Activate** the session, then **Join voting** (member). In a second browser context open the
     `…/join/<code>` link and join as a guest.
   - Both participants appear on the board in real time. Each casts a vote; the other side shows
     **Voted** with the value **hidden**. Click **Reveal**; both values appear, consistently, on
     both clients. **Reset** re-opens the round on both clients.
5. **(e) Azure SQL persistence via managed identity.** Reload the board / Sessions page (fresh WASM
   boot). The created session and its data are still present, proving persistence to Azure SQL via
   the container app's managed identity (no SQL password in config).

### Revision-change resilience (3.3)

While a session board is open in two clients, force a new ACA revision and confirm the realtime
room survives / reconnects:
```powershell
az containerapp update -g rg-test -n plandeck-server --set-env-vars "VALIDATION_REVISION_TEST=$(Get-Date -Format yyyyMMddHHmmss)"
```
After traffic shifts to the new revision, a realtime action (e.g. **Reset**) on one client must
still broadcast to the other.

### Results — executed 2026-06-25 (revision 0000010 → 0000011)

| # | Check | Result | Notes |
|---|-------|--------|-------|
| a | Serverless DB warmup (`/health`) | ✅ Pass | First `/health` 200 in ~6.0s (cold start incl. DB connectivity via MI); `/alive` ~0.3s, `/` ~0.4s. |
| b | Hosted Blazor WASM loads | ✅ Pass | Home renders (MudBlazor shell, auto-signed-in as Test User). Only console error: `favicon.ico` 404 (benign). |
| c | gRPC-Web unary | ✅ Pass | **Call server** → `Hello World!` (HelloGrpcService over gRPC-Web through ACA ingress). |
| d | SignalR hidden-vote / reveal round | ✅ Pass | Two participants (Test User + Guest Voter). Votes hidden as **Voted** until reveal; on **Reveal** both saw 8 / 5 consistently. Presence + reveal broadcast in real time on both clients. |
| e | Azure SQL persistence (managed identity) | ✅ Pass | Created session survived a full page reload (fresh WASM boot); data served from Azure SQL via the app's managed identity. |
| 3.3 | SignalR survives ACA revision change | ✅ Pass | Forced revision `0000010 → 0000011` with both boards open; revealed state survived and a subsequent **Reset** still broadcast to the other client (0 console errors). |

**gRPC-Web through ACA ingress** behaved identically to local: the unary `Call server` call
succeeded with no special ingress configuration. **SignalR through ACA** worked end-to-end with the
publish-mode sticky-session config (single replica): presence, hidden votes, reveal, and reset all
propagated between the member and guest clients, and the room cleanly survived a mid-session
revision change — the core unknown F-03 set out to de-risk.

> Validation artifacts left in the pilot DB: session **"Validation Session F-03"** and a transient
> `VALIDATION_REVISION_TEST` env var (self-clears on the next `azd deploy`). Safe to delete.
