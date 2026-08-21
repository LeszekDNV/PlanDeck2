# Enable Key Vault in Testing - Plan Brief

> Full plan: `context/changes/enable-key-vault-in-testing/plan.md`
> Frame brief: `context/changes/enable-key-vault-in-testing/frame.md`

## What & Why

The Testing publish topology breaks PlanDeck's project-secret-store contract by
excluding the Key Vault resource and reference, causing the server to select an
intentionally unavailable store and reject PAT persistence. This plan restores the
relationship and makes a missing binding a deployment failure.

## Starting Point

Active revision `plandeck-server--0000044` has neither recognized Key Vault setting.
The required `rg-test` vault, managed identity, Secrets Officer role, Azure RBAC, soft
delete, and purge protection already exist. The application supports the real store,
but current revision health and `/health` checks cannot detect the missing binding.

## Desired End State

Every AppHost mode includes logical resource `key-vault`. Testing fails deployment
unless the final revision contains `ConnectionStrings__key-vault`, without logging its
value. A project Owner can save, read back, and remove a sandbox PAT-backed connection.

## Key Decisions Made

| Decision | Choice | Why | Source |
| --- | --- | --- | --- |
| Boundary | Restore Testing topology only | Runtime inspection confirmed the missing binding | Frame / Plan |
| Vault | Reuse logical `key-vault` | The protected Testing vault already exists | Frame / Plan |
| Guard | Fail on missing final-revision binding | `/health` does not exercise the store | Plan |
| Paths | Guard `main` and `develop` | Both share Testing and one readiness action | Plan |
| Setting | `ConnectionStrings__key-vault` | AppHost manifest emits this server binding | Plan |
| Proof | Manual sandbox PAT save and cleanup | Presence alone does not prove write access | Plan |
| E2E | Keep browser E2E local | `rg-test` is for manual testing | Frame / Plan |

## Scope

**In scope:**
- Include Key Vault for Testing in `AppHost.cs`.
- Extend the shared readiness action with a names-only binding check.
- Update both active workflows and the Azure deployment runbook.
- Preview, deploy, verify security settings, and smoke-test PAT persistence.

**Out of scope:**
- Application secret-store logic or startup health-check changes.
- New vault, identity, or RBAC design.
- Legacy Azure Pipelines or automated E2E against `rg-test`.
- Automatic rollback or database changes.

## Architecture / Approach

`AppHost key-vault -> server reference/RBAC -> ACA revision binding -> KeyVaultProjectSecretStore`

The shared action validates the captured post-deploy revision's binding name before its
existing `/health` check. The manual PAT journey proves the application-to-vault path.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Restore and guard | Consistent AppHost and deployment gate | Logging values or checking the wrong revision |
| 2. Preview and prove | In-place Azure update and PAT smoke | Destructive preview or incomplete cleanup |

**Prerequisites:** Access to the existing `test` azd/GitHub environments and a valid,
least-privilege sandbox Azure DevOps PAT.

**Estimated effort:** About 2 focused sessions across 2 phases.

## Open Risks & Assumptions

- Provision preview must confirm reuse of the deterministic existing vault.
- Only the post-`azd deploy` final revision is authoritative.

## Success Criteria (Summary)

- Manifest and final revision contain `ConnectionStrings__key-vault`.
- Both workflows fail closed without exposing the binding value.
- Sandbox PAT save/read/remove succeeds with vault protection and RBAC unchanged.
