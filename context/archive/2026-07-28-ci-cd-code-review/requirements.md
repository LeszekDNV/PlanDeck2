## Overall concept

- GHA workflow run for every new pull request targeting `develop`
- composite action for the review itself so that main workflow is easy to reason about

## Input parameters

- pull request title
- pull request description (?? cost tradeoff)
- git diff

## Code Review Criteria

Each criterion is scored on a 1–10 scale, where 1 is the worst outcome and 10 is the best.

### 1. SOLID and object-oriented design

- **Single Responsibility Principle:** classes, components, services, handlers, and methods have one clear reason to change.
- **Open/Closed Principle:** new behavior is added through extension or composition rather than repeated modification of stable code.
- **Liskov Substitution Principle:** implementations preserve the contracts and expected behavior of their abstractions.
- **Interface Segregation Principle:** interfaces are small, cohesive, and tailored to their consumers.
- **Dependency Inversion Principle:** high-level policies depend on abstractions; infrastructure details are injected and kept at the edges.
- Composition is preferred over inheritance unless inheritance clearly models an "is-a" relationship.
- Abstractions are introduced only when they reduce coupling or support a real variation point.

### 2. Clean Architecture boundaries

- Dependencies point inward: `Domain` has no dependency on `Application`, `Infrastructure`, or UI projects.
- `Application` contains use cases and abstractions, but no framework-specific, database-specific, or UI-specific implementation details.
- `Infrastructure` implements interfaces defined by inner layers and does not leak persistence or external-service details into them.
- The Blazor UI layer coordinates user interaction and delegates business operations to application use cases.
- Business rules are not implemented in Razor components, controllers, EF Core configurations, repositories, or external-service clients.
- Cross-layer communication uses explicit contracts, commands, queries, DTOs, or domain types appropriate to the boundary.
- No circular project or namespace dependencies are introduced.
- The PR preserves existing architectural conventions unless an intentional architectural change is clearly documented.

### 3. DRY, KISS, and YAGNI

- Repeated business logic, validation rules, mapping rules, and authorization decisions are consolidated in one appropriate place.
- Duplication is removed only when the extracted abstraction is clearer than the repeated code.
- The implementation is the simplest solution that fully satisfies the current requirement.
- The PR does not introduce speculative abstractions, extension points, configuration, or infrastructure for unconfirmed future needs.
- Methods, components, and services remain small enough to understand without excessive navigation or hidden side effects.
- Clever, overly generic, reflection-heavy, or metaprogramming-based solutions are avoided when straightforward code is sufficient.

### 4. Blazor and MudBlazor component design

- MudBlazor components are the default and preferred building blocks for the UI.
- Before introducing custom markup, CSS, JavaScript, or a new UI component, the PR verifies whether an existing MudBlazor component or supported composition already satisfies the requirement.
- Custom components wrap or compose MudBlazor components where possible instead of reimplementing equivalent controls, behaviors, validation, dialogs, tables, navigation, or feedback patterns.
- Raw HTML elements are used when they provide necessary semantic structure, accessibility, or behavior that is not adequately covered by MudBlazor.
- Any deliberate replacement or bypass of a suitable MudBlazor component is justified by a concrete accessibility, performance, maintainability, compatibility, or product requirement.
- MudBlazor APIs are used consistently with the version adopted by the solution; deprecated components, parameters, and patterns are not introduced.
- Styling prefers MudBlazor themes, design tokens, utility classes, and component parameters over duplicated inline styles or broad CSS overrides.
- Global CSS overrides and use of `!important` are avoided unless narrowly scoped and clearly justified.
- UI behavior and visual patterns remain consistent with existing MudBlazor usage across the application.
- Razor components focus on presentation and interaction rather than business logic or direct data access.
- Large components are decomposed into cohesive child components when this improves readability, reuse, or testability.
- Component parameters, cascading values, and callbacks form explicit, minimal contracts.
- Component state has a clear owner and is not duplicated unnecessarily across UI, services, and stores.
- Lifecycle methods are used correctly, avoid repeated work, and do not cause unintended render loops.
- Asynchronous component code supports cancellation where operations can outlive navigation or component disposal.
- Disposable resources and event subscriptions are released correctly, preferably through `IDisposable` or `IAsyncDisposable`.
- Rendering behavior is considered for frequently updated collections and expensive component trees, including stable keys where appropriate.
- Blazor Server, WebAssembly, and interactive render-mode constraints are respected for the hosting model used by the application.

### 5. Correctness and maintainability

- The implementation matches the PR description and handles expected success, failure, empty, and boundary cases.
- Public APIs, domain invariants, nullable reference types, and validation rules are explicit and consistently enforced.
- Naming communicates intent and follows established .NET and repository conventions.
- Methods and types have appropriate visibility; implementation details are not exposed unnecessarily.
- Magic strings, magic numbers, and duplicated configuration values are replaced with well-named constants, options, or types where justified.
- Comments explain non-obvious decisions and constraints rather than restating the code.
- Dead code, commented-out code, unused dependencies, and obsolete compatibility paths are removed.

### 6. Dependency injection and configuration

- Services use the narrowest correct lifetime (`Singleton`, `Scoped`, or `Transient`) for the application's hosting model.
- Scoped services are not captured by singletons, and disposable dependencies are not resolved or retained incorrectly.
- Dependencies are constructor-injected or otherwise supplied explicitly; service location is avoided.
- Configuration uses strongly typed options with validation for required or constrained values.
- Secrets, credentials, connection strings, and environment-specific values are not committed to source control.
- New packages are necessary, actively maintained, compatible with .NET 10, and do not duplicate existing capabilities.

### 7. Data access and persistence

- EF Core queries are efficient, bounded, and translated server-side where expected.
- Read-only queries use no-tracking behavior where appropriate.
- Related data is loaded intentionally; accidental N+1 queries and unnecessarily large object graphs are avoided.
- Cancellation tokens are propagated to database and external I/O operations.
- Transactions are used only where atomicity is required and have a clear boundary.
- Domain or application layers do not depend directly on `DbContext`, provider-specific APIs, migrations, or persistence entities unless explicitly allowed by the project's architecture.
- Schema changes include safe migrations and consider backward compatibility, deployment order, and existing data.

### 8. Asynchronous and concurrent code

- Async operations are awaited end-to-end; sync-over-async patterns such as `.Result`, `.Wait()`, and unnecessary blocking are avoided.
- `async void` is used only for framework-required event handlers.
- Cancellation tokens are accepted and propagated for cancellable I/O-bound workflows.
- Shared mutable state is avoided or protected correctly.
- Parallelism is introduced only when beneficial, bounded, and safe for the underlying dependencies.
- Exceptions are not silently swallowed and preserve useful diagnostic context.

### 9. Security and authorization

- Authentication and authorization are enforced on the server or trusted backend boundary, not only by hiding UI elements.
- Authorization policies and role or claim checks are centralized and consistently applied.
- All external input is treated as untrusted and validated at the correct boundary.
- Output encoding is preserved; raw HTML or JavaScript interop does not introduce cross-site scripting risks.
- Sensitive information is not exposed through logs, exception messages, URLs, browser storage, rendered markup, or client-side assemblies.
- Anti-forgery, secure cookies, HTTPS, CORS, and content-security controls are preserved or strengthened where relevant.
- Redirects, file operations, uploads, and external URLs are validated against abuse scenarios.
- New dependencies and serialization behavior do not introduce known insecure defaults.

### 10. Error handling, logging, and observability

- Failures are handled at the correct layer and translated into meaningful user-facing outcomes without leaking internals.
- Logs are structured, actionable, and use appropriate severity levels.
- Log messages avoid sensitive or excessive data and include useful correlation context where available.
- Expected business failures are distinguished from unexpected technical exceptions.
- Retry, timeout, and resilience policies are applied only to transient operations and avoid retry storms or duplicate side effects.
- New critical flows expose sufficient telemetry, metrics, or tracing to diagnose production failures.

### 11. Performance and resource usage

- The PR avoids unnecessary renders, allocations, database round trips, network calls, serialization, and large in-memory collections.
- Expensive operations are not performed repeatedly during rendering or component lifecycle execution.
- Pagination, streaming, virtualization, or incremental loading is used when data volume can be large.
- Caching has explicit ownership, invalidation rules, scope, and memory limits.
- JavaScript interop calls are minimized, batched when reasonable, and disposed correctly.
- Performance optimizations are evidence-based and do not significantly reduce clarity without measurable benefit.

### 12. Accessibility and user experience

- MudBlazor components are configured so that labels, accessible names, keyboard interaction, focus behavior, and validation feedback remain correct.
- Interactive UI is keyboard accessible and uses semantic HTML.
- Form controls have associated labels, validation feedback, and understandable error messages.
- Focus management is correct after navigation, dialogs, validation failures, and dynamic updates.
- ARIA attributes are used only where native semantics are insufficient and remain accurate as state changes.
- Loading, empty, success, disabled, and failure states are represented clearly.
- User actions that can be repeated or take time are protected against accidental duplicate submission where necessary.

### 13. Testing and verification

- New or changed business behavior is covered by focused automated tests at the lowest practical layer.
- Domain and application tests do not require UI, network, database, clock, or filesystem access unless those dependencies are the subject of the test.
- Blazor components with meaningful behavior are covered by component tests where valuable.
- Integration tests cover critical boundaries such as persistence, authentication, authorization, serialization, and external-service adapters.
- Tests verify observable behavior rather than private implementation details.
- Tests are deterministic, isolated, readable, and do not depend on execution order or arbitrary delays.
- Bug fixes include a regression test when practical.
- The full solution builds without warnings introduced by the PR, and all relevant tests, analyzers, and formatting checks pass.

### 14. API and contract compatibility

- Public APIs, routes, component contracts, events, serialized payloads, and persisted data remain backward compatible unless a breaking change is intentional and documented.
- Validation and error contracts remain consistent for existing consumers.
- Versioning or migration strategy is provided for intentional breaking changes.
- Changes to shared DTOs do not unintentionally expose domain or persistence internals.

### 15. PR quality and scope

- The PR has one coherent purpose and avoids unrelated refactoring or formatting churn.
- The description explains the problem, chosen approach, important trade-offs, risks, and verification performed.
- Significant architectural or behavioral decisions are documented in code, the PR description, or an ADR as appropriate.
- Generated files, migrations, configuration, documentation, and tests required by the change are included.
- Reviewers can understand the change from the diff without relying on undocumented external context.

### Scoring guidance

- **9–10:** exemplary; fully satisfies the criterion with no meaningful concerns.
- **7–8:** good; minor improvements are possible but the PR is safe to merge.
- **5–6:** mixed; notable issues should be addressed or explicitly accepted before merge.
- **3–4:** poor; substantial rework is required.
- **1–2:** critical failure; the PR violates the criterion or introduces significant risk.
- Use `N/A` when a criterion is genuinely not applicable; do not lower the score solely because the PR does not touch that area.
- Any critical security flaw, broken architectural boundary, data-loss risk, or failing required test should block approval regardless of the average score.

## Parked for later

- business alignment (require broader context)
- architectural fit (require broader context)

## Expected side-effects

- PR comment with summary
- labels: `ai-cr:failed` (red) OR `ai-cr:passed` (green)

## Expected behavior

- on-demand retry when label `ai-cr:review` is added