# Security review (Phase 6)

Scope: a single-owner app for a home network or Home Assistant, possibly reachable over HTTPS through Nabu Casa or a
reverse proxy. Reviewed against the brief's requirements (cookie login, no secrets in the repository, AI never mutating
data, graceful behaviour when services are missing).

## Authentication and sessions

| Topic | State |
|---|---|
| Login | ASP.NET Identity, cookie `.LifeOS.Auth` (HttpOnly, SameSite=Lax, Secure when the request is HTTPS), 30 day sliding expiry. Password minimum 10 characters. |
| Brute force | `lockoutOnFailure: true` on every sign-in attempt (Identity default lockout). |
| First-run setup | `/setup` creates the owner and closes permanently once an owner exists (covered by tests). A fresh instance must not be exposed before the account exists (documented in the README). |
| Fallback policy | Every page and endpoint requires authentication unless explicitly anonymous (`/login`, `/setup`, `/health`, static assets). Covered by tests for each page. |
| CSRF | Antiforgery is enabled for forms and the logout endpoint. |
| Long-lived circuits | A Blazor circuit keeps the identity it started with. `RevalidatingAuthenticationStateProvider` re-checks the user and security stamp every 30 minutes and closes the circuit if the user is gone or the stamp changed. |
| Multi-user | Not a goal. All data carries `OwnerId`, enforced by a global query filter, stamping and owner immutability in the data layer, with isolation tests per feature (including AI, notifications and push subscriptions). Background work runs per owner through `OwnerOverride`. |

## Secrets

* No secret is stored in the repository. The OpenAI key, ntfy token and push subject are entered as Home Assistant app options (`/data/options.json`, `password` typed in the schema) or environment variables.
* The OpenAI key is only read server-side, sent in an `Authorization` header and never logged (only HTTP status codes are logged; no request or response bodies).
* VAPID keys are generated on first use in `/data/vapid.json` (owner-only permissions where supported) and included in backups. The private key never leaves the server.
* Data Protection keys live in `/data/keys` so cookies survive restarts.

## AI and privacy

* Off by default per owner; unavailable without a server key.
* Minimal context per feature, capped in size; never notes, emails, account data or passwords (tests assert this).
* The model's output is treated as untrusted data: JSON is parsed defensively, sprint picks must reference candidate numbers we sent, text lengths are truncated, and nothing is applied without the owner accepting a proposal. Accepting runs the normal service validation, so a manipulated suggestion cannot bypass any rule.
* Prompt injection surface: only the owner's own text and task titles reach the model (no emails, web pages or third-party content), and the model has no tools, so a hostile string can at worst produce a bad suggestion that the owner can reject.
* Daily call limit, insight cache and rate-limit handling bound cost and abuse.

## Network and transport

* Home Assistant ingress: `X-Ingress-Path` and `X-Forwarded-*` are honoured only from the Supervisor gateway (`172.30.32.2`) and only when ingress support is enabled; the path base is validated.
* No port is published by the HA app; Compose binds to `127.0.0.1` by default.
* Response headers: `X-Content-Type-Options: nosniff`, `Referrer-Policy: same-origin`, a restrictive `Permissions-Policy`. Framing is limited to the same origin by ASP.NET/Blazor (`frame-ancestors 'self'`), which matches HA ingress serving the app from HA's own origin.
* `/diagnostics/request` is authenticated and exposes no cookies or secrets.
* Web Push requires HTTPS (or localhost); the settings page explains this. The service worker caches nothing and shows only the generic push text.

## Home Assistant reminders (optional)

* Off unless `reminders_entity` is set. The Supervisor token comes from the environment, is sent only to the Supervisor proxy and is never logged; failures log status codes only, never list contents.
* Task titles and dates are shared only with your own Home Assistant (and from there with your iCloud account through the integration you set up), only for the one configured list and only after you enable import or sending.
* LifeOS calls only the to-do services for the one configured entity and, if configured, one `notify.*` service for notifications (generic title and text only). It does not read states, call other services or create automations.

## Notifications

* Nothing is sent until a category is enabled. Texts are generic (counts and invitations) and never contain task titles, goal names or retro content, so a lock screen or push service cannot read private data.
* Push subscription endpoints must be HTTPS URLs; gone subscriptions are deactivated.

## Dependencies

* `dotnet list package --vulnerable --include-transitive`: no vulnerable packages (checked 2026-10-16).
* Package versions are pinned where mismatches caused conflicts (EF Core and Options at 10.0.12).
* CI builds and tests on every push; the image workflow builds from the same sources.

## Known limitations and accepted risks

1. **HTTP inside the home network.** Plain HTTP is supported for LAN and ingress use; traffic on an untrusted network can be read. Use HTTPS (Nabu Casa or a TLS proxy) when leaving the home network.
2. **Single owner password.** There is no password reset or change UI yet; recovery is by restoring/removing the data volume. Security-stamp revalidation is in place for when it is added.
3. **No content security policy for scripts.** MudBlazor and Blazor rely on inline styles and framework scripts; a strict CSP would need nonces and is left for later.
4. **Backups are not encrypted.** They sit in `/data` and in Home Assistant backups, which can be encrypted by HA.
5. **ntfy topics are bearer-by-name.** Use a hard-to-guess topic or a token if you enable the optional ntfy channel.
