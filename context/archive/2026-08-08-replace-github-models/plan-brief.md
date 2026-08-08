# Replace GitHub Models Integration — Plan Brief

> Full plan: `context/changes/replace-github-models/plan.md`
> Research: `context/changes/replace-github-models/research.md`

## What & Why

Replace the discontinued custom GitHub Models pipeline with native GitHub Copilot Code Review for pull requests to `develop`. Preserve PlanDeck's 15 review criteria as a repository skill, but treat Copilot as an advisory reviewer rather than recreating unsupported pass/fail labels or an AI merge gate.

## Starting Point

The old workflow, local action, schema, validator, publisher, fixtures, and `ai-cr:*` state are still tracked in `HEAD` but already marked for deletion in the working tree. The repository has useful architecture instructions and a detailed archived review policy, but no tracked code-review skill and no automatic Copilot rule.

## Desired End State

Every non-draft PR to `develop` receives a native `Balanced` Copilot review and another review after each push. Copilot applies all 15 PlanDeck criteria as a checklist, reports only high-confidence findings, and never claims approval, merge status, or unobserved test results.

The provider-specific GitHub Models implementation is gone. A two-push canary proves draft exclusion, skill use, `Comment` review state, automatic re-review, and manual UI retry before the obsolete `ai-cr:*` labels are deleted.

## Key Decisions Made

| Decision | Choice | Why | Source |
| --- | --- | --- | --- |
| Replacement | Native GitHub Copilot Code Review | Removes model/API maintenance and uses GitHub's supported reviewer | Research |
| Review role | Advisory only | Native Copilot has no stable verdict or status-check contract | Research |
| Branch scope | `develop` only | Preserves the old workflow's scope and limits rollout risk | Plan |
| PR state | Non-draft only | Avoids noisy, costly review of unfinished work | Plan |
| Review profile | `Balanced` + new pushes | Balances depth with cost and replaces normal retry behavior | Plan |
| Policy format | 15-criterion checklist, high-confidence findings | Preserves DoD without pretending native review is deterministic | Plan |
| Retry | New pushes + manual GitHub UI | Avoids another privileged workflow and special label | Plan |
| Legacy labels | Delete after canary | Prevents stale labels from being mistaken for active state | Plan |
| Quality gates | Out of scope | Required CI and approvals change merge governance independently | Plan |
| Rollback | No retained fallback | The old pipeline is removed before canary by explicit decision | Plan |

## Scope

**In scope:**

- A tracked `.github/skills/code-review/` skill and complete 15-criterion policy.
- A narrow `.gitignore` exception and a review pointer in Copilot instructions.
- Removal of all GitHub Models workflow/action artifacts.
- Manual configuration of `Protect develop`.
- Draft-to-ready, two-push canary and manual UI re-review.
- Deletion of `ai-cr:review`, `ai-cr:passed`, `ai-cr:failed`, and `ai-cr:error`.

**Out of scope:**

- Automatic review on `main` or draft PRs.
- AI scoring, pass/fail output, comment parsing, retry workflows, or merge gates.
- Required build/test checks, CODEOWNERS, required approvals, or thread resolution.
- Application, database, deployment, Azure, or E2E changes.

## Architecture / Approach

The tracked skill supplies policy to native Copilot, while the GitHub ruleset owns when review runs. The old workflow/publisher layer is removed entirely; deterministic CI remains a separate concern. Rollout order is entitlement check, repository migration, immediate ruleset enablement, canary validation, then label cleanup.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Native review policy | Trackable skill, 15 criteria, instruction routing, entitlement confirmation | Broad `.gitignore` exception could expose local tooling |
| 2. Provider cutover | GitHub Models removal and `Protect develop` auto-review | Accepted interval without automatic review and no fallback |
| 3. Canary and cleanup | Proven review lifecycle and removal of stale labels | Native review may ignore or weakly apply repository policy |

**Prerequisites:** Copilot Code Review entitlement, repository policy permission, admin access to rulesets and labels, and acceptable `Balanced` AI-credit budget.

**Estimated effort:** About 2–3 implementation sessions across 3 phases, plus GitHub review latency during the canary.

## Open Risks & Assumptions

- Copilot reads instructions from PR head content, so the skill is not a trusted merge-control boundary.
- Native review behavior, skill attribution, latency, and credit usage are product-managed and may change.
- Removing the old pipeline before canary creates an accepted, minimized review gap with no code fallback.
- The repository still lacks required PR build/test checks and human approval; this migration does not close that governance gap.

## Success Criteria (Summary)

- Eligible ready PRs to `develop` receive `Balanced` Copilot `Comment` reviews, while drafts and `main` PRs do not.
- Reviews use the PlanDeck policy, refresh after a push, support manual UI retry, and avoid false CI or merge claims.
- No GitHub Models implementation or `ai-cr:*` labels remain after the canary succeeds.
