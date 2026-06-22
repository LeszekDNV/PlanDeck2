---
change_id: realtime-voting-round
title: Real-time voting round — hidden vote, reveal, manual pick
status: implementing
created: 2026-06-22
updated: 2026-06-22
archived_at: null
---

## Notes

S-06 from context/foundation/roadmap.md. Assigned members join a session and vote on each task in real time; participants see who has voted as it happens, values stay hidden until the round is revealed and then appear together, and the user manually selects the agreed estimate, which is persisted. PRD refs: FR-008, FR-009, US-01. Prerequisites: F-02 (realtime-vote-integrity), S-04, S-05. Built on the F-02 integrity contract so vote consistency + hidden reveal are not re-derived here.
