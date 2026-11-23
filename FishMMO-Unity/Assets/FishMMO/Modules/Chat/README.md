# Chat module

**Short description:** Conversation between players: the say, region, world, trade, tell, party, guild and team
channels, delivery across scene servers, chat persistence, and the chat window with its channel picker.
[Server/README.md](Server/README.md) documents the channel system in detail.

| | |
|---|---|
| **Module id** | `chat` (`ChatModule`) |
| **Assemblies** | `FishMMO.Chat`, `.Server`, `.Client`, `.Tests`, `.RenderScratch` |
| **Depends on** | The core (the chat message, the slash-command registry, the chat intake, party and guild contracts) |
| **Depended on by** | None |
| **Remove together with** | Nothing |
| **Moved** | Out of the core (the core is shrunk to login, world and scene) |

## Folders

```
Assets/FishMMO/Modules/Chat/
├── Runtime/              FishMMO.Chat          ChatModule, IChatHelper (the channel handlers both sides implement)
├── Server/               FishMMO.Chat.Server   ChatChannelSystem (+ asset; ArenaChat, GroupChat, LocalChat, TellChat, WorldChat
│                                               partials): routing, the duplicate filter, the cross-server pump
│                                               (ChatPumpCursor), batched persistence and outbound world chat;
│                                               ChatChannelRuntimeData, ChatSystemMainThreadQueueData
├── Client/               FishMMO.Chat.Client   UITKChat (+ UIChat.prefab/uxml/uss), UITKChatChannelPicker (+ prefab),
│                                               GuildMessageOfTheDayGate
├── Editor/RenderScratch/ FishMMO.Chat.RenderScratch   The chat window in the panel renders; the chat render and input probes
├── Tests/                FishMMO.Chat.Tests    Pump, tell routing, line format, scroll, input focus, guild message of the day
└── README.md
```

## Core seams it uses

| Seam | What the module does |
|---|---|
| `IChatLineHandler` (server core) | `ChatChannelSystem` takes every line the core intake accepted: past the rate gate, the slash commands, the mute and the flood mute. |
| `ChatHelper.InitializeOnce` (shared core) | Registers the channel words ("/w", "/g", …) with the core command registry. |
| `SceneServer_Systems` label | `ChatChannelSystem.asset` is adopted by the scene server. |
| `IChatWindow` / `ChatWindow` (client core) | `UITKChat` registers as the window; the core and other modules write lines and start tells through `ChatWindow`. |
| `ClientWorldGUI_Panels` label | `UIChat` and `UIChatChannelPicker` prefabs are placed into the world GUI. |
| `[assembly: GameModule]` | `ChatModule` ("chat"). |
| `PartySignals` (server core) | `ChatChannelSystem` installs the poster (a signal is persisted as a party row) and hands every pumped party row that is a signal to its module instead of showing it. A party line a **player** sends that starts with the marker is dropped (issue #241: Call to Arms rides it). |

## What stays in the core

The message itself (`ChatBroadcast`, `ChatChannel`, `ChatSanitizer`, `ChatTellAddress`), the slash-command registry
(`ChatHelper`), and the server's chat intake `ChatSystem`: receiving every line, the rate gate (`ChatRateGate`), the
incoming queue, sanitising, slash commands with `/help` and the command audit, the stored mute (`ChatState`, set by the
Staff module) and the flood mute, and system messages (`OnSendSystemMessage`). The player's `ChatState` is the core
`MessagingModule`'s. On the client: `ChatWindow` (system lines become toasts without a window) and `CommandLine` (the
chat key opens a command input without a window).

## Removing it

Delete `Assets/FishMMO/Modules/Chat` (to check it, follow [the gate](../../README.md#removing-a-module-the-gate)).
Players can then not talk to each other, but every slash command still works (the chat key opens a command input), and
system messages, mutes and the command audit are unchanged; system lines show as toasts. The chat table and its rows
stay in place, unused.

## License

This project is subject to the FishMMO project license.
