# Hotkey System

## Overview

The Hotkey system is the SceneServer authority for player hotkey bindings. It receives single and batch hotkey set requests from clients, validates slot boundaries, initializes per-character hotkey storage when needed, and applies updates to the character runtime state.

This subsystem is intentionally lightweight:
- No database persistence.
- No background workers.
- Main-thread request validation and in-memory mutation only.

## Directory Structure

```text
Hotkey/
├── HotkeySystem.cs   # Network handlers and hotkey validation/application logic
├── HotkeySystemRuntimeData.cs # Runtime state container
└── README.md         # System documentation
```

## Core Contract

Implementation target:
- `IHotkeySystem`

The interface is minimal and lifecycle-driven through `IServerBehaviour`.

## Lifecycle

### InitializeOnce()
- Validates server dependency.
- Registers broadcast handlers:
  - `HotkeySetBroadcast`
  - `HotkeySetMultipleBroadcast`

### OnDeinitialize()
- Unregisters hotkey broadcast handlers.

## Request Handling Model

### Single update path
`OnServerHotkeySetBroadcastReceived(...)`
1. Validates connection and player object.
2. Validates player character + incoming payload.
3. Ensures hotkey list is initialized.
4. Validates slot range (`0 <= slot < hotkeyCount`).
5. Applies hotkey data to target slot.

### Batch update path
`OnServerHotkeySetMultipleBroadcastReceived(...)`
1. Validates connection, player object, character, and batch payload.
2. Iterates each entry.
3. Skips invalid sub-messages.
4. Applies valid slot updates independently.

This behavior avoids failing the entire batch because of one malformed entry.

## Internal Helpers

### `EnsureHotkeysInitialized(...)`
Creates and seeds the character hotkey list with `Constants.Configuration.MaximumPlayerHotkeys` entries when missing.

### `TryApplyHotkey(...)`
Performs slot validation and applies a normalized `HotkeyData` value to the target slot.
Returns `true` on success, `false` on invalid slot.

## Validation Rules

- Connection and spawned object must exist.
- Player character component must exist.
- Incoming payload must be present.
- Slot index must be within configured range.

Invalid requests are ignored safely without server exceptions.

## Data Ownership

The source of truth for active hotkey bindings is the runtime list on `IPlayerCharacter.Hotkeys`.

Because this system only updates runtime memory, any long-term persistence/reload behavior is expected to be handled by separate character save/load systems.

## Failure Semantics

- Invalid single requests are no-ops.
- Invalid entries in batch requests are skipped; valid entries still apply.
- No network echo/broadcast is emitted by this subsystem during set operations.

This keeps hotkey updates deterministic, cheap, and resilient to malformed client payloads.