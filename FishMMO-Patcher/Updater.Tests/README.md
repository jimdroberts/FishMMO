# Updater.Tests

Assertion harness for `FishMMO.Patcher.PathContainment` — the containment helper that closes
the zip-slip hole in `Updater/Program.cs` (audit CRIT-8).

```
dotnet run --project FishMMO-Patcher/Updater.Tests
```

Exit code `0` means every containment case passed. Non-zero means at least one regressed, and
the failing cases are re-listed at the end of the output.

The harness compiles `../Updater/Patch/PathContainment.cs` by source link rather than by project
reference, so it exercises the shipping file itself rather than a copy that could drift.

It covers, at minimum:

* benign nested paths, Windows-style separators, `.` segments, spaces and unicode names —
  these **must be allowed**, because a containment check that rejects real patch entries is a
  broken updater rather than a secure one;
* `..` traversal in every position and with either separator, including a path whose `..`
  segments cancel out;
* absolute POSIX paths, Windows drive-absolute and drive-*relative* paths, UNC shares and the
  Win32 device namespace;
* a sibling directory that shares a string prefix with the install root (the classic
  `StartsWith` bypass);
* real symbolic links created inside the root and pointing outside it, skipped with a printed
  NOTE on hosts where the process may not create them;
* embedded NUL, trailing dot/space segments and NTFS alternate data streams;
* the `ResolveOrThrow` contract, on both a refused and an accepted path;
* a regression proof that the paths being refused are exactly the ones bare `Path.Combine`
  would have let escape.

## Patch application (`PatchApplyTests.cs`)

After the containment cases the harness runs the **real Updater build** as its own process
against throwaway installs, writing each archive the way the patch generator does (with the same
`FishMMO.Shared.ZstdDelta`). Build the Updater first; `dotnet run` builds it through the project
reference. Covered:

* format 2: a changed file rebuilt from its zstd delta, a renamed bundle rebuilt under its new
  name with the old one removed, a zstd-compressed new file in a new directory, a deletion, an
  untouched file, a patched executable keeping its execute bit, staging and archive cleaned up;
* a corrupt delta: failure reported and the install left **exactly** as it was (no new files,
  nothing deleted or changed), archive kept for a retry;
* a drifted install (a file not at the expected old version): refused, nothing touched;
* refusals before anything is touched: a format-3 manifest, a format-1 `manifest.json` carrying
  format-2 fields, an encoded new file without its size;
* a format-1 archive from the previous generator still applies;
* a 70 MiB file with a change 50 MiB in, patched from a delta of about 135 KB (exercises the
  one-at-a-time gate for large decodes).

