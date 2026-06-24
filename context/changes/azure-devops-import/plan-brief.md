# Azure DevOps Import (S-03) Hardening — Plan Brief

> Full plan: `context/changes/azure-devops-import/plan.md`
> Research: `context/changes/azure-devops-import/research.md`

## What & Why

S-03's core flow (connect ADO → fetch work items → select a subset → persist as session tasks) already
works, built incidentally alongside S-04. This change hardens it: a real import **filter**, removal of the
create/config code duplication, a **batch** persist, and **test coverage** that doesn't depend on a live
Azure DevOps instance.

## Starting Point

Import is fully wired end-to-end, but the UI exposes no filter (defaults only), the import/select logic is
duplicated across the create dialog and the config panel, the config panel persists one task per gRPC call,
and there is no unit test for the ADO gRPC service nor any E2E import coverage.

## Desired End State

Both import surfaces share one panel with WorkItemType + State multi-selects and a limit; users load a
filtered list, check items, and add them (staged on create, batch-persisted on config). Unit tests cover the
gRPC mapping and the WIQL builder; an E2E test drives import→select→add against a deterministic fake ADO
client, so CI never needs a real PAT.

## Key Decisions Made

| Decision | Choice | Why | Source |
| --- | --- | --- | --- |
| Scope | Hardening only | Core import already implemented | Research |
| Filter UI | Structured WorkItemType + State multi-selects + limit | Easier/safer than raw WIQL for users | Plan |
| WIQL building | Pure `AzureDevOpsWiqlBuilder` in Core.Shared | Unit-testable, shared by both flows | Plan |
| Duplication | Extract shared `AdoImportPanel` component | Removes create/config copy-paste | Plan |
| Config persist | Switch to batch `AddTasksAsync` | One round-trip instead of N | Plan |
| E2E strategy | Fake `IAzureDevOpsWorkItemClient` under test scheme | Deterministic, no real ADO/PAT in CI | Plan |
| Per-tenant ADO config | Out of scope | Single global config retained | Research/Plan |

## Scope

**In scope:**
- Structured import filters (WorkItemType, State, limit) mapped to WIQL client-side.
- Shared `AdoImportPanel` component replacing duplicated create/config ADO logic.
- Batch persist in the config flow.
- Fake ADO client (test scheme) + unit tests (gRPC mapping, WIQL builder) + E2E happy path.

**Out of scope:**
- Per-tenant ADO connection config; write-back (S-08); free-text WIQL; ADO re-sync of imported tasks; any
  schema/dedup/concurrency change; new server gRPC operations.

## Architecture / Approach

A pure `AzureDevOpsWiqlBuilder` (Core.Shared) turns selected types/states into a WIQL `WHERE` fragment. A new
`AdoImportPanel` Blazor component (code-behind, per repo convention) owns filters + load + list + selection
and raises chosen items via `OnAddSelected`; the parent decides *stage* (create dialog) vs *batch-persist*
(config panel). Tests inject a fake `IAzureDevOpsWorkItemClient` only inside the existing test-scheme DI
branch, so real-auth behavior is untouched.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Component + filters + refactor | Shared import panel, structured filters, batch persist, dedup'd UI | Razor refactor regressing the two flows |
| 2. Test coverage | Fake ADO client, unit + E2E tests | E2E flakiness / WASM timing in Playwright |

**Prerequisites:** Podman running for Aspire-backed E2E; .NET 10 SDK; existing build green.
**Estimated effort:** ~2 sessions (one per phase).

## Open Risks & Assumptions

- Predefined State list is a pragmatic superset; ADO process templates vary, so some states may not match a
  given project (acceptable — filtering is additive and optional).
- Fake-client E2E proves the UI/wiring, not real ADO API behavior (covered separately by the live client).

## Success Criteria (Summary)

- One consistent, filterable import panel works in both the create dialog and the config panel, with `en`/`pl`
  labels and no duplicate tasks.
- `dotnet build PlanDeck.slnx` and `dotnet test PlanDeck.slnx` (Podman up) pass, including the new unit + E2E
  tests, with no real Azure DevOps configured.
