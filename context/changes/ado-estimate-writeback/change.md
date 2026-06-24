---
change_id: ado-estimate-writeback
title: Write the agreed estimate back to the originating Azure DevOps task
status: implemented
created: 2026-06-24
updated: 2026-06-24
archived_at: null
---

## Notes

S-08 (north star) from context/foundation/roadmap.md. Outcome: a user can write the agreed estimate back to the originating Azure DevOps task, with success or failure surfaced explicitly and never silently dropped. PRD refs: FR-010, US-01. Prerequisites: S-03 (azure-devops-import), S-06 (realtime-voting-round). The write-back client (PATCH with an optimistic `/rev` concurrency test) already exists; risk is mapping the persisted agreed estimate to the right work item/field and surfacing failures per the guardrail.
