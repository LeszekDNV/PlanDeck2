---
change_id: guest-link-voting
title: Guest-link voting — join a session vote via a share link with a temporary username
status: impl_reviewed
created: 2026-06-24
updated: 2026-06-24
archived_at: null
---

## Notes

S-07 from roadmap.md. A user without an account can join a session's vote via a share link containing a code, providing only a temporary username, and vote like any participant. Prereqs: S-04, F-02. PRD ref: FR-013. Key isolation concern: per-session code scoping/expiry so guests cannot reach other sessions.
