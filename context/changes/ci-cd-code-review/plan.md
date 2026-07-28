# CI/CD Pull Request Code Review Implementation Plan

## Overview

Implement an advisory GitHub Actions workflow that reviews every eligible, non-draft pull request targeting `develop`. A trusted repository-local composite action will collect the pull request title, bounded description, and three-dot diff without executing pull request code, submit one schema-constrained request to GitHub Models, and publish one bot-owned summary comment with deterministic `ai-cr:*` labels.

The MVP uses GitHub Models free API usage and `openai/gpt-4.1-mini`. It deliberately optimizes for small and medium commits: input is capped below the free-tier limit, oversized reviews are marked incomplete, and trusted code refuses to emit a passing verdict for incomplete analysis.

The change also introduces a separate copy of the existing Azure deployment workflow for `develop`. Both `main` and `develop` deploy to the current Testing target for now; the workflows are kept separate so the `main` variant can later diverge to Production without changing the `develop` test deployment.

## Current State Analysis

- The repository has one active GitHub Actions workflow, which deploys pushes to `main`; it does not run for pull requests (`.github/workflows/azure-dev.yml:17-33`).
- The existing deployment identity's documented OIDC subject is limited to `refs/heads/main`; a copied `develop` workflow needs an additional federated credential before Azure login can succeed (`.github/workflows/azure-dev.yml:3-15`).
- The requirements define 15 review criteria, a 1-10 or `N/A` score, one summary comment, pass/fail labels, and label-triggered retry (`context/changes/ci-cd-code-review/requirements.md:12-192`).
- The requirements still name `master`, while the confirmed target for this change is `develop` (`context/changes/ci-cd-code-review/requirements.md:1-4`).
- `develop` exists locally but not on `origin`; the repository default branch is `main`. The remote branch must exist before a PR can target it.
- No pull request workflow, local composite action, AI review prompt/schema, `ai-cr:*` labels, CODEOWNERS file, branch protection, or required status checks exist.
- GitHub Models can be called from GitHub Actions with the run-scoped `GITHUB_TOKEN` and `models: read`; no long-lived model credential is required.
- Free GitHub Models usage is intended for prototyping and caps input at 8,000 tokens per request. The action must use a compact trusted policy and fail closed when it cannot analyze the complete reviewable diff.
- There is no PR-triggered build/test workflow whose result can be included in the verdict. This review is static analysis only and must say that the CI signal is unavailable.

## Desired End State

For each eligible pull request whose base branch is `develop`, GitHub Actions runs trusted code from the base/default revision, reads the pull request head only as diff data, and makes one bounded GitHub Models request. The model returns strict structured data for all 15 criteria; trusted PowerShell validates that data and calculates the verdict.

The workflow updates one `github-actions[bot]` marker comment and manages labels additively:

- complete analysis, no blockers, and every applicable score at least 7: `ai-cr:passed`;
- complete analysis with a blocker or any applicable score below 7: `ai-cr:failed`;
- truncated or otherwise incomplete analysis: `ai-cr:failed`, with the limitation shown;
- automation/model/schema/publishing failure: `ai-cr:error`, without changing the last `passed` or `failed` label;
- adding `ai-cr:review`: re-runs the review and consumes the retry label.

The workflow remains advisory: it does not configure branch protection and does not block merging.

Pushes to `develop` also run a dedicated copy of the existing Azure deployment workflow and publish the PlanDeck Testing target to the same test environment currently used by `main`. The two deployment workflows share a repository-wide concurrency group so they cannot mutate the test environment simultaneously. The existing `main` deployment behavior remains Testing in this change and can be changed independently to Production later.

### Key Discoveries:

- `pull_request_target` is needed for fork-safe access to the base repository token, but it is safe only when pull request code is never checked out or executed (`context/changes/ci-cd-code-review/research.md:66-100`).
- Review and publishing require separate jobs so model access and pull request write access do not coexist (`context/changes/ci-cd-code-review/research.md:87-100`).
- The active deployment workflow establishes explicit least-privilege permissions and GitHub-hosted Ubuntu runners (`.github/workflows/azure-dev.yml:30-37`).
- The compact policy must preserve PlanDeck's inward dependencies, code-first gRPC boundaries, server DI convention, Razor code-behind rule, MudBlazor preference, localization, and full-build requirement (`.github/copilot-instructions.md:71-87`).
- Native GitHub Copilot code review cannot provide the required schema, deterministic verdict, labels, marker comment, or label-triggered retry. GitHub Models keeps those contracts under repository control.
- `openai/gpt-4.1-mini` is a low-tier GitHub Models catalog model optimized for coding, instruction following, and long-context handling; the free API tier, rather than the model context window, is the MVP input constraint.

## What We're NOT Doing

- Enabling native GitHub Copilot code review as a second reviewer.
- Blocking merges or changing branch protection/rulesets.
- Adding CODEOWNERS protection.
- Running builds, tests, package installation, generators, or any pull request code.
- Consuming CI check results; the MVP explicitly reports `CI signal: unavailable`.
- Reviewing draft pull requests or automatically re-reviewing title/body-only edits.
- Reviewing pull requests targeting `main`, `master`, or any branch other than `develop`.
- Performing multi-call chunking, retrieval, repository-wide semantic search, tools, or agentic execution.
- Adding a new .NET test project, Pester, Node.js test framework, or another test dependency.
- Provisioning Azure AI resources, OIDC identities, API keys, or other model-provider infrastructure.
- Making GitHub Models paid usage mandatory for the MVP; the documentation will identify paid usage as the production upgrade path.
- Converting the `main` deployment workflow to Production in this change.
- Creating a production Azure environment or changing the current Testing resource group.

## Implementation Approach

Keep orchestration readable in one workflow and place review mechanics in a local composite action. Use PowerShell because it is already used by the repository's GitHub automation, runs on `ubuntu-latest`, provides native JSON/HTTP handling, and requires no package restore.

The review job receives only `contents: read` and `models: read`. It checks out the trusted base revision with persisted credentials disabled, fetches the pull request head object through `refs/pull/<number>/head`, verifies the fetched SHA, produces a three-dot diff, builds a compact prompt, and calls GitHub Models once. It uploads a short-lived result artifact.

The publishing job runs with `issues: write` and `pull-requests: write`, but no `models: read`. It downloads and validates the artifact, verifies that the reviewed SHA is still current, computes the verdict in trusted code, ensures the four labels exist, updates one marker comment, and changes only the `ai-cr:*` labels it owns.

Copy the existing deployment workflow into a branch-specific `develop` workflow rather than adding a branch condition to one shared file. Preserve the current Testing publish target and Azure variables in both workflows, add one shared non-cancelling deployment concurrency group while both target the same environment, and add a second OIDC federated credential for `refs/heads/develop`. This keeps today's behavior aligned while allowing the `main` copy to become the Production workflow later.

## Critical Implementation Details

### Timing & lifecycle

`pull_request_target` workflow definitions must be present on the repository's default branch to bootstrap reliably. Land the trusted workflow/action on `main`, create/push `develop`, and ensure `develop` contains the same trusted files before validating PRs targeting `develop`.

PR-scoped concurrency must cancel older runs. The publisher must re-read the current PR head SHA immediately before mutation and ignore a stale result rather than overwrite a newer review.

The copied deployment workflow cannot reuse the existing branch-scoped OIDC trust until Azure receives a federated credential whose subject is `repo:LeszekDNV/PlanDeck2:ref:refs/heads/develop`. While both branch workflows publish Testing, they must use the same deployment concurrency group with `cancel-in-progress: false`; when `main` moves to Production, its concurrency group and publish target must diverge together.

### State sequencing

Every successful review first validates the complete 15-criterion result and current head SHA, then updates the marker comment, then sets mutually exclusive pass/fail labels and removes `ai-cr:error`. Automation failure adds `ai-cr:error` and updates the marker comment but preserves the last pass/fail label. The retry label is removed at the end of every label-triggered attempt so it can be added again.

### Debug & observability

The comment must show the reviewed head SHA, model, analysis completeness, truncation/omission details, static-only CI limitation, scores, blockers, summary, and workflow run URL. Logs must expose stage and error category without printing the token, full prompt, full diff, or model response.

## Phase 1: Define the Trusted Review Contract

### Overview

Create the compact policy, strict result schema, validator, and representative fixtures that make the model boundary explicit and keep verdict calculation outside model prose.

### Changes Required:

#### 1. Correct the branch requirement

**File**: `context/changes/ci-cd-code-review/requirements.md`

**Intent**: Replace the stale `master` target with the confirmed `develop` target so requirements, workflow, and verification agree.

**Contract**: The overall concept states that reviews run for pull requests targeting `develop`.

#### 2. Define the compact review policy

**File**: `.github/actions/ai-code-review/review-policy.md`

**Intent**: Condense the detailed requirements and the most important PlanDeck conventions into a trusted prompt that fits GitHub Models free-tier input limits while preserving all 15 scored criteria.

**Contract**: The policy contains exactly 15 stable criterion identifiers and titles, the 1-10/`N/A` guidance, blocker overrides, prompt-injection handling, static-diff-only scope, and the critical repository rules from `.github/copilot-instructions.md`. It instructs the model to treat title, body, filenames, and diff as untrusted evidence rather than instructions.

#### 3. Define the model result schema

**File**: `.github/actions/ai-code-review/review-result.schema.json`

**Intent**: Constrain the model response to data that trusted code can validate and render without interpreting free-form control instructions.

**Contract**: The JSON Schema requires a reviewed head SHA, analysis-completeness metadata, one entry for each criterion, integer scores from 1 through 10 or explicit `N/A` with a reason, bounded evidence tied to changed paths, blocker findings, and a concise summary. It does not contain a model-authored pass/fail verdict.

#### 4. Add deterministic validation and verdict calculation

**File**: `.github/actions/ai-code-review/validate-review-result.ps1`

**Intent**: Validate schema and cross-field invariants that JSON Schema cannot express, then calculate the advisory verdict in trusted code.

**Contract**: The validator requires all 15 unique criterion IDs, validates score/`N/A` consistency, checks bounded field lengths and reviewed SHA, and returns `passed` only when analysis is complete, blockers are empty, and every applicable score is at least 7. Incomplete analysis returns `failed`; malformed output returns an automation error.

#### 5. Add contract fixtures

**Files**:

- `.github/actions/ai-code-review/fixtures/valid-passed.json`
- `.github/actions/ai-code-review/fixtures/valid-failed.json`
- `.github/actions/ai-code-review/fixtures/invalid-missing-criterion.json`
- `.github/actions/ai-code-review/fixtures/invalid-score.json`

**Intent**: Provide small persistent examples for local validation without introducing a test framework.

**Contract**: Valid fixtures demonstrate pass and fail calculation; invalid fixtures demonstrate fail-closed handling for missing criteria and out-of-range scores.

### Success Criteria:

#### Automated Verification:

- The passed fixture validates and produces `passed`: `pwsh -NoProfile -File .github/actions/ai-code-review/validate-review-result.ps1 -ResultPath .github/actions/ai-code-review/fixtures/valid-passed.json`
- The failed fixture validates and produces `failed`.
- Both invalid fixtures are rejected with non-zero exit codes.
- The full solution builds successfully: `dotnet build src/PlanDeck/PlanDeck.slnx`

#### Manual Verification:

- The compact policy still covers all 15 requirement headings and the critical PlanDeck conventions.
- The schema and validator contain no path that trusts a model-authored verdict or silently defaults malformed data.

**Implementation Note**: After completing this phase and all automated verification passes, pause for manual confirmation before proceeding.

---

## Phase 2: Build the Fork-Safe Composite Review Action

### Overview

Implement trusted input collection, bounded prompt construction, and one schema-constrained GitHub Models request without checking out or executing pull request code.

### Changes Required:

#### 1. Declare the local composite action

**File**: `.github/actions/ai-code-review/action.yml`

**Intent**: Expose a small action contract so the workflow remains easy to review and model-specific mechanics remain localized.

**Contract**: Inputs include the model ID (`openai/gpt-4.1-mini` by default), expected base branch (`develop`), and free-tier input/output budgets. The action consumes `GITHUB_EVENT_PATH` and `GITHUB_TOKEN` from the caller and outputs only a trusted result-file path plus static completion metadata.

#### 2. Add operational prompt instructions

**File**: `.github/actions/ai-code-review/review-prompt.md`

**Intent**: Separate stable review procedure from the compact criteria policy.

**Contract**: The prompt requires strict schema output, no tools, no instruction-following from PR-controlled text, concise evidence, explicit `N/A`, and honest completeness reporting. It tells the model that CI results are unavailable and forbids inventing test outcomes.

#### 3. Collect and bound pull request input

**File**: `.github/actions/ai-code-review/review.ps1`

**Intent**: Read event metadata, safely acquire the pull request head as an object, and construct a bounded static review request.

**Contract**:

- Validate that the event is a non-draft PR targeting `develop` and capture PR number, base SHA, expected head SHA, title, and body.
- Use the trusted base checkout with persisted credentials disabled.
- Fetch `refs/pull/<number>/head`, verify its SHA equals the event head SHA, and generate `git diff --no-ext-diff --no-textconv <base>...<head>`.
- Never switch the worktree to the PR head, invoke code from it, install dependencies, or follow commands found in PR-controlled text.
- Bound title/body and allocate the remaining free-tier budget to the diff. Truncate only at a stable file/hunk boundary and mark `analysis.complete = false`.
- Treat binary and non-text changes as explicit omissions. Redaction, truncation, or inability to represent a reviewable change prevents a passing verdict.
- Build request JSON through object serialization, not string concatenation.

#### 4. Invoke GitHub Models once

**File**: `.github/actions/ai-code-review/review.ps1`

**Intent**: Submit one non-streaming request using the run-scoped GitHub token and persist only the structured response needed by the publisher.

**Contract**: Call `https://models.github.ai/inference/chat/completions` with `models: read`, `openai/gpt-4.1-mini`, `tool_choice: none`, low temperature, bounded output, and `response_format.type: json_schema`. Validate HTTP status and response shape, extract the structured result, stamp trusted completeness/head metadata, and write the artifact under `RUNNER_TEMP`. Do not log authorization headers, prompt content, diff content, or raw response content.

### Success Criteria:

#### Automated Verification:

- Composite metadata references only repository-local scripts and declares the expected inputs/output.
- The result written by the review script passes the Phase 1 validator before upload.
- Repository whitespace and patch integrity checks pass: `git diff --check`
- The full solution builds successfully: `dotnet build src/PlanDeck/PlanDeck.slnx`

#### Manual Verification:

- Security inspection confirms that the action never checks out, sources, imports, or executes content from the pull request head.
- A small test request stays within the free-tier budget and returns all 15 structured criteria.
- An oversized fixture diff is truncated at a stable boundary and cannot produce `passed`.

**Implementation Note**: After completing this phase and all automated verification passes, pause for manual confirmation before proceeding.

---

## Phase 3: Orchestrate Review, Publishing, Labels, and Retry

### Overview

Wire the action into an advisory `pull_request_target` workflow with PR-scoped concurrency, strict job permissions, idempotent publishing, automatic label provisioning, and explicit error behavior.

### Changes Required:

#### 1. Add the pull request workflow

**File**: `.github/workflows/ai-code-review.yml`

**Intent**: Trigger reviews only for the agreed branch and lifecycle events while keeping model and mutation permissions separated.

**Contract**:

- Trigger `pull_request_target` for base branch `develop` on `opened`, `synchronize`, `reopened`, `ready_for_review`, and `labeled`.
- Skip drafts. For `labeled`, proceed only when the added label is `ai-cr:review`.
- Use PR-number concurrency with `cancel-in-progress: true`.
- Set top-level permissions to none/read-minimal and grant per-job permissions.
- Review job: `contents: read`, `models: read`; no issue/PR write permission.
- Publish job: `issues: write`, `pull-requests: write`; no model permission.
- Check out only the trusted base revision with persisted credentials disabled.
- Upload/download the result as a one-day artifact. The publish job runs with `always()` and handles a missing/invalid artifact as `ai-cr:error`.
- Pin external/GitHub actions to reviewed immutable commit SHAs.

#### 2. Add the trusted publisher

**File**: `.github/actions/ai-code-review/publish-review.ps1`

**Intent**: Convert the validated result into one bounded comment and additive label changes without giving model output control over API operations.

**Contract**:

- Re-fetch PR metadata and reject stale reviewed SHAs before mutation.
- Validate the result again and calculate verdict through `validate-review-result.ps1`.
- Ensure `ai-cr:review`, `ai-cr:passed`, `ai-cr:failed`, and `ai-cr:error` exist with stable colors/descriptions; tolerate already-existing labels.
- Paginate issue comments and update only a comment containing `<!-- ai-cr:summary:v1 -->` authored by `github-actions[bot]`; create it when absent.
- Render bounded Markdown from known fields. Include head SHA, model, completeness, omissions/truncation, `CI signal: unavailable`, scores, blockers, summary, and run URL.
- On pass: add `ai-cr:passed`, remove `ai-cr:failed` and `ai-cr:error`.
- On review fail/incomplete: add `ai-cr:failed`, remove `ai-cr:passed` and `ai-cr:error`.
- On automation error: add `ai-cr:error` and preserve the last pass/fail label.
- Remove `ai-cr:review` at the end of every label-triggered attempt.
- Never replace unrelated labels or interpolate model text into shell/GitHub command syntax.

#### 3. Preserve failure visibility

**File**: `.github/workflows/ai-code-review.yml`

**Intent**: Publish actionable diagnostics while keeping the workflow's technical state truthful.

**Contract**: A code-review `failed` verdict leaves the advisory workflow technically successful after publishing. An automation error publishes the error state and then leaves the run failed. Cancellation/stale-result handling logs a clear reason and does not overwrite a newer review.

### Success Criteria:

#### Automated Verification:

- Workflow job permissions match the review/publish separation and no job combines `models: read` with PR mutation permissions.
- Publisher accepts both valid fixtures and rejects both invalid fixtures through the shared validator.
- Repository whitespace and patch integrity checks pass: `git diff --check`
- The full solution builds successfully: `dotnet build src/PlanDeck/PlanDeck.slnx`

#### Manual Verification:

- A non-draft PR to `develop` receives exactly one updated marker comment and the correct pass/fail label.
- Adding `ai-cr:review` starts a new review, updates the same comment, and removes the retry label.
- A simulated model/schema failure adds `ai-cr:error`, preserves the previous pass/fail label, updates diagnostics, and leaves the run failed.
- Draft PRs and PRs targeting branches other than `develop` do not invoke the model.
- Existing unrelated labels remain unchanged.

**Implementation Note**: After completing this phase and all automated verification passes, pause for manual confirmation before proceeding.

---

## Phase 4: Add Develop Deployment and Validate the Rollout

### Overview

Add a branch-specific copy of the current Testing deployment for `develop`, document the bootstrap and operating contracts, then validate AI review and deployment behavior on GitHub.

### Changes Required:

#### 1. Add the `develop` deployment workflow and serialize Testing deployments

**Files**:

- `.github/workflows/azure-dev.yml`
- `.github/workflows/azure-develop.yml`

**Intent**: Keep independent deployment definitions for `main` and `develop` so both publish Testing today while allowing the `main` workflow to become Production later.

**Contract**:

- Copy the existing deployment steps into `azure-develop.yml`, give it a distinct display name, and trigger pushes to `develop` plus manual dispatch.
- Preserve `PLANDECK_PUBLISH_TARGET: "Testing"`, the existing Azure repository variables, migration ordering, temporary SQL firewall lifecycle, and `rg-${AZURE_ENV_NAME}` target.
- Keep `azure-dev.yml` triggered by `main` and semantically unchanged in this phase.
- Add the same repository-wide deployment concurrency group to both workflows with `cancel-in-progress: false` while they target the same Testing resources.
- Document in the new workflow header that the existing Entra application needs a federated credential for `repo:LeszekDNV/PlanDeck2:ref:refs/heads/develop`.
- Document in the existing workflow header that moving `main` to Production is a later change and must also change its publish target, environment variables/resource scope, OIDC trust, and concurrency group.

#### 2. Document setup and operations

**File**: `.github/actions/ai-code-review/README.md`

**Intent**: Give maintainers a single operational reference for enabling, validating, retrying, and troubleshooting the workflow.

**Contract**: Document:

- prerequisite GitHub Models access and Actions permissions;
- why no model secret is needed;
- selected model and 8,000-input/4,000-output free-tier constraints;
- requirement to land trusted workflow/action files on `main`;
- requirement to create/push `develop` and synchronize trusted files into it;
- event behavior, draft exclusion, marker comment, label semantics, retry flow, and static-only CI limitation;
- one-call truncation/fail-closed behavior;
- free usage as prototype-only and paid GitHub Models as the production path;
- known lack of CODEOWNERS/branch protection and the resulting trust assumption;
- troubleshooting for missing artifact, 4xx/429 model response, malformed schema, stale SHA, and publishing failure.

#### 3. Add an implementation smoke-test checklist

**File**: `.github/actions/ai-code-review/README.md`

**Intent**: Make rollout verification repeatable without adding a test framework.

**Contract**: Include test PR scenarios for pass, score-based fail, incomplete/truncated fail, automation error, retry, comment idempotency, draft skip, non-`develop` skip, unrelated-label preservation, and fork-origin input safety.

#### 4. Reconcile research lineage

**File**: `context/changes/ci-cd-code-review/research.md`

**Intent**: Add a dated planning addendum recording the user-approved deviations from the original recommendation.

**Contract**: The addendum records `develop` as the review target, GitHub Models instead of Azure AI Foundry, `openai/gpt-4.1-mini`, free-tier one-call truncation, advisory-only behavior, compact policy, `ai-cr:error`, static-only CI signal, per-run label provisioning, no CODEOWNERS, and the separate `develop` copy of the Testing deployment workflow.

### Success Criteria:

#### Automated Verification:

- All valid/invalid fixtures still produce their expected validator exit states.
- Both deployment workflows retain the same Testing steps and use the same non-cancelling deployment concurrency group.
- Repository whitespace and patch integrity checks pass: `git diff --check`
- The full solution builds successfully: `dotnet build src/PlanDeck/PlanDeck.slnx`

#### Manual Verification:

- GitHub recognizes the workflow after the trusted files land on `main`.
- The complete smoke-test checklist passes on test PRs targeting remote `develop`.
- A push to `develop` authenticates through its branch-specific OIDC credential and completes the Testing deployment.
- `main` remains bound to its existing Testing deployment and does not share an active deployment window with `develop`.
- Workflow logs and comments contain no token, full diff, full prompt, or raw model response.
- Maintainers confirm that free-tier incompleteness and the paid-usage production path are clearly documented.

**Implementation Note**: This is the final phase. Complete the manual checklist before treating the workflow as operational.

---

## Testing Strategy

### Contract Validation:

- Validate schema-conforming pass and fail fixtures.
- Reject missing criteria, duplicate criteria, invalid scores, inconsistent `N/A`, wrong head SHA, oversized fields, and malformed JSON.
- Confirm trusted code, not model output, calculates the verdict.

### Workflow Integration:

- Exercise one real GitHub Models request on a small PR.
- Verify the one-call input budget and deterministic incomplete-analysis handling.
- Verify review and publish jobs have disjoint permission sets.
- Verify marker-comment update and additive label behavior through GitHub APIs.

### Manual Testing Steps:

1. Land the trusted workflow/action files on `main`.
2. Create and push `develop`, including the trusted workflow/action revision.
3. Open a draft PR to `develop` and confirm no model request runs.
4. Mark it ready and confirm one review, one marker comment, and one outcome label.
5. Push a new commit and confirm concurrency cancels/replaces an older review without stale publication.
6. Add `ai-cr:review` and confirm retry consumes the label and updates the existing comment.
7. Use a deliberately large diff and confirm the review reports truncation and cannot pass.
8. Simulate malformed model output or an unavailable endpoint and confirm `ai-cr:error` preserves the last pass/fail label.
9. Add an unrelated label and confirm publishing does not remove it.
10. Open a PR to `main` and confirm the AI review workflow does not run.
11. Provision the `refs/heads/develop` federated credential on the existing deployment identity.
12. Push a harmless change to `develop` and confirm `azure-develop.yml` deploys the Testing target.
13. Confirm `main` still owns its separate workflow and both Testing workflows use the shared deployment concurrency group.

## Performance Considerations

- Make exactly one model request per eligible workflow run.
- Use `openai/gpt-4.1-mini`, low temperature, non-streaming output, no tools, and a bounded output budget.
- Reserve prompt space for the compact policy/schema and cap title/body before allocating the remaining free-tier input budget to diff content.
- Truncate at file/hunk boundaries and report incompleteness rather than silently dropping arbitrary text.
- Use PR-scoped concurrency to avoid paying for obsolete reviews after rapid pushes.
- Bound the rendered comment below GitHub's comment-size limit and cap each evidence/summary field in the validator.

## Migration Notes

No application or database migration is required.

Rollout has three repository prerequisites:

1. The trusted `pull_request_target` workflow and local action must be present on default branch `main`.
2. Remote branch `develop` must be created before PRs can target it.
3. The existing Azure deployment identity must receive a federated credential for `repo:LeszekDNV/PlanDeck2:ref:refs/heads/develop`.

The MVP runs on GitHub Models free API usage. Moving to paid GitHub Models should require configuration/billing changes, not a redesign of the workflow contract. If a future provider is needed, keep the validated result schema, publisher, and labels stable and replace only the model invocation boundary.

Both branch deployment workflows target Testing initially. The future Production conversion of `main` is a separate migration that must change its publish target, environment/resource variables, OIDC/RBAC scope, and concurrency group without altering `azure-develop.yml`.

## References

- Requirements: `context/changes/ci-cd-code-review/requirements.md`
- Research: `context/changes/ci-cd-code-review/research.md`
- Existing workflow pattern: `.github/workflows/azure-dev.yml:17-37`
- Repository review rules: `.github/copilot-instructions.md:71-87`
- Planned CI gates: `context/foundation/test-plan.md:71-81`
- GitHub Models inference API: https://docs.github.com/en/rest/models/inference
- GitHub Models prototyping and rate limits: https://docs.github.com/en/github-models/use-github-models/prototyping-with-ai-models#rate-limits
- GitHub Models responsible use: https://docs.github.com/en/github-models/responsible-use-of-github-models
- GitHub Actions `pull_request_target` security: https://docs.github.com/en/actions/reference/security/securely-using-pull_request_target
- GitHub Actions workflow permissions: https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax#permissions
- Pull request comments API: https://docs.github.com/en/rest/issues/comments
- Labels API: https://docs.github.com/en/rest/issues/labels

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Define the Trusted Review Contract

#### Automated

- [x] 1.1 The passed fixture validates and produces `passed` — 80d72a5
- [x] 1.2 The failed fixture validates and produces `failed` — 80d72a5
- [x] 1.3 Both invalid fixtures are rejected with non-zero exit codes — 80d72a5
- [x] 1.4 The full solution builds successfully — 80d72a5

#### Manual

- [x] 1.5 The compact policy still covers all 15 requirement headings and the critical PlanDeck conventions — 80d72a5
- [x] 1.6 The schema and validator contain no path that trusts a model-authored verdict or silently defaults malformed data — 80d72a5

### Phase 2: Build the Fork-Safe Composite Review Action

#### Automated

- [x] 2.1 Composite metadata references only repository-local scripts and declares the expected inputs/output — 10245f6
- [x] 2.2 The result written by the review script passes the Phase 1 validator before upload — 10245f6
- [x] 2.3 Repository whitespace and patch integrity checks pass — 10245f6
- [x] 2.4 The full solution builds successfully — 10245f6

#### Manual

- [x] 2.5 Security inspection confirms that the action never checks out, sources, imports, or executes content from the pull request head — 10245f6
- [x] 2.6 A small test request stays within the free-tier budget and returns all 15 structured criteria — 10245f6
- [x] 2.7 An oversized fixture diff is truncated at a stable boundary and cannot produce `passed` — 10245f6

### Phase 3: Orchestrate Review, Publishing, Labels, and Retry

#### Automated

- [x] 3.1 Workflow job permissions match the review/publish separation and no job combines model access with PR mutation permissions
- [x] 3.2 Publisher accepts both valid fixtures and rejects both invalid fixtures through the shared validator
- [x] 3.3 Repository whitespace and patch integrity checks pass
- [x] 3.4 The full solution builds successfully

#### Manual

- [x] 3.5 A non-draft PR to `develop` receives exactly one updated marker comment and the correct pass/fail label
- [x] 3.6 Adding `ai-cr:review` starts a new review, updates the same comment, and removes the retry label
- [x] 3.7 A simulated model/schema failure adds `ai-cr:error`, preserves the previous pass/fail label, updates diagnostics, and leaves the run failed
- [x] 3.8 Draft PRs and PRs targeting branches other than `develop` do not invoke the model
- [x] 3.9 Existing unrelated labels remain unchanged

### Phase 4: Add Develop Deployment and Validate the Rollout

#### Automated

- [ ] 4.1 All valid/invalid fixtures still produce their expected validator exit states
- [ ] 4.2 Both deployment workflows retain the same Testing steps and use the same non-cancelling deployment concurrency group
- [ ] 4.3 Repository whitespace and patch integrity checks pass
- [ ] 4.4 The full solution builds successfully

#### Manual

- [ ] 4.5 GitHub recognizes the workflow after the trusted files land on `main`
- [ ] 4.6 The complete smoke-test checklist passes on test PRs targeting remote `develop`
- [ ] 4.7 A push to `develop` authenticates through its branch-specific OIDC credential and completes the Testing deployment
- [ ] 4.8 `main` remains bound to its existing Testing deployment and does not share an active deployment window with `develop`
- [ ] 4.9 Workflow logs and comments contain no token, full diff, full prompt, or raw model response
- [ ] 4.10 Maintainers confirm that free-tier incompleteness and the paid-usage production path are clearly documented
