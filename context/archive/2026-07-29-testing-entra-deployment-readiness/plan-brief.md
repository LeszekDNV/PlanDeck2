# Testing Entra Deployment Readiness — Plan Brief

> Full plan: `context/changes/testing-entra-deployment-readiness/plan.md`
> Frame brief: `context/changes/testing-entra-deployment-readiness/frame.md`

## What & Why

Testing has no complete, dedicated Microsoft Entra web-application contract or public
callback registration. The workflows neither supply its required values nor fail
when the final Azure Container Apps revision is unhealthy. This plan completes that
boundary without changing application authentication behavior.

## Starting Point

AppHost already forwards `AZURE_ENTRA_*` values and the server fails startup when a
published target lacks them. Both GitHub workflows currently provide only the
pipeline identity and end at `azd deploy` without inspecting the resulting revision
or public `/health`.

## Desired End State

Both Testing workflows consume a protected GitHub Environment backed by a dedicated
`PlanDeck Testing` Entra registration. They reject incomplete inputs before Azure
provisioning and report success only after the final ACA revision is healthy and the
public readiness endpoint returns HTTP 200.

Users can complete Microsoft sign-in through the public callback. Failed deployments
stop with safe diagnostics and leave rollback decisions to an operator.

## Key Decisions Made

| Decision | Choice | Why | Source |
| --- | --- | --- | --- |
| Entra registration | Dedicated `PlanDeck Testing` web app | Keeps local, Testing, pipeline, and future Production identities separate | Frame / Plan |
| Secret boundary | GitHub Environment `Testing` | Scopes the client secret and supports branch policy and rotation | Plan |
| Workflow scope | Both `main` and `develop` Testing workflows | Every route to `rg-test` must enforce the same contract | Plan |
| Deployment success | Final ACA revision plus public `/health` | Covers platform startup, ingress, application, and SQL readiness | Frame / Plan |
| Revision identity | Capture `latestRevisionName` after `azd deploy` | Provision and deploy can each create a revision | Research / Plan |
| Failure response | Fail workflow; no automatic rollback | Migrations may already have run and traffic changes require human judgment | Plan |

## Scope

**In scope:**

- Dedicated Testing Entra web registration and public `/signin-oidc` callback.
- GitHub Environment variables for tenant/client ID and a client-secret secret.
- Shared Entra preflight and ACA/public-health gate for both workflows.
- Safe deployment diagnostics and corrected deployment handoff documentation.
- Real Microsoft sign-in smoke test in Testing.

**Out of scope:**

- Client, route, claims, provisioning, or OIDC behavior changes.
- Azure Key Vault migration for Testing secrets.
- Legacy Azure Pipelines, Production, WAF, canary, or automatic rollback changes.
- Automated E2E execution against deployed `rg-test`.

## Architecture / Approach

GitHub Environment `Testing` supplies `AZURE_ENTRA_*` to both workflows. A shared
preflight action validates completeness before `azd provision`; AppHost forwards the
same values and the server retains its startup validation. After `azd deploy`, a
second shared action captures the final ACA revision, waits for healthy running
state, then probes the public `/health` endpoint.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. External contract | Dedicated Entra app and protected GitHub Environment | Redirect URI or credential ownership is configured incorrectly |
| 2. Entra preflight | Both workflows fail before Azure on missing inputs | Logs accidentally expose a secret |
| 3. Readiness gate | Exact revision and public health determine success | Provision and deploy revisions are confused |
| 4. Handoff and smoke test | Both workflow paths and real sign-in are verified | External Entra configuration differs from source documentation |

**Prerequisites:** Entra permission to create a web registration, GitHub Environment
administration, Azure access to inspect `rg-test`, and an organizational test account.

**Estimated effort:** About 3 implementation sessions plus one coordinated external
configuration and smoke-test session.

## Open Risks & Assumptions

- The Testing FQDN remains stable; a hostname change requires updating the Entra
  redirect URI before deployment.
- The GitHub deployment identity can read ACA revision state and ingress metadata.
- A failed gate can leave traffic on an unhealthy revision by explicit user decision;
  the runbook must make the manual response clear.
- Credential rotation must preserve the GitHub secret name.

## Success Criteria (Summary)

- Both Testing workflows reject missing Entra inputs before provisioning.
- Both workflows verify the exact final ACA revision and public `/health` response.
- Microsoft sign-in redirects through the dedicated Testing registration and
  completes successfully without exposing credentials.
