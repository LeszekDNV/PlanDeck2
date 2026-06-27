---
change_id: testing-critical-path-integrity
title: Critical-path integrity tests (reconnect/reveal, estimate save, session config)
status: implemented
created: 2026-06-27
updated: 2026-06-27
archived_at: null
---

## Notes

Rollout Phase 1 of context/foundation/test-plan.md: "Critical-path integrity".
Risks covered: #1 (session disconnect/reveal consistency), #2 (estimate save/write-back reliability), #5 (session configuration correctness).
Test types planned: integration + targeted e2e.
Risk response intent:
- #1: prove reconnect/reveal yields one consistent round outcome without vote loss/duplication.
- #2: prove estimate save has explicit success/failure signaling and no silent drop.
- #5: prove session configuration persists valid task selection and voting-scale state for the next round.
