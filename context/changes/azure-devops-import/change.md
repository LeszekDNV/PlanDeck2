---
change_id: azure-devops-import
title: Connect Azure DevOps and import selected tasks
status: impl_reviewed
created: 2026-06-24
updated: 2026-06-24
archived_at: null
---

## Notes

S-03 from roadmap. Outcome: a user can connect to Azure DevOps and import selected tasks into PlanDeck (FR-003). Prereq: F-01. The import client (WIQL + batch fetch) already exists in `Infrastructure`; this slice wires it to persisted tasks and a selection UI rather than building the integration. Main risk: PAT auth/integration handling. Open unknown: which work-item field is the estimate field per tenant/project, and how it is configured vs. defaulted (owner: user).

**Scope decision (2026-06-24):** Research found the core import→select→persist flow is already implemented (built alongside S-04). This change is scoped as **hardening**: add a WIQL filter + limit input to the import UI, switch the active-session add path to batch `AddTasksAsync`, and add E2E + unit test coverage for the import path. Per-tenant ADO connection config is explicitly OUT of scope. See `research.md` for details.
