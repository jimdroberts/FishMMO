# Dedicated server database linker preservation

The 2026-10-25 Linux IL2CPP server failed before database registration because
UnityLinker removed the public constructor of
`Microsoft.EntityFrameworkCore.Internal.SingletonOptionsInitializer`. The type
and its `EnsureInitialized` method survived, so the runtime error came from
Microsoft.Extensions.DependencyInjection attempting to instantiate a type whose
constructor had been removed. Original and stripped assemblies were both EF Core
5.0.17. The three missing-heartbeat warnings in AppHealthMonitor were downstream
symptoms; its PulseServerName values already matched the built server CFGs.

`ServerDatabaseLinkerProcessor` implements Unity's `IUnityLinkerProcessor` callback.
For standalone dedicated-server reports it writes an additional descriptor under
`Library/FishMMO/Linker/ServerDatabase.link.xml`, preserving the runtime EF Core,
Npgsql, naming-convention, FishMMO database and DI/configuration/cache/logging
assemblies. Reflection-created implementation classes need their constructors and
members, so preserving just the first failing type would leave the rest of the
service graph exposed to the same failure. Design-time EF tooling is excluded.

The callback uses the actual build report and typed standalone subtarget, rather
than editor `UNITY_SERVER` compilation symbols. It returns no descriptor for
standalone clients, WebGL, or mobile builds. The file lives outside Assets so
client builds do not collect it as a global link.xml. Standard Unity builds and
FishMMO's dashboard builds both use the callback. Existing global preservation
rules and shader/build-cache policies are unchanged.

## Validation

- Unity 6000.4.10f1 compiled the callback and passed all five focused EditMode tests.
  See `2026-10-25/linker-tests.xml`.
- Replayed the previous server UnityLinker command into an isolated temporary
  output, with the additional runtime assembly preservation rules: exit code 0.
  The previous generated `Assets/AddressableAssetsData/link.xml` was unavailable
  and omitted from this diagnostic replay; this was not a complete player build.
- Metadata inspection confirmed the original stripped assembly had no constructor
  for `SingletonOptionsInitializer`, while the newly linked output retained its
  public `.ctor` and `EnsureInitialized` method (assembly version 5.0.17.0).
- Runtime startup of a newly rebuilt native player remains to be verified. The
  existing installed GameAssembly.so/GameServer.exe were not replaced.

## Applying the fix

Rebuild the Linux dedicated server through the normal FishMMO Build Game flow.
The IL2CPP player must be rebuilt: editing XML cannot repair the native code in
an already built GameAssembly.so. No WebGL client or AppHealthMonitor rebuild is
needed for this change. Then restart the supervised game-server roles and check
that database initialization and registration complete. Additional runtime/AOT
issues, if present, require their own diagnostics; the validation above proves
retention of the constructor responsible for the observed failure.

Unity API reference:
https://docs.unity.com/en-us/engine/6000.3/script-reference/unityeditor/build/iunitylinkerprocessor/generateadditionallinkxmlfile
