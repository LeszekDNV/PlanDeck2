# Microsoft Login Error — Plan Brief

> Full plan: `context/changes/microsoft-login-error/plan.md`
> Frame brief: `context/changes/microsoft-login-error/frame.md`

## What & Why

Testing exposes Microsoft login without a configured OpenID Connect handler, causing
HTTP 500 that the exception pipeline masks as a 404 page. The change restores real
Microsoft login in Testing and makes provider availability and errors truthful.

## Starting Point

OIDC registration depends on complete credentials, but Entra routes and controls do
not. The exception handler targets an unmapped `/Error` path that falls into WASM 404.

## Desired End State

Published Testing and Production require complete Entra configuration. Optional
hosts hide Microsoft actions and omit challenge routes. Unexpected failures retain
HTTP 500 with safe Problem Details or HTML and a correlatable trace ID.

## Key Decisions Made

| Decision | Choice | Why | Source |
| --- | --- | --- | --- |
| Published Testing without Entra | Fail startup | A visible Microsoft login must always be operational | Plan |
| Configuration boundary | Explicit `Authentication:Microsoft:Required` | Keeps published Testing strict without breaking isolated `Testing` hosts | Plan |
| Optional environments | Hide UI and omit challenge routes | UI, routing, and scheme registration remain one contract | Frame / Plan |
| Capability transport | New operation on existing `IAuthService` | Reuses the typed gRPC-first auth boundary without exposing configuration | Plan |
| Server exceptions | `IExceptionHandler` with Problem Details/HTML | Preserves HTTP 500, traceability, and safe browser UX | Frame / Plan |
| Final verification | Integration tests plus deployed smoke test | Deterministic tests cannot validate Azure secrets, app registration, or redirect URI | Plan |

## Scope

**In scope:**

- Typed Entra options and startup validation.
- AppHost propagation and conditional OIDC registration/routes.
- Non-secret hosted-WASM capability and conditional Microsoft actions.
- Safe global 500 handling, integration tests, and a manual `rg-test` smoke test.

**Out of scope:**

- Entra authority, claims, provisioning, account-linking, or database redesign.
- Making Entra mandatory for local development and isolated test hosts.
- Committed credentials or automated browser tests against deployed `rg-test`.

## Architecture / Approach

One typed options value drives startup validation, OIDC, routes, and capabilities.
The auth gRPC service informs the client before it shows controls. A DI-backed
exception handler formats failures directly without SPA route re-execution.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Configuration and routing | Fail-closed published targets and conditional challenge routes | Configuration, scheme, and routes could drift |
| 2. Client availability | Typed capability and truthful login/register/security UI | Initial render could briefly expose unavailable actions |
| 3. Error handling and deployment | Accurate 500 responses and verified Testing SSO | Azure secret/app-registration mismatch is only visible after deployment |

**Prerequisites:** Testing secrets, Entra app registration, and an organizational account.
**Estimated effort:** About 2–3 implementation sessions across 3 phases.

## Open Risks & Assumptions

- The existing Entra app registration permits the current Testing callback URI.
- `AZURE_ENTRA_*` inputs are available without being committed.
- Provider availability is process-static and changes require restart or deployment.
- Content negotiation must distinguish real navigation from fetch/API requests.

## Success Criteria (Summary)

- Testing redirects to Microsoft and completes organizational sign-in.
- Optional hosts expose neither Microsoft controls nor challenge routes.
- Server failures remain HTTP 500 with safe trace correlation, never PlanDeck 404.
