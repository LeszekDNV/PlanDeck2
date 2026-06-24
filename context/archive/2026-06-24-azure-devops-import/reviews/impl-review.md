<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Azure DevOps Import (S-03) Hardening

- **Plan**: context/changes/azure-devops-import/plan.md
- **Scope**: Phase 2 of 2 (full plan)
- **Date**: 2026-06-24
- **Verdict**: NEEDS ATTENTION → all findings resolved
- **Findings**: 0 critical, 3 warnings, 0 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | WARNING (F3 — resolved) |
| Scope Discipline | WARNING (F3 — resolved) |
| Safety & Quality | WARNING (F1, F2 — resolved) |
| Architecture | PASS |
| Pattern Consistency | PASS |
| Success Criteria | PASS (build, 74 unit, 11 E2E) |

## Findings

### F1 — gRPC accepted a raw WIQL WHERE clause from the client

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: Core/PlanDeck.Core.Shared/Contracts/IAzureDevOpsWorkItemService.cs:21 (consumed in AzureDevOpsWorkItemClient.cs)
- **Detail**: `ImportWorkItemsRequest.WiqlWhereClause` was a free-form string spliced into a WIQL query, letting an authenticated caller bypass the fixed UI filter lists. Mitigating: WIQL is read-only, scoped to the single configured ADO project under the server PAT — no escalation/mutation. The param pre-existed this change (commit 534ae7c) and the plan deferred it under "What We're NOT Doing".
- **Fix**: Replaced the string clause with typed `WorkItemTypes` + `States` lists on the gRPC contract; `AzureDevOpsWorkItemGrpcService` builds the WHERE server-side via `AzureDevOpsWiqlBuilder`. Client wrapper, panel, and unit tests updated.
- **Decision**: FIXED

### F2 — Failed reload left stale items + selection addable

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality (Reliability)
- **Location**: Web/PlanDeck.Client/Components/AdoImportPanel.razor.cs (LoadAsync catch block)
- **Detail**: On `RpcException`, `_items`/`_selectedIds` were not reset, so a previous successful load's items + checkboxes stayed visible and addable after a failed refresh.
- **Fix**: The catch block now clears `_items`, `_selectedIds`, sets `_loaded = true`, and raises `SelectedCountChanged(0)`.
- **Decision**: FIXED

### F3 — Component shape & extra files diverged from the plan (undocumented)

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence / Scope Discipline
- **Location**: AdoImportPanel.razor.cs, AdoImportDialog.razor(.cs), AzureDevOps{Options,WorkItemClient}.cs
- **Detail**: Implementation evolved (user-directed) into a dialog-hosted pure selector (`SelectedItems` + `SelectedCountChanged`), a filter + `MudHighlighter`, an extra `Sessions_AdoFilter` key, collapsible task descriptions, and a ReproSteps description fallback — none recorded in the plan.
- **Fix**: Added an "## Implementation Deviations" section to plan.md documenting all deviations (including the F1 server-side WIQL change).
- **Decision**: FIXED
