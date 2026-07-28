---
date: 2026-07-28T19:50:42.593+02:00
researcher: GitHub Copilot
git_commit: 08b08bf155728009c672c673af260a168c462995
branch: develop
repository: LeszekDNV/PlanDeck2
topic: "CI/CD workflow for pull request code reviews"
tags: [research, codebase, github-actions, code-review, ci-cd]
status: complete
last_updated: 2026-07-28
last_updated_by: GitHub Copilot
---

# Research: CI/CD Workflow for Pull Request Code Reviews

**Date**: 2026-07-28T19:50:42.593+02:00
**Researcher**: GitHub Copilot
**Git Commit**: 08b08bf155728009c672c673af260a168c462995
**Branch**: develop
**Repository**: LeszekDNV/PlanDeck2

## Research Question

Research `ci-cd-code-review` based on the requirements in
`context/changes/ci-cd-code-review/requirements.md`.

## Summary

The requested workflow is feasible as a trusted GitHub Actions workflow plus a
local composite action. The repository already uses GitHub Actions for
deployment, but has no pull-request workflow, reusable review action, AI
integration, PR-comment publishing, or `ai-cr:*` label handling.

The requirements target `master`, while the repository's default branch and
live deployment workflow use `main`. The review workflow should therefore
target `main`.

For fork-safe operation, the strongest design is `pull_request_target` with no
execution of pull-request code. A trusted action from the base revision should
fetch the PR head only as data, verify its SHA, create a three-dot diff, and
send bounded title/body/diff data to a model without tools. Review and publish
should be separate jobs with different permissions. The publishing job should
validate structured output, update one bot-owned marker comment, and change
labels without replacing unrelated labels.

## Detailed Findings

### Existing CI/CD Baseline

- The only GitHub Actions workflow deploys on pushes to `main` and supports
  manual dispatch; it does not run for pull requests
  (`.github/workflows/azure-dev.yml:19-22`).
- The deployment workflow establishes a useful convention of explicit,
  minimal permissions (`id-token: write`, `contents: read`)
  (`.github/workflows/azure-dev.yml:30-33`).
- An Azure Pipelines definition still targets `master` and disables PR runs
  (`.azuredevops/pipelines/azure-dev.yml:2-6`). It contains reusable build,
  test, and forbidden-pattern checks (`.azuredevops/pipelines/azure-dev.yml:17-56`),
  but it is not the active GitHub PR path.
- The repository builds from `src/PlanDeck/PlanDeck.slnx` using .NET SDK
  `10.0.301` (`global.json:1-4`). Unit and integration tests use NUnit; E2E
  tests use Playwright.
- No `.github/actions/` implementation, PR review workflow, model client,
  review comment updater, or AI review labels currently exist.

### Trigger and Workflow Shape

Recommended event configuration:

```yaml
on:
  pull_request_target:
    branches: [main]
    types: [opened, synchronize, reopened, ready_for_review, labeled]
```

Jobs triggered by `labeled` should proceed only when the label is
`ai-cr:review`; GitHub Actions cannot filter an event by label name in the
trigger itself. Runs should use PR-scoped concurrency with
`cancel-in-progress: true`.

`pull_request_target` is appropriate because a normal `pull_request` workflow
receives a read-only token and no repository secrets for forked pull requests.
Its elevated context is safe only if the workflow never checks out or executes
code from the PR.

Suggested structure:

```text
pull_request_target
  review
    trusted composite action
    contents: read
    model credential only
  publish (always, after review)
    no model credential
    issues/pull-requests: write
```

A composite action cannot declare job permissions. The caller workflow must
own the permission boundary.

Official references:

- [Events that trigger workflows](https://docs.github.com/en/actions/reference/workflows-and-actions/events-that-trigger-workflows)
- [Workflow permissions](https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax#permissions)
- [Secure use of pull_request_target](https://docs.github.com/en/actions/reference/security/securely-using-pull_request_target)
- [Composite action metadata](https://docs.github.com/en/actions/reference/workflows-and-actions/metadata-syntax)

### Safe PR Data Collection

The trusted action should:

1. Check out the trusted base SHA with credentials disabled.
2. Fetch `refs/pull/<number>/head` as an object, not as executable workspace
   content.
3. Verify the fetched SHA equals `github.event.pull_request.head.sha`.
4. Generate `git diff --no-ext-diff --no-textconv base_sha...head_sha`.
5. Never run builds, package installation, generators, or scripts from the PR.

The three-dot diff matches pull-request semantics. A local Git diff also avoids
the REST pull-files limit of 3,000 files and the compare response's reduced
file list.

Title, body, and diff should be written to files rather than passed as action
inputs. The PR body is worth including because requirements explicitly assess
alignment with the declared purpose and PR quality
(`requirements.md:67-75`, `requirements.md:162-168`). Cap it at a small,
documented size such as 8-16 KiB.

Large diffs need per-file chunking and synthesis. Binary, generated, vendored,
and suspected credential-bearing content should be excluded. A truncated or
partially analyzed review must state that limitation and must not receive a
passing verdict.

### Model Boundary and Review Result

All PR-controlled text, including title, body, filenames, and diff, is
untrusted and vulnerable to prompt injection. The model should have no shell,
tools, repository token, or unrestricted network access.

The action should require schema-constrained JSON containing:

- one score or `N/A` for each of the 15 criteria;
- concise evidence tied to changed files;
- blocker findings;
- analysis-completeness metadata;
- the reviewed head SHA.

Trusted code, not model prose, should calculate the final verdict. A reasonable
initial rule is: complete analysis, no blocker, and every applicable criterion
scored at least 7. The requirements already establish that critical security,
architectural-boundary, data-loss, and required-test failures override an
average score (`requirements.md:170-178`).

The requirements treat failing tests as blockers, but the specified inputs do
not include test results. The implementation must either consume trusted check
results or explicitly limit its verdict to static review.

### Comment and Label Publishing

Use one issue comment with a stable marker such as:

```html
<!-- ai-cr:summary:v1 -->
```

The publisher should paginate comments, require both the marker and the
`github-actions[bot]` author, update the existing comment, and create a comment
only when none exists. The comment should include the reviewed head SHA,
analysis completeness, run link, scores, blockers, and summary.

Labels must be changed additively:

- pass: add `ai-cr:passed`, remove `ai-cr:failed`;
- fail: add `ai-cr:failed`, remove `ai-cr:passed`;
- retry: consume `ai-cr:review` so it can be added again.

Do not replace the complete label set, because that would remove unrelated
labels. Labels should be provisioned once outside normal review runs.

An automation failure is different from a failed code review. A third
`ai-cr:error` label would preserve that distinction, although it is not in the
current requirements.

Official references:

- [Issue comments REST API](https://docs.github.com/en/rest/issues/comments)
- [Issue labels REST API](https://docs.github.com/en/rest/issues/labels)
- [Security hardening for GitHub Actions](https://docs.github.com/en/actions/reference/security/secure-use)
- [Prompt-injection risks and mitigations](https://docs.github.com/en/copilot/concepts/agents/cloud-agent/risks-and-mitigations)

### Repository-Specific Review Context

The 15 requested criteria align with repository conventions:

- dependencies flow inward through the layered architecture;
- backend contracts are code-first gRPC contracts in `Core.Shared`;
- service implementations belong in `Application`, not the web host;
- server DI is composed through `ServiceCollectionExtensions`;
- Blazor logic belongs in `.razor.cs` code-behind;
- MudBlazor is preferred over reimplementing UI controls;
- all user-facing text is localized;
- the full solution must build without newly introduced warnings.

These trusted rules should be loaded from the base revision, not from the PR,
and supplied as review policy separately from untrusted PR content
(`.github/copilot-instructions.md:1-89`).

## Code References

- `context/changes/ci-cd-code-review/requirements.md:1-10` - Workflow concept
  and proposed inputs.
- `context/changes/ci-cd-code-review/requirements.md:12-178` - Review criteria,
  scoring, and blocker rules.
- `context/changes/ci-cd-code-review/requirements.md:185-192` - Comment, labels,
  and label-triggered retry.
- `.github/workflows/azure-dev.yml:19-33` - Active `main` trigger and explicit
  permissions pattern.
- `.azuredevops/pipelines/azure-dev.yml:2-56` - Stale `master` trigger plus
  existing build, test, and source guardrails.
- `.github/copilot-instructions.md:1-89` - Current stack, architecture,
  development, and code-quality conventions.
- `global.json:1-4` - Pinned .NET SDK.
- `context/foundation/test-plan.md:72-80` - Planned CI quality gates.

## Architecture Insights

- GitHub Actions is already the live deployment mechanism even though older
  plans preferred Azure Pipelines. New PR automation should follow the live
  GitHub path rather than extend the stale pipeline.
- Workflow orchestration should stay readable while review mechanics live in a
  repository-local composite action, as requested.
- Trust boundaries matter more than action reuse: model access, PR write access,
  and untrusted PR data should never coexist unnecessarily.
- The review policy and executable action must come from the trusted base
  revision. PR content is input data only.
- AI review labels are advisory unless branch rules require the workflow's
  status check. Labels alone cannot block a merge.

## Historical Context (from Prior Changes)

- `context/changes/fix-github-actions-deploy/change.md` records the move to a
  working GitHub Actions deployment path on 2026-07-25.
- `context/deployment/deploy-plan.md:45-46` planned Azure-native CI/CD,
  `master`, and human approval for production; live implementation later
  diverged toward GitHub Actions and `main`.
- `context/foundation/infrastructure.md:33-46` originally assumed no GitHub
  Actions. The existing deployment workflow has superseded that assumption.
- `context/foundation/test-plan.md:49-80` plans progressively stronger CI
  quality gates. Automated AI review complements but does not replace build
  and deterministic test gates.
- `context/archive/2026-07-21-secure-ado-grpc-endpoints/plan.md:42-64`
  reinforces fail-closed secret handling and trusted-boundary enforcement.

## Related Research

No existing research artifact covers automated pull-request code review.
Relevant operational context is in:

- `context/changes/fix-github-actions-deploy/`
- `context/changes/harden-deployment-pipeline/`
- `context/foundation/test-plan.md`
- `context/deployment/deploy-plan.md`

## Open Questions

1. Which model/provider, credential type, retention policy, token budget, and
   per-run cost ceiling should be used?
2. Should drafts be reviewed, and should body edits trigger a new review?
3. Is AI review advisory, or must its status check block merging?
4. Is the recommended pass threshold of all applicable scores at least 7
   acceptable?
5. Should automation errors use a separate `ai-cr:error` label?
6. Should trusted CI check results be included so test failures can block the
   verdict?
7. How much trusted base-revision context beyond the diff may the model receive?
8. Who provisions labels and protects workflow, action, and prompt files with
   CODEOWNERS or repository rules?

## Planning Addendum — 2026-07-28

The approved implementation plan resolves the open decisions and supersedes the
original recommendation where noted:

- Reviews target non-draft pull requests to `develop`, not `main` or `master`.
- GitHub Models is the provider; no Azure AI Foundry resource or model secret is
  introduced.
- The MVP model is `openai/gpt-4.1-mini`, using one bounded free-tier request.
- Oversized, binary, redacted, or otherwise incomplete input fails closed and
  cannot receive a passing verdict.
- The review remains advisory and uses a compact trusted 15-criterion policy.
- Automation failures use `ai-cr:error` and preserve the previous pass/fail
  label.
- CI results are unavailable, so the verdict is explicitly static-analysis
  only.
- The four `ai-cr:*` labels are provisioned idempotently during each publishing
  run.
- CODEOWNERS and branch protection remain out of scope; normal maintainer review
  protects trusted workflow and action changes.
- `develop` receives a separate copy of the current Testing deployment workflow.
  It shares a non-cancelling deployment concurrency group with `main` while both
  mutate the same Testing environment.
