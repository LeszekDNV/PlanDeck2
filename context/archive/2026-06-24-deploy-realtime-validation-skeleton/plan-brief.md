# Deploy & Real-time-Stack Validation Skeleton — Plan Brief

> Full plan: `context/changes/deploy-realtime-validation-skeleton/plan.md`

## What & Why

Stand up a minimal pilot of PlanDeck on **Azure Container Apps + Azure SQL**, deployed by a **GitHub Actions pipeline**, and prove the exact runtime contract works in the cloud: hosted Blazor WASM load, gRPC-Web unary calls, a SignalR voting round staying connected through reveal, and SQL via managed identity. This is foundation F-03 — it de-risks the gRPC-Web/SignalR/Azure-SQL unknowns `infrastructure.md` flags *before* the full import→vote→write-back loop depends on a deployed environment.

## Starting Point

The Azure surface is already scaffolded: `AppHost.cs` publish-mode wires an ACA environment, Azure SQL, and Key Vault; `azure.yaml` exists; the server is already managed-identity-aware for SQL; gRPC-Web, the SignalR hub, and a test-auth scheme are all in place. The gaps are: migrations run only in Development, no ACA replica/affinity config, no CI/CD pipeline, and no deployed-env auth posture.

## Desired End State

A push to `main` triggers a GitHub Actions workflow that (via `azd`, OIDC federated auth) provisions + deploys the pilot to ACA + Azure SQL in West Europe and applies EF Core migrations. The app runs as a single replica with session affinity in test-auth mode over HTTPS. A human follows `runbook.md` and confirms the four runtime-contract checks pass against the live URL.

## Key Decisions Made

| Decision | Choice | Why | Source |
| --- | --- | --- | --- |
| Deliverable | Full GitHub Actions CI/CD pipeline | User wants automated deploy, not just local `azd up` | Plan |
| Pipeline → Azure auth | OIDC federated via `azd pipeline config` | No secrets to store/rotate; easiest (azd automates it) | Plan |
| Trigger / environments | Single pilot env, deploy on push to `main` | Lightest, matches "kept light / single region" | Plan |
| App auth in pilot | Test-auth scheme (run env = `Testing`) | Validates SignalR/gRPC-Web without Entra app registration | Plan |
| Migrations in cloud | Explicit pipeline step (idempotent EF script) | Separate gate; doesn't couple to container startup | Plan |
| Validation proof | Human-run manual runbook | Simpler, no flaky cloud E2E in pipeline | Plan |
| Azure SQL tier | Serverless GP, auto-pause | Cheapest for an occasional pilot; accept cold start | Plan |
| Region | West Europe | Close, broad service availability | Plan |
| SignalR scaling | Single replica + affinity, no backplane | In-process room state; backplane deferred per roadmap | Roadmap |

## Scope

**In scope:** ACA replica/affinity + test-auth + serverless SQL config in AppHost; GitHub Actions pipeline with OIDC federated auth; EF migration pipeline step; manual validation runbook + one execution.

**Out of scope:** Azure SignalR/Redis backplane; multi-env/prod gate; automated tests in pipeline; real Entra OIDC sign-in; ADO write-back validation; multi-region/HA/DR.

## Architecture / Approach

`push main` → GitHub Actions (OIDC federated) → `azd provision` + `azd deploy` → ACA app (1 replica, sticky sessions, `ASPNETCORE_ENVIRONMENT=Testing`, test-auth on) + Azure SQL (serverless auto-pause, managed identity) → pipeline migration step applies idempotent EF script → human runs `runbook.md` against the live URL.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Harden AppHost spec | ACA manifest with replica=1, affinity, test-auth env, serverless SQL SKU | Aspire provisioning API surface differs from snippet — verify type names |
| 2. CI/CD pipeline + migrations | Green GitHub Actions deploy on push; EF schema applied | Pipeline SP needs SQL `db_owner`; RBAC misconfig blocks migrate |
| 3. Validation runbook + run | Human-verified runtime contract on live URL | SignalR drop / gRPC-Web mismatch through ACA ingress (the thing we're de-risking) |

**Prerequisites:** An Azure subscription + a human to run the first `azd pipeline config` and approve the initial provision (agent has no subscription credentials).
**Estimated effort:** ~2–3 sessions across 3 phases.

## Open Risks & Assumptions

- The `azd` federated service principal must be provisioned as a SQL AAD admin / contained user with `db_owner` for the migration step — a human-approved RBAC action.
- Test-auth in the cloud relies on running the container as `Testing`; this is non-prod by design and must never carry real user traffic.
- Single-replica SignalR is a hard ceiling: raising `maxReplicas > 1` silently breaks room state until a backplane is added.
- Serverless SQL cold start can add first-request latency; the runbook warms the DB first.

## Success Criteria (Summary)

- A push to `main` deploys the pilot to ACA + Azure SQL with no stored Azure secrets, and migrations apply cleanly.
- A human validates, against the live URL: WASM loads, a gRPC-Web call works, a hidden-vote/reveal SignalR round stays consistent, and data persists via managed identity.
- The SignalR room survives (or cleanly reconnects through) one ACA revision change.
