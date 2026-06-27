<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Critical-Path Integrity Tests

- **Plan**: `context/changes/testing-critical-path-integrity/plan.md`
- **Scope**: Phase 1-4 of 4
- **Date**: 2026-06-27
- **Verdict**: NEEDS ATTENTION
- **Findings**: 0 critical, 1 warning, 0 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | WARNING |
| Safety & Quality | PASS |
| Architecture | PASS |
| Pattern Consistency | PASS |
| Success Criteria | PASS |

## Findings

### F1 — Unplanned bulk scope in .github

- **Severity**: ⚠️ WARNING
- **Impact**: 🔬 HIGH — architectural stakes; think carefully before deciding
- **Dimension**: Scope Discipline
- **Location**: `src/PlanDeck/.github/.10x-cli-manifest.json:2`
- **Detail**: The executed change includes extensive `.github` content unrelated to Phase 1-4 test/cookbook contracts, increasing review and rollback blast radius.
- **Fix A ⭐ Recommended**: Move unrelated `.github` changes into a separate change/PR and keep this one test-focused.
  - Strength: Restores scope fidelity and cleaner auditability.
  - Tradeoff: Requires split/cherry-pick follow-up work.
  - Confidence: HIGH — diff clearly shows unrelated paths.
  - Blind spot: None significant.
- **Fix B**: Amend plan with an explicit addendum that includes `.github` scope.
  - Strength: Preserves already-landed commits without code movement.
  - Tradeoff: Weakens scope discipline for future phase reviews.
  - Confidence: MEDIUM — process fix, not technical isolation.
  - Blind spot: Stakeholder alignment on broadened scope not verified.
- **Decision**: SKIPPED

### F2 — Phase 1 contract mismatches disconnect behavior

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Plan Adherence
- **Location**: `context/changes/testing-critical-path-integrity/plan.md:28,66`
- **Detail**: Plan wording expected vote clearing on full disconnect while implemented behavior preserves vote and marks participant offline.
- **Fix**: Align plan contract text to implemented/approved behavior.
- **Decision**: FIXED

### F3 — Progress wording claimed new ADO unit tests

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: `context/changes/testing-critical-path-integrity/plan.md:283`
- **Detail**: Progress line implied newly added ADO tests in this change.
- **Fix**: Reword progress entry to validated existing ADO tests for this phase.
- **Decision**: FIXED

### F4 — Invalid automated verification command syntax

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Success Criteria
- **Location**: `context/changes/testing-critical-path-integrity/plan.md:141`
- **Detail**: Phase 2 command used an invalid pipeline into a string literal.
- **Fix**: Replace with two executable commands for JoinRoom and Config filters.
- **Decision**: FIXED

### F5 — Reconnect e2e race risk before page close

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: `src/PlanDeck/Tests/PlanDeck.E2e.Tests/VotingRoomTests.cs:165`
- **Detail**: User-B page could close before vote acknowledgement reached stable room state.
- **Fix**: Wait for two voted statuses before closing user-B page.
- **Decision**: FIXED

### F6 — Hub assertion depended on brittle transport message text

- **Severity**: 👀 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: `src/PlanDeck/Tests/PlanDeck.Integration.Tests/Realtime/PlanningRoomHubTests.cs:376`
- **Detail**: Assertion targeted framework message wording instead of stable behavior.
- **Fix**: Assert exception presence plus no extra post-reveal broadcast.
- **Decision**: FIXED
