# Replace GitHub Models Integration Implementation Plan

## Overview

Replace the discontinued, repository-maintained GitHub Models pipeline with native GitHub Copilot Code Review for non-draft pull requests targeting `develop`. Preserve the valuable part of the old solution -- the 15-criterion PlanDeck review policy -- as a repository skill, while accepting that native Copilot review is advisory and does not provide a structured verdict, outcome labels, or a merge gate.

The migration deliberately removes the old provider-specific workflow before the production canary. Copilot entitlement and AI-credit budget must therefore be confirmed first, and the repository ruleset must be enabled immediately after the repository change lands to minimize the accepted interval without automatic review.

## Current State Analysis

The repository currently contains a complete custom GitHub Models review implementation in `HEAD`, while the working tree already marks its workflow and all local action files for deletion. That implementation owns model invocation, schema validation, marker comments, retry handling, and the `ai-cr:*` labels; native Copilot Code Review cannot preserve those machine-readable contracts.

PlanDeck's review knowledge is split between the detailed 15 criteria in the archived requirements and the concise, provider-oriented policy being deleted. The repository's general Copilot instructions contain the application architecture and code-quality conventions but do not direct code review to a dedicated skill. In addition, `.gitignore` currently excludes all local tooling under `.github/*`, so a new tracked review skill needs a narrow exception.

Automatic Copilot review is configured through the GitHub repository ruleset, not through a versioned workflow. The active `Protect develop` ruleset does not yet request Copilot review, and the repository has no required PR build/test checks. The latter is an independent governance gap and is outside this migration.

## Desired End State

Every non-draft pull request targeting `develop` automatically receives native GitHub Copilot Code Review at `Balanced` effort, and a new review is requested after each push. Reviewers can also request another review manually in the GitHub UI.

Copilot uses a tracked `code-review` skill containing all 15 PlanDeck criteria as an analysis checklist and publishes only concrete, high-confidence findings. Reviews remain advisory `Comment` reviews and never claim approval, request changes, infer a merge decision, or invent build/test results.

The GitHub Models workflow, local action, schema, scripts, fixtures, publisher, and provider documentation are absent. After a successful canary pull request with two pushes, all obsolete `ai-cr:*` labels are removed from the repository.

### Key Discoveries:

- Native Copilot review supports automatic review and review of new pushes through a ruleset, but it always produces `Comment` and cannot recreate the old deterministic outcome (`context/changes/replace-github-models/research.md:134-188`).
- The durable asset is the 15-criterion review policy in `context/archive/2026-07-28-ci-cd-code-review/requirements.md:12-178`; the scoring, JSON schema, publisher, and labels are provider-specific contracts.
- The existing instructions already define PlanDeck's architecture and quality constraints, so the new skill should reference rather than duplicate them (`.github/copilot-instructions.md:71-87`).
- `.gitignore` excludes `/.github/*` except workflows and actions; the review skill requires a narrowly scoped exception without tracking all local 10x skills (`.gitignore:430-434`).
- Copilot reads custom instructions and skills from the pull request head, so those files are untrusted for merge governance; AI review must remain advisory (`context/changes/replace-github-models/research.md:288-304`).
- Existing deployment workflows run after pushes and are not PR quality gates; adding required checks is a separate change (`context/changes/replace-github-models/research.md:274-286`).

## What We're NOT Doing

- Enabling automatic Copilot review for pull requests targeting `main`.
- Reviewing draft pull requests.
- Adding a workflow or label for retry; new pushes and the GitHub UI are the supported retry paths.
- Recreating 1-10 scoring, JSON output, `passed`/`failed` verdicts, marker comments, or an AI-based status check.
- Parsing Copilot comments or treating the absence of a finding as approval.
- Adding required build/test checks, CODEOWNERS, human-approval requirements, or other ruleset governance.
- Changing application code, tests, deployment workflows, database schema, or Azure infrastructure.
- Retaining the GitHub Models implementation as a disabled fallback.

## Implementation Approach

First establish the replacement policy surface: confirm Copilot availability and budget, make only the new review skill trackable, preserve all 15 criteria as a checklist, and point the repository instructions to that skill. Validate the skill and policy before deleting the old provider implementation.

Then perform the intentionally one-way cutover: remove every GitHub Models artifact and enable automatic `Balanced` review in `Protect develop` for non-draft pull requests and new pushes. Do not add compatibility automation for old labels or outcomes.

Finally validate the real GitHub behavior with a controlled draft-to-ready canary pull request and two pushes. Only after the canary proves automatic review, skill use, review state, draft exclusion, and re-review behavior should the obsolete labels be removed.

## Critical Implementation Details

### Timing & lifecycle

Copilot entitlement and AI-credit budget are a hard prerequisite, but the canary occurs only after the old workflow is removed. Minimize the accepted review gap by enabling the `Protect develop` rule immediately after the repository change lands, then open the canary without waiting for another release.

### State sequencing

The required order is: confirm entitlement and budget; land the tracked skill, instruction update, and old-pipeline deletion; enable the ruleset; pass the two-push canary; remove `ai-cr:*` labels. Label removal before canary success would erase the remaining visible history without proving the replacement works.

### Security boundary

The skill is guidance loaded from pull request content, not trusted policy enforcement. It must explicitly avoid approval or merge verdicts, and no later phase may promote Copilot comments into a status check by parsing their text.

## Phase 1: Establish the Native Review Policy

### Overview

Confirm that native Copilot Code Review is available and funded, then create the tracked review skill that preserves all 15 PlanDeck criteria without carrying forward provider-specific scoring or verdict behavior.

### Changes Required:

#### 1. Confirm Copilot review availability

**Surface**: GitHub account and repository settings

**Intent**: Prevent removal of the current automatic review before the replacement is known to be available for this repository.

**Contract**: The repository owner confirms that the account or organization plan includes Copilot Code Review, repository policy permits automatic review, and the available AI-credit budget is acceptable for `Balanced` reviews on every eligible push.

#### 2. Track only the repository review skill

**File**: `.gitignore`

**Intent**: Allow GitHub to receive the new review skill without exposing the other local 10x tooling currently excluded from version control.

**Contract**: Keep the general `/.github/*` ignore rule and existing workflow/action exceptions, then add exceptions only for `.github/skills/`, `.github/skills/code-review/`, and the files below that skill. Other `.github/skills/*` directories remain ignored.

#### 3. Define the code review skill

**File**: `.github/skills/code-review/SKILL.md`

**Intent**: Give native Copilot Code Review a concise, review-specific procedure that routes detailed policy to a reference file.

**Contract**: The skill has valid `name` and `description` frontmatter and instructs code review to:

- apply all 15 criteria as an internal checklist;
- report only concrete, high-confidence issues tied to changed code;
- prioritize correctness, security, data loss, architecture boundaries, and demonstrated test failures;
- distinguish static review from deterministic build/test evidence;
- use `.github/copilot-instructions.md` for PlanDeck architecture;
- avoid exhaustive scoring, approval, request-changes, status-check, and merge-decision claims.

#### 4. Preserve the full policy

**File**: `.github/skills/code-review/references/review-policy.md`

**Intent**: Retain the accepted Definition of Done while adapting it from a structured model contract to native review guidance.

**Contract**: The reference contains exactly the same 15 stable criterion identifiers and substantive checks derived from `context/archive/2026-07-28-ci-cd-code-review/requirements.md:12-168`. It preserves blocker classes and the prohibition on invented test results, but removes numeric scoring guidance, pass/fail thresholds, expected labels, retry behavior, JSON schema assumptions, and provider-specific input limits.

#### 5. Route repository reviews to the skill

**File**: `.github/copilot-instructions.md`

**Intent**: Make the review policy discoverable without duplicating its criteria in the already substantial general instruction file.

**Contract**: Add a short code-review section that directs Copilot reviews to `.github/skills/code-review/SKILL.md`, states that AI findings are advisory, and leaves the existing stack, architecture, E2E, and build rules unchanged.

### Success Criteria:

#### Automated Verification:

- The new review skill is not ignored while sibling local skills remain ignored: `git check-ignore -q -- .github/skills/code-review/SKILL.md` returns non-zero, and `git check-ignore -q -- .github/skills/10x-plan/SKILL.md` returns zero.
- Skill structure and frontmatter validate: `gh skill publish --dry-run .github/skills`.
- The policy contains exactly 15 unique criterion identifiers matching the archived requirements.
- Repository whitespace and patch integrity checks pass: `git diff --check`.
- The full solution builds successfully: `dotnet build src/PlanDeck/PlanDeck.slnx`.

#### Manual Verification:

- Copilot Code Review entitlement, repository availability, and `Balanced` AI-credit budget are confirmed before Phase 2.
- The policy preserves the substance of all 15 criteria and blocker classes without retaining scoring or verdict semantics.
- The skill produces focused finding guidance rather than a mandatory 15-section report.
- No unrelated local skill is made trackable by the `.gitignore` change.

**Implementation Note**: After completing this phase and all automated verification passes, pause for manual confirmation that Copilot access and budget are available before proceeding to the irreversible cutover.

---

## Phase 2: Remove GitHub Models and Cut Over `develop`

### Overview

Delete the provider-specific implementation and immediately configure the repository ruleset to request native Copilot review for the same PR population the old workflow covered.

### Changes Required:

#### 1. Remove the GitHub Models workflow

**File**: `.github/workflows/ai-code-review.yml`

**Intent**: Stop invoking the discontinued GitHub Models integration and eliminate the old review/publish orchestration.

**Contract**: Delete the workflow completely. Do not replace it with a retry workflow, comment parser, compatibility status check, or another model invocation.

#### 2. Remove the provider-specific local action

**Files**:

- `.github/actions/ai-code-review/README.md`
- `.github/actions/ai-code-review/action.yml`
- `.github/actions/ai-code-review/review.ps1`
- `.github/actions/ai-code-review/review-prompt.md`
- `.github/actions/ai-code-review/review-policy.md`
- `.github/actions/ai-code-review/review-result.schema.json`
- `.github/actions/ai-code-review/validate-review-result.ps1`
- `.github/actions/ai-code-review/publish-review.ps1`
- `.github/actions/ai-code-review/fixtures/*.json`

**Intent**: Remove model selection, structured output, trusted validation, publishing, and label state that native Copilot cannot or should not emulate.

**Contract**: The entire `.github/actions/ai-code-review/` tree is absent. The migrated policy under `.github/skills/code-review/` is the sole retained review knowledge.

#### 3. Enable automatic review on `develop`

**Surface**: GitHub repository ruleset `Protect develop`

**Intent**: Restore automatic review immediately after the old workflow is removed.

**Contract**: Configure the existing `refs/heads/develop` ruleset with:

- `Automatically request Copilot code review`: enabled;
- `Review new pushes`: enabled;
- `Review draft pull requests`: disabled;
- review effort: `Balanced`.

Do not modify `Protect main`, required status checks, required approvals, or thread-resolution rules in this change.

### Success Criteria:

#### Automated Verification:

- All files under `.github/actions/ai-code-review/` and `.github/workflows/ai-code-review.yml` are absent.
- Tracked `.github` content has no GitHub Models endpoint, `models: read`, or `openai/gpt-4.1-mini` reference.
- Repository whitespace and patch integrity checks pass: `git diff --check`.
- The full solution builds successfully: `dotnet build src/PlanDeck/PlanDeck.slnx`.

#### Manual Verification:

- The merged repository contains the tracked `code-review` skill before the ruleset is enabled.
- `Protect develop` shows automatic Copilot review enabled with new-push review, drafts disabled, and `Balanced` effort.
- `Protect main` and all deterministic merge requirements remain unchanged.
- No old workflow run can be manually or automatically started after the cutover.

**Implementation Note**: This phase intentionally has no fallback to the old provider. Complete the ruleset operation immediately after the repository change lands, then proceed directly to the canary.

---

## Phase 3: Validate the Canary and Retire Legacy Labels

### Overview

Prove the native review behavior in a controlled pull request, including draft exclusion and re-review after a second push, then remove labels whose semantics no longer exist.

### Changes Required:

#### 1. Run a draft-to-ready canary pull request

**Surface**: GitHub pull request targeting `develop`

**Intent**: Validate the actual GitHub product behavior and confirm that the repository policy is being used before declaring the migration complete.

**Contract**: Create a uniquely named draft PR with a harmless, reviewable change that exercises at least one PlanDeck-specific criterion. Confirm no automatic review while it is draft, mark it ready, and verify a Copilot `Comment` review with findings grounded in changed lines and the repository skill.

#### 2. Validate re-review behavior

**Surface**: The same canary pull request

**Intent**: Prove that the selected replacement for label-triggered retry works for normal iteration and that a manual retry path remains available.

**Contract**: Push a second commit and confirm Copilot performs a fresh review of the new head. Request another review from the GitHub UI and confirm that no repository workflow or special label is needed.

#### 3. Remove obsolete labels

**Surface**: GitHub repository labels

**Intent**: Prevent stale `ai-cr:*` labels from being mistaken for active merge or review state after the old publisher is gone.

**Contract**: After the canary passes, delete `ai-cr:review`, `ai-cr:passed`, `ai-cr:failed`, and `ai-cr:error`. Do not rename or archive them, and do not alter unrelated labels.

### Success Criteria:

#### Automated Verification:

- The repository has no remaining `ai-cr:*` labels: `gh label list --limit 100 --json name --jq '.[].name'` returns no name beginning with `ai-cr:`.
- The old review workflow remains absent after the canary and label cleanup.

#### Manual Verification:

- The draft canary receives no automatic review before it is marked ready.
- Marking the PR ready produces a Copilot review with state `Comment`, not `Approve` or `Request changes`.
- Review output demonstrates use of the `code-review` skill and does not claim build/test execution or a merge verdict.
- The second push receives a fresh review of the current head.
- Manual re-review from the GitHub UI works without a workflow or label.
- Deleting the four legacy labels does not change unrelated repository labels.

**Implementation Note**: This is the final phase. Do not remove the legacy labels or mark the migration complete until every canary criterion has been observed.

---

## Testing Strategy

### Static Contract Verification:

- Validate skill frontmatter and directory structure with the installed GitHub skill tooling.
- Compare the policy's 15 stable identifiers against the archived requirements and reject missing, duplicate, or renamed criteria.
- Verify `.gitignore` exposes only the new repository skill.
- Search tracked `.github` files for provider endpoint, model ID, model permission, old workflow, and publisher remnants.
- Run `git diff --check` and the full solution build after repository-file changes.

### GitHub Integration Verification:

- Confirm the `Protect develop` ruleset configuration in repository settings.
- Exercise draft exclusion, ready-for-review activation, automatic review, new-push review, and manual UI re-review on one canary PR.
- Inspect the review state and available Copilot session details to verify advisory output and skill use.
- Verify label cleanup through the GitHub API or CLI.

### Manual Testing Steps:

1. Confirm Copilot Code Review entitlement and acceptable AI-credit budget.
2. Land the tracked skill, policy, instruction update, and GitHub Models deletion.
3. Enable automatic `Balanced` review and review of new pushes in `Protect develop`; keep draft review disabled.
4. Open a uniquely named draft canary PR targeting `develop` and confirm no review is created.
5. Mark the canary ready and confirm a `Comment` review tied to the current head.
6. Inspect findings and session details for evidence that the repository skill and PlanDeck-specific policy were applied.
7. Push a second harmless commit and confirm a new review is created for the new head.
8. Request another review manually from the GitHub UI and confirm it requires no label or workflow.
9. Delete all four `ai-cr:*` labels and verify unrelated labels remain.
10. Confirm a PR targeting `main` does not receive automatic review from this ruleset.

## Performance Considerations

`Balanced` review consumes more AI credits and time than `Lite`, and `Review new pushes` may trigger multiple reviews during rapid iteration. Draft exclusion is the chosen cost control: unfinished work does not consume review credits, while every ready-state push remains covered.

No local model calls, diff serialization, schema validation, artifacts, or publisher jobs remain. This removes the repository's previous token budgeting and workflow-run overhead; ongoing cost and latency are owned by the native GitHub feature.

## Migration Notes

No application, database, or Azure migration is required.

This is an intentionally one-way operational migration. The old provider implementation is removed before the canary and is not retained as disabled fallback code. Entitlement validation reduces but does not eliminate the accepted risk of a temporary period without automatic review between merging the repository change and enabling the ruleset.

The ruleset and repository labels are remote GitHub state, not files in this repository. Their changes must be recorded through the manual Progress items and canary evidence; they cannot be inferred from the committed diff.

Required status checks, CODEOWNERS, human approvals, and protection of AI instruction files should be planned as a separate governance change. They must not be smuggled into this migration because they alter merge behavior independently of the provider replacement.

## References

- Research: `context/changes/replace-github-models/research.md`
- Previous requirements: `context/archive/2026-07-28-ci-cd-code-review/requirements.md`
- Previous implementation plan: `context/archive/2026-07-28-ci-cd-code-review/plan.md`
- Repository instructions: `.github/copilot-instructions.md:71-87`
- Ignore rules for local agent tooling: `.gitignore:430-434`
- GitHub automatic review setup: https://docs.github.com/en/copilot/how-tos/copilot-on-github/set-up-copilot/configure-automatic-review
- GitHub Copilot code review behavior: https://docs.github.com/en/copilot/how-tos/use-copilot-agents/request-a-code-review/use-code-review

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Establish the Native Review Policy

#### Automated

- [x] 1.1 The new review skill is not ignored while sibling local skills remain ignored — 089796d
- [x] 1.2 Skill structure and frontmatter validate — 089796d
- [x] 1.3 The policy contains exactly 15 unique criterion identifiers matching the archived requirements — 089796d
- [x] 1.4 Repository whitespace and patch integrity checks pass — 089796d
- [x] 1.5 The full solution builds successfully — 089796d

#### Manual

- [x] 1.6 Copilot Code Review entitlement, repository availability, and `Balanced` AI-credit budget are confirmed before Phase 2 — 089796d
- [x] 1.7 The policy preserves the substance of all 15 criteria and blocker classes without retaining scoring or verdict semantics — 089796d
- [x] 1.8 The skill produces focused finding guidance rather than a mandatory 15-section report — 089796d
- [x] 1.9 No unrelated local skill is made trackable by the `.gitignore` change — 089796d

### Phase 2: Remove GitHub Models and Cut Over `develop`

#### Automated

- [x] 2.1 All files under `.github/actions/ai-code-review/` and `.github/workflows/ai-code-review.yml` are absent
- [x] 2.2 Tracked `.github` content has no GitHub Models endpoint, `models: read`, or `openai/gpt-4.1-mini` reference
- [x] 2.3 Repository whitespace and patch integrity checks pass
- [x] 2.4 The full solution builds successfully

#### Manual

- [x] 2.5 The merged repository contains the tracked `code-review` skill before the ruleset is enabled
- [x] 2.6 `Protect develop` shows automatic Copilot review enabled with new-push review, drafts disabled, and `Balanced` effort
- [x] 2.7 `Protect main` and all deterministic merge requirements remain unchanged
- [x] 2.8 No old workflow run can be manually or automatically started after the cutover

### Phase 3: Validate the Canary and Retire Legacy Labels

#### Automated

- [ ] 3.1 The repository has no remaining `ai-cr:*` labels
- [ ] 3.2 The old review workflow remains absent after the canary and label cleanup

#### Manual

- [ ] 3.3 The draft canary receives no automatic review before it is marked ready
- [ ] 3.4 Marking the PR ready produces a Copilot review with state `Comment`, not `Approve` or `Request changes`
- [ ] 3.5 Review output demonstrates use of the `code-review` skill and does not claim build/test execution or a merge verdict
- [ ] 3.6 The second push receives a fresh review of the current head
- [ ] 3.7 Manual re-review from the GitHub UI works without a workflow or label
- [ ] 3.8 Deleting the four legacy labels does not change unrelated repository labels
