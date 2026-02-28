# FishMMO-SharedUtility

A lightweight, **pure C# / netstandard2.1** class library containing cross-cutting
utility code shared between the FishMMO Unity client and the FishMMO-Database
server project.

## What belongs here

| ✅ Include | ❌ Do NOT include |
|---|---|
| Validation helpers (Authentication, naming rules) | Anything that references `UnityEngine` |
| Pure math / string / collection utilities | Database / EF Core entities or services |
| Shared constants and enums | Networking code that depends on FishNet |

## Build

```bash
dotnet build FishMMO-SharedUtility.sln
```

The post-build target automatically copies `FishMMO-SharedUtility.dll` into
`../FishMMO-Unity/Assets/Dependencies/` so Unity picks it up as a managed plugin.

## Consuming from FishMMO-DB

FishMMO-DB references this project via a `<ProjectReference>`.  No manual DLL
copying is needed for the database side.
