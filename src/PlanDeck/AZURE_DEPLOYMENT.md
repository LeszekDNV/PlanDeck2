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

## CI/CD direction

Use Azure Pipelines for automatic deployment after push to `master`; do not add GitHub Actions for this deployment path.

Pipeline source: `.azuredevops\pipelines\azure-dev.yml`.

Required Azure Pipelines variables:

- `AZURE_SERVICE_CONNECTION` - Azure Resource Manager service connection authorized for the target subscription/resource group.
- `AZURE_ENV_NAME` - `azd` environment name, for example `plandeck-dev`.
- `AZURE_RESOURCE_GROUP` - target resource group, for example `rg-plandeck-dev`.

Store application secrets such as `Authentication__Microsoft__ClientSecret` and `AzureDevOps__PersonalAccessToken` in Key Vault or secure pipeline variables only.

## Support runbooks

Set these variables before running support commands:

```powershell
$ResourceGroup = "rg-plandeck-dev"
$ContainerApp = "plandeck-server"
```

Inspect revisions and replica state:

```powershell
az containerapp revision list --resource-group $ResourceGroup --name $ContainerApp --output table
az containerapp replica list --resource-group $ResourceGroup --name $ContainerApp --revision <revision-name> --output table
```

Follow application logs:

```powershell
az containerapp logs show --resource-group $ResourceGroup --name $ContainerApp --follow
```

Rollback a bad revision by shifting traffic back to the previous known-good revision:

```powershell
az containerapp ingress traffic set --resource-group $ResourceGroup --name $ContainerApp --revision-weight <good-revision>=100
```

After rollback, do not assume database state rolled back. Identify whether EF Core migrations ran, preserve logs, and use a reviewed migration rollback script or database point-in-time restore only after human approval.

Common incident checks:

- ACA provisioning failure: capture `azd` logs, generated Bicep, resource group deployment operation ID, and Azure activity-log correlation ID before editing resources.
- Managed identity or Key Vault failure: verify identity assignment, RBAC/access policy, secret names, and whether a new ACA revision/restart is needed.
- SQL failure: check `DefaultConnection` binding, Azure SQL firewall/private access, managed identity user mapping, migration state, and `/health` output.
- Entra ID callback failure: verify redirect URI, forwarded HTTPS headers, cookie settings, tenant ID, and client ID.
- Azure DevOps import/write-back failure: surface 401/403/404/409/429 separately, honor `Retry-After`, retain the PlanDeck estimate, and retry only after correcting permission, field, or revision conflicts.
- Active planning room disconnects: keep one active ACA replica and session affinity until Azure SignalR Service or another external room-state/backplane is implemented.

## Production readiness gate

Production launch requires a successful production-like rehearsal covering deploy, `/health`, hosted Blazor load, gRPC-Web call, SignalR planning-room reconnect, Entra sign-in, Azure SQL read/write smoke test, Azure DevOps import/write-back against a sandbox project, ACA revision rollback, and database rollback/PITR procedure review.
