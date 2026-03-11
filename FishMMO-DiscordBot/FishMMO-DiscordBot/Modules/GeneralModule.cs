using Discord;
using Discord.Commands;
using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using FishMMO.DiscordBot.Services;

namespace FishMMO.DiscordBot.Modules
{
	/// <summary>
	/// General-purpose utility commands available to all users.
	/// </summary>
	[Group("general")]
	public class GeneralModule : ModuleBase<SocketCommandContext>
	{
		private readonly CommandService commandService;
		private readonly DynamicChannelManagerService dynamicChannelManager;
		private readonly BotConfigurationService botConfigService;

		/// <summary>
		/// Initializes a new instance of the <see cref="GeneralModule"/> class.
		/// </summary>
		public GeneralModule(
			CommandService commandService,
			DynamicChannelManagerService dynamicChannelManager,
			BotConfigurationService botConfigService)
		{
			this.commandService = commandService;
			this.dynamicChannelManager = dynamicChannelManager;
			this.botConfigService = botConfigService;
		}

		/// <summary>
		/// Responds with Pong to verify the bot is alive.
		/// </summary>
		[Command("ping")]
		[Summary("Responds with 'Pong!' and the current gateway latency.")]
		public async Task PingAsync()
		{
			await ReplyAsync($"Pong! Latency: {Context.Client.Latency}ms");
		}

		/// <summary>
		/// Echoes the provided text back to the channel with mentions suppressed.
		/// </summary>
		/// <param name="text">The text to echo.</param>
		[Command("echo")]
		[Summary("Echoes the provided text.")]
		public async Task EchoAsync([Remainder] string text)
		{
			await ReplyAsync(text, allowedMentions: AllowedMentions.None);
		}

		/// <summary>
		/// Displays bot uptime, latency, and connected guild count.
		/// </summary>
		[Command("status")]
		[Summary("Displays bot uptime, latency, and connected guild count.")]
		public async Task StatusAsync()
		{
			var uptime = DateTime.UtcNow - System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime();

			var embed = new EmbedBuilder()
				.WithTitle("Bot Status")
				.WithColor(Color.Blue)
				.AddField("Uptime", $"{uptime.Days}d {uptime.Hours}h {uptime.Minutes}m {uptime.Seconds}s", true)
				.AddField("Latency", $"{Context.Client.Latency}ms", true)
				.AddField("Guilds", Context.Client.Guilds.Count.ToString(), true)
				.WithTimestamp(DateTimeOffset.UtcNow)
				.Build();

			await ReplyAsync(embed: embed);
		}

		/// <summary>
		/// Lists all available commands with their summaries.
		/// </summary>
		[Command("help")]
		[Summary("Lists all available commands.")]
		public async Task HelpAsync()
		{
			var sb = new StringBuilder();
			sb.AppendLine("**Available Commands:**");

			foreach (var module in commandService.Modules)
			{
				string groupPrefix = string.IsNullOrEmpty(module.Group) ? "" : $"{module.Group} ";

				foreach (var cmd in module.Commands)
				{
					string summary = string.IsNullOrEmpty(cmd.Summary) ? "No description" : cmd.Summary;
					sb.AppendLine($"• `/{groupPrefix}{cmd.Name}` — {summary}");
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
		/// Mutes a game chat zone channel so the user no longer sees messages from it.
		/// Uses Discord permission overwrites to hide the channel from the user.
		/// </summary>
		[Command("mute-zone")]
		[Summary("Hides a game chat zone channel from your view. Run in the channel you want to mute.")]
		public async Task MuteZoneAsync()
		{
			if (Context.Channel is not SocketTextChannel textChannel || textChannel.Guild == null)
			{
				await ReplyAsync("This command must be used in a server text channel.");
				return;
			}

			if (!dynamicChannelManager.IsOurDynamicChannel(textChannel.Guild.Id, textChannel.Id))
			{
				await ReplyAsync("This channel is not a managed game chat zone. Use this command in a game chat channel.");
				return;
			}

			// Store mute in persistent data
			var persistentData = botConfigService.GetPersistentData();
			if (!persistentData.MutedZones.TryGetValue(Context.User.Id, out var mutedSet))
			{
				mutedSet = new HashSet<ulong>();
				persistentData.MutedZones[Context.User.Id] = mutedSet;
			}

			if (!mutedSet.Add(textChannel.Id))
			{
				await ReplyAsync("You have already muted this zone.");
				return;
			}

			// Apply Discord permission overwrite to hide the channel from this user
			try
			{
				await textChannel.AddPermissionOverwriteAsync(
					Context.User,
					new OverwritePermissions(viewChannel: PermValue.Deny));

				await botConfigService.SavePersistentDataAsync();
				await ReplyAsync($"Zone **{textChannel.Name}** has been muted. Use `/general unmute-zone` in another channel to unmute.");
			}
			catch (Exception)
			{
				mutedSet.Remove(textChannel.Id);
				await ReplyAsync("Failed to mute the zone. The bot may not have permission to manage channel permissions.");
			}
		}

		/// <summary>
		/// Unmutes a previously muted game chat zone channel.
		/// </summary>
		[Command("unmute-zone")]
		[Summary("Unhides a previously muted zone channel. Usage: /general unmute-zone [channel-mention or ID]")]
		public async Task UnmuteZoneAsync(ulong channelId)
		{
			var persistentData = botConfigService.GetPersistentData();
			if (!persistentData.MutedZones.TryGetValue(Context.User.Id, out var mutedSet) || !mutedSet.Contains(channelId))
			{
				await ReplyAsync("That channel is not in your muted list.");
				return;
			}

			try
			{
				var guild = Context.Guild;
				if (guild != null)
				{
					var channel = guild.GetTextChannel(channelId);
					if (channel != null)
					{
						await channel.RemovePermissionOverwriteAsync(Context.User);
					}
				}

				mutedSet.Remove(channelId);
				if (mutedSet.Count == 0)
				{
					persistentData.MutedZones.Remove(Context.User.Id);
				}

				await botConfigService.SavePersistentDataAsync();
				await ReplyAsync("Zone unmuted successfully.");
			}
			catch (Exception)
			{
				await ReplyAsync("Failed to unmute the zone. The bot may not have permission to manage channel permissions.");
			}
		}

		/// <summary>
		/// Lists all muted zone channels for the calling user.
		/// </summary>
		[Command("my-mutes")]
		[Summary("Lists your muted game chat zone channels.")]
		public async Task ListMutesAsync()
		{
			var persistentData = botConfigService.GetPersistentData();
			if (!persistentData.MutedZones.TryGetValue(Context.User.Id, out var mutedSet) || mutedSet.Count == 0)
			{
				await ReplyAsync("You have no muted zones.");
				return;
			}

			var sb = new StringBuilder();
			sb.AppendLine("**Your Muted Zones:**");
			foreach (ulong channelId in mutedSet)
			{
				sb.AppendLine($"• <#{channelId}> (ID: {channelId})");
			}

			await ReplyAsync(sb.ToString(), allowedMentions: AllowedMentions.None);
		}
	}
}