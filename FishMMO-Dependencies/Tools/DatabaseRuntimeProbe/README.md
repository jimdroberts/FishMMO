# Unity database runtime compatibility probe

Read-only regression checks for the exact Unity Mono and AOT framework profiles.
Uses Unity's Mono executable, imported EF assemblies and database configuration;
it does not change any rows or start the application servers. The driver tests
exercise Unicode text/length-cache writeback, integers, alternate CLR type
handling, UUID, timestamp, byte array, integer array, boolean, decimal, DBNull,
and propagation of the original OverflowException from a reflected validator.
EF checks load the full model, query each of the three server registries, run
asynchronous queries, and materialize/track a synthetic server row from a SELECT
without saving or inserting it. The native harness runs these blocking diagnostic
waits on a worker task so UnitySynchronizationContext remains responsive.

From this directory:

```bash
python3 run.py --editor /path/to/Unity/Editor/Unity
# Also verify actual Linux Mono and IL2CPP standalone players (takes longer):
python3 run.py --editor /path/to/Unity/Editor/Unity --native
```

Defaults use the current game server's appsettings.json and the imported Unity
Npgsql DLL. `--settings`, `--driver` and `--artifacts` override these paths.
Existing secure database credential resolution is used; no credentials are passed
on the command line or written into the report. PgBouncer/PostgreSQL must be running.
The official Npgsql 5.0.18 netstandard2.1 NuGet package must be restored locally.
The matching Unity Linux Mono/IL2CPP build modules must be installed for native mode.

The matrix requires original Mono to pass, original AOT to reproduce the precise
byref-delegate failure, and patched Mono/AOT to pass. Profile tests run the Unity
AOT managed libraries under Mono to diagnose interpreter behavior; those tests
alone do not verify native IL2CPP generic code generation. `--native` supplies that
additional check in a separate temporary Unity project, leaving the real game's
Editor, backend preference, and installed servers untouched.

Inspect summary.json and the per-profile/player logs in the reported artifact
folder. A passing probe does not assert full authenticated game-session readiness.
