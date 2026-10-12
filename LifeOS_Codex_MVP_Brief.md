# LifeOS — MVP implementation brief for Codex / Claude Code

You are the lead engineer implementing **LifeOS**, a personal life management application inspired by Agile/Scrum. Read this entire brief, inspect the repository, and **first produce a phased implementation plan with architectural decisions and risks**. Then implement **Phase 1 only**, unless explicitly instructed to continue. Keep the application runnable and tested after every phase. Ask about genuinely blocking decisions; otherwise make pragmatic choices and document them.

## Product vision and guiding principles

LifeOS helps a person organize life, set measurable long-term goals, execute through flexible weekly sprints, maintain habits, and learn through **AI-assisted retrospectives**. Optimize for sustainable progress, well-being, and reflection, not merely task throughput. UI language is Swedish initially; make localization possible. Design mobile-first, with a calm modern dashboard and powerful detail views. Avoid unnecessary data entry.

One user initially, with a path to multi-user support later. Data should be scoped to an owner from the outset. Do not build multi-user collaboration in the MVP.

## Locked decisions

- .NET 10, ASP.NET Core **Blazor Web App with Interactive Server**, Razor components; no React SPA.
- MudBlazor UI components, responsive/mobile-first.
- Modular monolith: `LifeOS.Web`, `LifeOS.Application`, `LifeOS.Domain`, `LifeOS.Infrastructure`; xUnit tests.
- EF Core + **SQLite** for the MVP; persistent database file, schema migrations, and backups.
- Docker multi-stage build and Docker Compose for independent hosting on a NAS.
- First deployment target: **Home Assistant OS with Supervisor**, packaged as a Home Assistant app (formerly add-on), accessible through Ingress/sidebar. **This is a hosting/packaging requirement, NOT an integration with HA entities, APIs, or automations.** The app must also run independently without Home Assistant.
- Private GitHub repository, GitHub Actions for build/test/container publishing. Never commit secrets. Do not assume permission to create/push a repository: request explicit approval before external changes.
- AI via a server-side provider abstraction, initially OpenAI API; AI is optional at runtime and never required for core CRUD, planning, or retros.
- Notifications via an extensible interface, initially **ntfy**. Configurable notifications for tasks, goals and retros. No HA notification integration in MVP. Never send sensitive retro content in notifications by default.
- Use `Program.cs` for ASP.NET Core composition.

## Core domain and functional requirements

### 1. Life areas
Customizable life areas (e.g. Home, Health, Cycling, Personal Development). Goals, projects, tasks and habits may be associated with a life area. Avoid golf-related example data.

### 2. Goals and planning horizons
Long-term goals at **year, quarter and month** horizons; **weekly sprints** for concrete execution, plus a daily focus view. Do not implement separate yearly/monthly sprint backlogs. Goals may have parent/child relationships and optional links to projects, tasks and habits.

Support four goal progress modes:
1. Numeric with start/current/target values, including decreasing targets and units (e.g. cycling FTP).
2. Milestones.
3. Completion of linked tasks.
4. Manually recorded percentage.

Persist timestamped goal-progress history for graphs and retrospective comparisons. Handle zero-distance numeric goals, bounds, and changes to target safely.

### 3. Projects, tasks, Inbox and Backlog
Projects and subtasks. Task status, priority, optional estimate, due date, scheduled day/time (optional), life area, project and goal links. One-click/quick-capture Inbox without mandatory categorization. Move captured items to backlog and plan into sprints. Do not require every task to belong to a project or goal.

### 4. Weekly sprints
Plan weekly sprints with optional sprint goal and Kanban states (e.g. To Do, In Progress, Done). Allow adding, removing, rescheduling and reprioritizing tasks mid-week. **Record original sprint scope and subsequent scope changes** so retrospective reports distinguish planned versus added work. Unfinished work can be moved to the next sprint or backlog. Optional time estimates and weekly capacity; warn about overplanning, never block it. Today view can show selected daily focus tasks without forcing calendar time blocks.

### 5. Habits and routines
Habit tracker with flexible recurring targets (e.g. three cycling sessions per week), completion history, and optional generation of sprint tasks **per habit**. Prevent duplicate generated tasks and double counting when a generated task is completed. Habit completions may contribute to relevant goals.

### 6. Retrospectives — a flagship feature
Support **monthly, quarterly, annual**, and optionally weekly retros; **weekly retros are never mandatory**. Retros can be started at any time, saved as drafts, resumed, and completed without AI. Each retro provides accurate period-specific statistics (tasks completed, planned vs added work, carryover, goal progress, habit consistency) plus guided questions. Avoid interpreting incomplete metrics as failure. Record qualitative reflections on energy, balance, stress, and satisfaction only if the user chooses to provide them.

An AI coach offers **analytical, reflective, or balanced** modes. It can ask context-aware follow-up questions, compare with earlier retros, identify tentative patterns, and propose specific improvements. Retro actions/experiments have owners, dates, status, and follow-up outcomes. Future retros should revisit previous actions. Annual retros may summarize earlier monthly/quarterly retros, but must work with sparse historical data.

### 7. AI across the product
Use AI for goal formulation, sprint planning suggestions, dashboard insights, and guided retros. Build an `IAiCoachService` or similar interface in Application and provider implementation in Infrastructure. Construct minimal, explicitly scoped context server-side; protect personal data and API keys. Distinguish observed data from hypotheses. Rate-limit/cache on-demand insight generation; do not continuously invoke AI on every page render. **AI must never directly mutate application data.** Store proposals with Pending/Accepted/Rejected state; validate accepted proposals in application services before applying changes. Graceful fallback when API keys are absent or AI calls fail.

### 8. Dashboard and navigation
Modern, uncluttered mobile-first dashboard: today's focus, current sprint, goal progress, upcoming deadlines, and optional AI insights. Main sections: Overview, Goals, Planning/Sprints, Projects, Habits, Retros. Inbox/quick add accessible everywhere. More advanced Kanban, goal charts and retro history in detail views. Swedish UI initially; localization-ready.

### 9. Notifications
Configurable notification categories (tasks, goals, retros; habits optional later), quiet hours, scheduling, delivery status, retry policy and duplicate suppression. Use `INotificationSender` and an ntfy implementation. Do not require ntfy for the rest of the application to work. A scheduled/background worker may use .NET hosted services; make execution and restart behavior safe. Store credentials outside the repository. No Home Assistant API dependency.

## Suggested data model (refine before implementation)

- `ApplicationUser` / user ownership
- `LifeArea`
- `Goal`, `GoalMilestone`, `GoalProgressEntry`, goal relations
- `Project`, `TaskItem`, task links / hierarchy
- `Sprint`, `SprintTask` and immutable sprint-scope change/audit records
- `Habit`, `HabitCompletion`, optional generated-task linkage
- `Retrospective`, `RetroResponse`, `RetroAction` / experiment, `RetroActionFollowUp`
- `AiConversation` / messages (if needed), `AiInsight`, `AiProposal`
- `NotificationPreference`, `ScheduledNotification`, `NotificationDelivery`

Choose sensible relationships and indexes, preserve history, use UTC timestamps for events, and convert to user-local dates for weekly/monthly boundaries. Define timezone behavior explicitly. Prefer application-layer rules over business logic in Blazor components.

## Security and identity

Use secure authentication for standalone deployment (ASP.NET Core Identity is acceptable). For HA Ingress, investigate how HA-authenticated requests are trusted and mapped to the local user; **do not assume that Ingress alone secures an independently exposed port or automatically supplies a usable application identity**. The MVP may use a single-owner account but must have a clear, tested authorization boundary. CSRF/antiforgery, secrets, database ownership, and access to AI/ntfy endpoints must be considered. Do not expose the database or private endpoints publicly by default.

## Deployment and HA packaging

- Multi-stage Dockerfile, health endpoint, container-friendly configuration and logging.
- Docker Compose with persistent data volume, environment variables, documented backup/restore.
- Home Assistant app packaging (`config.yaml`, Dockerfile or build setup, entrypoint) with Ingress and `/data` persistence.
- **Early technical spike:** verify Blazor Interactive Server/SignalR WebSockets, base paths, static assets, authentication, and reconnect behavior behind Home Assistant Ingress. If this does not work reliably, report the problem and propose a fallback before building further features.
- Verify SQLite migration/backup strategy under HA and standalone Docker. Do not run multiple replicas against one SQLite database file.
- GitHub Actions: restore, build, tests, and optionally publish container image after credentials/registry strategy are approved. Private repository and image access must not be assumed to be automatically available to HA OS.

## Phased delivery and acceptance

**Phase 1 — Foundation and hosting spike (implement first):** Create solution, projects, DI, EF Core/SQLite migration, minimal secure identity, MudBlazor shell, Swedish navigation, health endpoint, unit-test project, Docker/Compose, HA packaging prototype. Prove local and Docker startup; document and test HA Ingress behavior. Do not build all product features yet.

**Phase 2 — Organize and execute:** Life areas, projects, tasks/subtasks, Inbox/backlog, weekly sprints, scope-change audit, Kanban, Today and dashboard. Tests for sprint changes and carryover.

**Phase 3 — Goals and habits:** Annual/quarterly/monthly goals, four progress modes and history, habit recurrence and completion, optional generated sprint tasks. Tests for numeric goals and duplicate prevention.

**Phase 4 — Retros without AI:** Monthly/quarterly/annual retros, optional weekly retros, period statistics, guided questions, drafts, historical review, actions/experiments and follow-up. Tests for period boundaries and aggregations.

**Phase 5 — AI and notifications:** Provider abstraction, AI-guided retro, approved proposals, AI insights and planning suggestions; ntfy sender, preferences, quiet hours and reliable scheduling. Graceful operation when AI/ntfy is unconfigured.

**Phase 6 — Polish and release:** Mobile UX, accessibility, charts, onboarding to set initial goals, security review, backup/restore verification, documentation, and release workflows.

## Definition of done for each phase

- App builds, starts and relevant tests pass.
- Feature is usable on mobile and desktop where applicable.
- EF Core migrations included and data persists after container restart.
- Security/authorization and error handling considered.
- README includes setup and validation steps.
- Changes are small and reviewable; summarize files changed, tests run, risks, and next phase.

## First response expected from coding assistant

1. Inspect current repository (if any).
2. Present architecture/implementation plan, key risks and only essential unresolved questions.
3. Explicitly address the HA Ingress + Blazor Interactive Server spike and single-user authentication model.
4. Then implement **Phase 1 only** once the plan is agreed, or ask for approval if repository creation/push or external actions are needed.
