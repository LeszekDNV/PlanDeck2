# Multi-tenant Persistence Baseline (F-01) — Plan Brief

> Full plan: `context/changes/multitenant-persistence-baseline/plan.md`

## What & Why

Establish PlanDeck's EF Core persistence pattern and a **central tenant-scoping convention**, plus **one verified migration** against SQL Server. Every data-backed slice (S-01…S-06, S-08) persists through this, so tenant isolation is fixed once, centrally — a leak here would be a cross-tenant data exposure. F-01 ships the convention + one seed entity + one verified migration, not the full domain model.

## Starting Point

`PlanDeckDbContext` is empty (no entities, no migrations). DI is already wired: `AddSqlDatabase` registers the context with retry + a health check, and `ApplyMigrationsAsync` runs `MigrateAsync()` on startup in Development. Entra ID auth preserves raw `tid`/`oid` claims (`MapInboundClaims = false`); `IHttpContextAccessor` is registered. Aspire provisions a real SQL Server container locally.

## Desired End State

Any entity marked `ITenantScoped` is automatically filtered to the caller's tenant on read and stamped with tenant + audit fields on insert — **fail-closed** (un-tenanted write throws; no-context read returns nothing). One seed entity (`AppUser`, the Entra identity record) is mapped, a single `InitialCreate` migration applies cleanly on startup, and an integration test against real SQL Server proves migration-applies + tenant isolation.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
| --- | --- | --- | --- |
| Isolation mechanism | EF global query filter + `SaveChanges` stamping, centralized in `DbContext` | Cannot be forgotten by a later slice; no scattered per-query predicates | Plan |
| Scope dimension | Tenant (`tid`) only at baseline | Membership/sharing filters layer on in S-01/S-04; keeps F-01 minimal | Plan |
| Enforcement | Fail-closed (throw on un-tenanted write, empty read with no context) | A bug must not silently leak or persist cross-tenant data | Plan |
| Seed entity | `AppUser` (Id=`oid`, TenantId=`tid`) | Foundational identity record every slice references; not owned by a later slice | Plan |
| Verification | Real SQL via the existing Aspire AppHost testing builder | Deliverable is a *verified* migration; reuses the repo's proven Podman path, no new dependency | Plan |
| Code placement | Entities/abstractions in `PlanDeck.Application`; `HttpContext` impl in `PlanDeck.Server` | Respects inward dependency flow; DbContext stays free of ASP.NET types | Plan |

> All rows are `Plan` — no frame/research doc preceded this; decisions were made autonomously (user unavailable) and are listed as assumptions in the full plan.

## Scope

**In scope:** `ITenantScoped`/`TenantEntity` + `ICurrentUserContext` abstractions; `AppUser` seed entity + config; tenant read-filter + write-stamping in `PlanDeckDbContext`; host claims-based current-user impl + DI; a design-time `IDesignTimeDbContextFactory` so migrations generate without a live DB; one `InitialCreate` migration; unit tests (SQLite) + integration test (real SQL Server via the Aspire AppHost).

**Out of scope:** full domain model (Team/Session/Vote/etc.); user-provisioning/login-sync; membership/sharing filters; RLS, soft-delete, temporal tables; any auth-pipeline change.

## Architecture / Approach

Isolation is enforced **only** inside `PlanDeckDbContext`: a generic global query filter applied to every `ITenantScoped` type compares `TenantId` against a per-request value from the injected `ICurrentUserContext`; `SaveChanges` stamps tenant + audit on insert and rejects un-tenanted writes. The host reads `tid`/`oid` from `IHttpContextAccessor`; the DbContext never touches ASP.NET types. The migration is generated into `PlanDeck.Infrastructure/Migrations/` and applied by the existing startup hook.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Convention & seed entity | Abstractions, `AppUser`, DbContext filter/stamping, DI, SQLite unit tests | Query filter capturing a stale tenant (cross-tenant leak) — mitigated by per-request member resolution |
| 2. Migration & verification | `InitialCreate` migration + integration test (real SQL via Aspire AppHost) proving apply + isolation | Aspire host boot time per fixture; needs the Podman runtime running |

**Prerequisites:** none (F-01 has no upstream slice). Container runtime (Podman) for Phase 2 integration test + manual app run. `dotnet-ef` tool for migration generation.
**Estimated effort:** ~1–2 sessions across 2 phases.

## Open Risks & Assumptions

- Global query filter **must** re-evaluate the tenant per request, not capture it once — the single highest-risk detail; covered in the plan's Critical Implementation Details.
- `AppUser` provisioning is deliberately deferred; the verification test inserts rows directly through the context.
- The integration test boots the Aspire AppHost (which provisions SQL Server via Podman); it fails fast without a running container runtime — the same dependency as running the app.

## Success Criteria (Summary)

- `dotnet build PlanDeck.slnx` and `dotnet test PlanDeck.slnx` are green.
- Running the app applies the migration on startup and creates the `AppUsers` table; `sql` health check is healthy.
- Integration test proves tenant-A data is invisible to tenant B and un-tenanted writes are rejected against real SQL Server.
