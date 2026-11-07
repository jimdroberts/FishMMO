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


## Npgsql runtime compatibility (both Mono and IL2CPP)

After constructor preservation, the game servers hit a second failure while
registering: `NotImplementedException("byref delegate")`. An isolated read-only
probe reproduced it with the actual Unity AOT framework libraries and identified
Npgsql 5.0.18's `NpgsqlTypeHandler<TDefault>.GenerateNonGenericValidationMethod`.
The simple-handler variant has the same problem. Both compile a validator delegate
with a `ref NpgsqlLengthCache` parameter; Unity's expression interpreter cannot
create that delegate. The EF model itself successfully loaded 77 entity types.

A pinned, source-controlled Npgsql compatibility build now replaces those two
expression factories with normal AOT-compiled C# delegates and cached interface
reflection dispatch. Ref-cache updates and original exception types are preserved.
Native validation then found an additional synchronous connection issue: IL2CPP
returns SocketError.InProgress (10036) for non-blocking connect, while the original
driver accepted only WouldBlock. The existing socket readiness/error/timeout path
now accepts both pending statuses. These are the only three upstream runtime source
methods changed. The PostgreSQL license and signing identity are retained.

Source and build notes:
`FishMMO-Dependencies/ThirdParty/Npgsql.UnityAot/README.md` (relative to repository root).
The dependency project builds the compatibility DLL and copies it into Unity
**after** copying the standard package dependencies, so a subsequent dependency
refresh retains the fix. Ordinary .NET applications keep their original NuGet
Npgsql driver. No backend is switched or disabled.

Verification on 2026-10-25:

- Original Npgsql: Mono passed; AOT-profile probe reproduced the byref-delegate failure.
- Patched Npgsql: both framework-profile probes passed.
- Actual Unity 6000.4.10f1 standalone dedicated players: Mono and size-optimized
  IL2CPP both exited 0. Both loaded the full EF model, queried the login/world/scene
  registries, executed an async query, materialized and tracked a synthetic server
  row, and round-tripped ten PostgreSQL parameter types. Overflow exceptions from
  validators were correctly propagated without TargetInvocationException wrappers.
- All database SQL was read-only; the synthetic row came from a SELECT expression,
  with no insertion or persisted records. The asynchronous probe runs on a worker
  task so the diagnostic's blocking waits cannot deadlock Unity's main thread.
- Results: `2026-10-25/npgsql-runtime-results.json` and the two database-probe text
  files. The regression harness is in `FishMMO-Dependencies/Tools/DatabaseRuntimeProbe`.

The imported Unity DLL is updated. Rebuild the actual GameServer using the selected
Mono or IL2CPP backend and restart its monitored roles. This work built isolated
validation players; the installed full GameServer binary was not rebuilt/replaced.
No AppHealthMonitor or WebGL client rebuild is required for this server fix.
AppHealthMonitor now supports per-application `LaunchInTerminal` on Linux desktops,
with automatic terminal detection. Game output is visible in the terminal and
retained in each role's `.Player.log` file. See the AppHealthMonitor README.
