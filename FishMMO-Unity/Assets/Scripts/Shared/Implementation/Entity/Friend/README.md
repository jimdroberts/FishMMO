# Friend System

## Overview

The Friend system manages player friend lists in FishMMO. It provides a simple ID-based friend list per character, with add/remove operations, online status tracking, and FishNet network synchronization. Friend relationships are persisted to the database and restored on character load. The system uses a client-server architecture where friend requests are validated server-side with async database verification before being applied.

## Directory Structure

```
Friend/
├── FriendController.cs            # Per-entity controller (CharacterBehaviour / NetworkBehaviour)
└── IFriendController.cs           # Friend controller interface + instance events
```

### Related Files (Outside This Directory)

```
Shared/Implementation/Network/Character/FriendBroadcasts.cs                     # FishNet broadcast structs for friend add/remove
Server/Implementation/World/SceneServer/Friend/FriendSystem.cs                 # Server-side friend management, validation, and DB persistence
Server/Implementation/World/SceneServer/Friend/FriendSystemMainThreadQueueData.cs  # Main-thread queue for marshalling async DB results
Server/Implementation/World/SceneServer/Character/CharacterSystem.cs           # Loads friend list from DB on character load
Client/UI/Controls/World/FriendList/UIFriendList.cs                            # Friend list UI panel
Client/UI/Controls/World/FriendList/UIFriend.cs                                # Individual friend entry UI element
```

## Inheritance Hierarchies

### Controllers (NetworkBehaviour)

```
CharacterBehaviour
└── FriendController : IFriendController
```

### Supporting Types

```
FriendAddNewBroadcast              # Client → Server: request to add a friend by character ID
FriendAddBroadcast                 # Server → Client: friend added (ID + online status)
FriendAddMultipleBroadcast         # Server → Client: bulk friend add (initial load)
FriendRemoveBroadcast              # Bidirectional: remove a friend by character ID
```

## Data Model

The friend list is a simple `HashSet<long>` of character IDs. There is no `Friend` runtime class — the system stores only IDs. Online status is provided transiently through broadcasts but is not persisted in the controller's state.

| Field | Type | Description |
|-------|------|-------------|
| `Friends` | `HashSet<long>` | Set of friend character IDs |

## Friend Lifecycle

### 1. Adding a Friend

```
Client sends FriendAddNewBroadcast(characterID)
  └── Server: OnServerFriendAddNewBroadcastReceived
      ├── Validate: connection active, controller exists
      ├── Validate: friend count < maxFriends
      ├── Validate: not self-friending
      └── Async task: AddFriendAsync
          ├── Verify friend character exists in DB (ICharacterService.FetchAsync)
          ├── Persist friendship to DB (ICharacterFriendService.PersistAsync)
          └── Marshal to main thread:
              ├── Re-validate connection still active
              ├── friendController.AddFriend(friendID)
              └── Broadcast FriendAddBroadcast(friendID, online) to client
                  └── Client: OnClientFriendAddBroadcastReceived
                      ├── Add to Friends set
                      └── Fire OnAddFriend event
```

### 2. Removing a Friend

```
Client sends FriendRemoveBroadcast(characterID)
  └── Server: OnServerFriendRemoveBroadcastReceived
      ├── Validate: connection active, controller exists
      ├── Validate: friend exists in Friends set
      ├── Remove from in-memory state immediately
      ├── Broadcast FriendRemoveBroadcast(friendID) to client
      │   └── Client: OnClientFriendRemoveBroadcastReceived
      │       ├── Remove from Friends set
      │       └── Fire OnRemoveFriend event
      └── Fire-and-forget async: DeleteFriendAsync
          └── ICharacterFriendService.DeleteAsync
```

### 3. Loading Friends

Friends are loaded during character initialization in `CharacterSystem`:

```
CharacterSystem.LoadCharacter
  └── Fetch friend data from DB (ICharacterFriendService)
      └── For each friend:
          └── friendController.AddFriend(friend.FriendCharacterID)
```

The initial friend list is sent to the client via `FriendAddMultipleBroadcast` during payload synchronization.

## Server Validation

The server (`FriendSystem`) performs the following validations before adding a friend:

| Check | Purpose |
|-------|---------|
| `conn.FirstObject != null` | Connection has an active character |
| `friendController != null` | Character has a friend controller |
| `Friends.Count < maxFriends` | Friend list not full (default: 100) |
| `characterID != msg.CharacterID` | Prevents self-friending |
| `ICharacterService.FetchAsync` | Verifies the friend character exists in the database |

For removal, only connection and controller validity are checked, plus existence in the friend set.

## Async Architecture

The friend system uses a two-queue architecture for safe async-to-main-thread communication:

| Queue | Purpose |
|-------|---------|
| `AsyncWorkerData` | Executes database operations on a background thread |
| `FriendSystemMainThreadQueueData` | Marshals results back to the main thread for in-memory state changes and broadcasts |

This ensures:
- **Database operations** never block the main thread
- **In-memory state changes** and **FishNet broadcasts** always execute on the main thread
- **Connection re-validation** occurs after the async gap (connection may have disconnected during the DB query)

`OnLateUpdate` drains the main-thread queue each frame.

## Instance Events

Events are defined on both `IFriendController` and `FriendController`:

| Event | Signature | When Fired |
|-------|-----------|------------|
| `OnAddFriend` | `Action<long, bool>` | When a friend is added (character ID, online status) |
| `OnRemoveFriend` | `Action<long>` | When a friend is removed (character ID) |

These are instance events (not static), so each character's controller fires its own events independently.

## Network Synchronization

### Broadcast Types

| Broadcast | Direction | Purpose |
|-----------|-----------|---------|
| `FriendAddNewBroadcast` | Client → Server | Request to add a new friend |
| `FriendAddBroadcast` | Server → Client | Confirm friend added (includes online status) |
| `FriendAddMultipleBroadcast` | Server → Client | Bulk friend sync on login |
| `FriendRemoveBroadcast` | Client → Server | Request to remove a friend |
| `FriendRemoveBroadcast` | Server → Client | Confirm friend removed |

Note: `FriendRemoveBroadcast` is used bidirectionally — the same struct serves both the client request and the server confirmation.

## Configuration

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `MaxFriends` | `int` | `100` | Maximum number of friends per character (configurable via inspector on FriendSystem) |

## External Integration Points

The friend system is consumed by and interacts with:

- **CharacterSystem** — Loads friend list from database during character initialization via `AddFriend()`.
- **Database Layer** — Friends are persisted via `ICharacterFriendService` (add: `PersistAsync`, remove: `DeleteAsync`, load: fetched during character load).
- **UI** — `UIFriendList` displays the friend list panel; `UIFriend` represents individual entries with remove buttons. The UI subscribes to `OnAddFriend` and `OnRemoveFriend` instance events.
- **Input System** — `PlayerInputController` toggles `UIFriendList` visibility via the Friends keybind.
