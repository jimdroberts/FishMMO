using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using FishMMO.DiscordBot.Data;
using FishMMO.DiscordBot.Services;
using Microsoft.Extensions.Logging;

namespace FishMMO.DiscordBot.Modules
{
	/// <summary>
	/// Administrative commands for managing the bot and viewing game server status.
	/// All commands in this group require the Administrator permission.
	/// </summary>
	[Group("admin")]
	[RequireUserPermission(GuildPermission.Administrator)]
	public class AdminModule : ModuleBase<SocketCommandContext>
	{
		private readonly DynamicChannelManagerService dynamicChannelManager;
		private readonly BotConfigurationService botConfigService;
		private readonly CommandService commandService;
		private readonly ILogger<AdminModule> logger;

		public AdminModule(
			DynamicChannelManagerService dynamicChannelManager,
			BotConfigurationService botConfigService,
			CommandService commandService,
			ILogger<AdminModule> logger)
		{
			this.dynamicChannelManager = dynamicChannelManager;
			this.botConfigService = botConfigService;
			this.commandService = commandService;
			this.logger = logger;
		}

		/// <summary>
		/// Lists all dynamically managed game chat channels in this guild.
		/// </summary>
		[Command("channels")]
		[Summary("Lists all managed game chat channels in this server.")]
		public async Task ListChannelsAsync()
		{
			logger.LogInformation(
				"Admin command 'channels' executed by {User} in {Guild}.",
				Context.User.Username, Context.Guild.Name);

			List<(long WorldId, long SceneId, DynamicGameChatChannelState State)> channels =
				dynamicChannelManager.GetManagedChannelsForGuild(Context.Guild.Id);

			if (channels.Count == 0)
			{
				await ReplyAsync("No managed game chat channels in this server.");
				return;
			}

			var sb = new StringBuilder();
			sb.AppendLine("**Managed Game Chat Channels:**");
			foreach (var (worldId, sceneId, state) in channels)
			{
				string lastActivity = state.LastActivity.ToString("yyyy-MM-dd HH:mm:ss UTC");
				sb.AppendLine(
					$"• World: {state.WorldServerName} (ID: {worldId}) | Scene: {state.SceneServerName} (ID: {sceneId}) | Channel: <#{state.DiscordChannelId}> | Last: {lastActivity}");
			}

			string response = sb.ToString();
			if (response.Length > 1900)
			{
				response = response.Substring(0, 1900) + "\n... (truncated)";
			}

			await ReplyAsync(response, allowedMentions: AllowedMentions.None);
		}

		/// <summary>
		/// Forces an immediate cleanup of stale game chat channels.
		/// </summary>
		[Command("cleanup")]
		[Summary("Forces immediate cleanup of stale game chat channels.")]
		public async Task ForceCleanupAsync()
		{
			logger.LogInformation(
				"Admin command 'cleanup' executed by {User} in {Guild}.",
				Context.User.Username, Context.Guild.Name);

			await ReplyAsync("Starting forced channel cleanup...");
			int removed = await dynamicChannelManager.ForceCleanupAsync();
			await ReplyAsync($"Cleanup complete. {removed} stale channel(s) removed.");
		}

		/// <summary>
		/// Displays bot status information including uptime, latency, and managed channel count.
		/// </summary>
		[Command("status")]
		[Summary("Shows bot status and statistics.")]
		public async Task StatusAsync()
		{
			logger.LogInformation(
				"Admin command 'status' executed by {User} in {Guild}.",
				Context.User.Username, Context.Guild.Name);

			var client = Context.Client;
			var uptime = DateTime.UtcNow - System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime();
			int guildCount = client.Guilds.Count;
			int channelCount = dynamicChannelManager.TotalManagedChannelCount;

			var embed = new EmbedBuilder()
				.WithTitle("Bot Status")
				.WithColor(Color.Green)
				.AddField("Uptime", $"{uptime.Days}d {uptime.Hours}h {uptime.Minutes}m {uptime.Seconds}s", true)
				.AddField("Guilds", guildCount.ToString(), true)
				.AddField("Managed Channels", channelCount.ToString(), true)
				.AddField("Latency", $"{client.Latency}ms", true)
				.WithTimestamp(DateTimeOffset.UtcNow)
				.Build();

			await ReplyAsync(embed: embed);
		}

		/// <summary>
		/// Disables a command in this guild. Users will be blocked from executing it.
		/// Use the full command key, e.g. "mod kick", "general echo", "link".
		/// </summary>
		[Command("disable-cmd")]
		[Summary("Disables a command in this server. Usage: /admin disable-cmd [command key]")]
		public async Task DisableCommandAsync([Remainder] string commandKey)
		{
			commandKey = commandKey.Trim().ToLowerInvariant();

			if (!IsValidCommandKey(commandKey))
			{
				await ReplyAsync($"Unknown command key `{commandKey}`. Use `/admin list-cmds` to see all command keys.");
				return;
			}

			var config = botConfigService.GetOrCreateCommandPermissionConfig(Context.Guild.Id);

			if (!config.DisabledCommands.Add(commandKey))
			{
				await ReplyAsync($"Command `{commandKey}` is already disabled.");
				return;
			}

			await botConfigService.SavePersistentDataAsync();

			logger.LogInformation(
				"Command '{CommandKey}' disabled in guild {Guild} by {User}.",
				commandKey, Context.Guild.Name, Context.User.Username);

			await ReplyAsync($"Command `{commandKey}` has been **disabled** in this server.");
		}

		/// <summary>
		/// Re-enables a previously disabled command in this guild.
		/// </summary>
		[Command("enable-cmd")]
		[Summary("Re-enables a previously disabled command. Usage: /admin enable-cmd [command key]")]
		public async Task EnableCommandAsync([Remainder] string commandKey)
		{
			commandKey = commandKey.Trim().ToLowerInvariant();

			var config = botConfigService.GetCommandPermissionConfig(Context.Guild.Id);
			if (config == null || !config.DisabledCommands.Remove(commandKey))
			{
				await ReplyAsync($"Command `{commandKey}` is not disabled.");
				return;
			}

			await botConfigService.SavePersistentDataAsync();

			logger.LogInformation(
				"Command '{CommandKey}' re-enabled in guild {Guild} by {User}.",
				commandKey, Context.Guild.Name, Context.User.Username);

			await ReplyAsync($"Command `{commandKey}` has been **re-enabled** in this server.");
		}

		/// <summary>
		/// Requires a specific Discord role to use a command.
		/// </summary>
		[Command("require-role")]
		[Summary("Requires a role to use a command. Usage: /admin require-role [command key] [role mention or ID]")]
		public async Task RequireRoleAsync(string commandKey, IRole role)
		{
			commandKey = commandKey.Trim().ToLowerInvariant();

			if (!IsValidCommandKey(commandKey))
			{
				await ReplyAsync($"Unknown command key `{commandKey}`. Use `/admin list-cmds` to see all command keys.");
				return;
			}

			var config = botConfigService.GetOrCreateCommandPermissionConfig(Context.Guild.Id);
			config.RoleRequirements[commandKey] = role.Id;

			await botConfigService.SavePersistentDataAsync();

			logger.LogInformation(
				"Role requirement set: command '{CommandKey}' requires role '{RoleName}' ({RoleId}) in guild {Guild}.",
				commandKey, role.Name, role.Id, Context.Guild.Name);

			await ReplyAsync($"Command `{commandKey}` now requires role **{role.Name}**.");
		}

		/// <summary>
		/// Removes the role requirement from a command.
		/// </summary>
		[Command("unrequire-role")]
		[Summary("Removes a role requirement from a command. Usage: /admin unrequire-role [command key]")]
		public async Task UnrequireRoleAsync([Remainder] string commandKey)
		{
			commandKey = commandKey.Trim().ToLowerInvariant();

			var config = botConfigService.GetCommandPermissionConfig(Context.Guild.Id);
			if (config == null || !config.RoleRequirements.Remove(commandKey))
			{
				await ReplyAsync($"Command `{commandKey}` has no role requirement.");
				return;
			}

			await botConfigService.SavePersistentDataAsync();

			logger.LogInformation(
				"Role requirement removed from command '{CommandKey}' in guild {Guild} by {User}.",
				commandKey, Context.Guild.Name, Context.User.Username);

			await ReplyAsync($"Role requirement removed from command `{commandKey}`.");
		}

		/// <summary>
		/// Shows the current command permission configuration for this guild.
		/// </summary>
		[Command("cmd-config")]
		[Summary("Shows command permission settings for this server.")]
		public async Task ShowCommandConfigAsync()
		{
			var config = botConfigService.GetCommandPermissionConfig(Context.Guild.Id);

			if (config == null ||
				(config.DisabledCommands.Count == 0 && config.RoleRequirements.Count == 0))
			{
				await ReplyAsync("No command permission overrides configured for this server. All commands use default permissions.");
				return;
			}

			var sb = new StringBuilder();
			sb.AppendLine("**Command Permission Configuration:**\n");

			if (config.DisabledCommands.Count > 0)
			{
				sb.AppendLine("__Disabled Commands:__");
				foreach (string cmd in config.DisabledCommands)
				{
					sb.AppendLine($"• `{cmd}`");
				}
				sb.AppendLine();
			}

			if (config.RoleRequirements.Count > 0)
			{
				sb.AppendLine("__Role Requirements:__");
				foreach (var kvp in config.RoleRequirements)
				{
					var role = Context.Guild.GetRole(kvp.Value);
					string roleName = role != null ? role.Name : kvp.Value.ToString();
					sb.AppendLine($"• `{kvp.Key}` → **{roleName}**");
				}
				sb.AppendLine();
			}

			string response = sb.ToString();
			if (response.Length > 1900)
			{
				response = response.Substring(0, 1900) + "\n... (truncated)";
			}

			await ReplyAsync(response, allowedMentions: AllowedMentions.None);
		}

		/// <summary>
		/// Lists all registered command keys for use with disable-cmd and require-role.
		/// </summary>
		[Command("list-cmds")]
		[Summary("Lists all registered command keys for configuration purposes.")]
		public async Task ListCommandKeysAsync()
		{
			var sb = new StringBuilder();
			sb.AppendLine("**All Command Keys:**\n");

			foreach (var module in commandService.Modules)
			{
				string groupPrefix = string.IsNullOrEmpty(module.Group) ? "" : $"{module.Group} ";

				foreach (var cmd in module.Commands)
				{
					string key = CommandListModule.BuildCommandKey(cmd);
					sb.AppendLine($"• `{key}` — /{groupPrefix}{cmd.Name}");
				}
			}

			string response = sb.ToString();
			if (response.Length > 1900)
			{
				response = response.Substring(0, 1900) + "\n... (truncated)";
			}

			await ReplyAsync(response, allowedMentions: AllowedMentions.None);
		}

		/// <summary>
		/// Validates that a command key corresponds to a real registered command.
		/// </summary>
		private bool IsValidCommandKey(string cmdKey)
		{
			foreach (var module in commandService.Modules)
			{
				foreach (var cmd in module.Commands)
				{
					if (CommandListModule.BuildCommandKey(cmd) == cmdKey)
					{
						return true;
					}
				}
			}
			return false;
		}
	}
}