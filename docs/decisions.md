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
