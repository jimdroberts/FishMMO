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
>
> **Staff reads are recorded too, except automatic refreshes** (2026-09-14). The first load of
> a page and every refresh somebody asked for — Refresh, a filter, a page, the re-read after an
> action — write a `view.<controller>.<action>` row. A page re-reading itself on a timer (the
> server board, maintenance, daemon hosts, platform health and queues, the character editor's
> kick-and-wait) sends `X-Panel-Refresh: auto` through `api.auto.*` in `js/api.js`, and a GET or
> HEAD carrying it is not recorded. The header is trusted for reads only: a write is recorded
> whatever it carries, and a **refused** request is recorded even when marked, a refused poll
> being a probe. The in-game staff console follows the same rule with `AutoRefresh` on its roster
> request. The mark is the client's word: an operator who edits the page's script to mark every
> read would go unrecorded, which is the accepted cost of a client-side mark.

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
- [Account security (issue #252)](#account-security-issue-252)
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

## Account security (issue #252)

The panel side of registration fields, verification channels, the beta gate, the registration
honeypot, sign-in lockout and the delayed self-service two-factor reset. The database layer
(`IAccountService`, `IBetaCodeService`, `ITwoFactorResetRequestService`, `ISmsQueueService`) holds
the state and the concurrency control; the panel holds the policy described here.

### Registration fields and verification

Registration accepts, beyond name, email, password and age: an optional **phone number** (E.164,
with its country code), an optional **Discord username** (`fishfan`, or the older `FishFan#1234`), an
optional **beta code** (required while the gate is on), optional **real name**, **country or region**,
**address** and **referring account**, and a choice of **verification channels** — email, SMS and a
Discord DM, SMS needing a phone and Discord a username. The optional fields are validated by
`AccountProfileRules.TryValidate`, the one rule set the game and the panel share, and its messages come
back as field errors (`{ error, field }`). They are written with `PersistProfileAsync` immediately after
the account row is created and before anything else, following the same best-effort discipline as the
rest of `AccountRegistrationService`: once the row exists, a failed follow-on step is logged and the
account is kept. The referring account is not checked for existence — an anonymous form that said "no
such account" would be an oracle.

**Any one code verifies the account.** A player who chooses several channels is sent a code on each,
and whichever arrives first can be typed. More channels are more ways to receive a code, never more
codes to enter — the earlier rule, where every chosen channel had to be proven, turned a slow SMS
gateway into a locked account for somebody whose email code had arrived long before.

`Verification:Email`, `Verification:Sms` and `Verification:Discord` say which channels the server
switches on, and **`AccountVerificationRules`** (in the database layer, shared with the login server)
decides which codes an account is sent and asked for. The panel never re-implements it:

- A chosen channel that is switched on, and that the account can receive, gets a six-digit code. Email
  and SMS codes expire after 24 hours and go out through `email_queue` / `sms_queue`.
- When **nothing the player chose** is switched on, the account falls back to the first channel that is
  — email, then SMS, then Discord. Every account has an email address, so switching a channel off
  never lets an account in without a code; it moves the code to another channel.
- An account **no enabled channel can reach** (SMS alone switched on, no phone given) is verified
  without a code with `PersistChannelsVerifiedAsync`, because no code could ever arrive.
- **All three off** takes the auto-verify path (`PersistAutoVerifiedAsync`) — the same path the
  development-only `Panel:AutoVerifyAccounts` takes. There is one mechanism, reachable two ways, and
  the panel logs a warning at startup when a server verifies nothing.

The switches must equal the login server's `VerifyEmail` / `VerifySms` / `VerifyDiscord`: both read the
same rule over the same row, and different switches would let an account into one and not the other.

**Discord.** The panel does not talk to Discord. It issues the code with `PersistDiscordVerifyCodeAsync`,
which wakes the Discord bot (`FishMMO-DiscordBot`), and the bot sends it as a direct message. A bot can
only message someone who shares a server with it, so the form shows `Verification:DiscordInviteUrl`
beside the username field and asks the player to join first; the bot waits for them if they have not.
The code is sent **exactly once, ever**: the database refuses to issue a second one, and there is no
resend button, because a new code would make the DM the player already has worthless and the bot never
sends another. An account that chose Discord and has no code yet — one made in-game before the bot was
running — is issued one after its next **correct** password at panel sign-in, never before, so a wrong
password cannot make the bot message a stranger. One Discord account can be linked to only one game
account; a code that would link a Discord user already linked elsewhere does not match.

`POST verify` takes a code from any channel; the database matches it against every code the account
holds in one statement. A correct code answers `{ verified: true, outstanding: [] }` — nothing is ever
still owed after one. The old `verify-phone` twin is gone with the every-channel rule.

**Three wrong codes open a support ticket.** Each wrong code is counted in the database, in the same
column the login server counts into, through `VerificationFailureTicket.RecordAsync`, and the third in a
row opens a ticket filed as the account, which the player can follow from **My tickets**. It is for the
player who never receives a code: with no grace period, staff are their only way in. What recording
did — a count, a ticket, a ticket that could not be opened — is logged and never returned. Every wrong
answer is the same `400` with the same words (wrong code, unknown account, verified account, the
ticket-opening third), because the endpoint is anonymous; the words say what three wrong codes do,
which is true of every account and so tells nobody anything.

`POST verify/resend` sends a new `email` or `sms` code with a two-minute per-account cooldown, never
replaces a code still waiting in its queue, and answers identically whether or not anything was sent.
`discord` is refused with a `400` for every account alike, which is not an oracle for the same reason.

**No grace period.** An unverified account used to be allowed to sign in until its first code had been
delivered (`verification_email_sent_at` still null). That made a broken mail relay indistinguishable
from "this shard does not verify", for as long as the relay stayed broken. Now an account owes a code
from the moment it exists. At sign-in it gets its real salt; a wrong password still fails exactly as
before, and a **correct** one is answered `403` with stage `verification-required` and the `outstanding`
channels, whose message names every place a code went. That answer exists only after a correct proof,
so it tells nothing to anyone without the password.

**SMS delivery.** There is no SMS gateway. `LoggingSmsSender` writes each message to the panel log.
It is configured by default outside Production; in Production only when `Sms:Provider` is `log`
explicitly (it then logs verification codes, and says so at startup). Otherwise `SmsQueueDrainService`
logs once and idles, as the email drain does with no SMTP host. The drain mirrors the email drain —
claim identity, batch size, backoff — and stamps `verification_email_sent_at` after delivering a
*verification* SMS. Sign-in no longer reads that column; it records when a code last reached the player,
which is what staff need to know about an account that cannot verify, and the database has no
SMS-specific one.

### Beta gate

With `Beta:Enabled`, registration requires a code. `CheckRedeemableAsync(code, Beta:Programs)` runs
**before** the account exists — redeeming first would attach a use to a name the insert may then find
taken — and every failure (unknown, revoked, expired, used up, malformed, wrong program) answers with
one message, "That beta code is not valid.". Then the account is created, then `RedeemAsync`. If the
redeem loses a race for the code's last use, the account exists without access and the response says so
and points at **My account → Beta access**, where `POST api/account/beta/redeem` redeems a code later.
Panel sign-in is never gated on beta access, precisely so that path exists. `GET api/account/policy`
publishes `betaRequired` so the form marks the field required.

Administrators mint and revoke codes under **Administration → Beta codes**. A minted batch is shown
once, with copy-all and a plain-text download. The `beta.mint` audit row records the program, count,
uses and expiry — **never the codes**: the audit log is read by more people than that page, and a live
code in it is a code anyone reading the log can redeem.

### Registration honeypot

`GET api/account/register/form` issues a token, `base64url(json).base64url(HMAC-SHA256)` under a
random per-process key, binding the issue time and two or three decoy field names drawn at random from
names autofill and form-filling bots fill (`website`, `company`, `fax`, `middle_name`, `url`,
`homepage`, `nickname`...). The page renders the decoys as ordinary labelled text inputs, positioned
off-screen (not `display:none`), `aria-hidden`, `tabindex=-1`, `autocomplete=off`, and posts them back
under their own names with the token. Registration is refused when:

| Check | Why it exists |
|---|---|
| Token missing, malformed or its MAC does not verify | A script posting straight to `register` never fetched a form. A forged or edited token cannot name different decoys or an older issue time. A panel restart changes the key, so a form opened before it is refused once — the cost is a reload |
| Submitted less than 3 s after issue | Nobody types a name, an address, two matching passwords and picks an age in three seconds; a script does it in milliseconds |
| Token older than 1 hour | A harvested token is not good forever |
| Any decoy has a value | A person never sees them. A bot fills every field it finds, and because the names are random per form it cannot learn one fixed list to leave empty |

Every refusal answers with **exactly** the body a genuine creation failure gets — `400`,
"That account could not be created." — plus a warning in the panel log naming the check. Nothing in
the response says which check tripped: a bot told why it was refused passes next time.

**The in-game client gets no honeypot.** Bots there speak the LoginServer's protocol, not a page;
there is no form for them to fill and nothing a decoy field could catch. The game's defences are its
own rate limits and the same lockout counters described below.

### Sign-in lockout

Failures are counted in the database with `RecordAuthFailureAsync`, in the same columns the
LoginServer counts into, so the panel and the game share one count per account. The thresholds are
`Auth:Lockout:*` and must match the game's: 5 failed passwords in 15 minutes lock password sign-in for
15 minutes; 5 failed authenticator or recovery codes in 15 minutes lock the second step for 30.

- **Password step** (`srp/proof`). A locked account is refused **without evaluating the proof**. The
  refusal is the one generic message every password failure gets — "That username and password do
  not match an account, or sign-in is temporarily locked after repeated failures. Try again later." —
  so a lock can never confirm that a name exists or that a guess was right. The lookup and the count
  run for unknown and banned names too, so the paths cost the same. A good proof clears the count.
  The failure that starts a lock queues one `SecurityNotice` email to the account.
- **Two-factor step** (`2fa/verify`, `step-up`, `2fa/reset/confirm`). The session has already proven
  the password, so a lock is stated plainly: `423` with `lockedUntilUtc`. The lock that starts also
  emails the holder, warning that somebody knows the password.
- **Staff** see both locks on the account page and can clear them with `clear-lockout`
  (step-up, reason required, audited `account.clear-lockout`).

### Self-service two-factor reset

For an account holder who has lost the authenticator **and** every recovery code. The owner chose
self-service with a waiting period, which staff may shorten or cancel.

1. At the two-factor step of sign-in — the password is proven — **Lost your authenticator AND your
   recovery codes?** leads to `POST api/auth/2fa/reset/request`. It opens a request effective in
   `TwoFactorReset:DelayDays` (7) and emails the account: when it takes effect, and that signing in with
   the authenticator or a recovery code before then cancels it. Asking again returns the same request;
   the clock never restarts and never shortens.
2. **Any** successful two-factor verification — `2fa/verify` with a TOTP or recovery code, and
   `step-up` — cancels a pending request and emails that it was cancelled. Passing the real factor
   proves the owner still has it, which is the case the waiting period exists to catch.
3. Once effective, the password-proven session calls `2fa/reset/complete`. `CompleteAsync` spends the
   request in one conditional update. The account page's own enrolment code
   (`SelfServiceService.BeginTwoFactorSetupAsync`) then overwrites the secret and replaces the recovery
   codes **while `totp_enabled` stays on**. Every other panel session and every game token is revoked,
   a `SecurityNotice` is sent, and the new otpauth URI and codes go to the same handover screen
   registration uses (QR, manual key, "I have saved them").
4. Nothing is signed in until `2fa/reset/confirm` receives a code from the **new** authenticator;
   recovery codes are refused there.

The rules this keeps:

- **A reset never immediately satisfies two-factor.** Completion issues a fresh session that has
  proven only the password.
- **The account is never left with two-factor cleared.** Clearing first and enrolling second would
  leave a window — or, after a failed write, an account — guarded by the password alone. The game
  demands the new authenticator from the moment the reset completes. The KEK is checked before the
  request is spent, so a server that cannot encrypt a new secret does not use up the player's week.
- **A password reset never satisfies two-factor, and a recovery code never resets a password.**
  Nothing here touches the SRP credentials, and `PasswordResetService` touches nothing here.

Staff see a pending reset on the account page, in the account history's **Standing**, and in a list of
every pending reset on **Support → Accounts**. They can **Shorten** it to now or a chosen earlier time,
or **Cancel** it. Both are step-up with a reason, audited (`account.2fa-reset-shorten`,
`account.2fa-reset-cancel`), refused against one's own or a peer's account, and emailed to the holder.

**Platform → Queues** shows the SMS queue beside the email queue: the same states, counts,
oldest-pending alarm and step-up retry (`IQueueBoardService.FetchSmsQueueAsync` / `RetrySmsAsync`).
The phone number is masked by the server to its last four digits (`+44•••••0123`) before it reaches
the browser, the search matches a number only whole so a masked one cannot be recovered a digit at a
time, and neither the number nor the message body is read into an audit row or the log.

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
| `Verification:Email` / `Verification:Sms` / `Verification:Discord` | Which channels a code can be sent on; any one code verifies. A player whose choices are all off is sent a code on one that is on; all three off verifies every account on creation. Must match the login server's `VerifyEmail` / `VerifySms` / `VerifyDiscord`. Discord needs the Discord bot running. Default `true` / `true` / `true` (Production template: Discord `false`) |
| `Verification:DiscordInviteUrl` | The `https://` invite to the game's Discord server, shown beside the Discord username field — the bot can only message members. Anything not `https://` is ignored with a startup warning. Default empty |
| `Beta:Enabled` / `Beta:Programs` | Registration requires a beta code of one of these program keys (empty list: any program). Default off |
| `Auth:Lockout:PasswordThreshold` / `PasswordWindowMinutes` / `PasswordLockMinutes` | 5 failed passwords in 15 minutes lock password sign-in for 15. Shared with the LoginServer; keep equal |
| `Auth:Lockout:TwoFactorThreshold` / `TwoFactorWindowMinutes` / `TwoFactorLockMinutes` | 5 failed codes in 15 minutes lock the second step for 30. Shared with the LoginServer; keep equal |
| `TwoFactorReset:DelayDays` | Waiting period before a self-service two-factor reset may be completed. Default 7 (clamped 1–90) |
| `Sms:Provider` | `log` writes messages to the panel log. Empty: `log` outside Production, idle in Production. Nothing else exists yet |
| `Sms:DrainIntervalSeconds` / `Sms:MaxPerPass` | The SMS drain's pace, as `Email:*` is the email drain's. Defaults 2 / 5 |

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
| POST | `2fa/verify` | A TOTP code or a recovery code; promotes a pending session. Counted and locked (`423`); cancels a pending two-factor reset |
| POST | `step-up` | Re-prove the authenticator for a destructive action. Counted and locked like `2fa/verify` |
| GET | `2fa/reset` | TwoFactorPending. The pending self-service reset: requested and effective times, `delayDays` |
| POST | `2fa/reset/request` | TwoFactorPending. Opens a delayed reset, or returns the pending one unchanged; emails the account |
| POST | `2fa/reset/complete` | TwoFactorPending. Uses an effective reset: new secret and recovery codes for the handover, other sessions and game tokens revoked, a fresh password-only session. Does NOT sign in |
| POST | `2fa/reset/confirm` | TwoFactorPending. A code from the NEW authenticator (no recovery codes); promotes the session |
| GET | `session` | The current identity, or nothing |
| POST | `logout` | Revoke this session |

`srp/proof` answers every failure — wrong password, unknown or banned account, locked sign-in — with
one `401` and one message. Its only other refusal, `403` with `stage: "verification-required"` and
`outstanding`, is given only after a correct proof. None of these endpoints is audited: they are
anonymous or `TwoFactorPending`, an account authenticating as itself.

### `api/account` — player self-service

| Method | Route | Policy | Purpose |
|---|---|---|---|
| GET | `register/form` | Anonymous, `Register` rate limit | A signed form token and 2–3 randomised decoy field names (the honeypot) |
| POST | `register` | Anonymous, `Register` | Create an account from a browser-computed salt and verifier, plus the optional profile, channels and beta code. Errors carry `field` |
| POST | `verify` | Anonymous, `Register` | A code from any channel; one correct code verifies. Every failure is one message; the third wrong code in a row opens a support ticket |
| POST | `verify/resend` | Anonymous, `Register` | A new code for `email` or `sms`, two-minute cooldown; always the same answer. `discord` is refused: that code is sent once |
| GET | `policy` | Anonymous | The account rules, `betaRequired`, the verification switches with the Discord invite, and profile limits. Takes no input |
| GET | (root) | Self | The signed-in account's profile, including phone and verification state |
| GET | `beta` | Self | The beta codes this account has redeemed |
| POST | `beta/redeem` | Self, `Auth` rate limit | Redeem a beta code later |
| GET | `sessions` | Self | The signed-in account's live panel sessions |
| DELETE | `sessions/{id}` | Self | Revoke one of them |

### `api/support/accounts` — account security (issue #252)

| Method | Route | Policy | Audit action | Purpose |
|---|---|---|---|---|
| GET | `{username}` | Support | `view.supportaccounts.get` | The detail projection: the list fields plus phone, Discord username and where its one DM stands, verification state with the wrong-code count and last delivery, real name, country, address, referral, both lockouts, the pending reset and beta codes. The search projection carries none of these |
| GET | `2fa-resets` | Support | `view.supportaccounts.pendingtwofactorresets` | Every pending self-service reset, soonest first |
| POST | `{username}/clear-lockout` | SupportStepUp | `account.clear-lockout` | Lift both sign-in locks. Reason required |
| POST | `{username}/2fa-reset/shorten` | SupportStepUp | `account.2fa-reset-shorten` | Bring a pending reset forward to `effectiveUtc` (null: now). Earlier only. Reason required; holder emailed |
| POST | `{username}/2fa-reset/cancel` | SupportStepUp | `account.2fa-reset-cancel` | Cancel a pending reset. Reason required; holder emailed |

The writes apply `ModerationGuards.Check`: not one's own account, not a peer or superior.

### `api/admin/beta` — beta codes

| Method | Route | Policy | Audit action | Purpose |
|---|---|---|---|---|
| GET | `programs` | Operator | `view.adminbeta.programs` | Program totals and the registration gate's configuration |
| GET | `codes?program=&includeRevoked=&page=&pageSize=` | Operator | `view.adminbeta.codes` | Codes, newest first |
| POST | `mint` | OperatorStepUp | `beta.mint` | Mint `count` codes of `program` with `maxUses` and optional `expiresUtc` and `note`. Reason required. The codes are in the response only; the audit row holds program, count, uses and expiry |
| POST | `{id}/revoke` | OperatorStepUp | `beta.revoke` | Revoke a code, ending the access of every account that redeemed it. Reason required |

### `api/platform/queues` — outbound messages and the group finder

| Method | Route | Policy | Audit action | Purpose |
|---|---|---|---|---|
| GET | `email?state=&search=&page=&pageSize=` | Operator | `view.platformqueues.emailqueue` | Email queue page, whole-table counts and the oldest pending/unsent ages. Never the body |
| POST | `email/{id}/retry` | OperatorStepUp | `platform.email-retry` | Release the claim on a claimed or failed email. Reason required. Sends nothing; attempts and error kept |
| GET | `sms?state=&search=&page=&pageSize=` | Operator | `view.platformqueues.smsqueue` | SMS queue page, the same shape. `recipientPhone` masked to the last four digits; `search` is an account substring or a whole number. Never the body |
| POST | `sms/{id}/retry` | OperatorStepUp | `platform.sms-retry` | Release the claim on a claimed or failed SMS. Reason required. The audit row names the account, never the number |
| GET | `group-finder?status=&page=&pageSize=` | Operator | `view.platformqueues.groupfinderqueue` | Group finder rows with heartbeat staleness. Read-only |

There is no purge or delete for any queue. Retry only ever puts a message back in line.

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
