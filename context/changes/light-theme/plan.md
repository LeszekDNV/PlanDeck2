# Light and Dark Theme Switch Implementation Plan

## Overview

Add an accessible light/dark theme control to the PlanDeck client. The first visit follows the operating-system color preference, while an explicit user choice is stored locally and takes precedence on later visits.

## Current State Analysis

The client already defines light and dark MudBlazor palettes, passes them to `MudThemeProvider`, and keeps a dark-mode flag in `MainLayout`. A toggle method and icon property also exist, but no rendered control calls the method and the preference is not loaded or saved. The client already uses direct `IJSRuntime` calls and `localStorage` for culture selection, so theme persistence can follow the same client-only pattern without backend or database work.

## Desired End State

Every user, authenticated or anonymous, sees a theme button in the AppBar at desktop and mobile widths. With no valid saved preference, PlanDeck follows `prefers-color-scheme`; selecting a theme stores `light` or `dark`, survives reloads, and overrides later system changes. The control communicates the action it will perform in the active language and uses the existing palettes unchanged.

### Key Discoveries:

- `MainLayout` already provides both palettes and binds `_isDarkMode` to `MudThemeProvider`: `src/PlanDeck/Web/PlanDeck.Client/Layout/MainLayout.razor:6` and `MainLayout.razor.cs:16,71-136`.
- The existing direct local-storage convention is established by culture initialization and switching: `src/PlanDeck/Web/PlanDeck.Client/Program.cs:38-42` and `Layout/MainLayout.razor.cs:47-51`.
- The AppBar already has a responsive navigation split, but the theme control must remain outside `MudHidden` to stay visible at all breakpoints: `src/PlanDeck/Web/PlanDeck.Client/Layout/MainLayout.razor:13-49`.
- English and Polish resource keys are mechanically checked for parity: `src/PlanDeck/Tests/PlanDeck.Unit.Tests/Client/LocalizationResourceParityTests.cs:8-22`.
- The PRD requires both display modes and respect for the user's choice: `context/foundation/prd.md:107-110`.

## What We're NOT Doing

- Redesigning or tuning the existing light and dark palettes.
- Adding an Auto option after the user makes an explicit choice.
- Reacting live to operating-system theme changes after a manual override.
- Persisting theme preference in the server, database, or user account.
- Expanding the localization scope beyond the existing English and Polish resources.
- Adding new unit, component, or E2E tests.
- Removing the brief initial theme change while the client reads the preference.

## Implementation Approach

Keep theme state in `MainLayout` and add a small browser helper dedicated to reading the saved preference, resolving the system fallback, and saving an explicit choice. Initialize the layout asynchronously from that helper, then make the existing toggle asynchronous so state changes and persistence remain one awaited user action. Render one MudBlazor icon button in the AppBar, with its icon, tooltip, accessible name, and pressed state derived from the active mode.

## Critical Implementation Details

### State sequencing

Only the exact stored values `light` and `dark` are authoritative. Missing, invalid, or inaccessible local storage falls back to `prefers-color-scheme` without rewriting storage; if the media query is unavailable, retain the current dark default. Update the in-memory mode before awaiting persistence so the click responds immediately, while storage failures remain observable through the awaited JS interop call.

## Phase 1: Theme Preference and Lifecycle

### Overview

Introduce the browser preference contract and connect it to the existing layout state without changing the visible navigation.

### Changes Required:

#### 1. Browser theme preference helper

**File**: `src/PlanDeck/Web/PlanDeck.Client/wwwroot/js/themePreferences.js`

**Intent**: Centralize the browser-only operations needed to resolve and persist the theme, avoiding inline JavaScript or `eval` calls from C#.

**Contract**: Expose functions that return `dark` or `light` by first accepting an exact saved value and otherwise consulting `prefers-color-scheme`, and that store an explicit `dark` or `light` value under one stable PlanDeck theme key. A storage read blocked by the browser is treated like a missing preference; invalid or unreadable data is not overwritten during initialization.

#### 2. Load the helper with the client

**File**: `src/PlanDeck/Web/PlanDeck.Client/wwwroot/index.html`

**Intent**: Make the theme helper available before the Blazor WebAssembly application starts using it.

**Contract**: Add the helper script alongside the existing MudBlazor and Blazor startup scripts without changing service-worker or loading-screen behavior.

#### 3. Initialize and persist layout theme state

**File**: `src/PlanDeck/Web/PlanDeck.Client/Layout/MainLayout.razor.cs`

**Intent**: Resolve the initial mode from browser state and persist every explicit toggle while preserving the existing MudTheme configuration.

**Contract**: Replace synchronous initialization with the appropriate asynchronous lifecycle override, keep palette construction intact, and set `_isDarkMode` from the helper result. Change the toggle handler to an awaited operation that flips the mode and stores the corresponding exact value. Derive the action icon and localized action key from the resulting mode.

### Success Criteria:

#### Automated Verification:

- Client project compiles: `dotnet build Web/PlanDeck.Client/PlanDeck.Client.csproj`

#### Manual Verification:

- With no saved theme value, a fresh visit follows light and dark operating-system preferences.
- Valid saved `light` and `dark` values override the operating-system preference after reload.
- A missing, invalid, or browser-blocked saved preference falls back to the system preference without being rewritten.

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Phase 2: Accessible Theme Control and Verification

### Overview

Expose the theme switch consistently across responsive layouts, localize its action, and verify the complete user flow.

### Changes Required:

#### 1. Responsive AppBar control

**File**: `src/PlanDeck/Web/PlanDeck.Client/Layout/MainLayout.razor`

**Intent**: Let every user change theme directly from the AppBar on desktop and mobile without opening the navigation drawer.

**Contract**: Add a MudBlazor icon button outside breakpoint-specific hidden regions and wire it to the asynchronous toggle handler. Its icon indicates the target mode; its tooltip and accessible name say "Enable light theme" or "Enable dark theme" as appropriate, and its pressed state exposes the current binary mode.

#### 2. English theme labels

**File**: `src/PlanDeck/Web/PlanDeck.Client/Resources/SharedResource.resx`

**Intent**: Provide the English action labels used by the theme button and tooltip.

**Contract**: Add resource keys for enabling the light theme and enabling the dark theme.

#### 3. Polish theme labels

**File**: `src/PlanDeck/Web/PlanDeck.Client/Resources/SharedResource.pl.resx`

**Intent**: Provide Polish equivalents of every new English theme action label.

**Contract**: Add the same keys as the English resource so the existing parity invariant remains satisfied.

### Success Criteria:

#### Automated Verification:

- English and Polish resource keys remain aligned: `dotnet test Tests/PlanDeck.Unit.Tests/PlanDeck.Unit.Tests.csproj --filter "FullyQualifiedName~LocalizationResourceParityTests"`
- Full solution compiles: `dotnet build PlanDeck.slnx`

#### Manual Verification:

- The theme control is visible and usable in the AppBar at desktop and mobile widths for authenticated, anonymous, and guest users.
- The icon, tooltip, accessible action name, and pressed state update immediately after each toggle in both English and Polish.
- A manual choice survives reload and continues to override a changed operating-system preference.
- Existing pages remain readable in both modes and use the unchanged light and dark palettes.

**Implementation Note**: After completing this phase and all automated verification passes, pause here for manual confirmation from the human that the manual testing was successful before proceeding to the next phase.

---

## Testing Strategy

### Unit Tests:

- Do not add new tests by decision; run the existing localization resource parity test after adding labels.

### Integration Tests:

- Do not add automated integration or E2E coverage for this change.

### Manual Testing Steps:

1. Clear the theme storage key, set the operating system to light mode, open PlanDeck, and confirm light mode is selected.
2. Repeat with the operating system in dark mode and confirm dark mode is selected.
3. Toggle to the opposite mode, reload, and confirm the explicit choice persists.
4. Change the operating-system preference, reload, and confirm the explicit choice still wins.
5. Insert an invalid stored value and confirm PlanDeck falls back to the current system preference without replacing the invalid value.
6. Verify the control and action label in English and Polish at desktop and mobile widths.
7. Check representative account, project, session, and voting screens in both modes for obvious regressions.

## Performance Considerations

The change adds one small static script and one local browser-state read during layout initialization. No network request, backend dependency, subscription to system-theme changes, or server-side state is introduced.

## Migration Notes

No data or schema migration is required. Existing users have no theme key, so their first visit after deployment resolves from the operating-system preference.

## References

- Change definition: `context/changes/light-theme/change.md`
- Product requirement: `context/foundation/prd.md:107-110`
- Existing theme state and palettes: `src/PlanDeck/Web/PlanDeck.Client/Layout/MainLayout.razor.cs:16,71-136`
- Existing theme provider: `src/PlanDeck/Web/PlanDeck.Client/Layout/MainLayout.razor:6`
- Existing local-storage pattern: `src/PlanDeck/Web/PlanDeck.Client/Program.cs:38-42`
- Localization parity test: `src/PlanDeck/Tests/PlanDeck.Unit.Tests/Client/LocalizationResourceParityTests.cs:8-22`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Theme Preference and Lifecycle

#### Automated

- [x] 1.1 Client project compiles — 21e0ef8

#### Manual

- [ ] 1.2 Fresh visit follows the operating-system preference
- [ ] 1.3 Saved light and dark values override the operating-system preference
- [ ] 1.4 Invalid or unavailable storage falls back without rewriting the preference

### Phase 2: Accessible Theme Control and Verification

#### Automated

- [x] 2.1 English and Polish resource keys remain aligned
- [x] 2.2 Full solution compiles

#### Manual

- [ ] 2.3 Theme control works for every user at desktop and mobile widths
- [ ] 2.4 Localized icon action, tooltip, accessible name, and state update immediately
- [ ] 2.5 Manual preference persists and overrides later system changes
- [ ] 2.6 Existing pages remain readable with the unchanged palettes
