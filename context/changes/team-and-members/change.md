---
change_id: team-and-members
title: Team and members — create a team and add members to it
status: implementing
created: 2026-06-18
updated: 2026-06-18
archived_at: null
---

## Notes

Roadmap slice S-01 (`context/foundation/roadmap.md`). Outcome: a signed-in user can create a team and add members to it. PRD refs: FR-002 (create team, add members), FR-001 (management actions require Entra sign-in). Prerequisite F-01 (multitenant-persistence-baseline) is done — this is mostly CRUD over the F-01 persistence pattern. Teams are the membership root that assignment (S-05) and notifications build on.
