# Light and Dark Theme Switch — Plan Brief

> Full plan: `context/changes/light-theme/plan.md`

## What & Why

PlanDeck will expose an accessible light/dark theme switch and respect the user's display preference. A first visit follows the operating system; an explicit choice is retained across later visits, satisfying the PRD requirement without adding account or server state.

## Starting Point

Both MudBlazor palettes, the provider binding, and an unused toggle method already exist in `MainLayout`. The missing delta is browser preference resolution, persistence, visible UI, localized accessible labels, and end-to-end manual verification.

## Desired End State

Every authenticated, anonymous, and guest user can switch themes from the AppBar on desktop or mobile. The button immediately updates the existing palette, survives reload, overrides later system changes, and clearly announces the action it will perform in English or Polish.

## Key Decisions Made

| Decision | Choice | Why |
| --- | --- | --- |
| First visit | Follow `prefers-color-scheme` | Matches the user's device without forcing a PlanDeck default. |
| Explicit choice | Persist `light` or `dark` | Keeps behavior predictable and satisfies the PRD. |
| Later system changes | Manual choice wins | Avoids surprising theme changes after user intent is known. |
| Mobile placement | Always-visible AppBar button | Keeps the control discoverable without opening the drawer. |
| Accessible label | Describe the target action | "Enable light/dark theme" communicates the result of clicking. |
| Invalid or unavailable storage | Fall back to system without rewrite | Preserves user data and keeps initialization resilient. |
| Initial visual transition | Brief theme flash is acceptable | Keeps the Blazor lifecycle and implementation small. |
| Automated coverage | No new tests | Scope is limited to build, existing parity test, and manual smoke testing. |
| Palette scope | Reuse existing palettes unchanged | Prevents the feature from becoming a visual redesign. |

## Scope

**In scope:**

- System-derived initial theme.
- Browser-local persistence of a manual light/dark choice.
- AppBar switch at every breakpoint.
- Dynamic English and Polish tooltip and accessible action label.
- Existing localization parity test, client/full builds, and manual verification.

**Out of scope:**

- Palette redesign or contrast tuning.
- Auto/light/dark three-state selection.
- Live system-theme synchronization after manual selection.
- Server/account synchronization.
- New unit, component, or E2E tests.

## Architecture / Approach

`MainLayout` remains the owner of MudBlazor theme state. A small browser helper reads an exact saved value, falls back to `prefers-color-scheme`, and stores explicit choices; the layout invokes it through the existing direct `IJSRuntime` pattern and drives one responsive MudBlazor icon button.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Theme Preference and Lifecycle | Browser fallback, initialization, and persistence | Handling invalid or blocked storage without corrupting preference. |
| 2. Accessible Theme Control and Verification | Responsive control, localization, and full smoke verification | Responsive AppBar space and correct action semantics. |

**Prerequisites:** Existing MudBlazor theme provider and palettes remain in place.
**Estimated effort:** About 1 focused implementation session across 2 phases.

## Open Risks & Assumptions

- Browser storage may be blocked; the helper must fall back to system preference without silently rewriting data.
- The accepted initial theme flash may be noticeable on slower WebAssembly startup.
- No new automated behavior test means persistence and responsive accessibility depend on the documented manual checks.
- Existing palette quality is accepted and is not a completion criterion for this change.

## Success Criteria (Summary)

- First-time users receive their operating-system theme, while manual choices survive reloads and override later system changes.
- The localized theme action is accessible and available in the AppBar on desktop and mobile for every user type.
- Existing localization parity passes and the full solution builds without regressions.
