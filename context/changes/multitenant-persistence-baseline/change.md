---
change_id: multitenant-persistence-baseline
title: Multi-tenant EF Core persistence baseline with verified migration
status: implementing
created: 2026-06-18
updated: 2026-06-18
archived_at: null
---

## Notes

F-01 from context/foundation/roadmap.md. Establish the EF Core domain-persistence pattern and the per-user/tenant data-scoping convention, with a real migration applied on startup against the configured SQL database. PRD refs: Access Control Changes (multi-tenant, flat role model), Success Criteria → Guardrails (tenant/data isolation), FR-001 (authenticated Entra identity scopes all data). Unlocks every data-backed slice (S-01..S-06, S-08). Keep minimal — establishes the scoping pattern + one verified migration, not the full entity set.
