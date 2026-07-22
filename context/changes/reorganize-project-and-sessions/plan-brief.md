# Reorganize Projects and Project-Owned Sessions — Plan Brief

> Full plan: `context/changes/reorganize-project-and-sessions/plan.md`
> Research: `context/changes/reorganize-project-and-sessions/research.md`

## What & Why

PlanDeck will treat Project as the required parent for Session management, Azure DevOps configuration, and delegated access. The change aligns the API, authorization, navigation, deletion lifecycle, and tests with the Project ownership already present in the data model.

## Starting Point

Sessions already have a required `ProjectId`, Project creators become Owners, and ADO operations resolve configuration through the Project. Session listing, mutation authorization, participant authorization, top-level navigation, Project deletion, and multi-role E2E coverage still follow or permit the older independent-Session model.

## Desired End State

Users begin at Projects, open a Project dashboard, and manage Sessions through `/projects/{projectId}/sessions`. Owner/Admin administer Sessions, Member reads and votes, inaccessible resources are concealed, and deleting a Project removes its complete owned graph after one explicit confirmation.

The full Owner/Admin/Member matrix runs deterministically through local Aspire and a protected remote Testing deployment.

## Key Decisions Made

| Decision | Choice | Why | Source |
| --- | --- | --- | --- |
| Session ownership | Project is mandatory for listing and management | Matches the existing FK and ADO ownership model | Research |
| Delegated roles | Admin edits; Member reads and votes | Reuses the current hierarchy without a role migration | Plan |
| Project UI | Dashboard plus separate Sessions route | Provides stable deep links without overloading one page | Plan |
| Legacy `/sessions` | Remove without redirect | Enforces the new navigation model explicitly | Plan |
| Inaccessible resources | Return `NotFound` | Prevents resource enumeration and closes IDOR consistently | Plan |
| Project deletion | Database cascade after external cleanup | Deletes the owned graph atomically while preserving shared entities | Plan |
| Delete confirmation | One explicit dialog | Keeps the selected low-friction destructive workflow | Plan |
| Voting return | Owning Project's Session list | Restores organizer context without changing voting URLs | Plan |
| Teams | Remain global and reusable | Avoids changing Team ownership and migration semantics | Plan |
| E2E role coverage | Full mutation matrix | Makes UI and backend role enforcement independently visible | Plan |
| E2E infrastructure | Dedicated harness and Testing pipeline | Makes the full matrix repeatable locally and in CI | Plan |

## Scope

**In scope:**

- Project-scoped Session contracts, queries, and client calls.
- Owner/Admin/Member authorization for Session and participant operations.
- IDOR closure and not-found concealment.
- Project dashboard and project-scoped Session management.
- Cascade deletion, migration, Key Vault ordering, and room invalidation.
- EN/PL localization and role-aware controls.
- Deterministic three-identity test harness and protected Testing pipeline.
- Full unit, integration, and E2E role/deletion/routing coverage.

**Out of scope:**

- A new Editor role.
- Project-owned Teams.
- Project archival or restore.
- Changes to `/join/{code}` and `/voting/{sessionId}` identity.
- A global Session list or legacy-route redirect.
- Distributed SignalR state or cross-resource SQL/Key Vault transactions.

## Architecture / Approach

The backend first requires `ProjectId` for Session listing and resolves Session mutations through `sessionId -> projectId -> effective role`. SQL Server cascade owns relational deletion, while the application preserves Key Vault cleanup and invalidates in-memory rooms before committing the Project delete. The Blazor client then exposes Projects as the entry point and passes route-owned Project context to Session operations. A test-only, environment-guarded scenario API seeds isolated Owner/Admin/Member cases for Playwright.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Project-scoped backend | Correct queries, role checks, and IDOR closure | Approved Member behavior is a breaking authorization change |
| 2. Cascading lifecycle | Atomic relational graph deletion and cleanup ordering | Deleted data cannot be restored by schema rollback |
| 3. Project-first UI | Dashboard, scoped Sessions, role-aware controls | Large existing Razor pages must be split without UX regressions |
| 4. Multi-role harness | Three identities, isolated scenarios, Testing pipeline | Test-only capabilities must never reach Production |
| 5. E2E and regression | Full role matrix plus deletion and routing coverage | Browser suite size and isolation can affect runtime/flakiness |

**Prerequisites:** Podman for local Aspire; Azure CLI/Key Vault access for current local infrastructure; protected Test deployment and secret pipeline variables for remote E2E.

**Estimated effort:** Approximately 6–9 implementation sessions across 5 phases.

## Open Risks & Assumptions

- The selected single confirmation provides less protection than typed-name confirmation for irreversible cascade deletion.
- Database/PITR is the recovery path for Projects deleted after the migration; rollback only restores FK behavior.
- Client and server remain one atomic deployment unit for the changed gRPC request behavior.
- The Test deployment can be isolated and supplied with a secret scenario token.
- Full browser coverage of every mutation will require disciplined scenario cleanup and may increase pipeline duration.

## Success Criteria (Summary)

- Sessions cannot be listed or managed outside a Project, and role behavior is enforced by both UI and backend.
- Project-first navigation, voting/guest deep links, and cascade deletion behave consistently in English, Polish, desktop, and mobile views.
- Unit, integration, local E2E, remote Testing E2E, and the full solution build pass with isolated Owner/Admin/Member scenarios.
