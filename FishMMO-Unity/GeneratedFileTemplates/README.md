# Generated file templates

The three `*.generated.cs` files below carry real deployment values — public host
names, TLS certificate pins, and the **client gate secret** — so they are not tracked
in git. These sentinel templates are tracked in their place.

| Template | Restored to |
| --- | --- |
| `HostConfig.generated.cs.template` | `Assets/Scripts/Shared/Implementation/HostConfig.generated.cs` |
| `CertificatePins.generated.cs.template` | `Assets/Scripts/Client/Security/CertificatePins.generated.cs` |
| `ClientApiSecret.generated.cs.template` | `Assets/Scripts/Client/Security/ClientApiSecret.generated.cs` |

Every template value is a `FISHMMO_SENTINEL_PLACEHOLDER*` string.
`ClientSecurityBuildValidator` blocks non-development builds that still contain the
marker, so a project restored from templates can be opened and compiled but cannot
ship a release build until the real values are written.

## How the files get there

**Automatically, on Editor load.** `Assets/Editor/GeneratedFiles/GeneratedFileRestorer.cs`
copies any missing file from its template and refreshes the AssetDatabase. It lives in
its own assembly with no references, because the generated files compile into
`FishMMO.Shared` and `FishMMO.Client` — when they are missing, those assemblies fail
to compile and nothing inside them can run.

**Manually**, from the menu: `FishMMO > Restore Missing Generated Files`.

**Without Unity** (fresh clone, or CI before the Editor runs):

```bash
./FishMMO-Unity/GeneratedFileTemplates/restore-generated-files.sh
```

Restoring never overwrites an existing file, so it is always safe to re-run.

## Keeping an existing file in step with its template

Never overwriting has a cost: a checkout that already has a generated file keeps
whatever shape that file had when it was written. Add a field to a template and to
the dashboard writer, and fresh clones get it while every existing checkout does not
— and the only symptom is a `CS0117` in an assembly that looks unrelated. That is
[issue #122](https://github.com/jimdroberts/FishMMO/issues/122): the field was added
to the API and to neither writer, and the build kept working for whoever added it.

So both restore paths also compare each existing generated file against its template
and report every member the template declares that the file does not:

- `restore-generated-files.sh` prints the missing members and exits `1`.
- The Editor logs them on load, quoting the template's declaration so it can be
  pasted straight in.

The file is never patched automatically — it holds real values. Paste the missing
declaration in, or delete the file and restore it, which discards those values.

**Use the script in CI, not `-executeMethod`.** A batch-mode Unity that finds any
assembly failing to compile logs `Scripts have compiler errors.` and shuts down
before running `-executeMethod` or any `[InitializeOnLoad]` constructor — and a
missing or drifted generated file breaks `FishMMO.Shared` or `FishMMO.Client` by
definition, so the editor-side check never gets to run in exactly the case it is for.
The interactive Editor has no such abort, so opening the project does run it.

Adding a field to a generated file's API means editing two places by hand — this
folder's template and the matching writer in `FishMMODashboard.GameSettings.cs`.
The checks above do not remove that duplication; they make forgetting one of them
report itself, by name, instead of surfacing as a compile error somewhere downstream.

## How the real values get there

Open **FishMMO Dashboard > Game Settings**:

- **Host Configuration** → *Write Host Config*
- **Certificate Pins** → *Fetch Pins*, then *Write Pins to File*. Each host needs a TCP
  connect plus a TLS handshake, so the fetch runs on a background thread and the Editor
  stays responsive; the button disables until it completes
- **Client Secret** → paste the gate secret from the FishMMO-Installer output or the
  `deployment_secrets` table, then *Write Secret to File*

CI can instead substitute the sentinels directly, using the env vars documented in
each template header (`FISHMMO_API_HOST`, `FISHMMO_GAME_HOST`, `FISHMMO_PLAY_HOST`,
`FISHMMO_ROOT_DOMAIN`, `FISHMMO_CLIENT_GATE_SECRET`).

Real values stay on the machine that wrote them: the generated files are gitignored,
and the dashboard keeps unsaved edits in `SessionState` (Editor memory only), never
in `EditorPrefs`.
