# FishMMO-SharedUtility

A lightweight, **pure C# / netstandard2.1** class library containing cross-cutting
utility code shared between the FishMMO Unity client and the FishMMO-Database
server project. The library has no dependency on `UnityEngine`, FishNet, or EF
Core, which lets it be referenced from both Unity and headless .NET tooling.

The post-build target copies `FishMMO-SharedUtility.dll` into
`../FishMMO-Unity/Assets/Dependencies/` so Unity picks it up automatically as a
managed plugin.

---

## Table of Contents

- [Description](#description)
- [Supported Platforms](#supported-platforms)
- [Architecture](#architecture)
- [Key Components](#key-components)
  - [Top-level utilities](#top-level-utilities)
  - [Compression](#compression)
  - [Extensions](#extensions)
- [What Belongs Here](#what-belongs-here)
- [Configuration](#configuration)
- [Build](#build)
- [Consuming from FishMMO-DB](#consuming-from-fishmmo-db)
- [Flow Diagram](#flow-diagram)

---

## Description

`FishMMO-SharedUtility` is the lowest layer of the FishMMO C# stack. It is the
only assembly that is safe to reference from every other C# project in the
monorepo, including FishMMO-DB, FishMMO-AppHealthMonitor, the WebServers, and
the Unity managed scripts. Its scope is intentionally narrow:

- Pure-data validators (e.g. SRP-6a-friendly password and username rules).
- Generic, allocation-aware data structures (`CircularBuffer<T>`, `SetOnce<T>`).
- Math and bit utilities that aren't already in BCL.
- String / dictionary compression that wraps `System.IO.Compression`.
- A large set of primitive and collection extensions used throughout the codebase.

---

## Supported Platforms

| Target | Status |
|---|---|
| .NET Standard 2.1 | Yes |
| Unity 6.3 LTS (IL2CPP / Mono) | Yes (via `Assets/Dependencies/`) |
| .NET 8.0 server projects | Yes |

| Requirement | Version |
|---|---|
| .NET SDK | 8.0+ |
| Language Version | `latest` |
| Nullable Reference Types | Enabled |
| `ZString` | `2.6.0` |

---

## Architecture

```
FishMMO-SharedUtility/
├── Authentication.cs          # Username/password validators
├── CircularBuffer.cs          # Allocation-light ring buffer
├── Configuration.cs           # Hierarchical key/value config tree
├── FastActivator.cs           # Expression-tree compiled object factory
├── MathHelper.cs              # Vector/scalar/clamp helpers
├── MemoryAccess.cs            # Unsafe span / pointer helpers
├── IReference.cs              # Reference-equality interface contract
├── RefWrapper.cs              # Boxed reference wrapper for value types
├── SetOnce.cs                 # Write-once latch with optional validator
├── Compression/
│   ├── StringCompression.cs   # GZip-based string round-trip
│   └── DictionaryCompression.cs
└── Extensions/
    ├── ArrayExtensions.cs
    ├── DirectoryExtensions.cs
    ├── EnumExtensions.cs
    ├── IListExtensions.cs
    ├── ProcessExtensions.cs
    ├── RandomExtensions.cs
    ├── StringExtensions.cs
    ├── TypeExtensions.cs
    └── Primitive/             # Byte, Short, Int, Long, Float bit helpers
```

All public types live in the `FishMMO.Shared` namespace.

---

## Key Components

### Top-level utilities

| Type | Responsibility |
|------|----------------|
| `Authentication` | Static validators for usernames, passwords, character names — the same rules used by the LoginServer and account-creation flows. |
| `CircularBuffer<T>` | Fixed-capacity ring buffer with overwrite-on-full semantics. |
| `Configuration` | In-memory hierarchical config (`Node` tree) that can be loaded from key/value text files. |
| `FastActivator<TResult>` | Compiled-expression factory — faster than `Activator.CreateInstance` and avoids reflection per-call. |
| `MathHelper` | Numeric helpers (clamp, lerp, snapping). |
| `MemoryAccess` | `Span<T>` / unsafe helpers for high-throughput serialization. |
| `RefWrapper<T>` | Wraps a value type so reference-comparison works (used for parameter capture). |
| `SetOnce<T>` | Latch that allows exactly one assignment; throws thereafter. |
| `IReference` | Marker interface for objects compared by reference. |

### Compression

| Type | Responsibility |
|------|----------------|
| `StringCompression` | GZip compress/decompress for arbitrary UTF-8 strings. |
| `DictionaryCompression` | Compresses dictionaries of strings using a shared dictionary frame. |

### Extensions

| Namespace | Highlights |
|---|---|
| `Extensions/*Extensions.cs` | `ArrayExtensions`, `IListExtensions` (binary search, swap, shuffle), `StringExtensions` (case-insensitive contains, hex), `TypeExtensions` (assignable-from cache), `RandomExtensions` (range pickers), `DirectoryExtensions`, `EnumExtensions`, `ProcessExtensions`. |
| `Extensions/Primitive/` | `ByteExtensions`, `ShortExtensions`, `IntExtensions`, `LongExtensions`, `FloatExtensions`, and their bit-twiddling helpers (`IntBitExtensions`, `LongBitExtensions`). |

---

## What Belongs Here

| Include | Do NOT Include |
|---|---|
| Validation helpers (Authentication, naming rules) | Anything that references `UnityEngine` |
| Pure math / string / collection utilities | Database / EF Core entities or services |
| Shared constants and enums | Networking code that depends on FishNet |
| Compression / allocation-free helpers | Logging — use FishMMO-Logger instead |

If a candidate utility imports `UnityEngine`, FishNet, or `Microsoft.EntityFrameworkCore`, it does **not** belong here.

---

## Configuration

None. The library is dependency-injection-free and runtime-config-free.

---

## Build

```bash
dotnet build FishMMO-SharedUtility.slnx
```

The `CopyToUnityDependencies` MSBuild target copies the built DLL into
`../../FishMMO-Unity/Assets/Dependencies/` after every successful build (both
`Debug` and `Release`).

---

## Consuming from FishMMO-DB

FishMMO-DB references this project via a `<ProjectReference>`. No manual DLL
copying is needed for the database side.

```xml
<ProjectReference Include="../../FishMMO-SharedUtility/FishMMO-SharedUtility/FishMMO-SharedUtility.csproj" />
```

---

## Flow Diagram

```mermaid
flowchart LR
    SU[FishMMO-SharedUtility<br/>netstandard2.1]

    SU -->|ProjectReference| DB[FishMMO-DB]
    SU -->|ProjectReference| LOG[FishMMO-Logger]
    SU -->|ProjectReference| AHM[FishMMO-AppHealthMonitor]
    SU -->|ProjectReference| WEB[FishMMO-WebServers]
    SU -->|DLL copy after build| UN[FishMMO-Unity<br/>Assets/Dependencies/]

    UN --> CL[Unity Client / Editor]
    UN --> SV[Unity Headless Server builds]
```
