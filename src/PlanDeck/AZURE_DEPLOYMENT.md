# Azure deployment handoff

PlanDeck deploys through the Aspire AppHost and Azure Developer CLI.

## Source of truth

- `Aspire\PlanDeck.AppHost\AppHost.cs` models local resources and Azure publish-mode resources.
- `azure.yaml` points `azd` at the AppHost and Azure Container Apps host target.
- Generated infrastructure from `azd`/Aspire is the source of truth until there is an explicit handoff to owned Bicep.
- Do not patch Azure resources manually in the portal without reflecting the decision in source.

## Local vs Azure

- Local mode keeps SQL Server and MailPit containers for `dotnet run --project Aspire\PlanDeck.AppHost`.
- Publish mode models Azure Container Apps, Azure SQL Database, Azure Key Vault, Azure Container Registry, and Log Analytics/Application Insights resources through Aspire Azure hosting packages.
- MVP real-time voting uses the hosted `/hubs/planning-room` SignalR endpoint with in-memory room state, so Azure Container Apps must stay at one active replica with session affinity until Azure SignalR Service or another external room-state/backplane is added.

## First Azure environment

Run from `src\PlanDeck` after Azure Developer CLI is installed:

```powershell
azd auth login
azd env new plandeck-dev
azd env set AZURE_LOCATION polandcentral
azd up
```

Use `polandcentral` (Europe, Poland Central) as the default Azure location. If a required Azure resource or SKU is unavailable there, use West Europe only for that resource and record the exception in source-controlled deployment notes or generated infrastructure configuration.

If provisioning fails, capture the `azd` output, generated Bicep, resource group deployment operation ID, and Azure activity-log correlation ID before changing the AppHost model.

## Test environment

Current `azd` environment: `test`.

- Resource group: `rg-test`
- Location: `polandcentral`
- App URL: `https://plandeck-server.wittymeadow-96369440.polandcentral.azurecontainerapps.io/`
- Aspire dashboard: `https://aspire-dashboard.ext.wittymeadow-96369440.polandcentral.azurecontainerapps.io`
- SQL server: `sqlserver-ade7omipejs3a.database.windows.net`
- Database: `PlanDeckDb`

The app uses the user-assigned managed identity `plandeck_server_identity-ade7omipejs3a` for Azure SQL. The identity is represented as a contained database user in `PlanDeckDb`; keep this grant in place when reprovisioning or replacing the SQL server.

### Testing Microsoft sign-in contract

Testing uses a dedicated, single-tenant Microsoft Entra web application named
`PlanDeck Testing`. It accepts organizational accounts from the application tenant
and has this exact web redirect URI:

```text
https://plandeck-server.wittymeadow-96369440.polandcentral.azurecontainerapps.io/signin-oidc
```

The GitHub Environment named `Testing` permits deployments from `main` and
`develop`. Configure the application sign-in values at environment scope:

| Name | GitHub Environment value type |
| --- | --- |
| `AZURE_ENTRA_TENANT_ID` | Variable |
| `AZURE_ENTRA_CLIENT_ID` | Variable |
| `AZURE_ENTRA_CLIENT_SECRET` | Secret |

These values configure the user-facing OpenID Connect client. They are separate
from `AZURE_TENANT_ID` and `AZURE_CLIENT_ID`, which identify the federated
service principal used by GitHub Actions to deploy Azure resources.

Keep the client-secret value only in the GitHub Environment. Record its expiry
on the Entra application credential and assign at least one named owner to the
application. The owner is responsible for rotating the credential before expiry
without changing the GitHub secret name. Do not add tenant IDs, client IDs,
secret values, or copies of the credential to this repository.

## CI/CD direction

The active Testing deployments are:

- `.github\workflows\azure-dev.yml` for `main`.
- `.github\workflows\azure-develop.yml` for `develop`.

Both jobs use the GitHub Environment `Testing` and concurrency group
`plandeck-testing-deployment`, so only one deployment updates `rg-test` at a
time. The legacy `.azuredevops\pipelines\azure-dev.yml` pipeline is not an
active deployment source and is outside this workflow.

The `plandeck-pipeline-oidc` application must contain a federated identity
credential with subject
`repo:LeszekDNV/PlanDeck2:environment:Testing`. Assigning a GitHub Environment
to the job changes its OIDC subject from a branch ref to this environment
subject; the environment deployment-branch policy remains responsible for
limiting use to `main` and `develop`.

Each GitHub workflow:

1. Authenticates the pipeline identity through federated OIDC.
2. Validates the dedicated application Entra settings before provisioning.
3. Provisions infrastructure and applies database migrations.
4. Deploys the application.
5. Captures the final Container App revision once and waits for that immutable
   revision to become provisioned, healthy, and running.
6. Requires the public HTTPS `/health` endpoint to return HTTP 200 without
   following redirects.

Deployment failure stops the workflow but does not automatically alter traffic,
activate or deactivate revisions, or roll back database migrations. Preserve
the workflow URL and verified revision name before performing manual recovery.

Real Key Vault integration verification is opt-in by default (`PLANDECK_RUN_REAL_KEYVAULT_TESTS=true`). In CI security gates, also set `PLANDECK_REQUIRE_REAL_KEYVAULT_TESTS=true` to fail fast instead of silently skipping the test.

## Support runbooks

Set these variables before running support commands:

```powershell
$ResourceGroup = "rg-test"
$ContainerApp = "plandeck-server"
```

Inspect revisions and replica state:

```powershell
az containerapp revision list --resource-group $ResourceGroup --name $ContainerApp --output table
az containerapp replica list --resource-group $ResourceGroup --name $ContainerApp --revision <revision-name> --output table
az containerapp revision show --resource-group $ResourceGroup --name $ContainerApp `
  --revision <revision-name> `
  --query "{provisioning:properties.provisioningState,health:properties.healthState,running:properties.runningState}" `
  --output table
```

Inspect system and application logs:

```powershell
az containerapp logs show --resource-group $ResourceGroup --name $ContainerApp `
  --type system --tail 100
az containerapp logs show --resource-group $ResourceGroup --name $ContainerApp `
  --revision <revision-name> --type console --tail 100
```

Confirm public readiness without following redirects:

```powershell
curl.exe --max-redirs 0 --fail-with-body `
  "https://plandeck-server.wittymeadow-96369440.polandcentral.azurecontainerapps.io/health"
```

After human review, restore traffic to a known-good active revision:

```powershell
az containerapp ingress traffic set --resource-group $ResourceGroup --name $ContainerApp --revision-weight <good-revision>=100
```

Do not run the traffic command until an operator confirms the selected revision
is healthy and understands whether migrations ran. Traffic rollback does not
roll back the database. Preserve logs, then use a reviewed migration rollback
script or database point-in-time restore only after separate human approval.

Common incident checks:

- ACA provisioning failure: capture `azd` logs, generated Bicep, resource group deployment operation ID, and Azure activity-log correlation ID before editing resources.
- Managed identity or Key Vault failure: verify identity assignment, RBAC/access policy, secret names, and whether a new ACA revision/restart is needed. Testing does not provision Key Vault, so its readiness checks only configured dependencies.
- SQL failure: check `DefaultConnection` binding, Azure SQL firewall/private access, managed identity user mapping, migration state, and `/health` output.
- Entra ID callback failure: verify redirect URI, forwarded HTTPS headers, cookie settings, tenant ID, and client ID.
- Azure DevOps import/write-back failure: surface 401/403/404/409/429 separately, honor `Retry-After`, retain the PlanDeck estimate, and retry only after correcting permission, field, or revision conflicts.
- Active planning room disconnects: keep one active ACA replica and session affinity until Azure SignalR Service or another external room-state/backplane is implemented.

## Production readiness gate

Production launch requires a successful production-like rehearsal covering deploy, `/health`, hosted Blazor load, gRPC-Web call, SignalR planning-room reconnect, Entra sign-in, Azure SQL read/write smoke test, Azure DevOps import/write-back against a sandbox project, ACA revision rollback, and database rollback/PITR procedure review.
