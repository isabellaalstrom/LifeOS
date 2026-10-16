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
