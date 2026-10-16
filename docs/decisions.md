# Decisions (Phase 1)

| # | Decision | Why |
|---|---|---|
| 1 | Layers: Domain (no deps) ← Application (interfaces, services) ← Infrastructure (EF Core, providers) ← Web (composition in `Program.cs`) | Keeps business rules testable and out of Blazor components |
| 2 | Every owned entity has `OwnerId`; EF global query filter + insert stamping + owner immutability | Authorization boundary exists from day one; multi-user later without a data migration |
| 3 | Events stored in UTC (`DateTimeOffset` as sortable binary); calendar periods computed in the user's IANA zone (`AppUser.TimeZoneId`, default `Europe/Stockholm`), weeks start Monday (`PeriodCalculator`) | Correct week/month/quarter statistics across DST |
| 4 | SQLite + WAL, single instance, migrations applied at startup after an automatic `VACUUM INTO` pre-migration backup | Safe upgrades on a NAS/HA; consistent online backups |
| 5 | Auth: ASP.NET Core Identity, single owner created on first run (registration closes afterwards), lockout on, antiforgery on, fallback policy requires authentication | Secure standalone default; also used in HA (ingress is not identity) |
| 6 | Data Protection keys persisted in `/data/keys` | Cookies/antiforgery survive restarts and container recreation |
| 7 | Localization: `IStringLocalizer<SharedResource>`, Swedish is the neutral resource language, `sv-SE` default culture | UI strings never hard-coded in pages (login/setup/nav/placeholders) |
| 8 | MudBlazor with system fonts only (no Google Fonts) | Works offline on a NAS; no third-party requests |
| 9 | Notifications: **Web Push (PWA) first**, `INotificationSender` abstraction, ntfy optional later | User does not want a separate app. Web Push needs HTTPS and a stable origin, so the HTTPS access path is a prerequisite checked in the spike/Phase 5 |
| 10 | HA ingress support is opt-in (`LifeOS:Ingress:Enabled`), trusts only `172.30.32.2` | Prevents header spoofing on independently exposed ports |
| 11 | Central package management skipped for now (5 projects) | Avoid ceremony; revisit if versions drift |

## Known limitations / follow-ups

- Docker image and HA app build are written but see the README status table for what has been executed.
- Data Protection keys are not encrypted at rest (no OS key store in the container); the volume itself is the trust boundary.
- No circuit-level auth revalidation yet (sign-out elsewhere does not end an open circuit); Phase 6.
- First-run setup is open until the owner exists: do not expose a fresh instance publicly before creating the account.

# Decisions (Phase 2)

| # | Decision | Why |
|---|---|---|
| 12 | Inbox is a *triage* state on a task, not a status. Planning into a sprint is a separate membership (`SprintTask`). | A task can be triaged, planned, moved or dropped without losing its own status; capture needs no categorization |
| 13 | Statuses To do / In progress / Done / Cancelled. Kanban columns are the first three. Cancelled is how history-bearing tasks are "deleted". | Retros stay accurate; deleting never rewrites past sprints |
| 14 | Sprint = ISO week identified by its Monday in the owner's time zone. Created lazily on first change; viewing never writes. | No empty rows; boundaries are correct across DST |
| 15 | Original scope is *derived* from an append-only change log with a boundary: the optional explicit start, otherwise Monday 00:00 local. Changes after the boundary count as added/removed. | Planned vs added work for retros without a mandatory ceremony; deterministic and testable (`SprintScope`) |
| 16 | `SprintTask` rows are never deleted (`LeftUtc` + reason). Closed sprints keep their full membership. | Retros can show what a week contained, including work that moved on |
| 17 | Carry-over at close is per task (next sprint or backlog), logged as removed + added with reason `CarryOver` / `ReturnedToBacklog`. Done work is not moved. | No double counting; "carried" is reportable separately from "dropped" |
| 18 | A task can be in only one *open* sprint at a time. Moving is an explicit operation. | Avoids ambiguous capacity and duplicate counting |
| 19 | Capacity is a pure advisory function (`SprintCapacity`): not set / ok / near limit (>= 90%) / over. UI wording is calm. | Brief: warn about overplanning, never block it |
| 20 | Subtasks are one level deep, cannot be planned on their own (planned via the parent), and inherit area/project. | Keeps the board and capacity simple |
| 21 | Daily focus is `FocusDate` on a task, not a calendar block. "Today" = tasks with focus today or scheduled today, plus due/overdue not already in focus. | No forced time blocking |
| 22 | Application services use a short-lived `IAppDbContext` per operation (scoped `IDbContextFactory`). | Blazor circuits live long; a context must never be shared across interactions |
| 23 | Navigation and the planning tabs are plain links (CSS-driven, server-rendered); task actions, dialogs and Kanban need the live circuit. | Learned in Phase 1: never make navigation depend on client JavaScript, so a broken circuit still leaves the app navigable |
| 24 | Enums are stored as text; `DateOnly`/`TimeOnly` natively. | Readable database, sortable dates |
| 25 | `SprintScopeChange` has no foreign keys and rejects update/delete at the DbContext level. | The audit must survive any other data change |

## Known limitations (Phase 2)

- Kanban drag and drop uses the browser's HTML5 drag events; on touch devices use the card menu ("Flytta till").
- Reordering is move up/down within a column; free drag-reordering is not implemented.
- Time zone is stored per user (`AppUser.TimeZoneId`) but there is no settings page yet (defaults to Europe/Stockholm).
- Goal and habit links on tasks arrive in Phase 3.

# Visual design

| # | Decision | Why |
|---|---|---|
| 26 | Warm "paper" look inspired by a calm family-hub app: cream background, sage side panel, one deep indigo accent, soft pastel cards (sage, butter, peach, lavender, sky), serif headings, large rounded controls, small illustrated greeting on the overview. | Brief: calm, modern, mobile-first, optimized for reflection rather than throughput |
| 27 | System fonts only: a serif stack for headings and the system sans for text. No web fonts, no external requests. | Works offline on a NAS; no third-party calls |
| 28 | The palette exists twice: as MudBlazor theme (`Theming/LifeOsTheme.cs`) and as CSS variables in `app.css`. The CSS variables follow `prefers-color-scheme` and override Mud's palette from `body`. | Correct light/dark colors on first paint, before the Blazor circuit starts (no flash) |
| 29 | Light and dark follow the system setting; there is no manual toggle yet. | Fewer settings; revisit with a settings page |

# Decisions (Phase 3: goals and habits)

| # | Decision | Why |
|---|---|---|
| 30 | Goals belong to a calendar period (year, quarter, month) identified by its first day. A child must have a shorter horizon and lie inside the parent's period. | Matches the brief's planning horizons; makes cycles impossible and the hierarchy meaningful |
| 31 | Four progress modes (numeric, milestones, linked tasks, manual) computed by `GoalProgressCalculator`. Numeric works in both directions, is clamped to 0-100, and a zero-distance goal is complete only when the value equals the target. | Safe arithmetic: no division by zero, target changes and overshoot handled |
| 32 | Progress history (`GoalProgressEntry`) is written by one component (`GoalProgressRecorder`) only when something changed, inside the same transaction as the change. Each point stores value, percent and the target at the time. | Consistent history for graphs and retro comparisons; changing a target never rewrites the past |
| 33 | "Achieved" is always set by the owner. 100% only suggests it. | The owner decides what counts as done |
| 34 | Tasks, projects and habits link to a goal with a nullable `GoalId`. A task counts for a goal directly or through its project; subtasks never count (their parent does). | Simple model, no many-to-many; no double counting |
| 35 | Habit = target count per period (day, week, month). Weekly habits can generate sprint tasks on request; the unique key (habit, week, index) in the database stops duplicates, and completions already logged this week reduce how many tasks are generated. | Idempotent generation; safe to press the button repeatedly |
| 36 | A generated task is exactly one completion: finishing adds it, reopening removes it (unique `TaskItemId`). Logging a habit that has an open generated task finishes that task instead. | Never counted twice, whichever way the owner records it |
| 37 | A habit adds its increment to a linked **numeric** goal only. Goals driven by linked tasks count the generated tasks themselves. | A completion can never contribute twice to the same goal |
| 38 | A completion counts in the period of the day it was done, also for a task generated for a later week that was finished early. | Consistency measures what was done when; simple and predictable |
| 39 | An unfinished current period never breaks a streak; wording is encouraging, never shaming. | Brief: do not interpret incomplete metrics as failure |
| 40 | Enum ordering uses explicit rank expressions (`EnumOrdering`), because enums are stored as text and ORDER BY would be alphabetical. | Found as a real bug: backlog priority was sorted None, Medium, Low, High |

# Decisions (Phase 4: retrospectives without AI)

| # | Decision | Why |
|---|---|---|
| 41 | One retro per type and period (weekly, monthly, quarterly, annual), unique in the database. Starting again resumes it. Weekly retros are optional and are never suggested. | Resumable drafts; no duplicates; no obligation |
| 42 | A retro can be completed with nothing filled in. Guided questions, well-being (1-5), summary and actions are all optional. Drafts save on field blur and with an explicit button. | Brief: usable without AI, never mandatory, well-being only if the owner chooses |
| 43 | Period statistics are facts in the owner's time zone: tasks completed, sprint scope (planned vs added vs carried), goal movement from the progress history, habit consistency. Missing data stays missing. | Accurate boundaries (a task done at 23:30 belongs to that day); incomplete metrics are not failures |
| 44 | "Planned" in a period counts fresh planning only: tasks that arrived by carry-over are reported as carried in, not planned again. (`OriginalCarriedIn` in the scope stats.) | A task carried through three weeks must not look like three weeks of planning |
| 45 | A sprint belongs to the period its Monday falls in. | Every sprint is counted exactly once, also across a month boundary |
| 46 | Habit consistency counts only whole periods that are inside the retro period, have fully passed, and began after the habit was created. | A habit is never "missing" a week it did not exist for or that is still running |
| 47 | Completing a retro stores a JSON snapshot of the statistics; reopening discards it. Drafts show live numbers. | A finished retro does not drift as data changes later |
| 48 | Actions and experiments (owner, dates, hypothesis, status) outlive their retro: plain ids, no foreign keys. Open ones are offered in every later retro for follow-up; follow-ups are appended with an outcome (worked, partly, did not work, too early). | "Future retros should revisit previous actions" without losing history if a draft is deleted |
| 49 | Larger retros show the completed smaller retros inside their period (annual: quarterly, quarterly: monthly, monthly: weekly) and the previous retro of the same type. Both may be empty. | Annual retros work with sparse history |
| 50 | Test clock: entity timestamps follow the test clock (`FakeClock` is also the `TimeProvider`). | Tests about "when was this created" were silently using wall-clock time |
