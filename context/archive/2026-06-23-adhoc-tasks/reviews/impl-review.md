<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Ad-hoc Tasks (S-02)

- **Plan**: context/changes/adhoc-tasks/plan.md
- **Scope**: Full plan — Phases 1–5 of 5
- **Date**: 2026-06-23
- **Verdict**: NEEDS ATTENTION
- **Findings**: 0 critical, 2 warnings, 3 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | WARNING |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | PASS |
| Success Criteria | PASS |

Automated success criteria re-verified at review time: `dotnet build PlanDeck.slnx` green; 63/63 unit tests pass; resx EN/PL parity 101=101. Integration + E2E were green at commit time (`95e22b8`).

## Findings

### F1 — Markdown renderer allows javascript:/data: link schemes

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: Web/PlanDeck.Client/Components/MarkdownView.razor:9-23
- **Detail**: `.DisableHtml()` strips raw HTML but Markdig does not filter link URL schemes, so `[x](javascript:alert(1))` rendered an executable anchor via MarkupString. Descriptions can originate from bulk paste or ADO import and surface to other members. Residual risk gated by authenticated same-tenant authorship + a click (hence WARNING, not CRITICAL). Contradicts the plan's "XSS-safe without a separate sanitizer" claim.
- **Fix**: Parse to a MarkdownDocument, rewrite any LinkInline whose URL scheme is not in {http, https, mailto} (and allow relative URLs) to `#`, then render. Implemented via a scheme allow-list in `Render`/`IsSafeUrl`.
- **Decision**: FIXED

### F2 — SignalR broadcast failure can fail an already-committed task mutation

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality (Reliability)
- **Location**: Web/PlanDeck.Server/Realtime/SignalRPlanningRoomNotifier.cs:24-25 (called from SessionGrpcService.NotifyIfActiveAsync after DB save)
- **Detail**: The notifier awaited SyncTasks+SendAsync with no guard, after the mutation had been persisted. A broadcast exception propagated out of the gRPC call, surfacing failure for a successful save and risking a duplicating client retry.
- **Fix**: Wrap the reconcile+broadcast in try/catch, log on failure, swallow — notification is best-effort and must not invalidate a committed mutation.
- **Decision**: FIXED

### F3 — gRPC task/ADO endpoints rely on tenant-only scoping (pre-existing)

- **Severity**: ◽ OBSERVATION
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: Program.cs:91-96 (no RequireAuthorization); SessionGrpcService task ops
- **Detail**: New UpdateTask/AddTasks endpoints and ADO import inherit the app-wide pattern: gRPC services mapped without RequireAuthorization and task mutations loaded by tenant only. Confirmed pre-existing (gRPC mapping + ADO WiqlWhereClause date to the First Commit). Consistent with existing convention, not a new regression.
- **Fix**: Out of scope here — a session-management authz pass + endpoint RequireAuthorization should be its own change.
- **Decision**: SKIPPED (pre-existing, own change)

### F4 — Unplanned scope landed without a plan addendum

- **Severity**: ◽ OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Scope Discipline
- **Location**: SessionGrpcService.cs (creator-as-member); AzureDevOps* (description import)
- **Detail**: ADO Description+Acceptance-Criteria import and creator-auto-added-as-member were added mid-implementation per user request; the plan's Changes Required / What We're NOT Doing were not updated.
- **Fix**: Add an addendum section to plan.md documenting both additions.
- **Decision**: FIXED (addendum added to plan.md)

### F5 — ParseBulkTasks is page-private, not an extracted shared helper

- **Severity**: ◽ OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: Web/PlanDeck.Client/Pages/Sessions.razor.cs:671
- **Detail**: Plan item 4 called for a "shared parse helper"; both bulk surfaces live in the same component and reuse the one private static method. Behavior matches; no functional gap.
- **Fix**: None needed unless a third surface needs it.
- **Decision**: SKIPPED (behavior compliant)
