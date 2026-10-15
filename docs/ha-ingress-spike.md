# Spike: Blazor Interactive Server behind Home Assistant Ingress

Goal: prove (or disprove) that LifeOS works as an HA app behind Ingress before building features on it.

## What is proven locally (simulated ingress)

`tools/IngressProxy` is a YARP reverse proxy that behaves like HA Ingress: it serves the app under
`/api/hassio_ingress/<token>/`, strips that prefix, sets `X-Ingress-Path` and the standard `X-Forwarded-*`
headers, and proxies WebSockets. Verified in a real browser through it:

| Check | Result |
|---|---|
| Static assets (CSS/JS, MudBlazor, `_framework/blazor.web.js`) load under the prefix | OK (`<base href>` is built from PathBase) |
| Blazor circuit: `_blazor/negotiate` + **WebSockets transport** (confirmed in server connection log) | OK |
| First-run setup → redirect → overview, all URLs stay under the prefix | OK |
| Interactive navigation without full reload; active nav state | OK |
| Auth redirect `Location` uses the forwarded host + prefix, not the container's internal host | OK, **after fix** (see below) |
| Auth cookie survives an app restart (Data Protection keys persisted in `/data/keys`) | OK |
| `X-Ingress-Path` / `X-Forwarded-*` ignored from untrusted peers or malformed values | OK (unit tests) |

Bug found by the spike and fixed: cookie-auth redirects are absolute URLs; without forwarded-host handling the
browser was sent to `127.0.0.1:5169` (the container address). The app now applies `X-Forwarded-Host/Proto/For`
and `X-Ingress-Path` only from `LifeOS:Ingress:TrustedProxyAddresses` (default `172.30.32.2`, the Supervisor
ingress gateway), and only when `LifeOS:Ingress:Enabled=true`.

Reproduce:

```powershell
# terminal 1: app with ingress support, trusting the local proxy
$env:ASPNETCORE_URLS='http://127.0.0.1:5169'; $env:LifeOS__Ingress__Enabled='true'
$env:LifeOS__Ingress__TrustedProxyAddresses__0='127.0.0.1'
dotnet run --project src/LifeOS.Web
# terminal 2: simulated ingress
dotnet run --project tools/IngressProxy
# browse http://127.0.0.1:5180/api/hassio_ingress/TEST_INGRESS_TOKEN/
```

## NOT proven yet: real Home Assistant OS

The simulator is not HA. These must be verified on a real instance (procedure below) because they depend on
Supervisor behaviour I could not observe from here:

1. Does the Supervisor send `X-Forwarded-Host`/`X-Forwarded-Proto`? (If not, absolute redirects need another fix.)
2. Is the app reached from `172.30.32.2`? (Check `remoteIp` on the diagnostics page.)
3. WebSocket stability over the real proxy chain (HA frontend iframe → HA core → Supervisor → app), including
   after phone sleep / network change, and in the HA Companion app.
4. Cookie behaviour inside the HA iframe (SameSite=Lax, path = ingress prefix).
5. Build of the app image on the HA host (arm/amd64 availability of the .NET 10 images).

### Install option A: as a repository app (prebuilt image, no local build)

Requires the repo and the two GHCR packages (`lifeos-amd64`, `lifeos-aarch64`) to be public and a release tag published
(`git tag v0.1.0 && git push --tags`; the tag must equal `version` in `lifeos/config.yaml`).

1. HA: Settings → Add-ons → Add-on store → ⋮ → **Repositories** → add `https://github.com/isabellaalstrom/LifeOS`.
2. Reload the store, open **LifeOS** under "LifeOS apps" → Install → Start. Then continue at step 4 below.

### Install option B: local build

1. On the dev machine: `./ha/build-local-app.ps1` (stages `artifacts/ha-app/lifeos`).
2. Copy the `lifeos` folder to HA's `addons` share, i.e. `/addons/lifeos` (Samba add-on or SSH).
3. HA: Settings → Add-ons → Add-on store → ⋮ → Check for updates → **Local add-ons** → LifeOS → Install → Start.
   (The Supervisor builds the image; this can take several minutes the first time.)
4. Open **LifeOS** in the sidebar. Create the owner account on first run, then sign in.
5. Open `diagnostics/request` through ingress (append to the app URL path in the iframe, or use the browser dev tools
   to fetch `diagnostics/request`) and record: `scheme`, `host`, `pathBase`, `remoteIp`, `receivedHeaders`.
6. Navigate between sections; confirm no full page reloads or console errors (browser dev tools → Network → WS shows a
   101 `_blazor` connection).
7. Stability: leave a page open 15 min; toggle airplane mode on a phone and return; restart the add-on and confirm the
   page reconnects or reloads cleanly and you stay signed in.
8. Restart the whole add-on and verify data persists (create the owner once only; `/data` must survive).
9. Report results. If WebSockets fail: fallback order is (a) SignalR long polling via
   `AddInteractiveServerRenderMode(o => ...)`/hub options, (b) serve the app on its own port with the cookie login
   (`ports:` mapping, plus HTTPS reverse proxy) and keep HA only as a sidebar link (`webui`).

## Authentication model

Ingress is **not** treated as identity. The standalone cookie login (single owner) is used in all modes, including
HA. The ingress headers only affect URL generation. HA's user headers (`X-Remote-User-*`) are deliberately unused;
mapping them to the owner is a possible later step once the trust chain is verified on a real instance.
