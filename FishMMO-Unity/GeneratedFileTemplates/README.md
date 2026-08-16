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

## How the real values get there

Open **FishMMO Dashboard > Game Settings**:

- **Host Configuration** → *Write Host Config*
- **Certificate Pins** → *Fetch Pins*, then *Write Pins to File*
- **Client Secret** → paste the gate secret from the FishMMO-Installer output or the
  `deployment_secrets` table, then *Write Secret to File*

CI can instead substitute the sentinels directly, using the env vars documented in
each template header (`FISHMMO_API_HOST`, `FISHMMO_GAME_HOST`, `FISHMMO_PLAY_HOST`,
`FISHMMO_ROOT_DOMAIN`, `FISHMMO_CLIENT_GATE_SECRET`).

Real values stay on the machine that wrote them: the generated files are gitignored,
and the dashboard keeps unsaved edits in `SessionState` (Editor memory only), never
in `EditorPrefs`.
