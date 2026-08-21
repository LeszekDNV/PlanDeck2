# Complete Project and Session Reorganization Follow-ups — Plan Brief

> Full plan: `context/changes/complete-project-session-reorganization/plan.md`

## What & Why

The original Project-first reorganization is implemented and passes locally,
but eight acceptance criteria remain open. This follow-up secures the remote
Testing boundary through private ingress, repairs mobile usability, runs the
full protected E2E gate, and captures reproducible evidence for final closure.

## Starting Point

Project-owned Sessions, authorization, cascade deletion, deterministic roles,
and local E2E already exist. Testing still publishes externally, test identity
cookies are unsigned and default to Owner, the `375x812` layout overflows, and
remote environment evidence has not been produced.

## Desired End State

Testing is reachable only from an approved private Azure DevOps agent and is
provably blocked from a public agent. Production exposes no test endpoints.
Mobile navigation and long labels work at `375x812`, and local plus remote suites
prove deletion, roles, EN/PL, routing, direct links, and guest voting.

## Key Decisions Made

| Decision | Choice | Why | Source |
| --- | --- | --- | --- |
| Testing auth boundary | Private ingress | Keep the existing deterministic harness with minimal application changes | Plan |
| Missing identity cookie | Preserve implicit Owner | Maintain compatibility with existing tests; accept reliance on ingress | Plan |
| Ingress proof | Allowed and denied network probes | Verify runtime isolation rather than configuration alone | Plan |
| Production proof | Integration test plus deployed probe | Cover code configuration and real deployment | Plan |
| Mobile navigation | MudBlazor hamburger/drawer | Keep every action reachable without app-bar compression | Plan |
| Long labels | Two lines plus full tooltip | Preserve context while bounding layout height | Plan |
| Role acceptance | Deterministic Testing identities | Keep verification repeatable without real Entra accounts | Plan |
| Pipeline diagnostics | One E2E step with structured boundaries | Improve diagnosis without multiplying jobs per test layer | Plan |

## Scope

**In scope:**

- Private Azure Container Apps ingress for the test-auth Testing deployment.
- Public-denial and private-success probes against one Testing revision.
- Production test-auth configuration tests and deployed endpoint probe.
- Mobile drawer and bounded Project/Session labels.
- `375x812` browser regression coverage.
- Protected remote E2E with secret-safe structured diagnostics.
- Active/inactive Session deletion, role EN/PL, routing, and guest regression.
- Evidence linking all eight inherited pending criteria.

**Out of scope:**

- Signed identity cookies or token bootstrap.
- Removing the default Testing Owner identity.
- Real Entra accounts in the role matrix.
- Domain, schema, route, or deletion-contract changes.
- New languages, visual snapshots, or separate hand-written Azure IaC.

## Architecture / Approach

Aspire remains the source of generated Azure Container Apps infrastructure.
Publish configuration makes Testing ingress private while Production stays
external. A private self-hosted pipeline agent runs the allowed probe and remote
Playwright suite; a Microsoft-hosted agent proves public denial. MudBlazor
responsive components provide the mobile drawer, while existing scenario and
identity helpers drive isolated role and deletion tests.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Environment boundary | Private Testing and verified Production isolation | Requires private DNS/network and a self-hosted agent |
| 2. Responsive UI | Drawer, bounded labels, and `375x812` coverage | Mobile fixes must not regress desktop |
| 3. Remote E2E | Protected full suite with structured diagnostics | Cleanup and artifacts must survive failures |
| 4. Acceptance | Complete evidence and inherited-criteria closure | Manual marks must remain evidence-based |

**Prerequisites:** A dedicated Testing azd environment, private Container Apps
network path/DNS, a self-hosted Azure DevOps agent in that network, protected
Testing and Production URLs, and secret pipeline variables.

**Estimated effort:** Approximately 4–6 implementation sessions across 4 phases.

## Open Risks & Assumptions

- Unsigned Testing cookies and implicit Owner remain an accepted risk; any public
  ingress regression becomes security-critical.
- A Microsoft-hosted denied probe and private self-hosted allowed probe must
  target the same deployed revision.
- Production probing must distinguish an expected `404` from network failure.
- The self-hosted agent must support .NET 10 and Playwright Chromium.

## Success Criteria (Summary)

- Testing is reachable privately, blocked publicly, and Production exposes no
  test-auth/scenario surface.
- Project-first UI is usable at `375x812` and on desktop in English and Polish.
- Local and protected remote gates prove roles, deletion, routing, direct links,
  guest voting, cleanup, and diagnosable pipeline failures.
