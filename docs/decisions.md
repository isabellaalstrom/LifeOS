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

# Decisions (Phase 5: AI and notifications)

| # | Decision | Why |
|---|---|---|
| 51 | AI is off by default, per owner (`UserPreference`), and only usable when a server-side key exists. The Application layer depends on `IAiClient`; `OpenAiClient` is the only OpenAI-specific code. The key is read from configuration (HA option `openai_api_key`), never sent to the browser, never logged; only HTTP status codes are logged. | Privacy and provider independence |
| 52 | The AI never mutates data. Each feature returns a suggestion: retro actions, sprint picks and goal drafts become `AiProposal` rows (Pending, Accepted, Rejected); a goal formulation only pre-fills the form. Accepting applies the proposal through the normal service (`SprintService`, `RetroService`, `GoalService`), which revalidates everything. If it no longer applies it stays Pending with the reason. | "AI only proposes": the owner is always the decision maker; stale suggestions cannot corrupt state |
| 53 | Minimal context per feature (`AiContextBuilder`): retro = numbers + the owner's own answers; insight = numbers only; sprint = numbered candidate titles + capacity. Never notes, emails, account ids. Context is capped (`MaxContextChars`). The model refers to sprint candidates by number, and unknown numbers are discarded. | Data minimisation; the model cannot invent tasks |
| 54 | Coach replies keep *observations* (what the data shows) and *hypotheses* (tentative) as separate fields, in Swedish, in one of three modes (balanced, analytical, reflective). The system prompt forbids failure framing and states the AI cannot change anything. Unusable or malformed output is a clean `Failed` and stores nothing. | Brief: separate observations from interpretation; do not treat incomplete metrics as failure |
| 55 | Cost and abuse control: a per-owner daily call limit (`AiCallLog`, counted in the owner's local day), insight cache by context hash and 12 h TTL, forced refresh within 5 minutes served from cache, nothing is generated when a page renders. Generating also respects the owner's opt-out even if a cache exists (reading the cache is separate). | Predictable cost, no surprise calls |
| 56 | Notifications: nothing is planned or sent until a category is switched on. Reminders are rows (`ScheduledNotification`) with a stable key unique per owner (for example `tasks-due:2026-10-14`), so planning is idempotent. Texts are generic (counts and invitations), never titles or retro content. A reminder more than 6 h late is skipped, except retro invitations which are re-keyed to the current day within 7 days after the period. | No spam, no leaks via lock screens or push services |
| 57 | Delivery is restart-safe: a notification is claimed (`Sending` + 5 min lock), a dead worker's lock expires and it is retried; failures retry after 5 min, 30 min, 2 h and then become `Failed`; nowhere to send (no device) is `Suppressed`, not an error. Quiet hours (local, wrapping midnight) delay instead of drop; a category switched off after planning suppresses. | At-least-once without duplicates in the common cases; no endless retries |
| 58 | Web Push (PWA) is the primary channel; ntfy is an optional extra (JSON publish, token in config only). VAPID keys are generated on first use into `/data/vapid.json` (restricted permissions, included in backups); gone subscriptions (404/410) are deactivated. The service worker only shows generic push text and opens the app; it caches nothing. Push needs HTTPS (or localhost); the settings page says so. | User choice in the brief; secrets stay out of the repository |
| 59 | Background work runs per owner through `OwnerScopeRunner` + `OwnerOverride` (`CurrentUser` honors it), so the global owner query filter and stamping still apply. | The worker cannot read or write across owners |

# Decisions (Phase 6: polish)

| # | Decision | Why |
|---|---|---|
| 60 | Onboarding is one card on the overview while the owner has no goals at all: example goals pre-fill the normal goal dialog, nothing is created until saved, and the card disappears by itself. | Gentle start without a wizard or forced steps |
| 61 | The well-being trend uses only the owner's own optional ratings from completed monthly retros, draws gaps as gaps (nothing estimated), and ships a visually hidden table. | Brief: do not read missing data as failure; accessibility without scripts |
| 62 | Accessibility baseline: skip link (full URL because of `<base href>`), visible focus outlines, reduced-motion support, labelled icon buttons, chart alternatives as text. | Keyboard and screen reader users get the same app |
| 63 | Circuits re-validate the security stamp every 30 minutes (`RevalidatingAuthenticationStateProvider`). | An open circuit no longer outlives a removed user or a changed stamp |
| 64 | Security review is kept in `docs/security-review.md` with accepted risks (HTTP on the LAN, no password-change UI yet, no script CSP). | Risks are explicit and revisitable |

# Decisions (Phase 7: simplified structure)

| # | Decision | Why |
|---|---|---|
| 65 | Three building blocks: **tasks**, **goals**, **recurring tasks**. Projects are gone: a goal with tasks is the project. Goals are followed by their tasks or by a number; other modes remain in the engine for old data but are no longer offered. Parent goals can no longer be created. | "Goal and project are the same thing" in use; fewer concepts to hold in your head |
| 66 | The inbox is gone: quick capture lands in the backlog. `Triage` was removed from tasks. | One list instead of two places for unsorted tasks |
| 67 | A recurring task is the old habit engine under a new name: "N per week" always generates its tasks into the week plan, automatically when Idag or Vecka is opened (`EnsureWeekTasksAsync`, idempotent, silent for closed weeks) and after saving; daily and monthly ones are ticked off on Idag. Backlog can filter on recurring tasks. | The user's mental model ("a habit is a recurring task") without losing streaks, goal contribution or the no-duplicates guarantees |
| 68 | Life areas are shown as **Kategorier** (free tags). Their fields appear only when some exist. | Same function, plainer word, no clutter |
| 69 | Navigation: Idag, Vecka, Backlog, Mål, Retron. Idag is the home page (focus, recurring, due, plan suggestions, week, goals, insight, retro reminders) with shortcuts to Återkommande, Kategorier, Retron and the guide. The separate overview and today pages were merged. | The brief's "execute daily" loop starts where you land |
| 70 | Retro suggestions (last month, quarter, year) show on Idag and only for periods that ended after the owner's first data (task, goal or recurring item). Notifications use the same rule. | A new user is not asked to reflect on a year they were not here |

# Decisions (Phase 8: reminders from the phone)

| # | Decision | Why |
|---|---|---|
| 71 | LifeOS talks to Home Assistant only for one optional to-do entity (`reminders_entity`), through the standard `todo.get_items`, `add_item` and `update_item` services over the Supervisor API (`homeassistant_api: true`). Off unless an entity is set; the Supervisor supplies the token and it is never logged. Only status codes are logged. | iOS Reminders are already in HA through the iCloud integration; no extra account, key or cloud hop |
| 72 | One list, two directions, each off until the owner enables it: import (open reminders become backlog tasks) and send (open tasks that are due/overdue or in today's focus are added, once, with a date so they show under "today"). The background worker syncs about once a minute; Settings has "Synka nu". | The owner uses a single reminders list for today and overdue |
| 73 | Sync is three-way: each link keeps a snapshot (title, date, done) from the previous sync, and the side that changed since then wins, so an older value never overwrites a newer one. Done/not-done and titles follow whichever side changed; a date follows the phone, and a LifeOS-side date is sent when the reminder is unchanged. Status changes go through `TaskService`, so goals and recurring completions stay correct. | iCloud lags by minutes; last-writer-wins by wall clock would lose edits |
| 74 | A reminder LifeOS sent is tracked as a *pending* link until it shows up in the list (matched by title), so a slow iCloud never turns it into a second imported task; a pending link older than 24 h is dropped. A reminder deleted or cleaned up on the phone leaves the task alone and is not sent again. At most 20 tasks are sent per pass. | Duplicates are the main risk of two-way sync; deletion on the phone is treated as "dismissed" |
