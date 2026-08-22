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
