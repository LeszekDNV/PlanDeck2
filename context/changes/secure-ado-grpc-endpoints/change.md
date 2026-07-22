---
change_id: secure-ado-grpc-endpoints
title: Secure Azure DevOps gRPC endpoints
status: impl_reviewed
created: 2026-07-21
updated: 2026-07-22
archived_at: null
---

## Notes

Require authenticated, tenant-scoped access to every Azure DevOps gRPC operation. Remove or protect the raw write endpoint so anonymous callers cannot use the server PAT to read or modify arbitrary work items.

Planning expanded the boundary from tenant-only configuration to project-owned access: PlanDeck projects own Azure DevOps connections, sessions belong to projects, and project roles determine who can administer or use the connection.
