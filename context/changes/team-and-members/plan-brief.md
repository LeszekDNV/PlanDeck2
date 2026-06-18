# Team & Members (S-01) — Plan Brief

> Full plan: `context/changes/team-and-members/plan.md`

## What & Why

Deliver FR-002 — a signed-in user can create a team and add members to it. Teams are the membership root that later slices (session assignment, notifications) build on. This is the first data-backed slice on the F-01 multi-tenant persistence baseline.

## Starting Point

F-01 gives us a working tenant-scoping EF convention (inherit `TenantEntity`, add a config + `DbSet`, get auto filter + fail-closed stamping). gRPC is code-first; the Application layer talks to data through abstractions implemented in Infrastructure. But the app is a **standalone Blazor WASM** served by ASP.NET Core (no server Razor components), and it currently has **no client auth UI, no nav chrome, and zero `.resx` files** despite localization being half-wired server-side.

## Desired End State

A signed-in user sees their identity and a Teams link in the header, opens `/teams`, creates a team, and adds/removes members by email — every string available in English and Polish via a header toggle, every record tenant-isolated. Proven by integration tests on real MSSQL and a Playwright E2E that creates a team and adds a member.

## Key Decisions Made

| Decision | Choice | Why | Source |
| --- | --- | --- | --- |
| Member identity | Email invite (no account required) | Realistic MVP; no user directory exists yet | Plan |
| Team creator | Store `oid` only; don't touch `AppUser` | Minimal scope; no provisioning dependency | Plan |
| Role field | None (flat model) | PRD mandates a flat role model | PRD |
| CRUD scope | Create + list teams; add/remove/list members | Closes the FR-002 loop without over-building | Plan |
| Localization | Full en/pl now (resx + `IStringLocalizer`) | User wants the localization pattern established | Plan |
| Dev tenant | Require real Entra for manual verification | Test the real tenant path | Plan |
| Client auth | Full auth UI (`AuthenticationStateProvider` + `AuthorizeView` + header) | User wants the real auth pattern, reused later | Plan |
| Auth pattern | Server cookie/OIDC + gRPC `IAuthService` + client state provider | App is standalone WASM, not Razor-Components host | Plan |
| Tests | Integration (MSSQL) + E2E Playwright | Cover isolation invariant + full UI loop | Plan |
| E2E auth | Deterministic test-auth scheme, env-gated | Interactive Entra login is impractical headless | Plan |

## Scope

**In scope:** Team + TeamMember entities/migration; gRPC Team CRUD (create/list teams, add/remove/list members); client auth foundation (state provider, login/logout, header); WASM localization (en/pl) + language switch; Teams screen; integration + E2E tests.

**Out of scope:** `AppUser` provisioning / people-picker; roles/permissions; team rename/delete; sessions/assignments/notifications; client-side MSAL token acquisition; server-persisted culture preference; infra/deploy.

## Architecture / Approach

`Team`/`TeamMember : TenantEntity` → EF configs + migration. CRUD flows through `ITeamRepository` (Application abstraction) → `TeamRepository` over `PlanDeckDbContext` (Infrastructure), exposed by `TeamGrpcService` implementing the `ITeamService` code-first contract; the WASM client calls it via `ITeamClientService`. Auth: server keeps the cookie/OIDC session and exposes identity over `IAuthService`; a client `AuthenticationStateProvider` reflects it, with `/auth/login` + `/auth/logout` server endpoints and an env-gated test-auth handler for E2E. Localization: client `AddLocalization` + `SharedResource` resx (en/pl) + `BlazorWebAssemblyLoadAllGlobalizationData`, switched from the header.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Domain & persistence | Team/TeamMember entities, configs, migration | Unique-index shape `(TenantId, TeamId, Email)` |
| 2. gRPC + service + repo | Team CRUD server-side via abstraction | Keeping Application off Infrastructure (layering) |
| 3. Auth foundation | Identity over gRPC, login/logout, client state, header, test-auth | Standalone-WASM auth wiring; env-gating the test scheme |
| 4. Localization | resx + `IStringLocalizer`, en/pl, language switch | WASM globalization data / culture bootstrap |
| 5. Teams UI | Auth-gated, localized Teams screen | Composition over phases 2–4 |
| 6. Tests | MSSQL integration + Playwright E2E | E2E auth + WASM boot timing |

**Prerequisites:** F-01 done (it is); Podman running for integration/E2E; Entra configured in dev for manual checks; Playwright browsers installed.
**Estimated effort:** ~4–6 sessions across 6 phases (auth + localization foundations carry most of the weight).

## Open Risks & Assumptions

- Standalone-WASM auth (server cookie + gRPC identity) is the right pattern here — not the Razor-Components `PersistentComponentState` approach the repo docs imply.
- The E2E test-auth scheme must be provably off outside the E2E run; mis-gating would be a security hole.
- WASM Polish formatting depends on `BlazorWebAssemblyLoadAllGlobalizationData`; without it pl silently degrades.
- Members are email-only until a future slice links them to authenticated users.

## Success Criteria (Summary)

- A signed-in user creates a team and adds/removes members by email through the UI, in English or Polish.
- Teams and members are strictly tenant-isolated (integration tests prove both directions + the uniqueness constraint on real MSSQL).
- A Playwright E2E creates a team and adds a member end-to-end via the test-auth scheme; full suite green.
