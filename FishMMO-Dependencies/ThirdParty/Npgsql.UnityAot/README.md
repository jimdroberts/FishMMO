# Npgsql 5.0.18 Unity AOT compatibility build

Pinned upstream source: https://github.com/npgsql/npgsql/tree/v5.0.18/src/Npgsql
License: PostgreSQL (see LICENSE). Upstream signing identity and assembly/package
version are retained so the existing EF Core PostgreSQL provider remains compatible.

Three upstream runtime methods are changed:

- `NpgsqlTypeHandler<TDefault>.GenerateNonGenericValidationMethod`
- `NpgsqlSimpleTypeHandler<TDefault>.GenerateNonGenericValidationMethod`
- `NpgsqlConnector.Connect`: accept SocketError.InProgress as a pending non-blocking connection, alongside WouldBlock.

The two validator factories previously used `Expression.Compile` to create a delegate with a
`ref NpgsqlLengthCache` argument. Unity's IL2CPP profile interprets expressions;
its `LightLambda.CreateCustomDelegate` throws `NotImplementedException("byref delegate")`.
This happens while opening the connection and initializing driver handlers, before
any application SQL can run. Preserving more linker metadata cannot implement this
missing interpreter operation.

The replacement uses an ordinary C# delegate, compiled ahead of time by IL2CPP,
with an immutable dispatch table of the handler's implemented interfaces. Dispatch
still matches the exact boxed CLR type, preserves null/DBNull handling in the
original callers, copies a mutated ref length cache back even on exceptions, and
unwraps TargetInvocationException with ExceptionDispatchInfo so caller-visible
exception types and stacks are preserved. Simple validators leave the cache alone.
No runtime compilation of a byref expression delegate is required. Both Mono and
IL2CPP use the same implementation. Reflection dispatch allocates a small argument
array per non-generic validation; typed parameter paths are unchanged.

The wrapper builds the original source for netstandard2.1 using the same support
package versions already shipped in FishMMO Unity. Design-time tools are not added.
`FishMMO-Dependencies` builds this project and copies its DLL over the normal NuGet
Npgsql DLL only in Unity's Assets/Dependencies directory. Other .NET applications
keep their existing NuGet driver. Rebuilding dependencies therefore cannot silently
replace the fix with the unmodified package DLL.

To rebuild only the compatibility DLL:

```bash
dotnet build ThirdParty/Npgsql.UnityAot/Npgsql.UnityAot.csproj
dotnet msbuild FishMMO-Dependencies.csproj -t:CopyUnityAotNpgsql
```

After updating the DLL, rebuild the dedicated game server for the backend selected
in the dashboard. Existing native IL2CPP players cannot consume a replacement
managed DLL without rebuilding. Keep the server linker preservation rules as well:
constructor stripping and runtime expression support were independent failures.


The native IL2CPP test also exposed a separate synchronous socket compatibility
issue: Unity returned SocketError.InProgress (10036) for a non-blocking TCP connect,
while Npgsql accepted only WouldBlock and aborted before its existing Select/error
check. Both are valid pending-connect results on this path. The adapter now accepts
both and leaves the existing timeout, readiness, error and socket cleanup logic intact.


Verified with Unity 6000.4.10f1: the unmodified driver reproduced the failure under
Unity's AOT profile; the compatibility driver passed both framework-profile probes
and actual standalone dedicated-server Mono and IL2CPP players. Checks include all
77 EF entity models, registry reads, asynchronous EF queries, tracked synthetic-row
materialization, ten parameter roundtrips and validator exception propagation.
No application rows were modified. Evidence is under
`FishMMO-Unity/Docs/ServerDatabaseLinking/2026-10-25` in the repository root.
