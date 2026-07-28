# Advisory AI code review

This directory contains the trusted, repository-local action used by
`.github/workflows/ai-code-review.yml`. It performs one static review of an
eligible pull request targeting `develop`, validates the structured result, and
updates one bot-owned PR comment plus additive `ai-cr:*` labels.

## Prerequisites and bootstrap

1. Enable GitHub Models for the repository and permit Actions to use it.
2. Keep the workflow job permissions intact: the review job needs
   `contents: read` and `models: read`; the publisher needs `contents: read`,
   `issues: write`, and `pull-requests: write`.
3. Land the trusted workflow and `.github/actions/ai-code-review/` files on the
   default `main` branch. `pull_request_target` resolves trusted automation from
   the base repository, not from a fork.
4. Create and push remote `develop`, then synchronize the same trusted files
   into it before opening review PRs.
5. For the separate Testing deployment, add an Entra federated credential with
   subject `repo:LeszekDNV/PlanDeck2:ref:refs/heads/develop`.

No model secret is required. GitHub Actions exchanges its run-scoped
`GITHUB_TOKEN`, restricted by `models: read`, with the GitHub Models endpoint.
Do not add a model key or long-lived token.

The MVP model is `openai/gpt-4.1-mini`. Free GitHub Models API usage is intended
for prototyping and is constrained to 8,000 input tokens and 4,000 output
tokens per request. Production usage should move to paid GitHub Models while
keeping the schema, validator, publisher, and labels unchanged.

## Runtime behavior

The workflow handles non-draft PRs targeting `develop` on `opened`,
`synchronize`, `reopened`, and `ready_for_review`. Adding `ai-cr:review`
requests another review; other label events, drafts, and other base branches do
not invoke the model.

The trusted action:

- fetches `refs/pull/<number>/head` as a Git object and verifies its SHA;
- never checks out or executes pull request code;
- treats title, body, filenames, and diff as untrusted evidence;
- makes one no-tools, schema-constrained model request;
- caps title/body and allocates the remaining free-tier budget to the diff;
- redacts suspected credentials and truncates only at file or hunk boundaries;
- marks any omission, redaction, binary content, or truncation incomplete;
- validates all 15 criteria before exposing the result artifact.

Incomplete analysis always produces `failed`. The review is static only:
`CI signal: unavailable` means it does not claim that builds or tests ran.

The publisher revalidates the artifact and current PR head before mutation. It
updates one `<!-- ai-cr:summary:v1 -->` comment authored by
`github-actions[bot]` and manages only these labels:

| Label | Meaning |
| --- | --- |
| `ai-cr:passed` | Complete, blocker-free review with every applicable score at least 7 |
| `ai-cr:failed` | Review concern, blocker, or incomplete analysis |
| `ai-cr:error` | Model, schema, artifact, or publishing automation failure |
| `ai-cr:review` | One-shot retry request, removed after the attempt |

Pass and fail labels are mutually exclusive. An automation error preserves the
previous pass/fail label. Unrelated labels are never replaced or removed.
Superseded results are ignored after a fresh head-SHA check.

This workflow is advisory and does not configure branch protection, required
checks, or CODEOWNERS. Maintainer review of trusted workflow/action changes is
therefore part of the security boundary.

## Retry and troubleshooting

Add `ai-cr:review` to retry. Wait for the current PR-scoped run to finish before
adding it again.

| Symptom | Check |
| --- | --- |
| Missing artifact | Open the review job and verify that input collection, the model request, and trusted validation completed before upload. |
| Model HTTP 4xx | Confirm GitHub Models access, `models: read`, the selected model ID, and repository Actions policy. |
| Model HTTP 429 | Free-tier rate limits were exceeded; retry later or enable paid GitHub Models. |
| Malformed schema | Inspect the categorized workflow error; raw model output is intentionally not logged or published. |
| Stale SHA | A newer commit superseded the result. The newer PR-scoped run should publish instead. |
| Publishing failure | Confirm `issues: write` and `pull-requests: write`, repository Actions policy, and API availability. |
| `ai-cr:error` after cancellation | Confirm the workflow uses `!cancelled()` for the publisher and PR-scoped concurrency. |

Logs and comments must never contain the token, full prompt, full diff, or raw
model response.

## Rollout smoke-test checklist

- [ ] **Pass:** a small safe PR receives one marker comment and `ai-cr:passed`.
- [ ] **Score fail:** a fixture change scoring below 7 receives `ai-cr:failed`.
- [ ] **Incomplete fail:** a large or binary diff reports limitations and cannot pass.
- [ ] **Automation error:** a controlled model/schema failure adds `ai-cr:error`, preserves the prior outcome label, and fails the run.
- [ ] **Retry:** adding `ai-cr:review` starts one run, updates the same comment, and consumes the label.
- [ ] **Idempotency:** repeated reviews leave exactly one bot marker comment.
- [ ] **Draft skip:** a draft PR does not invoke GitHub Models.
- [ ] **Branch skip:** a PR not targeting `develop` does not invoke GitHub Models.
- [ ] **Label preservation:** unrelated labels survive pass, fail, error, and retry flows.
- [ ] **Fork safety:** a fork PR containing workflow/script instructions is treated only as diff data and no head code executes.
