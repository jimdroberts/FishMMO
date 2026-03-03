# Party System

## Overview

The Party system is a server-authoritative, database-backed framework for managing player parties in FishMMO. It supports party creation, invitation with pending-invite validation, member addition/removal, rank management (leader/member), leadership transfer, and cross-server synchronization via database polling. Game logic and broadcasts run synchronously on the main thread, while database operations are performed asynchronously and marshalled back via a main-thread queue.

## Directory Structure

```
Party/
├── IPartyController.cs   # Interface for per-character party state and events
├── PartyController.cs     # Client-side controller (CharacterBehaviour + broadcast listeners)
└── PartyRank.cs           # Enum defining party ranks (None, Member, Leader)
```

### Related Files (Outside This Directory)

```
Shared/Network/Character/
└── PartyBroadcasts.cs     # All party broadcast structs (Create, Invite, Add, Leave, Remove, ChangeRank)

Server/Implementation/World/SceneServer/Party/
├── PartySystem.cs                       # Server-side party logic (ServerBehaviour)
├── PartySystemRuntimeData.cs            # Runtime state container (pending invites, fetch time)
├── PartySystemMainThreadQueueData.cs    # Main-thread action queue for async marshalling
└── (PartyCharacterMappingData)          # Tracks which party members are on this scene server
```

## Inheritance Hierarchies

### Controllers (NetworkBehaviour)

```
CharacterBehaviour
└── PartyController : IPartyController
```

### Server System

```
ServerBehaviour
└── PartySystem : IPartySystem<NetworkConnection>
```

### Enums

```
PartyRank : byte
├── None   = 0   # No party membership
├── Member = 1   # Standard party member
└── Leader = 2   # Party leader with elevated permissions
```

## Client Architecture

### IPartyController

The interface exposes per-character party state and UI events:

| Member                   | Type                                    | Description                                      |
|--------------------------|-----------------------------------------|--------------------------------------------------|
| `ID`                     | `long`                                  | The party ID (0 = not in a party)                |
| `Rank`                   | `PartyRank`                             | Character's rank within the party                |
| `OnPartyCreated`         | `Action<string>`                        | Fired when a party is successfully created       |
| `OnReceivePartyInvite`   | `Action<long>`                          | Fired when an invite is received (inviter ID)    |
| `OnAddPartyMember`       | `Action<long, PartyRank, float>`        | Fired when a member is added (ID, rank, HP%)     |
| `OnValidatePartyMembers` | `Action<HashSet<long>>`                 | Fired to validate the full member set            |
| `OnRemovePartyMember`    | `Action<long>`                          | Fired when a member is removed                   |
| `OnLeaveParty`           | `Action`                                | Fired when the local character leaves the party  |

### PartyController

Attached to each player character. On the client (`!UNITY_SERVER`), registers/unregisters FishNet broadcast listeners in `OnStartCharacter` / `OnStopCharacter`. Only the owning client processes broadcasts.

#### Broadcast Handlers

| Broadcast                      | Handler                                          | Action                                                       |
|--------------------------------|--------------------------------------------------|--------------------------------------------------------------|
| `PartyCreateBroadcast`         | `OnClientPartyCreateBroadcastReceived`            | Sets `ID` and `Rank = Leader`, invokes `OnPartyCreated`      |
| `PartyInviteBroadcast`         | `OnClientPartyInviteBroadcastReceived`            | Invokes `OnReceivePartyInvite` with inviter ID               |
| `PartyAddBroadcast`            | `OnClientPartyAddBroadcastReceived`               | Updates local `ID`/`Rank` if self, invokes `OnAddPartyMember`|
| `PartyAddMultipleBroadcast`    | `OnClientPartyAddMultipleBroadcastReceived`       | Validates member set, then processes each add individually   |
| `PartyLeaveBroadcast`          | `OnClientPartyLeaveBroadcastReceived`             | Resets `ID = 0`, `Rank = None`, invokes `OnLeaveParty`       |
| `PartyRemoveBroadcast`         | `OnClientPartyRemoveBroadcastReceived`            | Invokes `OnRemovePartyMember` with member ID                 |

## Server Architecture

### PartySystem

A `ServerBehaviour` (`CreateAssetMenu`) that manages all server-side party logic. Key design patterns:

- **Async worker pattern**: Database operations are fire-and-forget via `EnqueueAsyncWork()`.
- **Main-thread marshalling**: Results from async DB queries that require state changes or broadcasts are queued via `EnqueueMainThread()` and drained each frame in `OnLateUpdate()`.
- **Periodic polling**: `OnPeriodicUpdate()` fetches party updates from the database at `UpdatePumpRate` intervals for cross-server synchronization.
- **Optimistic concurrency**: Party membership uses version-based writes (`Version + 1`) to prevent conflicts.

### Configuration

| Field            | Type    | Default | Description                                    |
|------------------|---------|---------|------------------------------------------------|
| `MaxPartySize`   | `int`   | 6       | Maximum members allowed in a party             |
| `UpdatePumpRate` | `float` | 1.0     | Seconds between database polling cycles        |

### Party Lifecycle

#### Creation

1. Client sends `PartyCreateBroadcast`.
2. Server validates character is not already in a party.
3. Async: Creates party in DB, persists leader membership.
4. Main thread: Sets `ID` and `Rank = Leader`, broadcasts `PartyCreateBroadcast` to client.

#### Invitation

1. Leader sends `PartyInviteBroadcast` (or uses `/pi` / `/invite` chat commands).
2. Server validates: leader rank, party capacity (async DB check), target not already in party, no duplicate pending invite.
3. Stores pending invitation in `PendingInvitations` dictionary.
4. Sends `PartyInviteBroadcast` to target client.

#### Accept

1. Target sends `PartyAcceptInviteBroadcast`.
2. Server validates pending invitation exists and removes it.
3. Async: Checks party capacity, persists membership, notifies other servers via `PartyUpdateService`.
4. Main thread: Sets member `ID`/`Rank`, sends `PartyAddBroadcast` to joining client.

#### Decline

1. Target sends `PartyDeclineInviteBroadcast`.
2. Server removes the pending invitation.

#### Leave

1. Member sends `PartyLeaveBroadcast`.
2. Server immediately resets member's `ID`/`Rank` and sends leave broadcast.
3. Async: If leader left, randomly transfers leadership to a remaining member. Deletes leaving member from DB. If no members remain, deletes the party entirely.

#### Remove (Kick)

1. Leader sends `PartyRemoveBroadcast` with target member ID.
2. Server validates leader rank, target is in the same party, and leader is not kicking themselves.
3. Async: Fetches target member version, deletes from DB, notifies other servers.

#### Rank Change

1. Leader sends `PartyChangeRankBroadcast` with target member ID.
2. Server validates leader rank and target is different from self.
3. Async: Demotes current leader to member, promotes target to leader, notifies other servers.

### Cross-Server Synchronization

The `FetchAndProcessPartyUpdatesAsync()` method runs on `UpdatePumpRate`:

1. Collects all party IDs tracked on this scene server.
2. Fetches party updates from DB since `LastFetchTime`.
3. For each updated party, fetches current member list.
4. Marshals to main thread:
   - Computes member differences (removed members get `PartyLeaveBroadcast`).
   - Caches current member set in `PartyMemberTracker`.
   - Sends `PartyAddMultipleBroadcast` to all local party members with updated member list.

### Character Connect/Disconnect

| Event        | Action                                                                          |
|--------------|---------------------------------------------------------------------------------|
| `OnConnect`  | Adds to party tracker, persists member data with current health %               |
| `OnDisconnect` | Removes pending invitations, removes from party tracker, persists party update |

## Network Broadcasts

| Broadcast                        | Direction       | Purpose                                          |
|----------------------------------|-----------------|--------------------------------------------------|
| `PartyCreateBroadcast`           | Client → Server | Request to create a new party                    |
| `PartyCreateBroadcast`           | Server → Client | Confirmation of party creation (with ID)         |
| `PartyInviteBroadcast`           | Client → Server | Invite a target character to the party           |
| `PartyInviteBroadcast`           | Server → Client | Notify target of incoming invitation             |
| `PartyAcceptInviteBroadcast`     | Client → Server | Accept a pending party invitation                |
| `PartyDeclineInviteBroadcast`    | Client → Server | Decline a pending party invitation               |
| `PartyAddBroadcast`              | Server → Client | Notify client of a new party member              |
| `PartyAddMultipleBroadcast`      | Server → Client | Bulk member update (cross-server sync)           |
| `PartyLeaveBroadcast`            | Client → Server | Request to leave the party                       |
| `PartyLeaveBroadcast`            | Server → Client | Confirmation of party leave                      |
| `PartyRemoveBroadcast`           | Client → Server | Leader kicks a member                            |
| `PartyChangeRankBroadcast`       | Client → Server | Leader transfers leadership to another member    |

## Chat Commands

| Command     | Action                                               |
|-------------|------------------------------------------------------|
| `/pi`       | Invite a player to the party (by name)               |
| `/invite`   | Invite a player to the party (by name)               |

## External Integration Points

The Party system is consumed by many other systems:

- **Character System** — Connect/disconnect events trigger party tracker updates and DB persistence.
- **CharacterAttribute System** — Health percentage (`GetHealthResourceAttributeCurrentPercentage()`) is tracked per member for party UI.
- **Chat System** — Party invite chat commands (`/pi`, `/invite`) are registered via `ChatHelper`.
- **Faction System** — Party members share faction context for allied behavior.
- **UI System** — Events (`OnAddPartyMember`, `OnRemovePartyMember`, etc.) drive party frames, health bars, and invite dialogs.
- **Database Layer** — Persists/loads via `IPartyService`, `ICharacterPartyService`, `IPartyUpdateService` with `CharacterPartyData` and `PartyUpdateData` DTOs.
- **Scene System** — Party tracker maps which members are on this scene server for local broadcasts.
- **Periodic Update System** — Registers periodic callback for cross-server DB polling.