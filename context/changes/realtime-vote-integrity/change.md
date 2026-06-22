---
change_id: realtime-vote-integrity
title: Real-time vote-integrity baseline — hidden/reveal contract + reconnection
status: implemented
created: 2026-06-22
updated: 2026-06-22
archived_at: null
---

## Notes

F-02 from context/foundation/roadmap.md. Harden the existing in-memory planning-room spike into the authoritative hidden-vote/reveal contract: votes are never lost, duplicated, or reordered; values are not observable by any participant before reveal; and a participant can drop and reconnect without corrupting room state. Bind the room to persisted, tenant-scoped sessions (real session identity) rather than ad-hoc string keys. Kept minimal: defines and TESTS the contract + reconnection semantics; consuming slices (S-06 assigned-member voting, S-07 guest voting) wire it to real sessions and UI. PRD refs: Guardrails (real-time vote consistency; vote values not observable before reveal), Business Logic (synchronized hidden-vote-then-reveal round). Prerequisite: F-01. Sequenced BEFORE S-06.

Existing spike: PlanningRoomHub (/hubs/planning-room), PlanningRoomService (singleton, ConcurrentDictionary, per-session, hides votes until reveal), PlanningRoomClientService (HubConnection + WithAutomaticReconnect; registered but not yet used by any page). Transport decision (locked at foundation level): SignalR/WebSockets for real-time, NOT bidirectional gRPC — gRPC-Web supports only unary + server-streaming from the browser (infrastructure.md:101).
