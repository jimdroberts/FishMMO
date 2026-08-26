# Client Settings

## Overview

Everything the player can change about their client: display, audio, gameplay, key bindings and
interface. All of it lives in one `Configuration.cfg` beside the executable, is loaded before the
first scene, and is applied during the boot phase rather than when the settings screen is opened.

The settings screen itself is `UITKOptions` in
[`Client/GUI/World/Options`](../GUI/World/Options); this folder is the model behind it.

| File | Responsibility |
|---|---|
| `ClientSettings` | The single owner of `Configuration.GlobalSettings`. Names every key, clamps every read, owns the one debounced write. |
| `ClientSettingsBootstrap` | Loads the store before any scene, applies everything from the boot hook. |
| `ClientSettingsPump` | Drives the debounced write and forces it out on focus loss, pause and quit. |
| `ClientDisplaySettings` | Display modes, quality, VSync, frame-rate cap, brightness. |
| `ClientAudioSettings` | Per-channel volumes and the unfocused mute. |
| `ClientAudioFocusWatcher` | Reports window focus so the unfocused mute has something to act on. |
| `AudioChannel` | The volume groups. |
| `UIProfile` | Reads and writes a shareable UI layout/colour file, separate from `Configuration.cfg`. |

## Boot order

Settings are applied in two phases, and the split matters.

```
RuntimeInitializeOnLoadMethod(BeforeSceneLoad)
  └── ClientSettingsBootstrap.Initialize()
      ├── ClientDisplaySettings.CaptureAuthoredQuality()   (editor safeguard, before ANY write)
      ├── ClientSettings.EnsureLoaded()                    (store exists for the first panel)
      └── subscribe to MainBootstrapSystem.OnApplyClientBootSettings

MainBootstrapSystem.OnPreload()          (first scene's Awake)
  ├── QualitySettings.vSyncCount  = 0
  ├── Application.targetFrameRate = BootstrapTargetFrameRate (60)
  └── OnApplyClientBootSettings?.Invoke()
      └── ClientSettingsBootstrap.Apply()                  (guarded: runs once per session)
          ├── ClientSettings.ApplyAll()
          │   ├── display   — quality, mode, VSync, frame rate, brightness, scene hook
          │   ├── audio     — channel levels, master onto the listener, focus watcher
          │   ├── interface — UI scale onto the shared PanelSettings
          │   └── theme     — UITKThemeManager.Reload()
          ├── ClientSettingsPump.Install()
          └── PlayerInputController.EnsureControlsCreated()   (loads keybinding overrides)

RuntimeInitializeOnLoadMethod(AfterSceneLoad)
  └── ClientSettingsBootstrap.ApplyAfterFirstScene()       (backstop for scenes with no
                                                            bootstrap system — UI validation,
                                                            tests; no-ops if Apply already ran)
```

**Why the store loads before the scene.** It used to be created lazily by whichever of two
unrelated places asked first — the launcher, or the settings panel. The panel ships closed, so a
client launched past the launcher ran the whole of boot with `Configuration.GlobalSettings` null:
keybinding overrides were skipped silently, panel positions were never restored, and the theme
was built from nothing. Every one of those looked like a setting that had not saved rather than
one that had never been read.

**Why applying waits for the hook.** The bootstrap system installs a boot-time frame rate and
VSync default during the first scene's `Awake`. Anything applied before those two lines is
silently overwritten by them.

**Why `EnsureControlsCreated` is here.** `PlayerControls` used to be built only on world entry,
so the Key Bindings tab could show nothing until the player was in the world, and a saved
override was not read until then either. The asset is inert data until an action map is enabled,
so creating it at boot costs nothing and makes bindings editable from the login screen.

## Keys

Every key is named once, in `ClientSettings`, because a key is a string shared between the
control that writes it and the code that applies it — and those are almost always in different
files.

| Group | Keys |
|---|---|
| Display | `VSync`, `Frame Rate Limit`, `Brightness`, `Resolution Width`, `Resolution Height`, `Refresh Rate`, `Fullscreen`, `Quality Level` |
| Audio | `Audio.Volume.<Channel>`, `Audio.MuteWhenUnfocused` |
| Gameplay | `ShowDamage`, `ShowHeals`, `ShowAchievementCompletion`, `IgnorePartyInvites`, `IgnoreGuildInvites` |
| Interface | `UI.Scale`, `UI.SnapGridSize`, `UI.Panel.<PanelName>.X` / `.Y`, `<Name>ColorR/G/B/A` |
| Input | `InputBindingOverrides` (the Input System's override JSON) |

Quality is stored by **name**, not index: levels can be reordered between builds and an index
saved against the old order silently selects a different level. A name that no longer exists is
ignored.

## Writing

There is exactly one pending write in the client.

```
control changes → ClientSettings.Set/SetString/Remove → Configuration + RequestSave()
                                                             │
                                    ClientSettingsPump.Update ┴→ Pump() → Flush() after 0.75s quiet
```

`Configuration.Save()` serialises and rewrites the whole file, so a slider bound straight to it
rewrites the file once per frame for as long as it is held. Every change coalesces onto a short
quiet period instead.

`UITKPanelPositions` used to keep a **second** pending flag and a second deadline over the same
file, with a different interval. Both wrote the whole thing, each could fire inside the other's
quiet period, and which of two simultaneous changes reached disk first depended on which
subsystem had last been touched. It now requests and flushes through `ClientSettings` like
everything else. So does `LauncherSettings`, which previously called `Save()` on the store
directly and so bypassed both the editor guard and the WebGL sync.

Forced flushes: settings panel close and destroy, `UIProfile.Load`, `UIManager.ResetAllPanelPositions`,
`Client.OnDestroy`, and the pump on focus loss, pause, quit and destroy.

**The pump is a component, not a per-frame call on a panel.** It used to be driven from
`UITKControl.Update`, which made a guarantee about the player's settings depend on at least one
panel being alive to make it — a scene without panels silently stopped the clock on a write that
was already owed. It is created on demand by `RequestSave()` and lives in `DontDestroyOnLoad`.
Flushing on focus loss matters most in a browser, where `OnApplicationQuit` does not run when a
tab is closed.

## Platform behaviour

| Platform | Writes to disk | Notes |
|---|---|---|
| Standalone | Yes | `Constants.GetWorkingDirectory()` — the install directory, shared with the launcher |
| Editor | **No** | The working directory is the repository root; a play-mode session would rewrite the developer's checked-out `Configuration.cfg`. In-memory values still apply, so settings behave normally while playing. |
| WebGL | Yes | `persistentDataPath`, an Emscripten IDBFS mount. A write reaches IndexedDB only once the mount is synced — `WebGLPersistentData.Sync()` does that after every save, and after a UI profile is written or deleted. |

The editor also has a second safeguard. `QualitySettings` is a project asset, and a value written
into it at runtime **stays written**: running the client once left `m_CurrentQuality` and the
active level's `vSyncCount` modified in source control, describing whatever the last person to
press Play had saved. Every quality/VSync write therefore routes through
`ClientDisplaySettings.ApplyVSync` / `ApplyQualityLevel`, which capture the authored values once
and restore them on play-mode exit. This mirrors what `UITKPanelScale` does for `PanelSettings`.

## Reading

Every read clamps. `Configuration.cfg` is a plain text file: a player can edit it, a crash can
truncate it mid-write, and a build from another machine can leave values this one cannot honour.
The values reach `RenderSettings.ambientLight`, `Screen.fullScreenMode` and
`AudioListener.volume`, none of which validate what they are given.

`GetFloat` additionally rejects NaN and infinity explicitly, because NaN compares false against
every bound — `Mathf.Clamp` passes it straight through, and a NaN reaching a slider or a colour
channel corrupts everything downstream.

A value that is *present but unreadable* falls back to the caller's default rather than to the
type's. A truncated write or a hand edit would otherwise mean zero brightness, zero volume, or a
toggle reading off that ships on.

### Numbers are stored culture-invariantly

`Configuration` formats every value with `CultureInfo.InvariantCulture`, and gives `float` and
`double` the round-trip (`"R"`) format. This is load-bearing, not tidiness. It previously used
`value.ToString()` — the *current* culture — while every reader parses invariantly, so on any
machine whose locale writes a comma as the decimal separator, `0.75f` was stored as `"0,75"` and
read back as **75**: the comma was accepted as a digit-group separator. Interface scale,
brightness, every audio volume and every window position round-tripped to roughly a hundred times
their value and were then clamped to whatever bound the reader enforced. Float parsing now uses
`NumberStyles.Float`, so a legacy comma value is rejected and falls back rather than being
absorbed.

## Display

Display mode is the one group that is **staged, not live**. Every other setting can be undone by
the control that set it; a display mode cannot — pick one the monitor will not show and the player
cannot see the panel that would put it back.

```
dropdowns → (pending selection) → Apply → mode applied + 12s countdown armed
                                              ├── Keep   → written to Configuration.cfg
                                              ├── Revert → previous mode restored
                                              └── timeout → previous mode restored
```

Nothing is written until **Keep**, so a mode that blacked out the screen is not still there after
a restart. Closing the panel with a mode unconfirmed reverts immediately rather than leaving the
player waiting out a countdown with no prompt on screen.

At boot there is no countdown and no panel, so a saved resolution the display no longer offers is
**refused** rather than approximated — the usual way to get one is moving the install to another
machine.

Brightness drives both `RenderSettings.ambientLight` and `RenderSettings.ambientIntensity`.
`ambientLight` is consulted only under `AmbientMode.Flat`; every world scene is authored
`AmbientMode.Skybox`, where it is ignored outright — so a slider that wrote only `ambientLight`
did nothing anywhere the player actually plays. It is re-applied on every scene load, because
ambient is per-scene state that a load discards. It is an *ambient* control, not an exposure one:
a true gamma control would need a URP Volume with a Color Adjustments override.

## Audio

Levels are stored as slider positions and applied through a squared curve, so the middle of the
slider lands near the middle of the perceived range. The stored value is always the slider
position, so the curve can change later without invalidating anybody's settings.

**Only `Master` is offered today.** It is applied to `AudioListener.volume`, which scales
everything the scene plays. The other five channels keep their key, default, stored level and
change event so that wiring up an audio system later is adding entries to
`ClientAudioSettings.PlayableChannels` — but nothing in the client owns an `AudioSource` yet, so
sliders for them would save perfectly and change nothing audible. A control that does nothing is
worse than a missing one.

Muting when unfocused is a volume decision, not a pause: it is applied on top of Master rather
than by writing zero into it, so the saved level is not destroyed by switching windows.

## UI profiles

A **UI profile** is the player's window layout, theme colours and interface scale in a file of
its own, under `UIProfiles/` in the install directory, so it can be handed to somebody else.

`Configuration.cfg` holds the whole client's settings — API host, launcher state, this machine's
display mode. None of that is meaningful on another player's computer and some of it is actively
wrong there, so it is not something to hand around.

`Configuration.cfg` stays the source of truth: loading a profile writes its keys into the global
store and saves. Nothing reads a profile at runtime, so a profile that is later deleted cannot
take the player's interface with it.

A profile is applied **wholesale**, including the absence of a key: a profile that says nothing
about a panel means that panel belongs where the stylesheet puts it. Merging would produce a
layout neither the sender nor the receiver has ever seen. That is also why the format version key
must be *present* — the folder is meant to be shared into and the dropdown lists whatever `.cfg`
it finds, so a file with no profile keys in it would otherwise clear every colour and every panel
position.

Names are rejected rather than sanitised, including Windows reserved device names, trailing dots
and spaces, and anything containing a path separator.

## Verification

| Behaviour | How to test | Expected |
|---|---|---|
| Settings apply without opening the panel | Change brightness/volume/scale, restart | In force from boot |
| Fresh install frame rate | Delete `Configuration.cfg`, start, open Options | **60 FPS**, and that is what is applied |
| Debounce holds during a drag | Drag a slider for several seconds | One file write, after release |
| No write in the editor | Change anything in play mode | `Configuration.cfg` untouched |
| Quality asset is not dirtied | Change quality in play mode, exit | `ProjectSettings/QualitySettings.asset` clean in source control |
| Locale independence | Run with a comma-decimal locale, set UI scale, restart | Scale is what was chosen |
| Corrupt config recovers | Hand-edit a garbage value, start | The default is used, the client starts |
| Display mode cannot strand the player | Apply a mode, do not press Keep | Reverts after 12s; nothing persisted |
| Rebinding | Click a row, press a key | Bound; duplicates refused with an explanation; Escape cancels; Backspace clears |
| Profile round trip | Save, change things, load | Layout, colours and scale restored; unrelated `.cfg` refused |

## Related

- [Settings panel](../GUI/World/Options) — the five-tab UI over this model
- [Bootstrap](../../Shared/Implementation/Bootstrap/README.md) — where the apply hook is raised
- [Launcher](../Launcher/README.md) — shares the same `Configuration` instance
