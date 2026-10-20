# LifeOS

Personal life management: goals, weekly sprints, habits and AI-assisted retrospectives.
.NET 10 · Blazor Web App (Interactive Server) · MudBlazor · EF Core + SQLite. UI language: Swedish.

**Status: MVP feature-complete (Phases 1–6): foundation, organize and execute, goals and habits, retrospectives, AI and
notifications, polish.** See `LifeOS_Codex_MVP_Brief.md`, `docs/decisions.md` and `docs/security-review.md`.

## What works today

| Area | What you can do |
|---|---|
| **Idag** (home) | Daily focus, recurring items to tick off, what is due, suggestions from this week's plan, the week at a glance, your goals, an optional AI insight, and gentle reminders to look back (last month, quarter, year) when it is time. Shortcuts to the rest sit at the bottom. |
| **Quick capture** | The `+` button works on every page: one field, lands in the **Backlog**. |
| **Backlog** | All open tasks in one list. Filter by text, category or *recurring only*. Plan into this or next week from the row menu. |
| **Vecka** (`/planning`) | A weekly plan per ISO week: goal, capacity with a friendly warning (never a block), Kanban (drag and drop, or the card menu on mobile), reorder, move to next week. |
| **Scope history** | Every change to a week's scope is logged: *planned from start*, *added during the week*, *removed*, *carried in/out*. The start is automatic (Monday 00:00 local time) with an optional **Starta sprint** button. |
| **Close the week** | Per unfinished task: carry to next week or back to the backlog. Finished work stays in the closed week. |
| **Uppgifter** | Subtasks (one level), priority, estimate, deadline, optional scheduled day/time, notes. A task can belong to a goal and have a category. |
| **Mål** (`/goals`) | Year, quarter and month goals. Follow a goal by its **tasks** (progress = done tasks) or by a **number** you update (for example FTP 240 to 280). Progress history chart. |
| **Återkommande** (`/habits`) | Recurring tasks: "3 per week" puts the tasks into the week's plan by itself; daily and monthly ones are ticked off on Idag. Consistency and streaks, optionally counting towards a goal. |
| **Kategorier** | Optional free tags (Hälsa, Hem, Jobb) for tasks, goals and recurring items. |
| **Retron** (`/retros`) | Monthly, quarterly and annual reflections (weekly is optional), saved as drafts. Period facts, guided questions, optional well-being (with a small trend chart), actions and experiments that are followed up in later retros. Suggested on Idag only for periods you actually used the app in. |
| **AI coach** (optional, off by default) | In a retro: a coach that reads what you saved, separates *what the data shows* from *tentative interpretations*, asks one follow-up question and can suggest actions. Also: help formulating a goal, week suggestions from the backlog, and an on-demand insight. The AI **only proposes**; nothing is saved until you press *Godkänn*. Everything works without it. |
| **Notiser** (optional, nothing sent by default) | Web Push (PWA) reminders for due tasks, a weekly goal check-in and retro invitations, with a send time and quiet hours. Optional extra channel: ntfy. Texts are generic and never contain your titles or answers. |
| **Kom igång / guide** | A short onboarding card while there are no goals, and a one-page guide (`?` in the top bar) explaining tasks, goals and recurring tasks. |
| **Inställningar** (`/settings`) | Time zone, AI on/off and coach style (with what is shared), notification categories, this device's push registration, a test notification and recent deliveries. |

Dates and "today" use the owner's time zone (`Europe/Stockholm` by default); weeks start on Monday.

## Layout

```
src/LifeOS.Domain          entities, period/time rules (no dependencies)
src/LifeOS.Application     interfaces and use-case services
src/LifeOS.Infrastructure  EF Core/SQLite, Identity store, backups, health check
src/LifeOS.Web             Blazor UI, Program.cs composition, ingress support
tests/LifeOS.Tests         xUnit unit + integration tests
tools/IngressProxy         local stand-in for Home Assistant ingress (spike harness)
ha/                        Home Assistant app (add-on) packaging
docs/                      decisions and the HA ingress spike report
```

## Run locally

Requires the .NET 10 SDK.

```powershell
dotnet run --project src/LifeOS.Web      # http://localhost:5169
dotnet test
```

On first start the app creates `src/LifeOS.Web/data/` (SQLite database, keys) and applies migrations. Open the site and
create the owner account on the **first-run page** (password ≥ 10 characters). Registration closes once the owner exists.
Do not expose a fresh instance to the internet before creating the account.

Configuration (environment variables use `__`):

| Setting | Default | Purpose |
|---|---|---|
| `LifeOS__DataPath` | `data` (container: `/data`) | database, Data Protection keys, backups |
| `LifeOS__Ingress__Enabled` | `false` (HA app: `true`) | honor `X-Ingress-Path` / `X-Forwarded-*` from trusted peers |
| `LifeOS__Ingress__TrustedProxyAddresses__0` | `172.30.32.2` | HA Supervisor ingress gateway |

| `LifeOS__Ai__OpenAi__ApiKey` | empty (AI unavailable) | OpenAI key, server side only (HA app option `openai_api_key`) |
| `LifeOS__Ai__OpenAi__Model` | `gpt-4o-mini` | model name (HA option `openai_model`) |
| `LifeOS__Ai__DailyLimit` | `30` | most AI calls per day |
| `LifeOS__Notifications__WebPush__Subject` | project URL | VAPID contact, `mailto:` or `https:` (HA option `push_subject`) |
| `LifeOS__Notifications__Ntfy__Url` / `Topic` / `Token` | empty (ntfy off) | optional extra channel (HA options `ntfy_url`, `ntfy_topic`, `ntfy_token`) |

Health: `GET /health` (anonymous, returns `Healthy`/`Unhealthy`, no details).

### AI and privacy

* AI is **off by default**, per owner, and only usable when an API key is configured on the server. The key never reaches the browser.
* Only the minimum for the feature you use is sent: numbers and summaries, your own retro answers when you ask the coach to read a retro, and task titles only for sprint suggestions. Never notes, account data or passwords.
* The AI cannot create or change anything. Its suggestions are *proposals* you accept or reject; accepting runs the normal validation.
* Answers are cached and rate limited; failures never change data and the app keeps working.

### Notifications and HTTPS

Web Push needs a secure context: open LifeOS over **HTTPS** (for example through Home Assistant Cloud/Nabu Casa, or a TLS reverse proxy) and enable push per device under **Inställningar**. VAPID keys are generated on first use in `/data/vapid.json` and included in backups. Plain `http://localhost` also works for testing. Without HTTPS the settings page explains this and ntfy can be used instead.

## Docker / Compose (standalone, e.g. NAS)

```bash
docker compose up -d --build            # http://localhost:8080 (bound to 127.0.0.1 by default)
docker compose ps                       # healthy after ~20 s
```

Data lives in the `lifeos-data` volume (`/data`). Run **one** replica only. For LAN access change the port mapping in
`compose.yaml` and put a TLS reverse proxy in front (needed later for Web Push notifications).

### Backup and restore

Backups cover everything in `/data`: the SQLite database, Data Protection keys and the VAPID keys. In Home Assistant the app is backed up *cold* (stopped during the backup) so the database file is consistent; restoring an HA backup restores all of it, including push registrations.
Restore is verified by tests: a `VACUUM INTO` copy opens, migrates and reads back all data (`DatabaseTests`).


The app takes an automatic `VACUUM INTO` backup in `/data/backups` before applying a migration to an existing database.
For a manual backup, stop the container so the SQLite files are consistent, copy the volume, start again:

```bash
docker compose stop
docker run --rm -v lifeos-data:/data -v "$PWD:/backup" alpine sh -c 'cp -a /data /backup/lifeos-data-$(date +%F)'
docker compose start
```

Restore: stop, replace the contents of `/data` with the backup (`lifeos.db` plus `keys/`), start. Restoring `lifeos.db`
without `keys/` keeps the data but signs you out. A scheduled in-app backup arrives in a later phase; the backup/restore
path is covered by `DatabaseTests.Backup_is_a_consistent_copy_that_can_be_restored_and_read`.

## Home Assistant app

See [docs/ha-ingress-spike.md](docs/ha-ingress-spike.md) for the install and test procedure and what is/isn't proven.
Short version: add `https://github.com/isabellaalstrom/LifeOS` as an app repository in HA (needs a published release image),
or for a local build run `./ha/build-local-app.ps1`, copy `artifacts/ha-app/lifeos` to HA `/addons/lifeos` and install from *Local add-ons*.
Releases: bump `version` in `lifeos/config.yaml`, then push a matching `vX.Y.Z` tag; `publish-app-image.yml` publishes the images to GHCR.
LifeOS is only hosted by HA; it does not use HA entities, APIs or automations. Login is the app's own cookie login.

## Validation checklist

| Check | How | Status |
|---|---|---|
| Builds, all tests pass (109) | `dotnet test` | verified |
| Starts locally, migrations applied, data persists across restart | `dotnet run`, restart, still signed in | verified |
| `/health` anonymous 200; app pages redirect to login | integration tests + browser | verified |
| Mobile + desktop shell (drawer / bottom navigation) | browser at 375 px and desktop | verified |
| Behind simulated ingress incl. WebSockets | `tools/IngressProxy` + browser | verified |
| Docker image builds and runs, data survives container restart | built and run locally, volume kept db and keys across restart; CI smoke test | verified |
| HA app on real Home Assistant OS (install, login, WebSockets, navigation) | docs/ha-ingress-spike.md | verified (reconnect/Companion/diagnostics headers still open) |
| Phase 2 flows in a real browser (areas, project, task, quick capture, inbox, plan, Kanban drag and drop, capacity warning, focus, close sprint with carry-over) | local run + browser | verified locally |
| Phase 1 database upgrades to Phase 2 with data kept and a pre-migration backup | test + local run | verified |
| Phase 2 on Home Assistant (release 0.2.0) | install the update and walk through the flows above | **not yet run** |
| No horizontal overflow on mobile width on all new pages | browser at 375 px | verified |

## CI

`.github/workflows/ci.yml`: restore, build, test, and a Docker build check on every push/PR. Image publishing is not
configured: registry and credentials strategy (a private repo/image is not automatically reachable from HA OS) is an open decision.
