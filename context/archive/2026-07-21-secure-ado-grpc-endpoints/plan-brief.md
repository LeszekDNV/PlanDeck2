# Secure Project-Scoped Azure DevOps Endpoints — Plan Brief

> Full plan: `context/changes/secure-ado-grpc-endpoints/plan.md`

## What & Why

The current Azure DevOps gRPC service can be reached anonymously and uses one server-wide PAT.
Even after blocking anonymous calls, an authenticated caller can forge ADO task metadata and trigger
a write to an arbitrary work-item ID. This plan replaces that boundary with project-owned
connections, stable project membership, server-authoritative imports, and session-derived write-back.

## Starting Point

PlanDeck has tenant-filtered teams and sessions, Entra/guest authentication, ADO import/write-back,
and Aspire Key Vault provisioning in publish mode. It has no project aggregate or project ACL,
trusts browser-supplied ADO metadata, exposes a raw write RPC, and publishes with test auth forced on.

## Desired End State

Each PlanDeck project owns sessions, members/teams, and one ADO connection whose PAT lives only in
Azure Key Vault. Owner/Admin/Member permissions are enforced from SQL; imports re-fetch selected IDs
server-side; write-back derives project and task from persisted session state. Local Aspire and local
E2E use a real development Key Vault provisioned by Aspire, while production fails closed.

## Key Decisions Made

| Decision | Choice | Why |
| --- | --- | --- |
| Security boundary | PlanDeck Project inside tenant | Tenant isolation alone cannot express connection ownership |
| Project roles | Owner/Admin/Member | Owner controls ADO/ownership; Admin manages membership/teams; Member uses sessions/ADO |
| Team relationship | Tenant teams assigned many-to-many | Teams remain reusable; assignment grants Member only |
| Direct identity | AppUserId/Entra oid plus pending email invitation | Authorization uses stable identity while preserving invitations |
| Session owner | Required immutable ProjectId; remove TeamId | One unambiguous connection and authorization source |
| Session participants | Retain SessionMember separately | A participant may vote without receiving project or ADO access |
| Import provenance | Client sends work-item IDs; server re-fetches | Browser metadata is never trusted |
| Raw write RPC | Remove it | Write-back must load persisted session/task state |
| ADO target changes | Lock organization/project after first persisted import | Work-item IDs cannot silently change meaning |
| Secret storage | SQL metadata + opaque Key Vault name; PAT only in vault | No application database or DTO contains the secret |
| Local vault | Real development Azure Key Vault provisioned by Aspire | Same SecretClient/RBAC/versioning behavior as deployment |
| Existing sessions | Destructive MVP reset | Avoid a permanent nullable/legacy authorization path |
| Test depth | Unit + SQL + real gRPC-Web/SignalR + real-vault E2E | Security must be proven at every enforcement layer |
| Phase 5 test identity | Idempotent AppUser seed gated by test-auth | Deterministic claims must satisfy SQL foreign keys before secured UI setup |
| Zero-project Sessions UX | Explicit empty state linking to Projects | Project creation is intentional and never a hidden Sessions-page side effect |

## Scope

**In scope:**
- Stable AppUser provisioning and fail-closed member/room endpoint policies
- PlanDeck projects, direct invitations, Owner/Admin/Member roles, and project-team assignments
- Required Session.ProjectId and removal of Session.TeamId
- Project-owned ADO metadata plus PAT create/rotate/delete in Azure Key Vault
- Server-authoritative ADO imports and session-derived write-back
- Removal of global PAT configuration and raw write RPC
- Project/admin UI, localization, security tests, and destructive deployment procedure

**Out of scope:**
- Granular custom ACLs, team-granted Admin/Owner, or project-target migration
- PAT/secret references in SQL responses, DTOs, logs, or browser state
- Replacing session participant invitations or guest share links
- Key Vault emulator/fake for local E2E
- Preserving existing session data

## Architecture / Approach

`Entra identity → AppUser → ProjectMember / ProjectTeam → ProjectAccessResolver`.
The project owns connection metadata and an opaque Key Vault reference. Session ProjectId selects
the connection. ADO preview requires project membership; selected IDs are re-fetched before
persistence. Write-back accepts only session/task IDs and resolves the connection from the session.
Tenant filters, composite FKs, endpoint policies, and application resolvers provide layered defense.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Identity and endpoints | Stable users, valid actor shapes, protected gRPC/SignalR, no published test auth | Breaking guest/member mixed authentication |
| 2. Project model and reset | Project roles, invitations, team assignments, required session ownership | Irreversible session-data reset |
| 3. ADO connection and Key Vault | Owner-only connection lifecycle using real Aspire-provisioned vaults | Cross-service SQL/vault consistency and RBAC |
| 4. Hardened session/ADO | Authoritative imports, project client resolution, raw write removal | Missing a forged/cross-project path |
| 5. Project-first UI | Projects, roles, teams, connection health, and deterministic project-owned session E2E | Test identity/data drift; PAT retention in client state |
| 6. Verification/deployment | Full security matrix, real-vault E2E, controlled rollout/restore | Environment mix-up or incomplete cleanup |

**Prerequisites:** Azure developer login and permission to provision/use a dedicated non-production
Key Vault; Podman for Aspire SQL; accepted backup/reset window.

**Estimated effort:** approximately 6-9 implementation sessions across 6 gated phases.

## Open Risks & Assumptions

- Local and E2E runs now depend on Azure availability, credentials, quota, and strict environment
  naming; they must never resolve the production vault.
- Key Vault and SQL cannot commit atomically; create/delete compensation must be explicit and tested.
- One broad change absorbs the separate tenant-connection and production-auth concerns; review and
  rollback therefore need phase-sized commits.
- Existing session data is intentionally discarded. Backup restore is the only data rollback.
- The first persisted ADO task permanently locks the project's ADO organization/project target.
- Test-auth identities must remain gated to Development/Testing and synchronized with their seeded
  AppUsers; otherwise project creation fails at the owner-membership foreign key.

## Success Criteria (Summary)

- Anonymous, malformed, guest, nonmember, cross-project, and cross-tenant callers cannot cause any
  ADO or Key Vault operation outside their allowed scope.
- Project members import and write back only through their project's connection; persisted ADO
  metadata is server-verified and the raw write RPC is absent.
- PATs exist only in environment-specific Azure Key Vaults, never in SQL/DTOs/logs/browser state.
- Unit, SQL, gRPC-Web, SignalR, and real-vault E2E tests pass before the destructive rollout.
