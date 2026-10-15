# Generated file templates

The three `*.generated.cs` files below carry real deployment values — public host
names, TLS certificate pins, and the **client gate secret** — so they are not tracked
in git. These sentinel templates are tracked in their place.

| Template | Restored to |
| --- | --- |
| `HostConfig.generated.cs.template` | `Assets/Scripts/Shared/Implementation/HostConfig.generated.cs` |
| `CertificatePins.generated.cs.template` | `Assets/Scripts/Client/Security/CertificatePins.generated.cs` |
| `ClientApiSecret.generated.cs.template` | `Assets/Scripts/Client/Security/ClientApiSecret.generated.cs` |

Each generated file only **overrides** values. The classes themselves are tracked —
`HostConfig` (`Assets/Scripts/Shared/Implementation/HostConfig.cs`), `CertificatePinSet`
and `ClientGateSecret` (`Assets/Scripts/Client/Security/`) — and hold sentinel defaults.
Each declares `static partial void Configure(Values values)`, and the generated file is
just that method's body:

```csharp
public static partial class HostConfig
{
	static partial void Configure(Values values)
	{
		values.ApiHost = "https://api.example.com/";
		// ...
	}
}
```

An unimplemented partial method is removed by the compiler, so **a missing generated
file is not a compile error**. It used to be: the generated file declared the whole
class, a fresh clone failed to compile on first open, and Unity offered Safe Mode — which
runs no project editor code, so neither the restorer below nor the FishMMO menu that
writes the real values could ever run. A value the file does not assign stays at its
sentinel, so an old file never breaks the build either.

Every template value is a `FISHMMO_SENTINEL_PLACEHOLDER*` string, as is every default.
`ClientSecurityBuildValidator` blocks non-development builds that still contain the
marker, so a fresh clone opens and compiles but cannot ship a release build until the
real values are written.

## How the files get there

**Automatically, on Editor load.** `Assets/Editor/GeneratedFiles/GeneratedFileRestorer.cs`
copies any missing file from its template and refreshes the AssetDatabase, so the
dashboard always has a file to overwrite and CI a file to substitute into.

**Manually**, from the menu: `FishMMO > Restore Missing Generated Files`.

**Without Unity** (fresh clone, or CI before the Editor runs):

```bash
./FishMMO-Unity/GeneratedFileTemplates/restore-generated-files.sh
```

Restoring never overwrites an existing file, so it is always safe to re-run.

## Keeping an existing file in step with its template

Never overwriting has a cost: a checkout that already has a generated file keeps
whatever shape that file had when it was written. Add a value to a template and to
the dashboard writer, and fresh clones get it while every existing checkout does not
— and that value silently stays a sentinel. (Before the partial-class split the same
drift was a `CS0117` in an assembly that looked unrelated:
[issue #122](https://github.com/jimdroberts/FishMMO/issues/122).)

So both restore paths also compare each existing generated file against its template
and report every value the template assigns that the file does not:

- `restore-generated-files.sh` prints the missing values and exits `1`. It
  compares `values.<Name> =` assignments only.
- The Editor's `Assets/Editor/GeneratedFiles/GeneratedFileDriftCheck.cs` logs them
  on load, quoting the template's line — comment included — so it can be pasted
  straight into `Configure`. It also checks the file declares the template's partial
  class, which is how a file from **before the split** shows up: it declares its own
  `GeneratedHostConfig` / `GeneratedPinSet` / `GeneratedClientSecret`, which still
  compiles but which nothing reads, so every value in it is ignored. Open
  **FishMMO Dashboard > Game Settings** (it still reads the old shape) and click the
  section's Write button to rewrite it.

The file is never patched automatically — it holds real values. Rewrite it from the
dashboard, paste the missing line in, or delete the file and restore it, which
discards those values.

**Use the script in CI, not `-executeMethod`.** It needs no Unity, and a batch-mode
Unity that finds any assembly failing to compile logs `Scripts have compiler errors.`
and shuts down before running `-executeMethod` or any `[InitializeOnLoad]` constructor.

Adding a value means editing four places by hand — the tracked class's `Values`
default, the class's own field, this folder's template, and the matching writer in
`FishMMODashboard.GameSettings.cs`. The checks above make forgetting the template or
the writer report itself, by name.

## How the real values get there

Open **FishMMO Dashboard > Game Settings**:

- **Host Configuration** → *Write Host Config*
- **Certificate Pins** → *Fetch Pins*, then *Write Pins to File*. Each host needs a TCP
  connect plus a TLS handshake, so the fetch runs on a background thread and the Editor
  stays responsive; the button disables until it completes. The two manifest public keys
  have no dashboard field: set them in the file by hand, and a later write keeps them
- **Client Secret** → paste the gate secret from the FishMMO-Installer output or the
  `deployment_secrets` table, then *Write Secret to File*

CI can instead substitute the sentinels directly, using the env vars documented in
each template header (`FISHMMO_API_HOST`, `FISHMMO_GAME_HOST`, `FISHMMO_PLAY_HOST`,
`FISHMMO_ROOT_DOMAIN`, `FISHMMO_CLIENT_GATE_SECRET`).

Real values stay on the machine that wrote them: the generated files are gitignored,
and the dashboard keeps unsaved edits in `SessionState` (Editor memory only), never
in `EditorPrefs`.
