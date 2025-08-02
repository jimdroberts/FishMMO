# FishMMO Discord Bot

FishMMO Discord Bot is a C# application designed to integrate FishMMO game chat and features with Discord servers. It enables real-time communication between in-game chat channels and Discord channels, provides dynamic channel management, and supports various administrative and utility commands for server moderation and community engagement.

## Features
- **Game Chat Integration:** Relays messages between FishMMO game chat and Discord channels.
- **Dynamic Channel Management:** Automatically creates, manages, and removes Discord channels based on in-game activity.
- **Command Modules:** Includes modules for chat, database access, and general server commands.
- **Configuration via appsettings.json:** Flexible configuration for bot behavior, channel mappings, and permissions.
- **Extensible Services:** Built with dependency injection and modular services for easy extension.

## Getting Started

### Prerequisites
- [.NET 8.0 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)
- Access to a FishMMO game server (for full integration)
- Discord bot token and permissions

### Building and Running
1. Clone the repository and open the solution in Visual Studio or your preferred IDE.
2. Restore NuGet packages.
3. Configure `appsettings.json` (see below).
4. Build and run the project:
   ```powershell
   dotnet build
   dotnet run --project FishMMO-DiscordBot/FishMMO-DiscordBot.csproj
   ```

## Configuration

The bot uses an `appsettings.json` file for configuration. This file should be placed in the root of the `FishMMO-DiscordBot` project and will be copied to the output directory automatically.

### Example `appsettings.json`
```json
{
  "Discord": {
    "Token": "YOUR_DISCORD_BOT_TOKEN",
    "Prefix": "!"
  },
  "FishMMO": {
    "ApiUrl": "http://localhost:5000/api/",
    "ApiKey": "YOUR_FISHMMO_API_KEY"
  },
  "ChannelMappings": {
    "GameChat": "discord-channel-id",
    "AdminChat": "discord-admin-channel-id"
  },
  "DynamicChannels": {
    "Enabled": true,
    "CategoryId": "discord-category-id"
  }
}
```

### Configuration Options
- **Discord.Token**: Your Discord bot token (required).
- **Discord.Prefix**: Command prefix for bot commands (default: `!`).
- **FishMMO.ApiUrl**: URL to the FishMMO API endpoint.
- **FishMMO.ApiKey**: API key for authenticating with FishMMO server.
- **ChannelMappings**: Map in-game chat channels to Discord channel IDs.
- **DynamicChannels.Enabled**: Enable or disable dynamic channel creation.
- **DynamicChannels.CategoryId**: Discord category ID for dynamic channels.

## Project Structure
- `Program.cs`: Main entry point and host configuration.
- `Services/`: Core services for bot operation and integration.
- `Modules/`: Command modules for Discord bot features.
- `Data/`: Data models and configuration classes.
- `appsettings.json`: Main configuration file.