# Azure DevOps Estimate Write-Back (S-08) — Plan Brief

> Full plan: `context/changes/ado-estimate-writeback/plan.md`
> Research: `context/changes/ado-estimate-writeback/research.md`

## What & Why

Let a user push the agreed planning-poker estimate of an Azure DevOps-sourced task back to the
originating work item's estimate field. This closes PlanDeck's import → vote → write-back loop —
the north-star slice (S-08) that validates the product's core hypothesis. Per the PRD guardrail,
the write must hit the correct task/field and its success or failure must be surfaced explicitly,
never silently dropped.

## Starting Point

The entire gRPC transport for a raw write already exists (contract, server service, infra client
with optimistic `/rev` concurrency test, client wrapper, DI, test fake). But the raw op is "dumb":
it trusts a caller-supplied work-item id/revision/estimate, never loads the `SessionTask`, never
checks tenant or task source, and persists nothing. Agreed estimates are stored as scale-face
strings (`"5"`, `"XL"`, `"?"`, `"☕"`), produced during voting on Active sessions.

## Desired End State

A task that came from Azure DevOps and has a numeric agreed estimate shows a write-back action.
Clicking it pushes the numeric estimate to the work item using the stored revision; success shows
a snackbar and advances the stored revision; conflict, rate-limit, or other failure shows a
distinct localized error. Non-numeric estimates never expose the action.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) | Source |
| --- | --- | --- | --- |
| Orchestration location | New tenant-scoped op in `ISessionService`/`SessionGrpcService` | Server owns task/revision truth → satisfies "correct task/field" guardrail | Plan |
| Estimate mapping | Numeric only (invariant parse); non-numeric blocked | StoryPoints is a double; no guessing or unapproved values | Plan |
| Non-numeric scales (T-Shirt, `?`, `☕`) | No write-back in v1 (action hidden) | Avoids arbitrary face→points mapping | Plan |
| Error UX | Server maps ADO exceptions → distinct gRPC statuses; client shows localized messages | "Surfaced explicitly" for the most common failure (revision conflict) | Plan |
| Action visibility | Only for ADO tasks with a numeric estimate | Guides the user, no dead clicks | Plan |
| Post-success state | Persist ADO-returned revision onto the task | Next write uses fresh `/rev`, fewer false conflicts | Plan |
| Test scope | Unit tests (all branches) + 1 E2E round-trip | End-to-end proof of the north-star loop | Plan |

## Scope

**In scope:**
- New `WriteTaskEstimateToAdoAsync(sessionId, taskId)` gRPC operation + DTOs
- Tenant-scoped load/validate/map/write/persist in `SessionGrpcService`
- Typed ADO exceptions (concurrency, rate-limit) + gRPC status mapping
- Repo method to persist the returned revision
- UI action button, dedicated handler, per-task busy state, en+pl strings
- Unit tests for every server branch; one Playwright round-trip

**Out of scope:**
- T-Shirt/`?`/`☕` → number mapping; writing to non-estimate fields
- Auto-retry on conflict; bulk write-back; new persisted "written" timestamp/flag
- Changes to import/voting flows or the existing raw write operation

## Architecture / Approach

UI button (gated on numeric ADO estimate) → `ISessionClientService.WriteTaskEstimateToAdoAsync`
→ gRPC `ISessionService` → `SessionGrpcService`: load task tenant-scoped, validate
(`Source==AzureDevOps`, numeric estimate), parse invariant→double, call existing
`IAzureDevOpsWorkItemClient.WriteEstimateAsync` with stored `AdoWorkItemId`+`AdoRevision`, persist
returned revision via new `SetAdoRevisionAsync`, return refreshed `SessionDto`. Infra client throws
typed exceptions; server maps concurrency→`Aborted`, rate-limit→`ResourceExhausted`, other→`Unavailable`.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Backend op + error mapping + unit tests | Server-owned write-back, typed exceptions, revision persistence, full unit coverage | Status-code mapping must stay distinct from existing `FailedPrecondition` UX |
| 2. Client action + localization + E2E | Gated UI button, localized success/failure feedback, round-trip proof | E2E needs a numeric agreed estimate state on the fake-ADO path |

**Prerequisites:** S-03 (ADO import) and S-06 (voting round) already landed; Podman running for E2E.
**Estimated effort:** ~2 sessions across 2 phases.

## Open Risks & Assumptions

- Assumes the E2E harness can reach a task with a numeric agreed estimate (via UI or seeding); the
  test documents whichever approach is used.
- `StatusCode.Aborted` is chosen for concurrency to avoid colliding with the existing
  `FailedPrecondition`→"active locked" snackbar mapping.
- The existing raw `IAzureDevOpsWorkItemService.WriteEstimateAsync` stays in place, unused by the
  new path.

## Success Criteria (Summary)

- A user can write a numeric agreed estimate back to the originating ADO work item and sees explicit
  success; the stored revision advances.
- Every failure mode (conflict, rate-limit, generic) surfaces a distinct localized message — nothing
  is silently dropped.
- Non-numeric estimates never expose the action; unit tests + an E2E round-trip pass.
