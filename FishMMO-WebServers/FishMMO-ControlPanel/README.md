# FishMMO Control Panel

> **Status (2026-09-11): everything the sidebar offers is real.** Sign-in, registration,
> two-factor enrolment, self-service, the operator character surface, account search and
> moderation, the audit log, chat log search, the support ticket system, the server board
> with lock and shutdown control, and the dashboard all run against PostgreSQL and are
> verified end to end. **There is no fake login and no way to opt into one**, and no operator
> surface serves fabricated data in any build.
>
> The daemon control plane, maintenance sequencing, the platform views and the guilds and
> social views are **not built** — and neither is start, stop or restart, which cannot be a
> database row because a stopped process polls nothing. They are marked `built: false` in
> `wwwroot/js/routes.js`, kept out of the sidebar, and say so plainly if reached directly
> — they do not error and they do not invent numbers. A control panel showing a reassuring
> green where there is simply nothing is its worst failure mode.
>
> **Every Game Master and Admin action is recorded** in `admin_audit_log`, in the panel and
> in the game alike. On the HTTP side an action filter records every privileged write
> whether or not its author added anything, and the panel refuses to start outside
> production if a privileged endpoint does not declare what it records.

The Game Control Panel and Admin Dashboard for a FishMMO shard: account self-service,
character management, live server monitoring and per-server lifecycle control, gated on
`AccessLevel`. It is the out-of-game counterpart to the authentication the LoginServer
performs and to the in-game `/admin` chat commands.

Renamed and relocated from `FishMMO-CMS` at the repository root. The full design, the API
inventory, the schema additions and the phase plan live in
[CONTROL_PANEL_DESIGN.md](../../CONTROL_PANEL_DESIGN.md).

## Table of Contents

- [Not a news CMS](#not-a-news-cms)
- [Run the design mock](#run-the-design-mock)
- [Project structure](#project-structure)
- [The mock and how to remove it](#the-mock-and-how-to-remove-it)
- [What the mock demonstrates](#what-the-mock-demonstrates)
- [SRP](#srp)
- [Design system](#design-system)
- [Configuration](#configuration)
- [Deployment](#deployment)
- [API endpoints](#api-endpoints)
- [Implementation status](#implementation-status)
- [License](#license)

## Not a news CMS

The launcher's news panel is fetched from `Constants.Configuration.LauncherHtmlUrl`, baked
at build time from `GeneratedHostConfig.LauncherHtmlUrl` (CI substitutes it from
`FISHMMO_ROOT_DOMAIN`). Nothing in this project serves it.

## Run the design mock

```fish
cd FishMMO-WebServers/FishMMO-ControlPanel
dotnet build FishMMO-ControlPanel.slnx -c Release
dotnet run --project ControlPanel
# then browse http://localhost:8100/
```

Kestrel binds loopback on the port from `WebServer:HttpPort` (8100 by default). There is no
HTTPS redirect: TLS terminates at NGINX in a real deployment, and redirecting would break
opening the mock locally.

`Properties/launchSettings.json` sets `ASPNETCORE_ENVIRONMENT=Development` for `dotnet run`.
Without it the host starts in Production, loads `appsettings.Production.json` — whose
`AllowedHosts` is the deployed panel hostname — and answers every request from localhost
with **400**. Running the built assembly directly needs the variable set by hand:

```fish
set -x ASPNETCORE_ENVIRONMENT Development
dotnet ControlPanel/bin/Release/net8.0/FishMMO-ControlPanel.dll
```

`FISHMMO_ENVIRONMENT` also works and is what the systemd unit sets, matching the other web
hosts.

The mock is plain static files with no build step, so .NET is not actually required to look
at it:

```bash
cd FishMMO-WebServers/FishMMO-ControlPanel/ControlPanel/wwwroot
python3 -m http.server 8100
```

### Signing in

There are no demo accounts. Create one through **Create an account** on the sign-in card,
or use an account that already exists in the shard's database — the panel and the game
client authenticate the same accounts.

Registration mirrors the in-game panel field for field: account name, email, password and
age, validated by the same rules, and it produces the same result — an unverified account
with a verification code emailed, a TOTP secret enrolled, and recovery codes shown once.

**Your password never leaves the browser.** It is turned into an SRP salt and verifier
locally, and only those are sent, exactly as the game client does. Even field validation
is performed locally against rules the server publishes, so that no endpoint ever has a
reason to receive a password.

### Fabricated data for design work

The dashboards that have no server-side read model yet can be filled with fixtures:

```
http://localhost:8100/?mock=1
```

That flag affects **data only**. Authentication, registration and verification always go
to the real backend, whatever it is set to; there is no build of this panel that will
accept an invented credential.

## Project structure

```
FishMMO-ControlPanel/
├── FishMMO-ControlPanel.slnx
├── README.md
└── ControlPanel/
    ├── ControlPanel.csproj      # net8.0 web SDK; assembly FishMMO-ControlPanel;
    │                            # copies appsettings from FishMMO-Setup
    ├── Program.cs               # Host builder, Kestrel, static files, SPA fallback
    ├── Controllers/
    │   ├── AccountController.cs # api/Account — player self-service (stubs)
    │   └── AdminController.cs   # api/Admin   — operator actions (stubs)
    └── wwwroot/                 # The single-page app
        ├── index.html
        ├── css/                 # tokens, base, layout, components
        ├── srp-selftest.html    # verifies js/srp.js against the server's vectors
        ├── js/
        │   ├── api.js           # THE SEAM — mock or real HTTP
        │   ├── app.js           # shell, router, session, step-up
        │   ├── routes.js        # navigation map + access levels
        │   ├── srp.js           # SRP-6a client, byte-compatible with the LoginServer
        │   ├── srp-vectors.json # known answers generated from the .NET library
        │   ├── ui.js            # escaping, formatting, components, modals
        │   └── views/           # one module per page, lazily imported
        └── mock/                # DELETE ME — fixtures, mock API, mock SRP server
```

No bundler, no framework, no npm. Every file is served as authored, which keeps the design
reviewable and the diff readable. If a framework is wanted later, `js/api.js` is the only
file it has to keep.

## The mock and how to remove it

The mock is severable in two steps by construction:

1. **Delete `ControlPanel/wwwroot/mock/`.**
2. In `ControlPanel/wwwroot/js/api.js`, delete the `USE_MOCK` constant and the selection
   block at the bottom, replacing them with `export const api = httpApi;`.

That is the whole cutover. It works because of three rules the mock follows:

- **No view imports anything from `mock/`.** Views import `api.js` and `ui.js`, nothing else.
- **Authentication is never in the mock at all.** `api.js` keeps an `ALWAYS_REAL` list that
  is copied over the mock last, so nothing in `mock/` can shadow a sign-in method.
- **No view builds a URL or calls `fetch`.** Every call goes through the `api` object.
- **`mock/` imports nothing from outside itself.** It has no dependency on the app.

`js/api.js` already contains the real HTTP backend — `httpApi` — with one method per route
from the design's route table, each a single line. Turning the mock off swaps which object
`api` points at; the views cannot tell the difference.

The fixture shapes deliberately mirror the DTOs the design names (`AccountSummaryData`,
`CharacterSummaryData`, `WorldServerData`, `SceneServerData`, `daemon_apps`,
`admin_audit_log`), so views should not need edits when real responses replace them.

Fixtures are generated from a fixed seed, so the mock looks the same on every reload and
screenshots stay comparable between design iterations.

## What the mock demonstrates

The mock is not wallpaper. It encodes the design decisions that are easy to get wrong, so
they can be argued about before they are implemented:

| Behaviour | Where to see it |
|---|---|
| **A live character cannot be edited.** Its state lives in the owning scene server's memory and would overwrite the row on the next save, so the editor refuses and offers "kick and wait" instead | Administration → Character editor, pick a character shown *in world* |
| **A maintenance window is one operation, not eight buttons.** Lock, schedule, drain, pin the process, confirm the exit, hold the window, release, unlock — because scheduling a shutdown alone ends with the supervisor restarting the server you just drained | Servers → Maintenance |
| **A pinned process stays stopped.** Operator intent is distinct from failure, and the supervisor has no such notion today | Daemon → Hosts and processes, the ⋯ menu |
| **Cancelling a shutdown leaves the server locked.** Reopening is a separate decision | Servers → Board, on a server that is shutting down |
| **Currency has no direct-write path.** Every adjustment writes a ledger entry with a reason | Administration → Character editor → Currency |
| **Destructive actions need the target's name typed and a reason.** The reason lands in the audit log | Any red button |
| **Step-up.** Destructive actions demand a fresh authenticator code, and the action is retried after it rather than lost | Any red button, five minutes after signing in |
| **Denied attempts are audited too** | Administration → Audit log, filter by Denied |
| **Secrets are write-only.** Name, fingerprint and rotation date; never a value | Platform → Secrets |
| **Promotion to Admin is refused.** It is done by the installer or `psql` | Support → Accounts → an account → Change access level |
| **Nobody may act on their own account with an elevated policy** | Sign in as `operator` and open your own account |
| **The password never leaves the browser.** The sign-in runs a real SRP exchange; only a public ephemeral and a proof are sent | Sign in with a wrong password — it fails on the proof, not on a comparison |
| **The challenge endpoint is not a username oracle.** An account that does not exist gets a plausible salt and ephemeral, and fails identically | Sign in as a name that does not exist |

## SRP

`js/srp.js` is the browser half of the login. It runs SRP-6a over the RFC 5054 2048-bit
group with SHA-512, so the password never leaves the browser: the server stores only a
salt and a verifier and never sees a password-equivalent secret, exactly as the LoginServer
already works.

It is **byte-compatible with the server**, and that was measured rather than assumed. Every
intermediate value was compared against the `srp` 1.0.7 .NET library the LoginServer
authenticates with — private key, verifier, both ephemerals, the session key and both
proofs — across fixed vectors and 150 randomised full exchanges, 11 of which carried a
leading-zero value.

The compatibility hinges on one asymmetry that no reimplementation would guess: the
multiplier `k` hashes the generator **padded** to the group size, while the `H(g)` term
inside the client proof hashes it **unpadded**, as a single `0x02` byte. Get that wrong and
the private key, verifier, ephemeral and session key all still match the server exactly —
only the proof is rejected, on every single login.

Open **`/srp-selftest.html`** to check the implementation against
`js/srp-vectors.json`, which holds known answers generated from the .NET library. It runs
25 checks, needs no build step and no dependencies, and must be served over
`http://localhost` or HTTPS — `crypto.subtle` does not exist on `file://`.

Four of those checks cover `mock/srp-server.js`, the server half the mock ships so the
sign-in screen can run a real exchange with no backend. Without them the mock could be
validating the client against the wrong thing. They skip themselves once `mock/` is
deleted.

Regenerate the vectors only if the server's SRP configuration changes. A diff in that file
means the wire protocol moved and every stored verifier is invalid.

The full conventions are tabulated in
[CONTROL_PANEL_DESIGN.md §5.5](../../CONTROL_PANEL_DESIGN.md).

## Design system

Every colour, radius, spacing step and font size resolves through a token in
`css/tokens.css`. Nothing else declares a literal colour, so retheming is one file.

- **Light and dark.** The light palette is the base declaration; dark redefines only the
  tokens whose role changes, under both `prefers-color-scheme` and an explicit
  `data-theme`, so the in-app toggle wins in both directions. The choice persists in
  `localStorage` and every read and write is wrapped, so a private window still works.
- **Responsive to 400px.** The sidebar becomes a drawer below 900px. Tables scroll inside
  their own container; the page body never scrolls horizontally.
- **Icons** are inline SVG paths using `currentColor` — no icon font, no network request.
- **Charts** are hand-rolled inline SVG sparklines, likewise `currentColor`.
- **Focus is never removed**, only restyled. `prefers-reduced-motion` is honoured.
- **Every interpolation into markup is escaped** through `ui.esc`. Chat messages, account
  names, character names and audit reasons are all player-supplied.

## Configuration

`Program.cs` layers configuration in this order:

1. **Bundled defaults** — the build copies `FishMMO-Setup/Development/appsettings.ControlPanel.json`
   to the output directory as `appsettings.json`, and the Production variant as
   `appsettings.Production.json`.
2. **Working-directory overrides** — `./appsettings.json` and
   `./appsettings.{Environment}.json`, both optional, both reloading on change.

Edit the templates under `FishMMO-Setup/`, not the copies in `bin/` — the build overwrites
those.

| Key | Purpose |
|---|---|
| `WebServer:HttpPort` | Kestrel loopback port. Default 8100 |
| `AllowedHosts` | Host filter. `localhost` in Development, the panel hostname in Production |
| `Cors:AllowedOrigins` | Empty by design — the app is served same-origin |
| `Npgsql:*` | Database settings, per the shared database template |
| `ConnectionStrings:NpgsqlConnection` | Read only by the Production SSL-mode guard; supply via `ConnectionStrings__NpgsqlConnection` |

Database credentials are resolved at runtime from `FISHMMO_DB_USERNAME` /
`FISHMMO_DB_PASSWORD` or `/etc/fishmmo/db-secrets.env`. They never live in a committed file.

The installer configures this component under **Configuration → Web Server Settings →
Control Panel**.

## Deployment

`FishMMO-Setup/nginx.conf` carries a `panel.fishmmo.com` vhost proxying to
`127.0.0.1:8100`, with a tighter body cap than the patch host, a strict Content-Security-Policy,
a dedicated rate-limit zone, a much tighter one on `api/auth`, and a commented-out IP
allowlist for the privileged API surface.

`SystemdServiceInstaller` registers it as `fishmmo-controlpanel`.

**The `X-FishMMO-Client` gate is deliberately not applied here.** That header is produced by
`ClientApiSigner` using a secret compiled into the shipped game client; a browser cannot
produce it, and shipping the secret to a web page would be worse than not gating at all.
Authority comes from the panel's own session, its two-factor requirement and its audit log.
This is an intentional divergence from the other three web hosts.

## API endpoints

### `api/auth` — sign-in

| Method | Route | Purpose |
|---|---|---|
| POST | `srp/challenge` | Username in; salt, server ephemeral and an exchange handle out. **Never takes a password** |
| POST | `srp/proof` | Client ephemeral and proof in; server proof and a session out |
| POST | `2fa/verify` | A TOTP code or a recovery code; promotes a pending session |
| POST | `step-up` | Re-prove the authenticator for a destructive action |
| GET | `session` | The current identity, or nothing |
| POST | `logout` | Revoke this session |

### `api/account` — player self-service

| Method | Route | Purpose |
|---|---|---|
| POST | `register` | Create an account from a browser-computed salt and verifier |
| POST | `verify` | Confirm with the code emailed at registration |
| GET | `policy` | The account rules, so the browser validates locally. Takes no input |
| GET | (root) | The signed-in account's profile |
| GET | `sessions` | The signed-in account's live panel sessions |
| DELETE | `sessions/{id}` | Revoke one of them |

### `api/Admin` — operator actions

| Method | Route | Purpose |
|---|---|---|
| GET | `accounts/search?query=` | Find accounts by username or email |
| POST | `accounts/{username}/ban` | Set access level to `Banned`, revoke tokens, disconnect |
| POST | `accounts/{username}/unban` | Restore access level to `Player` |
| POST | `accounts/{username}/access-level` | Set an arbitrary `AccessLevel` |
| POST | `accounts/{username}/revoke-tokens` | Force re-authentication everywhere |
| POST | `accounts/{username}/reset-2fa` | Clear the TOTP secret and recovery codes |
| POST | `accounts/{username}/force-password-reset` | Admin-set password, revoking tokens |

Every handler body is a `TODO` stub returning a canned response. Input validation is real
and delegated to `FishMMO.Shared.Authentication` so the rules cannot drift from the ones
the LoginServer enforces.

The full route table the finished panel needs — roughly 90 routes across `api/auth`,
`api/account`, `api/characters`, `api/support`, `api/admin`, `api/servers`, `api/daemon`
and `api/platform` — is in the design document, and `js/api.js` already names every one.

## Implementation status

| Area | State |
|---|---|
| Design mock, all 19 views | **Done** |
| Design tokens, light and dark, responsive to 400px | **Done** |
| Route surface and request DTOs | Done (from the scaffold) |
| Input validation | Done — via `FishMMO.Shared.Authentication` |
| Static hosting and SPA fallback | **Done** |
| Swagger / OpenAPI | Done (Development only) |
| Configuration layering | Done |
| NGINX vhost, systemd unit, installer entry | **Done** |
| Database service registration | **Done** |
| Browser-side SRP | **Done** — proven byte-compatible, pinned by `srp-selftest.html` |
| SRP login (challenge + proof) | **Done** — verified against PostgreSQL |
| Registration matching the in-game flow | **Done** — verified against PostgreSQL |
| Email verification | **Done** — code, expiry and queued message |
| Two-factor and recovery codes | **Done** — shares the game's TOTP master key |
| Panel session store (`web_sessions`) | **Done** |
| Authorization policies and a fallback deny | **Done** |
| Support, server, daemon and platform read models | **Not started** — fabricated behind `?mock=1` |
| AppHealthMonitor control plane | **Not started** — it has no network interface today |

Authorization now fails closed. `Program.cs` registers a fallback policy requiring an
authenticated administrator with two-factor satisfied, so a route that carries no policy
attribute is **denied** rather than permitted — the defect the old CMS scaffold shipped
with, where `UseAuthorization` ran with nothing to enforce and every administrative route
was anonymous.

The routes that remain stubs under `api/Admin` are therefore unreachable rather than open.

## License

This project is part of the FishMMO project and is distributed under the FishMMO project
license.
