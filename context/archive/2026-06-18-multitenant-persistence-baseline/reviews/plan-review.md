<!-- PLAN-REVIEW-REPORT -->
# Plan Review: Multi-tenant Persistence Baseline (F-01)

- **Plan**: context/changes/multitenant-persistence-baseline/plan.md
- **Mode**: Deep
- **Date**: 2026-06-18
- **Verdict**: REVISE → SOUND (after fixes)
- **Findings**: 1 critical, 1 warning, 0 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| End-State Alignment | PASS |
| Lean Execution | PASS |
| Architectural Fitness | WARNING |
| Blind Spots | PASS |
| Plan Completeness | FAIL |

## Grounding
6/6 paths ✓, symbols ✓ (MapInboundClaims=false, AddSqlDatabase connection-string throw, Infrastructure→Application reference, dotnet ef 10.0.8 available, PlanDeckDbContext constructed only via DI), brief↔plan ✓.

## Findings

### F1 — Migration generation will fail at design time

- **Severity**: ❌ CRITICAL
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Plan Completeness
- **Location**: Phase 2 — Step 1 (Initial migration)
- **Detail**: `dotnet ef migrations add --startup-project Web/PlanDeck.Server` builds the Server DI to instantiate the DbContext. `AddSqlDatabase` throws `InvalidOperationException("Connection string 'DefaultConnection' is required.")` (ServiceCollectionExtensions.cs:22-25) because `DefaultConnection` is Aspire-injected only at runtime (AppHost.cs:33). The new `ICurrentUserContext` ctor arg compounds it. The plan specified no design-time path.
- **Fix A ⭐ Recommended**: Add `IDesignTimeDbContextFactory<PlanDeckDbContext>` in Infrastructure (placeholder connection + no-op ICurrentUserContext) so EF tools bypass Program.cs.
  - Strength: Sidesteps both the connection-string throw and ICurrentUserContext injection; standard EF pattern.
  - Tradeoff: One small extra class.
  - Confidence: HIGH.
  - Blind spot: None significant.
- **Fix B**: Document a placeholder `ConnectionStrings__DefaultConnection` env var for the command.
  - Strength: No new code.
  - Tradeoff: Fragile, undocumented; still runs full Program.cs.
  - Confidence: MEDIUM.
- **Decision**: FIXED via Fix A — added Phase 1 change #7 (`PlanDeckDbContextFactory`), a `dotnet ef dbcontext info` smoke check (Progress 1.3), and a reference to it from Phase 2 Step 1.

### F2 — Testcontainers duplicates existing Aspire test infra + Podman risk

- **Severity**: ⚠️ WARNING
- **Impact**: 🔬 HIGH — architectural stakes; think carefully before deciding
- **Dimension**: Architectural Fitness
- **Location**: Phase 2 — Steps 2 & 3 (integration test + csproj refs)
- **Detail**: Plan added Testcontainers.MsSql to get real SQL, but E2e.Tests already provisions SQL via `DistributedApplicationTestingBuilder<Projects.PlanDeck_AppHost>` through the same Podman runtime (AspireAppFixture.cs). Testcontainers defaults to a Docker socket and needs explicit DOCKER_HOST/podman-socket config on Windows — an unverified new unknown.
- **Fix A ⭐ Recommended**: Reuse the Aspire `DistributedApplicationTestingBuilder` pattern; read the `PlanDeckDb` connection string from the Aspire model.
  - Strength: No new dependency; same Podman path; consistent with E2e fixture.
  - Tradeoff: Boots more than just SQL.
  - Confidence: MED.
  - Blind spot: Full-host boot time per fixture.
- **Fix B**: Keep Testcontainers but document the Podman socket configuration.
  - Strength: Lighter isolated container if it runs.
  - Tradeoff: New dependency + unverified Windows+Podman socket setup.
  - Confidence: LOW.
- **Decision**: FIXED via Fix A — rewrote Phase 2 Steps 2-3, the csproj contract (Aspire.Hosting.Testing + AppHost ProjectReference, no Testcontainers), assumption #4, Testing Strategy, the verified-migration approach note, and plan-brief.
