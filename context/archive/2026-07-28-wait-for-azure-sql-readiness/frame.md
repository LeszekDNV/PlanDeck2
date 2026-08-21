# Frame Brief: Azure SQL readiness before migrations

> Framing step before /10x-plan. This document captures what is *actually*
> at issue, separated from what was initially assumed.

## Reported Observation

After a long period of inactivity, the first SQL connection in the GitHub
Actions deployment times out before the EF Core migration starts.

## Initial Framing (preserved)

- **User's stated cause or approach**: The Testing SQL server is asleep and
  takes several minutes to wake.
- **User's proposed direction**: Run a simple query such as `SELECT 1`
  repeatedly until the server is available, then continue.
- **Pre-dispatch narrowing**: The first SQL connection times out before the
  migration begins; the migration itself is not observed failing after a
  connection is established.

## Dimension Map

The observation could originate at any of these dimensions:

1. **Serverless database lifecycle** — the intentionally auto-paused database
   has not resumed within the first connection attempt. This is the initial
   framing.
2. **Firewall readiness** — the runner firewall rule exists, but Azure has not
   propagated it before the first database connection.
3. **Workflow connection policy** — the pipeline makes a single unprotected
   connection attempt and treats a transient readiness failure as terminal.

## Hypothesis Investigation

| Hypothesis | Evidence | Verdict |
| --- | --- | --- |
| Serverless resume delay | `AppHost.cs:60-74` configures serverless `GP_S_Gen5_1`, `AutoPauseDelay = 60`, and explicitly accepts first-query cold-start latency. Historical runbook notes DB resume latency. | STRONG |
| Firewall propagation | `.github/workflows/azure-dev.yml:89-103` and `azure-develop.yml:85-99` create a temporary firewall rule immediately before SQL work, with no readiness probe. The ordering is compatible with propagation delay, but no incident evidence identifies it as the observed cause. | WEAK |
| Missing connection resilience | Both workflows call `Invoke-Sqlcmd` once for reset/migration (`azure-dev.yml:132,153`; `azure-develop.yml:128,149`) without a readiness loop. Runtime EF uses `EnableRetryOnFailure()` in `ServiceCollectionExtensions.cs:52`, confirming the deployment path is the unprotected boundary. | STRONG |

## Narrowing Signals

- The user reports that the initial connection times out before migration work
  starts.
- The failure follows long inactivity, matching the configured 60-minute
  serverless auto-pause lifecycle.
- Both deployment workflows have the same one-shot SQL connection behavior.
- A readiness probe would also absorb short firewall propagation delays without
  requiring the pipeline to distinguish transient causes.

## Cross-System Convention

Microsoft documents that Azure SQL Database serverless automatically resumes
when a connection or other qualifying operation arrives. Clients must tolerate
resume latency and transient connectivity. The existing application already
uses EF Core transient retries; applying an explicit bounded readiness gate to
the deployment boundary follows the same reliability convention.

## Reframed (or Confirmed) Problem Statement

> **The actual problem to plan around is**: Both Testing deployment workflows
> apply migrations without first establishing bounded, retryable Azure SQL
> readiness after the firewall rule is created.

The initial framing was substantially correct: serverless auto-resume is
strongly supported by repository configuration and the inactivity pattern.
The robust boundary is readiness rather than a sleep-specific workaround,
because the same probe should safely absorb firewall propagation and other
transient connection failures while still failing after a finite deadline.

## Confidence

- **HIGH** — explicit auto-pause configuration, a matching observation, absent
  workflow retry behavior, and Microsoft serverless lifecycle guidance all
  support the same conclusion.

## What Changes for /10x-plan

Plan a shared, bounded SQL readiness gate used by both deployment workflows
after firewall creation and before reset/migration operations. Preserve
fail-closed behavior when the deadline expires and avoid duplicating retry
logic across the two workflow definitions.

## References

- Source files: `.github/workflows/azure-dev.yml:89-153`,
  `.github/workflows/azure-develop.yml:85-149`,
  `src/PlanDeck/Aspire/PlanDeck.AppHost/AppHost.cs:60-74`,
  `src/PlanDeck/Web/PlanDeck.Server/Extensions/ServiceCollectionExtensions.cs:52`
- Related operations: `context/archive/2026-06-24-deploy-realtime-validation-skeleton/runbook.md`
- Microsoft Learn: Serverless compute tier for Azure SQL Database; `Invoke-Sqlcmd`
- Investigation tasks: `sql-resume-check`, `firewall-check`,
  `connection-flow-check`
