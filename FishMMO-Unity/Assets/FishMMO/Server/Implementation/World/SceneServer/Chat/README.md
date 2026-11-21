# Chat intake (core)

**Short description:** The scene server's core `ChatSystem`: every line a player sends arrives here, is rate-gated,
queued, sanitised and offered to the slash commands, and passes the stored mute and the flood mute before the Chat
module routes it to a channel. System messages are sent from here too.

Conversation (the channels, cross-server delivery and chat persistence) moved to the
[Chat module](../../../../../Modules/Chat/README.md) on 2026-11-20; its
[Server/README.md](../../../../../Modules/Chat/Server/README.md) documents the channels in detail.

## The pipeline

1. **Receive** (`OnServerChatBroadcastReceived`, network thread): stamps the server receipt time (the audit record),
   resolves the sender from the server's own connection map, charges the per-sender rate on the monotonic clock
   (`ChatRateGate`; refusals count towards the flood mute), and queues the line. The queue's cap drops, never kicks.
2. **Drain** (`DrainIncomingChatQueue`, main thread): up to `maxIncomingChatsPerFrame` lines per frame; a line whose
   connection has no resident character is dropped.
3. **Process** (`ProcessNewChatMessage`): truncates to `maxMessageLength`, sanitises (`ChatHelper.SanitizeIncoming`),
   drops empty lines and lines from characters not yet loaded, then:
   - **Slash commands** (`ChatHelper.TryParseCommand`): the registry the core (`/unstuck`, `/leaveinstance`, `/help`)
     and the modules (`/gm`, `/admin`, `/invite`, `/report`, `/closedungeon`, …) add to. Commands run before the mutes,
     so a muted player can still `/helpme` and `/report`.
   - **Stored mute** (`ChatState.MutedUntilTicks`, `ChatMutePolicy`): refuses chat with the time left.
   - **Flood mute** (`ChatSystem.FloodMute.cs`, `ChatFloodMute`): the rate limit escalating; in memory only.
   - **Hand-off**: what is left is conversation, passed to the Chat module's `IChatLineHandler`. Without the module it
     goes nowhere.

## Also here

- `ChatSystem.Help.cs`: `/help` lists the commands the caller's access level may run (`ChatCommandHelpListing`).
- `ChatSystem.Audit.cs`: every elevated command, run or refused, is written to the admin audit log.
- `OnSendSystemMessage(conn, text)`: one line on the System channel; the Staff and other modules answer through it.

## Configuration

| Field | Default | Meaning |
|---|---|---|
| `maxIncomingChatsPerFrame` | 500 | Lines drained from the incoming queue per frame |
| `maxIncomingQueueSize` | 10000 | Incoming queue cap; lines beyond it are dropped and counted |
| `messageRateLimit` | 500 ms | Minimum gap between a sender's lines |
| `chatTokenBucketCapacity` / `chatTokenRefillRate` | 5 / 1 per s | The per-sender token bucket |
| `maxMessageLength` | 128 | Characters kept of a line (clamped to `ChatBroadcast.MaxTextLength`) |
| `floodMuteRefusalThreshold` / `floodMuteWindowSeconds` / `floodMuteDurationSeconds` | 10 / 10 s / 180 s | The flood mute |

## Files

```
Chat/
├── ChatSystem.cs              # Intake, commands, mutes, system messages; the hand-off to IChatLineHandler
├── ChatSystem.Audit.cs        # The command audit
├── ChatSystem.FloodMute.cs    # The flood mute
├── ChatSystem.Help.cs         # /help
├── ChatSystemRuntimeData.cs   # The incoming queue
├── ChatRateGate.cs, ChatFloodMute.cs, ChatMutePolicy.cs, ChatCommandHelpListing.cs
└── README.md
```

Contracts: `Server/Core/World/SceneServer/Chat/` (`IChatSystem`, `IChatSystemRuntimeData`, `IChatLineHandler`).

## License

This project is subject to the FishMMO project license.
