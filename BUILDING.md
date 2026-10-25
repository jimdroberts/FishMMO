# .NET build output

The root `Directory.Build.props` routes maintained .NET projects to:

```
Builds/<MSBuildProjectName>/<Configuration>/<TargetFramework>/[<RuntimeIdentifier>/]
```

For example, `dotnet build FishMMO-Installer/FishMMO-Installer/FishMMO-Installer.csproj -c Release`
produces `Builds/FishMMO-Installer/Release/net8.0/`. `dotnet publish` places deployable
files in the `publish/` subdirectory of that build variant. Projects retain their
own local `obj/` for NuGet restore and incremental build bookkeeping. Framework,
configuration, and runtime subdirectories prevent concurrent build variants from
sharing files. Unity-generated projects and native WebTransport builds retain
their existing output rules; Unity player builds use the Dashboard's build path.

Use `dotnet clean <project-or-solution> -c Release` (and the same runtime/framework
properties used to build) to clean a variant. The root `Directory.Build.targets`
also removes that variant's custom copied files and nested publish output. Clean
can recursively clean referenced projects, as usual. Other configurations and
projects remain untouched.

Patcher clean preserves runtime patch directories. It reads `Patches:DirectoryName`
from setup templates, runtime appsettings files, and the `Patches__DirectoryName`
environment variable before SDK Clean removes configuration files. The default
`Patches/` directory is always protected, including inside publish/RID output.
Known locations are recorded as relative paths in `.fishmmo-patch-directories.xml`
beside surviving patch data, so repeated cleans preserve custom paths even after
`obj/` and runtime configuration are removed. The record is removed when no patch
directories remain; do not delete it while custom patch data remains.
For configuration provided by another source, pass
`-p:FishMMOPatchDirectories=relative/path` (semicolon-separated for multiple paths).
Malformed JSON stops clean before SDK output deletion so patch locations are not
silently missed. Patch data is never moved; builds/publishes are still cleaned
around the protected directories. Directory symlinks are not traversed.

Explicit `--output`, `OutputPath`, or `PublishDir`
overrides are supported, but directories outside the centralized project tree
are only cleaned by the standard SDK file tracking.

Old local `bin/` directories are not migrated or deleted. They can be removed
manually after confirming the centralized builds work. Existing installed services
must be registered again to use the new binary paths. The Updater publish scripts,
Unity Dashboard updater lookup, Control Panel local runner, and Installer service
lookup use the centralized layout.
