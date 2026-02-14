# Naming System

## Overview

The Naming system provides procedural name generation for scene objects (NPCs, interactables) in FishMMO. It combines a randomly selected prefix and suffix from designer-authored `NameCache` ScriptableObjects to produce a two-part name (e.g., "Grumpy Thornbeard"). The server selects name indices on spawn, and the chosen indices are synchronized to clients via FishNet's payload system. This is used for non-player entities that need randomized, deterministic names without database persistence.

## Directory Structure

```
Naming/
├── SceneObjectNamer.cs        # NetworkBehaviour that generates and syncs a two-part name
└── Template/
    └── NameCache.cs           # ScriptableObject storing a list of name strings
```

### Related Files (Outside This Directory)

```
Shared/Entity/Interactable/Banker.cs          # [RequireComponent(typeof(SceneObjectNamer))]
Shared/Entity/Interactable/Merchant.cs        # [RequireComponent(typeof(SceneObjectNamer))]
Shared/Entity/Interactable/AbilityCrafter.cs  # [RequireComponent(typeof(SceneObjectNamer))]
Client/UI/Controls/World/Target/UITarget.cs   # Reads SceneObjectNamer for target display name
Shared/Constants.cs                           # Registers NameCache type name for caching
```

## Inheritance Hierarchies

### Components (NetworkBehaviour)

```
NetworkBehaviour
└── SceneObjectNamer
```

### Templates (ScriptableObjects)

```
CachedScriptableObject<NameCache>
└── NameCache : ICachedObject
```

## Data Model

### SceneObjectNamer

| Field | Type | Description |
|-------|------|-------------|
| `Prefix` | `NameCache` | ScriptableObject containing the pool of prefix names |
| `Suffix` | `NameCache` | ScriptableObject containing the pool of suffix names |
| `prefixID` | `int` (private) | Index into `Prefix.Names` selected on server start |
| `suffixID` | `int` (private) | Index into `Suffix.Names` selected on server start |

### NameCache

| Field | Type | Description |
|-------|------|-------------|
| `Names` | `List<string>` | Designer-authored list of name strings |

## Name Generation Lifecycle

```
Server: OnStartServer()
  ├── prefixID = Random.Range(0, Prefix.Names.Count)   // or -1 if cache is null/empty
  └── suffixID = Random.Range(0, Suffix.Names.Count)    // or -1 if cache is null/empty

Client: ReadPayload(connection, reader)
  ├── Read prefixID and suffixID from network
  ├── Validate: prefixID in bounds, Prefix cache exists and is non-empty
  ├── Validate: suffixID in bounds, Suffix cache exists and is non-empty
  ├── Compose name: "{Prefix.Names[prefixID]} {Suffix.Names[suffixID]}"
  ├── Assign to gameObject.name
  └── (Client only) Update character.CharacterNameLabel.text

Server: WritePayload(connection, writer)
  ├── Write prefixID
  └── Write suffixID
```

## Network Synchronization

Name synchronization uses FishNet's payload system (part of the initial object spawn data):

| Direction | Data | Purpose |
|-----------|------|---------|
| Server → Client | `prefixID` (int), `suffixID` (int) | Transmit selected name indices on object spawn |

The payload is compact (two integers) regardless of name length. Clients resolve indices locally against their own `NameCache` assets, ensuring consistency as long as assets match.

## Validation

Both `ReadPayload` prefix and suffix branches validate:

| Check | Purpose |
|-------|---------|
| `id < 0` | Server returned -1 (cache was null/empty at spawn time) |
| `Cache == null` | Cache asset not assigned in inspector |
| `Cache.Names == null` | Names list not initialized |
| `Cache.Names.Count < 1` | Names list is empty |
| `id >= Cache.Names.Count` | Index out of range (protects against mismatched assets) |

If any validation fails, the name component for that part is skipped silently.

## Configuration

Name caches are created via the Unity asset menu:

**Create → FishMMO → Character → Name Cache**

Each `NameCache` asset contains a `List<string>` of names. Assign a prefix cache and a suffix cache to the `SceneObjectNamer` component on any scene object prefab.

### Example Setup

```
Prefix NameCache: ["Grumpy", "Jolly", "Sneaky", "Ancient", "Wise"]
Suffix NameCache: ["Thornbeard", "Ironforge", "Shadowmend", "Stonewall"]

Possible results: "Grumpy Thornbeard", "Wise Shadowmend", "Sneaky Ironforge", etc.
```

## External Integration Points

| System | Integration |
|--------|-------------|
| **Interactable System** | `Banker`, `Merchant`, `AbilityCrafter` all require `SceneObjectNamer` via `[RequireComponent]` |
| **UI Target System** | `UITarget` reads `SceneObjectNamer` from targeted objects to display names |
| **ICharacter** | `ReadPayload` updates `CharacterNameLabel.text` on client for name plate display |
| **Constants** | `NameCache` type name registered in `Constants` for the `CachedScriptableObject` loading system |