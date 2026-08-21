# Test Environment Logout — Plan Brief

> Full plan: `context/changes/fix-test-environment-logout/plan.md`
> Frame brief: `context/changes/fix-test-environment-logout/frame.md`

## What & Why

The deterministic Testing authentication scheme has no representable signed-out
browser state, while `/auth/logout` claims to support logout by performing only a
redirect. The plan introduces a durable anonymous Testing state and closes the
coupled guest-cookie logout gap.

## Starting Point

The Blazor button already performs a correct full navigation. The server then
redirects without changing Testing state, and the next request recreates Test
Owner because a missing identity-selection cookie intentionally means Owner.

## Desired End State

Logout shows Log In and remains anonymous after refresh. Testing Log In restores
Test Owner, explicit E2E roles keep working, and both real and deterministic guest
state are removed by logout.

## Key Decisions Made

| Decision | Choice | Why | Source |
| --- | --- | --- | --- |
| Root problem | Testing auth lifecycle, not UI navigation | The deployed endpoint and source reproduce immediate reauthentication | Frame |
| Anonymous state | `e2e-user=anonymous` | Extends the existing selector without breaking no-cookie Test Owner | Plan |
| Testing login | Restore Test Owner | Preserves the simple current Testing model | Plan |
| Guest scope | Include real and deterministic guests | The same endpoint currently leaves both guest states behind | Plan |
| Regression coverage | Integration lifecycle plus one E2E | Proves cookies and the real user interaction without a brittle role matrix | Plan |

## Scope

**In scope:**

- Explicit anonymous state in `TestAuthenticationHandler`
- Testing login/logout cookie transitions
- Real and deterministic guest logout cleanup
- Integration tests for member and guest lifecycles
- One Playwright logout/reload/login scenario using a Page Object

**Out of scope:**

- Blazor layout changes
- Entra or deployment reconfiguration
- Server-side session storage
- Role-by-role logout E2E matrix

## Architecture / Approach

`/auth/logout` writes an anonymous selection marker and clears guest state.
`TestAuthenticationHandler` recognizes that marker before guest/default identity
selection. `/auth/login` removes the marker, allowing the unchanged no-cookie
fallback to restore Test Owner. Production member/OIDC logout remains intact,
while real guests exit through their dedicated cookie scheme.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Authentication lifecycle | Handler/endpoint transitions and integration coverage | Cookie precedence or deletion mismatch |
| 2. Browser regression | Real logout, refresh, and login through MudBlazor UI | Environment-dependent E2E startup |

**Prerequisites:** Podman for local Aspire-backed E2E, or a deployed Testing URL
through the existing `BaseUrl` run parameter.

**Estimated effort:** About 1–2 focused implementation sessions across 2 phases.

## Open Risks & Assumptions

- The anonymous marker must override stale deterministic guest state.
- Cookie options must work with both HTTPS Azure ingress and in-process HTTP tests.
- Guest logout must not trigger an unnecessary Entra remote-signout flow.

## Success Criteria (Summary)

- Logout remains anonymous after redirect and refresh.
- Testing login restores Test Owner without breaking owner/admin/member contexts.
- Real and deterministic guest cookies no longer survive logout.
