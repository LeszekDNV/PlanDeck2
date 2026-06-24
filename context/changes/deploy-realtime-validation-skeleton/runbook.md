# PlanDeck Pilot — Operations & Validation Runbook

> Change: `deploy-realtime-validation-skeleton` (F-03)
> Target: Azure Container Apps + Azure SQL, single region (West Europe), single replica, test-auth mode.
> Pipeline: `.github/workflows/azure-dev.yml` (deploy on push to `main`).

## One-time setup (human, requires an Azure subscription)

1. From `src/PlanDeck`:
   ```powershell
   azd auth login
   azd pipeline config --provider github
   ```
   This creates the Entra app registration + federated credential and sets the GitHub repo
   variables: `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`, `AZURE_ENV_NAME`,
   `AZURE_LOCATION` (set to `westeurope`).
2. Grant the pipeline's service principal `db_owner` on the Azure SQL database (add it as the
   SQL Entra admin, or create a contained user). Required for the migration step. **Human-approved
   RBAC action.**
3. After the first `azd provision`, set the repo variable `SQL_SERVER_FQDN` to the provisioned
   server FQDN (`<server>.database.windows.net`).

No long-lived secrets are stored — auth to Azure is OIDC federated. The pilot needs no application
secrets (test-auth needs none; SQL uses managed identity), so Key Vault stays empty.

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

> Authored in Phase 3.
