# Azure SQL Readiness Before Migrations Implementation Plan

## Overview

Add one repository-local, bounded Azure SQL readiness gate that both Testing deployment workflows run after opening the temporary runner firewall rule and before any reset or migration command. The gate will tolerate serverless auto-resume, firewall propagation, and other connection failures for at most five minutes, then fail the deployment rather than allowing SQL work to continue against an unready database.

The same change will minimally harden the existing reset and migration calls so SQL errors and finite timeouts are explicit. It will not change the Azure SQL SKU, auto-pause policy, authentication model, migration ownership, or deployment topology.

## Current State Analysis

The `main` and `develop` Testing workflows independently repeat the same SQL sequence: provision infrastructure, open a temporary firewall rule, optionally reset the schema, generate and apply an idempotent EF migration script, close the firewall, and deploy the application. Their first database operation is a single `Invoke-Sqlcmd` call with no readiness retry (`.github/workflows/azure-dev.yml:89-153`, `.github/workflows/azure-develop.yml:85-149`).

The Testing database is intentionally serverless and auto-pauses after 60 minutes (`src/PlanDeck/Aspire/PlanDeck.AppHost/AppHost.cs:57-75`). Microsoft documents that the first connection to a paused database triggers resume and can return error 40613; clients are expected to retry. Runtime EF Core already follows this convention through `EnableRetryOnFailure()` (`src/PlanDeck/Web/PlanDeck.Server/Extensions/ServiceCollectionExtensions.cs:28-53`), but the deployment path does not.

The repository already keeps reusable GitHub automation in local composite actions backed by PowerShell (`.github/actions/validate-azure-entra-config/action.yml:1-24`). Both SQL workflows run on the same GitHub-hosted Ubuntu runner for the duration of a job, so a module installed by the readiness action remains available to the later reset and migration steps.

## Desired End State

Both Testing deployment workflows invoke the same local readiness action immediately after the temporary firewall rule is created. The action validates its inputs, installs and imports the `SqlServer` module, acquires an Azure SQL access token without exposing it, and repeatedly opens a fresh SQL connection to execute `SELECT 1`.

Connection attempts continue for no more than 300 seconds. Every `Invoke-Sqlcmd` failure is retried, using delays of 5, 10, 20, 40, and then at most 60 seconds, clipped to the remaining deadline. Logs identify the attempt number, elapsed time, next delay, and a bounded one-line error summary, but never print the token. Success allows reset/migration to proceed; deadline expiry terminates the workflow with a non-zero result.

The existing reset and migration commands explicitly stop on PowerShell and SQL errors, use finite connection/query timeouts appropriate to each operation, and continue to use the existing token-based identity. The `if: always()` firewall cleanup remains reachable after readiness, reset, or migration failure.

### Key Discoveries:

- The same unprotected SQL boundary exists in both deployment workflows, so inline retry logic would immediately create drift (`.github/workflows/azure-dev.yml:89-153`, `.github/workflows/azure-develop.yml:85-149`).
- Serverless auto-pause and its accepted cold-start tradeoff are intentional; changing the compute policy would solve the wrong problem (`src/PlanDeck/Aspire/PlanDeck.AppHost/AppHost.cs:60-75`).
- The historical runbook already warms the database before timing-sensitive validation and observed a roughly six-second cold start (`context/archive/2026-06-24-deploy-realtime-validation-skeleton/runbook.md:56-63`, `:90-99`).
- A SQL login/query probe is required; a TCP-only probe cannot establish that the paused database has resumed.
- Microsoft recommends waiting at least five seconds before the first retry, increasing subsequent delays exponentially, and bounding retries.

## What We're NOT Doing

- Changing the Azure SQL serverless SKU, minimum capacity, or 60-minute auto-pause policy.
- Moving migration execution into the application or enabling startup migrations outside Development.
- Changing OIDC, database principals, firewall-rule naming, deployment triggers, or concurrency behavior.
- Consolidating reset and migration implementation into the readiness action.
- Retrying reset or migration commands after the readiness gate succeeds.
- Distinguishing serverless resume, firewall propagation, permissions, or other `Invoke-Sqlcmd` failures inside the retry loop; all probe failures remain retryable until the deadline.
- Adding Pester, a PowerShell fixture harness, a new test project, or an Azure-backed automated integration test.
- Adding deployment behavior to Production; both affected workflows continue to target Testing.

## Implementation Approach

Create a small composite action at `.github/actions/wait-for-azure-sql/` so workflow orchestration stays readable and retry behavior has one owner. Its PowerShell script will separate setup failures from probe failures: invalid inputs, module installation/import, and token acquisition fail immediately, while every failure raised by the readiness `Invoke-Sqlcmd` call is retried until the five-minute deadline.

Use a monotonic stopwatch for the total deadline rather than deriving completion from attempt count. Each probe creates a fresh SQL connection, sets a finite connection timeout, and runs only `SELECT 1`. Backoff starts at five seconds, doubles to a maximum of 60 seconds, and never sleeps beyond the remaining deadline.

Insert the action after firewall creation in both workflows. Since the action installs `SqlServer` once for the job, remove the duplicate module installations from reset and migration. Keep those operations separate, add explicit stop/abort behavior, and use finite timeouts without changing their SQL or idempotent migration generation.

## Critical Implementation Details

### Timing & lifecycle

Start the five-minute stopwatch only after setup succeeds and immediately before the first SQL probe. The retry delay must be clipped to the remaining deadline so a final sleep cannot extend the gate beyond 300 seconds. The firewall cleanup step must retain `if: always()` and remain after all SQL steps.

### Debug & observability

The access token must stay inside the PowerShell process and must never be passed as an action input, workflow output, command-line argument, or log value. Per-attempt diagnostics may include server/database identifiers and a bounded single-line exception message, but not exception dumps or authentication material.

## Phase 1: Build the Shared Readiness Gate

### Overview

Create the local composite action and PowerShell script that own Azure SQL module setup, access-token acquisition, bounded retry timing, probe execution, diagnostics, and fail-closed completion.

### Changes Required:

#### 1. Declare the readiness action contract

**File**: `.github/actions/wait-for-azure-sql/action.yml`

**Intent**: Provide one reusable workflow step for Azure SQL readiness so both deployment definitions consume identical behavior.

**Contract**: The composite action accepts required `server-instance` and `database-name` inputs plus an optional `max-wait-seconds` input defaulting to `300`. It maps inputs through environment variables to a repository-local PowerShell script and exposes no token or connection output. The action assumes `azure/login` already authenticated Azure CLI.

#### 2. Implement bounded SQL readiness

**File**: `.github/actions/wait-for-azure-sql/wait-for-azure-sql.ps1`

**Intent**: Trigger serverless resume and wait for a usable database connection without hanging indefinitely or leaking authentication data.

**Contract**: The script uses `[CmdletBinding()]` and validated parameters for server, database, and a positive maximum wait. Before starting the readiness timer, it installs/imports `SqlServer` and acquires one access token for `https://database.windows.net/`; failures in those setup operations terminate immediately.

The first `SELECT 1` probe runs immediately with a 30-second connection timeout and finite query timeout. Each probe uses a fresh `Invoke-Sqlcmd` connection. Every probe exception is retried until the monotonic 300-second deadline with 5, 10, 20, 40, then 60-second delays; each wait is clipped to remaining time. Success exits zero. Deadline expiry throws a concise final error and exits non-zero.

Logs contain the attempt number, elapsed seconds, next delay, and a bounded one-line summary of the last error. They do not emit the access token, a full exception dump, or serialized command state.

### Success Criteria:

#### Automated Verification:

- The readiness script has no PowerShell parser errors.
- Repository whitespace and patch integrity checks pass: `git diff --check`.
- The full solution builds successfully: `dotnet build src/PlanDeck/PlanDeck.slnx`.

#### Manual Verification:

- Review confirms setup errors fail immediately while every SQL probe error is retried until success or the deadline.
- Review confirms all loops, connection attempts, queries, and sleeps are finitely bounded and no log path prints the access token.

**Implementation Note**: After completing this phase and all automated verification passes, pause for manual confirmation before proceeding.

---

## Phase 2: Integrate and Harden Both Testing Workflows

### Overview

Wire the shared gate into the two Testing deployments and make the reset/migration failure contracts explicit while preserving the existing firewall lifecycle and deployment order.

### Changes Required:

#### 1. Gate the main-branch Testing deployment

**File**: `.github/workflows/azure-dev.yml`

**Intent**: Ensure the `main` Testing deployment does not begin reset or migration work until Azure SQL accepts a real query.

**Contract**: Add a `Wait for Azure SQL readiness` step immediately after `Open SQL firewall for runner`. Invoke `.github/actions/wait-for-azure-sql` with `SQL_SERVER_FQDN`, `PlanDeckDb`, and the agreed 300-second limit. Preserve all existing triggers, permissions, concurrency, OIDC login, firewall naming, SQL order, and `if: always()` cleanup.

Remove the duplicate `Install-Module SqlServer` calls from reset and migration because the readiness action installs the module once on the same runner. Both SQL operation blocks set `$ErrorActionPreference = 'Stop'`, import the module with stop-on-error behavior, and invoke `Invoke-Sqlcmd` with `-AbortOnError`, a 30-second connection timeout, and finite query timeouts: 120 seconds for schema reset and 600 seconds for the migration script.

#### 2. Gate the develop-branch Testing deployment

**File**: `.github/workflows/azure-develop.yml`

**Intent**: Apply the identical readiness and fail-closed contracts to `develop` so the two workflows cannot drift at the SQL boundary.

**Contract**: Mirror the main workflow's action invocation, inputs, module reuse, stop/abort behavior, and timeout values at the corresponding positions. Preserve the branch-specific trigger/header and all shared Testing environment behavior.

### Success Criteria:

#### Automated Verification:

- Both workflows reference the same repository-local readiness action after firewall creation and before reset/migration.
- Both workflows retain identical readiness inputs, reset/migration fail-closed flags, and timeout values.
- Repository whitespace and patch integrity checks pass: `git diff --check`.
- The full solution builds successfully: `dotnet build src/PlanDeck/PlanDeck.slnx`.

#### Manual Verification:

- A deployment against an auto-paused Testing database logs at least one failed readiness attempt, later succeeds, applies migrations, and deploys the application.
- A readiness failure still reaches the `Close SQL firewall for runner` step and does not run reset, migration, or application deployment.
- Workflow logs show bounded retry diagnostics and contain no Azure SQL access token.

**Implementation Note**: This is the final phase. Complete the manual deployment checks before treating the gate as operational.

---

## Testing Strategy

### Automated Checks:

- Do not add a dedicated retry harness, Pester suite, new test project, or live Azure SQL CI test.
- Parse the PowerShell script to catch syntax errors.
- Run `git diff --check` for repository patch integrity.
- Build `src/PlanDeck/PlanDeck.slnx` because repository policy requires a successful full build after implementation.
- Inspect both YAML definitions for symmetric use of the shared action and SQL timeout/error flags.

### Manual Testing Steps:

1. Allow the Testing database to auto-pause after its configured inactivity period.
2. Trigger either Testing deployment workflow and observe the readiness step retry before succeeding.
3. Confirm reset (when requested), migration, firewall cleanup, and application deployment retain their current order.
4. Cause the readiness probe to remain unavailable through its deadline and confirm the job fails at the readiness step.
5. Confirm the temporary firewall cleanup still executes and later SQL/deployment steps do not run.
6. Inspect logs for attempt timing and concise errors, and confirm no token appears.

## Performance Considerations

- The successful warm-database path adds one minimal `SELECT 1` connection and should complete on the first attempt.
- The cold path is bounded to 300 seconds and uses exponential backoff capped at 60 seconds, avoiding aggressive polling while covering the documented typical auto-resume latency with headroom.
- A 30-second per-attempt connection timeout is itself bounded by the remaining global deadline.
- The readiness action installs `SqlServer` once per job instead of once in reset and again in migration.

## Migration Notes

No application, EF Core, or infrastructure migration is required. The change is limited to repository-local GitHub Actions automation and preserves existing Azure resources and database schema semantics.

Rollback consists of removing the readiness step/action and restoring per-step `SqlServer` installation if needed. The existing firewall cleanup remains the safety boundary during either rollout or rollback.

## References

- Frame brief: `context/changes/wait-for-azure-sql-readiness/frame.md`
- Main Testing workflow: `.github/workflows/azure-dev.yml:89-162`
- Develop Testing workflow: `.github/workflows/azure-develop.yml:85-158`
- Serverless configuration: `src/PlanDeck/Aspire/PlanDeck.AppHost/AppHost.cs:57-75`
- Runtime retry convention: `src/PlanDeck/Web/PlanDeck.Server/Extensions/ServiceCollectionExtensions.cs:28-53`
- Existing local action pattern: `.github/actions/validate-azure-entra-config/action.yml:1-24`
- Historical serverless warmup: `context/archive/2026-06-24-deploy-realtime-validation-skeleton/runbook.md:56-63`
- Azure SQL serverless auto-resume: https://learn.microsoft.com/en-us/azure/azure-sql/database/serverless-tier-overview?view=azuresql
- Azure SQL transient connectivity guidance: https://learn.microsoft.com/en-us/azure/azure-sql/database/troubleshoot-common-connectivity-issues?view=azuresql
- `Invoke-Sqlcmd` parameters: https://learn.microsoft.com/en-us/powershell/module/sqlserver/invoke-sqlcmd?view=sqlserver-ps
- Azure SQL firewall rules: https://learn.microsoft.com/en-us/azure/azure-sql/database/firewall-configure?view=azuresql

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Build the Shared Readiness Gate

#### Automated

- [x] 1.1 The readiness script has no PowerShell parser errors — b68ae41
- [x] 1.2 Repository whitespace and patch integrity checks pass — b68ae41
- [x] 1.3 The full solution builds successfully — b68ae41

#### Manual

- [x] 1.4 Setup errors fail immediately while SQL probe errors retry until success or deadline — b68ae41
- [x] 1.5 All operations are finitely bounded and logs do not expose the access token — b68ae41

### Phase 2: Integrate and Harden Both Testing Workflows

#### Automated

- [x] 2.1 Both workflows reference the shared readiness action at the required boundary — dd1de1d
- [x] 2.2 Both workflows retain identical readiness, fail-closed, and timeout contracts — dd1de1d
- [x] 2.3 Repository whitespace and patch integrity checks pass — dd1de1d
- [x] 2.4 The full solution builds successfully — dd1de1d

#### Manual

- [x] 2.5 An auto-paused Testing database becomes ready and deployment completes — dd1de1d
- [x] 2.6 A readiness timeout fails closed and still runs firewall cleanup — dd1de1d
- [x] 2.7 Workflow logs contain bounded diagnostics and no access token — dd1de1d
