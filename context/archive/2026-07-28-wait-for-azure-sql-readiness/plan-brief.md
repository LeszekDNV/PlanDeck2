# Azure SQL Readiness Before Migrations - Plan Brief

> Full plan: `context/changes/wait-for-azure-sql-readiness/plan.md`
> Frame brief: `context/changes/wait-for-azure-sql-readiness/frame.md`

## What & Why

Both Testing deployment workflows apply migrations without first establishing bounded, retryable Azure SQL readiness after the firewall rule is created. A shared readiness gate will tolerate serverless auto-resume, firewall propagation, and other connection failures for up to five minutes while preserving fail-closed deployment behavior.

## Starting Point

The `main` and `develop` workflows each make one-shot `Invoke-Sqlcmd` calls immediately after opening a temporary firewall rule. The Testing database intentionally auto-pauses after 60 minutes, while the application runtime already handles transient SQL failures through EF Core retry.

## Desired End State

Both workflows call one local composite action after firewall creation and before reset/migration. It repeatedly opens a fresh SQL connection and runs `SELECT 1` with bounded timeouts and exponential backoff, then either allows deployment to continue or fails after 300 seconds.

Reset and migration remain separate workflow steps, but explicitly stop on SQL errors and use finite connection/query timeouts. Firewall cleanup still runs on every outcome.

## Key Decisions Made

| Decision | Choice | Why | Source |
| --- | --- | --- | --- |
| Problem boundary | Readiness, not a sleep-specific workaround | One probe absorbs auto-resume and firewall propagation | Frame |
| Reuse boundary | Local composite action plus PowerShell | Matches repository automation and prevents workflow drift | Research |
| Maximum wait | 300 seconds | Covers documented typical resume time with headroom | Plan |
| Retry scope | Retry every SQL probe failure until deadline | Maximizes tolerance of unknown transient connectivity failures | Plan |
| Backoff | 5, 10, 20, 40, then 60 seconds | Follows Microsoft retry guidance without aggressive polling | Research |
| Probe | Fresh `Invoke-Sqlcmd` connection with `SELECT 1` | Confirms database readiness; TCP reachability alone is insufficient | Research |
| Existing SQL calls | Minimal fail-closed hardening | Prevents reset/migration from reporting false success | Plan |
| Verification | No dedicated retry test harness | User chose no automated logic verification; baseline repository checks remain | Plan |
| Diagnostics | Attempt, elapsed time, delay, concise error; never token | Supports operations without exposing credentials | Plan |

## Scope

**In scope:**

- New local action and bounded PowerShell readiness script.
- Integration in both Testing deployment workflows.
- One `SqlServer` module installation per deployment job.
- Explicit stop/abort behavior and finite reset/migration timeouts.
- Preservation of firewall cleanup on readiness failure.

**Out of scope:**

- Azure SQL SKU or auto-pause changes.
- Runtime/startup migration changes.
- OIDC, RBAC, firewall naming, triggers, or concurrency changes.
- Retrying reset or migration after readiness succeeds.
- Pester, a custom retry harness, or live Azure SQL automated tests.
- Production deployment behavior.

## Architecture / Approach

```text
Provision -> Open runner firewall -> Shared readiness action
                                      | install/import SqlServer
                                      | acquire token in-process
                                      | SELECT 1 + bounded retry
                                      v
                             Reset (optional) -> Migrate
                                      |
                         Close firewall (always) -> Deploy
```

Setup failures terminate immediately. SQL probe failures retry until the monotonic five-minute deadline. A successful warm database completes on the first minimal query.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Shared readiness gate | Composite action and bounded probe script | Deadline/backoff or logging accidentally becomes unbounded or leaks data |
| 2. Workflow integration | Symmetric gate and SQL hardening in both workflows | Workflow drift or firewall cleanup regression |

**Prerequisites:** Existing Azure CLI OIDC login, `SQL_SERVER_FQDN` variable, `db_owner` contained user, and temporary firewall rule creation must continue to work.

**Estimated effort:** About 1 implementation session across 2 phases, plus one real Testing deployment after database auto-pause.

## Open Risks & Assumptions

- Retrying every probe error means a permanent configuration or authorization error consumes the full five-minute deadline before failing.
- Microsoft publishes no firewall-rule propagation SLA; the generic readiness retry is expected to absorb short delays.
- No dedicated automated retry harness will protect timing/classification behavior; review and a real deployment are the validation boundary.
- Access-token lifetime is assumed to exceed the five-minute gate; current Azure SQL tokens are normally valid long enough.

## Success Criteria (Summary)

- A deployment against an auto-paused Testing database waits for readiness and then completes migrations.
- Readiness never waits beyond 300 seconds and failure prevents subsequent SQL/deploy steps.
- Temporary firewall cleanup still runs after failure, and logs never expose the Azure SQL access token.
