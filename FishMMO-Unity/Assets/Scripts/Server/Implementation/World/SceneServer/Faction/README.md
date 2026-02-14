# Faction System

## Overview

The Faction system is the SceneServer bridge between runtime faction changes and client-facing updates. It subscribes to faction controller update events and forwards faction value changes to the owning player connection.

This subsystem is intentionally small and event-driven:
- No polling loops.
- No database writes.
- No channel queuing.
- Immediate broadcast on valid faction update events.

## Directory Structure

```text
Faction/
├── FactionSystem.cs   # SceneServer faction event subscription and broadcast relay
└── README.md          # System documentation
```

## Core Contract

Implementation target:
- `IFactionSystem`

The interface is intentionally minimal and inherits server behavior lifecycle requirements via `IServerBehaviour`.

## Lifecycle

### InitializeOnce()
- Validates server dependency.
- Subscribes to `IFactionController.OnUpdateFaction`.

### OnDeinitialize()
- Unsubscribes from `IFactionController.OnUpdateFaction`.
- Unsubscription always runs to avoid static event retention across reloads.

## Event Flow

Primary event:
- `IFactionController.OnUpdateFaction(ICharacter character, Faction faction)`

Processing path:
1. Validate `character`, `faction`, and `faction.Template`.
2. Cast to `IPlayerCharacter`.
3. Validate owning connection exists.
4. Broadcast `FactionUpdateBroadcast` to the owner with:
   - `TemplateID`
   - `NewValue`

## Broadcast Payload

`FactionUpdateBroadcast` is sent only to the owning player and contains the updated faction template/value pair required for client-side UI/state refresh.

## Integration Notes

The Faction system relies on:
- `IFactionController` as the producer of authoritative faction updates.
- `IPlayerCharacter` ownership mapping for target connection resolution.

It does not calculate faction relationships itself; it relays finalized updates from faction controllers.

## Failure Semantics

The handler exits silently when required data is missing:
- Null character/faction/template.
- Non-player character source.
- Missing owner connection.

This keeps the relay path safe and prevents invalid network sends while preserving server stability.