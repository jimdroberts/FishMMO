# Client Launcher

**Short description:** The launcher is the first window a standalone player sees. It renders news fetched over HTTP, resolves an API host, checks the installed version against the patch server, downloads and verifies a patch when one is needed, hands the install over to the external Updater, and — when the client is current — loads the game and gets out of the way.

## Table of Contents

- [Overview](#overview)
- [Supported Platforms](#supported-platforms)
- [Features](#features)
- [Prerequisites](#prerequisites)
- [Configuration](#configuration)
- [State Machine](#state-machine)
- [The Update Path](#the-update-path)
- [Handoff to the Game](#handoff-to-the-game)
- [API Host Resolution and Request Signing](#api-host-resolution-and-request-signing)
- [Operational Checks](#operational-checks)
- [Flow Diagram](#flow-diagram)
- [Project Structure](#project-structure)
- [License](#license)

## Overview

`ClientLauncher` is a plain `MonoBehaviour` living in the `ClientLauncher` Addressable scene. It is deliberately **not** a `BootstrapSystem`: it is a terminal stage that hands control onward explicitly rather than being auto-discovered and chained. See the [Bootstrap README](../../Shared/Implementation/Bootstrap/README.md) for where it sits in the boot order.

The launcher only exists on standalone builds. `ClientPreboot`'s postload scene list routes Editor and WebGL straight to `ClientPostboot`, skipping the launcher entirely — neither can run an external updater process, so there is nothing for it to do.

Everything it does over the network goes through an injected service so the flow can be tested and swapped:

| Dependency | Interface | Default implementation |
|---|---|---|
| Web requests | — | `UnityWebRequestService` |
| News HTML | `IHtmlContentFetcher` | `UnityHtmlContentFetcher` |
| Version + patch download | `IPatchServerService` | `HttpPatchServerService` |
| Updater process | `IUpdaterLauncher` | `SystemUpdaterLauncher` |
| Presentation | `ILauncherView` | `UITKClientLauncher` |

Rendering is behind `ILauncherView` so the state machine, version check, and patch flow exist
once regardless of which UI technology draws them. `UITKClientLauncher` — a UI Toolkit
`MonoBehaviour` over a `UIDocument`, living in [`Client/GUI/Launcher/`](../GUI/Launcher) beside
its UXML and USS — is the only implementation, and it must be assigned to `ClientLauncher`'s
`launcherViewComponent` field in the scene. The field is typed as `MonoBehaviour` because Unity
cannot serialize an interface reference; `ResolveView()` does the cast.

There is no fallback. `ClientLauncher` used to build a uGUI adapter out of the widget
references serialized on it whenever no view was assigned; that adapter rendered through
TextMeshPro, and both went with the client's conversion to UI Toolkit. `ResolveView()` now logs
an error and returns null when the assignment is missing or does not implement the interface,
which beats silently constructing a view over serialized fields that no longer exist. The
widget fields themselves (background, title, progress bar, buttons, status label, and the news
text and its link handler) were removed from the component at the same time.

The interface still expresses intent (*show this status*) rather than widget manipulation
(*set this label, activate that group*), and that is worth keeping with one implementation in
the tree: it is what allowed the uGUI view to be deleted without a line of the state machine
changing. A view is free to satisfy each call however its technology prefers — the uGUI one had
no dedicated status element and borrowed the progress label, while UI Toolkit simply writes to
a status label; encoding either quirk in the interface would export one view's limitation to
every other.

`SetVisible(bool)` is on the interface for one specific reason. The launcher calls it the
moment the game scene is ready, *before* the launcher scene is unloaded,
because the unload is asynchronous and in the editor may not happen at all — the launcher scene
is usually opened directly there, so Addressables holds no handle for it and its unload call is
a silent no-op. Hiding is synchronous and works on both paths, so the launcher can never be
left sitting behind the login screen. The UI Toolkit view disables the whole `UIDocument`
rather than just hiding the root, so the panel stops swallowing pointer input as well as
drawing.

## Supported Platforms

| Platform | Launcher runs | Notes |
|---|---|---|
| Windows | Yes | Full news → version check → patch → play flow |
| Linux | Yes | Full flow |
| macOS | Yes | Full flow |
| Editor | **No** | `ClientPreboot` postloads `ClientPostboot` directly |
| WebGL | **No** | No external updater in the browser sandbox; an outdated build reports "refresh the page" instead |

## Features

- **Explicit UI state machine** — one `LauncherState` drives the button label, its interactability, its click action, and progress-bar visibility, all from a single `SetLauncherState` switch. Every failure state is recoverable: the button is re-enabled and wired to a retry action that can actually succeed.
- **Multi-host API failover** — `ApiHostResolver.GetCandidates()` returns an ordered candidate list; the version check walks it until one responds, and the subsequent patch download is pinned to the host that answered so the archive matches the version we were told about.
- **Patch integrity verification** — the server reports the expected SHA-256 and byte size up front; the downloaded archive is hashed and rejected on mismatch.
- **Unreachable-update detection** — the server states whether it holds a patch *from this specific version*, so a client with no upgrade path lands in `PatchUnavailable` (reinstall required) rather than looping forever on a request that can only 404.
- **Transient-state watchdog** — any state with no interactive button is forced back to a recoverable one after `transientStateTimeoutSeconds`. Download progress resets the timer, so a slow patch is never interrupted. This is a catch-all for a coroutine that dies (Unity logs the exception and silently stops it), not a substitute for per-operation error handling.
- **Re-entrancy interlocks** — `isConnecting`, `isLaunching`, and `isUpdating` guard the three entry points. The button state alone is insufficient, because the update flow is also entered directly from the version check.
- **Signed requests** — every call carries an `X-FishMMO-Client` HMAC header via `ClientApiSigner`, which the IPFetch and Patcher servers' `ClientGate` middleware validates.

## Prerequisites

- The `ClientLauncher` scene, with `ClientLauncher`, its service components, and a component implementing `ILauncherView` (`UITKClientLauncher`) wired in the Inspector. A missing dependency is reported at `Awake` and the launcher refuses to run rather than null-referencing mid-flow.
- `Constants.Configuration.APIHost` and `LauncherHtmlUrl`, both baked at build time from `GeneratedHostConfig` (CI substitutes them from `FISHMMO_ROOT_DOMAIN`).
- `ClientApiSecret.generated.cs`, produced by **FishMMO Dashboard > Game Settings**. A build still carrying the sentinel value will be rejected by `ClientGate`.
- The Updater executable present at the install root (`Constants.Configuration.UpdaterExecutable`).

## Configuration

### Inspector

| Property | Type | Default | Purpose |
|---|---|---|---|
| `htmlViewURL` | `string` | `""` | Per-scene override for the news URL. **Leave empty.** |
| `divClass` | `string` | `content` | CSS class of the `div` whose text is extracted from the fetched page |
| `newsFallbackSummary` | `string` (`TextArea`) | built-in blurb | Shown in the news pane when no feed is configured, and when a fetch fails |
| `defaultScreenWidth` / `defaultScreenHeight` | `int` | — | Window size applied on launch |
| `transientStateTimeoutSeconds` | `float` | `120` | Watchdog timeout for states with no interactive button |

> **Why `htmlViewURL` defaults to empty.** A non-empty default gets baked into the scene the first time it is saved, and the serialized copy then silently wins over the build-time configured value for every subsequent build — which is exactly how a stale hard-coded URL once shipped. Resolution happens at read time instead: `HtmlViewURL` falls back to `Constants.Configuration.LauncherHtmlUrl` whenever the override is blank.

> **Why the news pane always has content.** No feed is a valid deployment, not a failure, but
> hiding the pane collapsed the panel into a header stacked directly on a footer, which reads
> as a broken window rather than as a launcher with no news today. The pane is filled with
> `newsFallbackSummary` instead — both when nothing is configured and when the fetch fails, so
> a dead feed never leaves an error string as the only thing to look at. It is serialized and
> multi-line so a shard running its own build can rewrite the copy without a code change.

> **Why an unsubstituted URL counts as "not configured".** `HostConfig.generated.cs` ships the
> news URL containing `FISHMMO_SENTINEL_PLACEHOLDER`, which CI rewrites from
> `FISHMMO_ROOT_DOMAIN` at build time. In a working copy that substitution has not happened, so
> the URL is non-empty but names a host that cannot resolve. `IsNewsUrlConfigured` screens the
> sentinel out and skips the request entirely, rather than reporting a fetch failure for a feed
> nobody configured — the same convention `ClientCertificatePinning` uses to keep sentinel
> values out of the pin set.

### Player settings

Persisted in the shared `Configuration.GlobalSettings` file, the same store the in-game Options
panel uses. The launcher no longer has to load it itself: `ClientSettingsBootstrap` creates the
store at `BeforeSceneLoad`, so it exists before the launcher scene comes up.
`LauncherSettings.EnsureLoaded()` delegates to `ClientSettings.EnsureLoaded()` and remains as the
entry point for scenes that come up without the boot phase.

Reads and writes go through `ClientSettings` rather than touching the store directly.
`LauncherSettings.Save()` used to call `Save()` on the `Configuration` instance, which bypassed
two things every other write in the client honours: the editor guard — so a play-mode launcher
session rewrote the developer's checked-out `Configuration.cfg` — and the WebGL sync, so a
launcher setting reached an in-memory filesystem and was gone on the next page load. It also left
the client's pending-write flag set, serialising the same file again moments later. `SetValue`
likewise marks the write as owed, so a launcher change is no longer stranded in memory until
something else happens to save — which matters most for the two values the updater relies on
across a restart, `Launcher.UpdateAttemptVersion` and `Launcher.UpdateAttemptCount`.

`Save()` is still immediate rather than debounced: the launcher writes at deliberate moments — a
field committed, an update attempt recorded — and the process it is about to hand over to may not
be this one. See [Client Settings](../Settings/README.md).

| Key | Default | Effect |
|---|---|---|
| `Launcher.AutoUpdate` | `true` | Off stops at `UpdateAvailable` with the download size instead of patching straight away |
| `Launcher.RequestTimeout` | inspector value | Per-request timeout, clamped 5–300s |
| `Launcher.MaxRetries` | inspector value | Retries after a failure, clamped 0–10. Patch downloads only — the news fetcher stays at 0 |
| `Launcher.RetryDelay` | inspector value | Seconds between retries, clamped 0–30 |
| `Launcher.PatchDirectory` | empty | Where patch archives are stored. Empty uses the install's own `Patches` folder |
| `Launcher.WindowWidth` / `Height` | unset | Last windowed size, restored on next launch |

The transfer tunables take the component's serialized value as their fallback, so an install
where nothing has been changed behaves exactly as it did before these existed.

The Settings panel's sliders take their `lowValue`/`highValue` from the same
`LauncherSettings` constants the clamps use (`MinRequestTimeout`/`MaxRequestTimeout`,
`MinRetries`/`MaxRetriesLimit`, `MinRetryDelay`/`MaxRetryDelay`) rather than repeating those
numbers as UXML literals, so a slider cannot offer a value the setter will silently clamp away.
Each label carries its range for the same reason — a slider with no numbers on it gives the
player no way to tell what a drag is worth.

`Launcher.PatchDirectory` is **not** a "move the game" setting and cannot be one — the Updater
patches files relative to its own location and ships beside the client binaries, so the install
root is fixed by construction. It exists to keep large, transient archives off a small system
drive. It must be an absolute path; anything unusable falls back to the default, and both the
launcher and the Updater apply that same rule so they fall back together.

Unity exposes no receive-buffer setting for `DownloadHandlerFile`, and on desktop
`UnityWebRequest` defers to the platform HTTP stack — timeout, retry count and retry delay are
the only real throughput controls available, which is why nothing else is offered.

### UI text

All player-facing strings are constants on the nested `UIText` class — status labels (`StatusPatchUnavailable`, `StatusServerRejectedVersion`, …), long-form explanations (`DetailPatchUnavailable`, `DetailApplyingPatch`, …), and log format strings. Change copy there, not at the call sites.

### Panel layout

The panel is authored in `UILauncher.uxml` and styled by `UILauncher.uss` on top of the shared
`FishMMO-Theme.uss`, so element names are the contract between the two: `UITKClientLauncher`
queries them by name and reports which ones it could not find rather than failing element by
element later.

Two layout decisions are enforced in code rather than in the stylesheet:

- **The brand banner's height is derived from its resolved width.** USS has no aspect-ratio
  property, so a fixed height on a full-width strip can only be wrong — `scale-and-crop` slices
  the top and bottom off the artwork, `scale-to-fit` leaves the panel background showing down
  both sides, and the banner is far wider in proportion than the panel it sits in. Driving
  height from width removes the tradeoff at any panel size. The ratio is measured from the
  assigned image, so replacing the artwork needs no code change.
- **Quit sits at the far left of the footer and Play/Connect at the far right**, separated by
  the full panel width. The affirmative action is where the eye and the cursor finish, and the
  destructive one is far enough away that neither is reachable by a mis-click on the other.

## State Machine

| State | Button | Click action | Meaning |
|---|---|---|---|
| `LoadingNews` | disabled | — | Fetching the news page |
| `Connecting` | disabled | — | Contacting the API host |
| `CheckingVersion` | disabled | — | Comparing installed version to the server's |
| `UpdateAvailable` | **Update** | `PlayButtonUpdate` | A patch exists and `Launcher.AutoUpdate` is off — the player starts it |
| `DownloadingPatch` | disabled | — | Progress bar visible; heartbeats the watchdog |
| `ApplyingPatch` | disabled | — | Updater has been handed the install |
| `ReadyToPlay` | **Play** | `PlayButtonLaunch` | Versions match |
| `ClientAhead` | enabled | `PlayButtonConnect` | Client is newer than the server — re-check, do **not** allow Play |
| `PatchUnavailable` | enabled | `PlayButtonConnect` | No upgrade path from this version; re-runs the version check rather than retrying an impossible download |
| `ServerRejectedVersion` | enabled | `PlayButtonConnect` | Reserved — see note below |
| `ConnectionFailed` | enabled | `PlayButtonConnect` | Retry |
| `VersionCheckFailed` | enabled | `PlayButtonConnect` | Retry |
| `PatchDownloadFailed` | enabled | `PlayButtonUpdate` | Retry the download |
| `UpdaterFailed` | enabled | `PlayButtonConnect` | Re-check, then retry the update |
| `LaunchFailed` | enabled | `PlayButtonConnect` | Back to the version check — a failing scene load is often fixed by re-patching |
| `VersionError` | enabled | `PlayButtonConnect` | Retry; version parse failures can be transient after a partial patch |

> `ServerRejectedVersion` is defined and wired into the state machine but not currently reachable. Version rejection is presently handled during the authentication handshake (`ClientAuthenticationResult.VersionMismatch`) and rendered through a separate UI path. The state exists for a future version-check endpoint that rejects a client outright without offering a patch.

## The Update Path

`GetLatestVersion()` walks the API host candidates, then compares versions:

- **client == server** → `ReadyToPlay`.
- **client > server** → `ClientAhead`. Play stays blocked.
- **client < server** → if `PatchInfo.PatchAvailable` is false, `PatchUnavailable`; otherwise `PlayButtonUpdate()` runs immediately.

`VersionConfig.Parse` returns `null` for malformed input rather than throwing, so both versions are null-checked explicitly. A null client version would otherwise compare as older than everything and start downloading a patch for a version the server has never heard of.

`PlayButtonUpdate()` then:

1. Computes the destination as `LauncherSettings.ResolvePatchDirectory(Constants.GetPatchesDirectory())` + `Constants.GetPatchFileName(currentVersion, latestVersion)` — by default `<install root>/Patches/<from>-<to>.zip`. **This path is a contract**, and it is now passed to the Updater as `-patches=<dir>` rather than derived independently on both sides. A disagreement fails silently: the Updater reports "patch file not found", relaunches the client unchanged, and the launcher detects the same mismatch on the next run, forever. See the [Updater README](../../../../../FishMMO-Patcher/Updater/README.md).
2. Downloads from the pinned host, verifying the SHA-256 when the server supplied one.
3. If the server answers **204 No Content** mid-flight — the version we checked against was superseded, or we raced a deployment — no archive is written and the launcher goes straight to `ReadyToPlay` instead of invoking the Updater on a file that does not exist.
4. Otherwise enters `ApplyingPatch` and starts the Updater. The archive is deliberately **not** deleted here: the Updater is about to read it, and removes it itself once applied.
5. On successful handoff, quits promptly so the client binaries are released for patching. `LaunchUpdater` must not wait for the Updater to exit — the Updater kills this process by PID before patching, so waiting is a mutual deadlock.

A download error deletes the partial file best-effort and lands in `PatchDownloadFailed`.

## Handoff to the Game

`PlayButtonLaunch()` enqueues the `ClientPostboot` scene, waits on the returned `AddressableLoadBatch`, then in `OnPostbootSceneLoaded`:

1. Hides the view via `ILauncherView.SetVisible(false)`.
2. Unloads the `ClientLauncher` scene.
3. Finds the `ClientPostbootSystem` among the loaded scene's roots and calls `StartBootstrap()` on it.

The hide happens first, and independently of whether the unload succeeds. Addressables unloads
only scenes it loaded itself and silently does nothing for any other, so in the editor — where
the launcher scene is usually opened directly or through QuickStart — the unload is a no-op and
the launcher UI would otherwise sit on screen behind the login screen for the rest of the
session. Unloading also destroys the component, so nothing after that call may depend on it.

If `ClientPostboot` is already loaded — reachable when a previous launch failed — the launch completes directly instead of enqueueing a second load, which would otherwise dead-end.

## API Host Resolution and Request Signing

`ApiHostResolver` turns the configured `APIHost` into an ordered candidate list and exposes `SanitizeForLog` so hosts can be logged without leaking full URLs. Loopback targets are detected explicitly.

`ClientApiSigner` builds the gate header:

```
X-FishMMO-Client: v1.<timestamp>.<nonce>.<signature>
```

The signing secret lives in `GeneratedClientSecret.Secret` (`ClientApiSecret.generated.cs`). **It is not a credential.** It ships inside the client binary and is recoverable by anyone with the build; its job is to filter crawlers, port scanners, and blatant header forgery. Real authority comes from the SRP-derived session token issued by the auth server.

## Operational Checks

| Check | How to verify | Expected result |
|---|---|---|
| News renders | Launch a standalone build | News panel populated; on failure, a logged warning, `newsFallbackSummary` in the pane, and the flow continues to the version check |
| No feed configured | Launch with an empty or still-sentinel news URL | No request is issued; the pane shows `newsFallbackSummary` rather than an error |
| Launcher gets out of the way | Press **Play** in the editor and in a build | Launcher UI disappears at the handoff on both paths, even where the scene unload no-ops |
| Host failover | Point the first candidate at a dead host | Debug log per failed candidate, then success on the next |
| Up-to-date client | Launch at the server's version | Button reads **Play** |
| Patch applied | Launch an outdated client with a published patch | Download progress → `ApplyingPatch` → launcher exits → client restarts at the new version, and `Patches/` is empty afterwards |
| No upgrade path | Launch an outdated client the server has no patch for | `PatchUnavailable`, button re-runs the version check — never a repeating 404 |
| Corrupt download | Corrupt the served archive | Hash mismatch → `PatchDownloadFailed`, partial file removed |
| Watchdog | Force a transient state and stall it | After `transientStateTimeoutSeconds`, an error is logged and the UI returns to a usable button |
| Slow patch not interrupted | Throttle the download below the timeout | Progress heartbeats keep the watchdog from firing |

## Flow Diagram

```mermaid
flowchart TD
    Start([Launcher scene loads]) --> News[LoadingNews<br/>fetch + extract div text]
    News --> Connect[Connecting]
    Connect --> Check[CheckingVersion<br/>walk API host candidates]

    Check -->|all hosts failed| CF[ConnectionFailed / VersionCheckFailed]
    Check -->|unparseable version| VE[VersionError]
    Check --> Cmp{client vs server}

    Cmp -->|equal| Ready[ReadyToPlay]
    Cmp -->|client newer| Ahead[ClientAhead]
    Cmp -->|client older| Avail{patch_available?}

    Avail -->|no| Unavail[PatchUnavailable<br/>full reinstall required]
    Avail -->|yes| DL[DownloadingPatch<br/>to Patches/from-to.zip]

    DL -->|204 No Content| Ready
    DL -->|hash mismatch / error| DF[PatchDownloadFailed]
    DL -->|verified| Apply[ApplyingPatch]

    Apply --> Updater[Start Updater, then Quit]
    Apply -->|failed to start| UF[UpdaterFailed]
    Updater --> Restart([Updater patches and relaunches client])

    Ready -->|Play| Load[Load ClientPostboot]
    Load -->|failed| LF[LaunchFailed]
    Load --> Hand[Unload launcher scene<br/>ClientPostbootSystem.StartBootstrap]
    Hand --> Game([Game])

    CF --> Connect
    VE --> Connect
    Ahead --> Connect
    Unavail --> Connect
    UF --> Connect
    LF --> Connect
    DF --> DL

    Watchdog[[Transient-state watchdog]] -.->|no button for 120s| Connect
```

## Project Structure

```
Client/Launcher/
├── ClientLauncher.cs            # MonoBehaviour: state machine, orchestration
├── LauncherState.cs             # The 16 UI/process states
├── LauncherSettings.cs          # Typed access to the persisted Launcher.* settings + their clamps
├── PatchInfo.cs                 # Server patch metadata: UpToDate, PatchAvailable, Sha256, Size
├── VersionFetch.cs              # Version response parsing
│
├── ILauncherView.cs             # Contract: the presentation surface the state machine drives
├── DownloadStats.cs             # Download snapshot handed to the view (bytes, rate, remaining)
├── DownloadRateTracker.cs       # Sliding-window transfer rate and time-remaining estimate
├── LauncherLinkPolicy.cs        # Scheme allowlist for opening news links (any view)
│
├── IPatchServerService.cs       # Contract: GetLatestVersion, DownloadPatch
├── HttpPatchServerService.cs    #   → HTTP implementation with SHA-256 verification
├── IHtmlContentFetcher.cs       # Contract: fetch a page, extract a div as a parsed node
├── UnityHtmlContentFetcher.cs   #   → UnityWebRequest implementation
├── IUpdaterLauncher.cs          # Contract: start the external updater and hand off
├── SystemUpdaterLauncher.cs     #   → Process.Start implementation
├── UnityWebRequestService.cs    # Shared UnityWebRequest wrapper
│
├── InstallSizeProbe.cs          # Off-thread install size measurement for the Settings panel
├── NativeFolderPicker.cs        # OS folder dialog for the patch directory (Windows only)
│
├── ApiHostResolver.cs           # Candidate host list, loopback detection, log sanitizing
├── ClientApiSigner.cs           # X-FishMMO-Client HMAC header
└── ClientApiSecret.cs           # Compiled-in gate secret (NOT a credential)
```

`ClientApiSecret.generated.cs` sits alongside these but is generated, not committed.

The view lives outside this folder, with the rest of the client's UI Toolkit assets:

```
Client/GUI/Launcher/
├── UITKClientLauncher.cs        # The ILauncherView implementation, over a UIDocument
├── UITKHtmlContentRenderer.cs   # News node tree → VisualElement tree
├── UILauncher.uxml              # Panel structure; element names are the code's contract
└── UILauncher.uss               # Launcher styles on top of the shared FishMMO-Theme.uss
```

`UITKHtmlContentRenderer` builds elements rather than a markup string on purpose: composing
markup means remote news content is concatenated into a string that is then parsed, so a page
containing tag-like text can restyle everything after it. Text assigned to a `Label` is never
parsed as markup. It also bounds traversal depth and element count, and routes every link
through `LauncherLinkPolicy`, because the news document comes from an operator-configured URL
this client does not control.

### Related

| Component | Relationship |
|---|---|
| [Bootstrap](../../Shared/Implementation/Bootstrap/README.md) | Loads the launcher scene; the launcher hands back to `ClientPostbootSystem` |
| [Updater](../../../../../FishMMO-Patcher/Updater/README.md) | Consumes the downloaded archive; shares the `Patches/` path contract |
| [Patcher server](../../../../../FishMMO-WebServers/PatcherASP.NET/README.md) | Serves `/latest_version` metadata and the patch archive |
| `Constants.Configuration` | `APIHost`, `LauncherHtmlUrl`, `UpdaterExecutable`, `PatchesDirectoryName` |

## License

This module is part of the FishMMO project and is distributed under the FishMMO project license.
