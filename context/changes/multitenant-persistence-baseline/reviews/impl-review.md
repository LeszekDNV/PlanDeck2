# Implementation Review — multitenant-persistence-baseline

- **Reviewed:** 2026-06-18
- **Scope:** commits `0ead8de` (p1) + `9546451` (p2)
- **Reviewer:** code-review agent (built-in)
- **Verdict:** ⚠ Changes requested — 2 High-severity correctness/security bugs violate the plan's fail-closed + central-enforcement contract.

## What is correct

- Per-query re-evaluation of the tenant filter (`PlanDeckDbContext.cs` `CurrentTenantId` instance member) is implemented correctly — EF Core rewrites the context-instance member access into a query-time parameter, so reads parameterize per request and do not pin to the first request's tenant.
- Sync and async `SaveChanges` overrides both route through `StampTenantAndAudit`; the parameterless overloads delegate to these.
- `DbContext` references no ASP.NET types; identity read from raw `tid`/`oid` claims in the host layer.

## Findings

### H-1 — Fail-closed write bypassed for unauthenticated caller with an explicit TenantId (High)

**File:** `src/PlanDeck/Core/PlanDeck.Infrastructure/Persistence/PlanDeckDbContext.cs` (`StampTenant`)

`StampTenant` only throws when `entity.TenantId == Guid.Empty` AND the current tenant is empty. If an unauthenticated caller (`IsAuthenticated == false` ⇒ `CurrentTenantId == Guid.Empty`) adds an entity with an **explicitly set, non-empty `TenantId`**, neither guard fires: the first branch is skipped (`TenantId != Guid.Empty`), and the cross-tenant guard is gated behind `_currentUser.IsAuthenticated` (false). The row persists with a caller-chosen tenant and zero tenant context — violating "a write with no resolvable tenant must throw," and letting a request with no tenant identity plant rows into an arbitrary tenant.

The existing fail-closed test only exercises the `TenantId == Guid.Empty` path, so it misses this.

**Fix:** Make the "no resolvable tenant" check unconditional — if `CurrentTenantId == Guid.Empty`, throw regardless of whether `entity.TenantId` was pre-set; validate `entity.TenantId == CurrentTenantId` independent of `IsAuthenticated`.

### H-2 — Tenant scoping enforced only on inserts; Modified and Deleted are unguarded (High)

**File:** `src/PlanDeck/Core/PlanDeck.Infrastructure/Persistence/PlanDeckDbContext.cs` (`StampTenantAndAudit`)

`StampTenant` (the only place tenant ownership is validated) is invoked exclusively in the `EntityState.Added` case. `Modified` only stamps `UpdatedAtUtc`; `Deleted` is unhandled. EF Core global query filters apply to LINQ queries only — they do **not** constrain UPDATE/DELETE generated from tracked entities. Consequences: (a) a row loaded under tenant A can have its `TenantId` reassigned to tenant B and saved (silent cross-tenant move); (b) an entity attached via `Update`/`Remove` with a guessed PK and another tenant's `TenantId` is updated/deleted with no check.

**Fix:** Validate tenant ownership for `Modified` and `Deleted` entries (reject when `TenantId != CurrentTenantId`, and when `CurrentTenantId == Guid.Empty`); reject changes to the `TenantId` property itself (compare `OriginalValue` / `IsModified`).

### M-1 — Isolation test does not prove per-query re-evaluation (Medium)

**File:** `src/PlanDeck/Tests/PlanDeck.Integration.Tests/Persistence/TenantPersistenceTests.cs`

`Write_UnderTenantA_IsInvisibleToTenantB` only asserts the negative (B cannot see A's row). A regressed filter that captured a one-time constant at model-build time would still return nothing for B, so this test passes even with a broken, leak-prone filter. It never asserts the positive direction (each tenant sees its own row), so it cannot distinguish "correctly scoped" from "over-filtered / always empty."

**Fix:** Add assertions that tenant A reads back its own row and tenant B reads back its own (separately written) row, so a stale-constant filter would fail.

## Recommendation

Fix H-1 and H-2 before archiving — they defeat the tenant-isolation guarantee that is the entire point of F-01. Add the M-1 positive-direction assertions so the regression is actually caught.
