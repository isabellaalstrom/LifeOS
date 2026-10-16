# LifeOS

Personal life management: goals, weekly sprints, habits and AI-assisted retrospectives.
.NET 10 · Blazor Web App (Interactive Server) · MudBlazor · EF Core + SQLite. UI language: Swedish.

**Status: Phase 2 (organize and execute) on top of the Phase 1 foundation.** Goals, habits, retrospectives, AI and
notifications arrive in Phases 3–6 (see `LifeOS_Codex_MVP_Brief.md`).

## What works today (Phase 2)

| Area | What you can do |
|---|---|
| **Quick capture** | The `+` button in the top bar works on every page: one field, lands in the **Inkorg** (Inbox). |
| **Inkorg → Backlog** | Sort at your own pace: move to backlog, or plan straight into this week. Nothing needs a project, area or goal. |
| **Livsområden** | Optional areas (Hem, Hälsa, Cykling, …). Offered as one-click suggestions, never created for you. |
| **Projekt** | Projects with tasks, status (active/paused/done/archived) and progress. |
| **Uppgifter** | Subtasks (one level), priority, estimate, deadline, optional scheduled day/time, notes. |
| **Sprint** (`/planning`) | A weekly sprint per ISO week: goal, capacity with a friendly warning (never a block), Kanban (drag and drop, or the card menu on mobile), reorder, move to next week, remove. |
| **Scope history** | Every change to a sprint's scope is logged. The sprint shows *planned from start*, *added during the week*, *removed*, *carried in/out*. The start is automatic (Monday 00:00 local time) with an optional **Starta sprint** button. |
| **Close sprint** | Per unfinished task: carry to next sprint or back to the backlog. Finished work stays in the closed sprint. |
| **Idag** (`/today`) | Daily focus without time blocks, plus due/overdue items and suggestions from the sprint. |
| **Översikt** | Today's focus, current sprint, deadlines and inbox count. |

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

Health: `GET /health` (anonymous, returns `Healthy`/`Unhealthy`, no details).

## Docker / Compose (standalone, e.g. NAS)

```bash
docker compose up -d --build            # http://localhost:8080 (bound to 127.0.0.1 by default)
docker compose ps                       # healthy after ~20 s
```

Data lives in the `lifeos-data` volume (`/data`). Run **one** replica only. For LAN access change the port mapping in
`compose.yaml` and put a TLS reverse proxy in front (needed later for Web Push notifications).

### Backup and restore

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
