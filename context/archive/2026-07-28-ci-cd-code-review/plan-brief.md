# CI/CD Pull Request Code Review — Plan Brief

> Full plan: `context/changes/ci-cd-code-review/plan.md`
> Research: `context/changes/ci-cd-code-review/research.md`

## What & Why

Add an advisory AI review to each eligible, non-draft pull request targeting `develop`, and add a separate copy of the current Testing deployment workflow for pushes to `develop`. The review scores 15 criteria and manages retryable `ai-cr:*` labels without executing PR code or storing a model credential.

## Starting Point

The repository deploys pushes to `main` with GitHub Actions but has no `develop` deployment, PR review workflow, composite action, AI policy/schema, or review labels. `develop` exists only locally, its OIDC trust is not configured, and no PR-triggered build/test signal is available.

## Desired End State

A fork-safe `pull_request_target` workflow invokes GitHub Models once, validates all 15 scores, calculates pass/fail in trusted PowerShell, and updates one bot comment plus additive labels. A separate `develop` deployment workflow mirrors the current `main` Testing deployment; shared concurrency serializes both until `main` later moves independently to Production.

## Key Decisions Made

| Decision | Choice | Why | Source |
| --- | --- | --- | --- |
| PR target | `develop` only | Review integration changes, not deployment branch `main` | Plan |
| Review engine | Custom GitHub Actions workflow | Native Copilot cannot provide the required schema, labels, or retry | Plan |
| Provider | GitHub Models | Run-scoped `GITHUB_TOKEN`; no Azure infrastructure or secret | Plan |
| Model | `openai/gpt-4.1-mini` | Low-tier model suited to code and instruction following | Plan |
| Usage tier | Free MVP; paid production path | Prototype now while documenting the 8k-token limit | Plan |
| Request | One bounded call | Lowest cost and simplest workflow | Plan |
| Context | Compact 15-criterion PlanDeck policy | Preserve requirements while reserving capacity for the diff | Plan |
| Verdict | Complete, no blockers, every score ≥7 | Matches the “good/safe” scoring boundary | Research / Plan |
| Incomplete input | `failed` with limitation | Prevent partial analysis from producing a false pass | Research |
| Automation failure | `ai-cr:error`; preserve pass/fail | Separate infrastructure failure from code quality | Plan |
| CI signal | Static review only; unavailable | No PR build/test workflow exists | Research / Plan |
| Trigger details | Skip drafts; metadata edits use retry | Control cost and noise | Plan |
| Labels | Provision on each run | Avoid a separate setup workflow | Plan |
| Merge policy | Advisory only | Calibrate feedback before governance gates | Plan |
| Protection | No CODEOWNERS or branch rules | Explicitly selected MVP scope | Plan |
| Develop deployment | Separate copy of current Testing workflow | Allows `main` to diverge to Production later | Plan |
| Deployment concurrency | Shared, non-cancelling Testing group | Prevent simultaneous mutation of the same environment | Plan |

## Scope

**In scope:**
- `pull_request_target` for non-draft PRs to `develop`
- trusted composite action, compact policy, JSON Schema, validator, and fixtures
- one GitHub Models request using `models: read`
- marker comment and additive `ai-cr:passed/failed/error/review` labels
- retry, stale-SHA protection, concurrency, documentation, and smoke checks
- separate `develop` Testing deployment and branch-specific OIDC trust

**Out of scope:**
- native Copilot review, branch protection, required checks, and CODEOWNERS
- build/test execution or CI-result consumption
- agentic tools, repository-wide retrieval, and multi-call chunking
- Azure AI infrastructure and model secrets
- new test frameworks or projects
- converting `main` to Production or creating production infrastructure

## Architecture / Approach

The review job has read/model permissions and treats the PR head only as data; a separate publisher has PR/issue write permissions and owns comments/labels. Independently, `azure-develop.yml` mirrors the current Testing steps from `azure-dev.yml`; both deployment workflows share a concurrency group while targeting the same Azure environment.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Trusted contract | Policy, schema, validator, fixtures | Losing criteria while reducing prompt size |
| 2. Composite action | Safe diff and one model request | Violating the `pull_request_target` trust boundary |
| 3. Workflow/publishing | Triggers, permissions, comment, labels, retry | Stale result or destructive label update |
| 4. Develop deployment/rollout | Mirrored Testing deploy, docs, and smoke validation | Shared environment or incorrect OIDC subject |

**Prerequisites:** Enable GitHub Models; land trusted files on `main`; create remote `develop`; add an OIDC federated credential for `refs/heads/develop`.
**Estimated effort:** Approximately 3-4 implementation sessions plus manual GitHub smoke testing.

## Open Risks & Assumptions

- Free GitHub Models usage is preview/prototyping-only and caps input at 8,000 tokens.
- `develop` is not currently present on `origin`.
- Workflow protection relies on normal maintainer review because CODEOWNERS/branch protection are excluded.
- GitHub Models behavior and limits may change; paid usage is the production upgrade.
- Static AI review cannot detect actual build or test failures.
- Until `main` moves to Production, both branch workflows deploy the same Testing environment and must remain serialized.

## Success Criteria (Summary)

- Eligible PRs to `develop` receive exactly one current comment and the correct advisory label.
- Trusted code rejects malformed, incomplete, stale, or blocker-bearing results.
- Retry/error handling is idempotent and preserves unrelated labels.
- No PR code executes and no token or full review payload appears in logs/comments.
- Pushes to `develop` deploy Testing through branch-specific OIDC while `main` keeps its separate workflow for future Production changes.
