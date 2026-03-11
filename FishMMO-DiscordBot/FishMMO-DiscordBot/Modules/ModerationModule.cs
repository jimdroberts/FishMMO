using System;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using FishMMO.Database.Npgsql;
using FishMMO.Database.Npgsql.Entities;
using FishMMO.DiscordBot.Services;

namespace FishMMO.DiscordBot.Modules
{
	/// <summary>
	/// Moderation commands for game server administration via Discord.
	/// Requires ManageGuild permission (moderator-level).
	/// </summary>
	[Group("mod")]
	[RequireUserPermission(GuildPermission.ManageGuild)]
	public class ModerationModule : ModuleBase<SocketCommandContext>
	{
		private readonly IServiceProvider serviceProvider;
		private readonly BridgeBanService bridgeBanService;
		private readonly BotConfigurationService botConfigService;
		private readonly ILogger<ModerationModule> logger;

		private const int MaxNameLength = 64;

		public ModerationModule(
			IServiceProvider serviceProvider,
			BridgeBanService bridgeBanService,
			BotConfigurationService botConfigService,
			ILogger<ModerationModule> logger)
		{
			this.serviceProvider = serviceProvider;
			this.bridgeBanService = bridgeBanService;
			this.botConfigService = botConfigService;
			this.logger = logger;
		}

		/// <summary>
		/// Kicks a player by inserting a kick request into the game database.
		/// The game server processes kick requests on its next tick.
		/// </summary>
		[Command("kick")]
		[Summary("Kicks a player from the game server by account name.")]
		public async Task KickAsync([Remainder] string accountName)
		{
			if (string.IsNullOrWhiteSpace(accountName) || accountName.Length > MaxNameLength)
			{
				await ReplyAsync($"Account name must be between 1 and {MaxNameLength} characters.");
				return;
			}

			try
			{
				using var scope = serviceProvider.CreateScope();
				var dbContext = scope.ServiceProvider.GetRequiredService<NpgsqlDbContext>();

				var account = await dbContext.Accounts.FirstOrDefaultAsync(a => a.Name == accountName);
				if (account == null)
				{
					await ReplyAsync($"Account '{accountName}' not found.");
					return;
				}

				var kickRequest = new KickRequestEntity
				{
					AccountName = accountName,
					TimeCreated = DateTime.UtcNow
				};

				await dbContext.KickRequests.AddAsync(kickRequest);
				await dbContext.SaveChangesAsync();

				logger.LogInformation(
					"Kick request created for account '{AccountName}' by moderator {User}.",
					accountName, Context.User.Username);

				await ReplyAsync($"Kick request submitted for account '{accountName}'. The game server will process it shortly.");
			}
			catch (Exception ex)
			{
				logger.LogError(ex, "Error creating kick request for '{AccountName}'.", accountName);
				await ReplyAsync("An error occurred while submitting the kick request.");
			}
		}

		/// <summary>
		/// Bans a game account by setting its AccessLevel to Banned (0).
		/// Also inserts a kick request to immediately disconnect the player.
		/// </summary>
		[Command("ban")]
		[Summary("Bans a game account (sets AccessLevel to Banned and kicks).")]
		public async Task BanAsync([Remainder] string accountName)
		{
			if (string.IsNullOrWhiteSpace(accountName) || accountName.Length > MaxNameLength)
			{
				await ReplyAsync($"Account name must be between 1 and {MaxNameLength} characters.");
				return;
			}

			try
			{
				using var scope = serviceProvider.CreateScope();
				var dbContext = scope.ServiceProvider.GetRequiredService<NpgsqlDbContext>();

				var account = await dbContext.Accounts.FirstOrDefaultAsync(a => a.Name == accountName);
				if (account == null)
				{
					await ReplyAsync($"Account '{accountName}' not found.");
					return;
				}

				if (account.AccessLevel == 0)
				{
					await ReplyAsync($"Account '{accountName}' is already banned.");
					return;
				}

				account.AccessLevel = 0; // Banned

				// Also kick them immediately
				var kickRequest = new KickRequestEntity
				{
					AccountName = accountName,
					TimeCreated = DateTime.UtcNow
				};
				await dbContext.KickRequests.AddAsync(kickRequest);
				await dbContext.SaveChangesAsync();

				logger.LogInformation(
					"Account '{AccountName}' banned by moderator {User}.",
					accountName, Context.User.Username);

				await ReplyAsync($"Account '{accountName}' has been **banned** and a kick request has been submitted.");
			}
			catch (Exception ex)
			{
				logger.LogError(ex, "Error banning account '{AccountName}'.", accountName);
				await ReplyAsync("An error occurred while banning the account.");
			}
		}

		/// <summary>
		/// Unbans a game account by setting its AccessLevel back to Player (1).
		/// </summary>
		[Command("unban")]
		[Summary("Unbans a game account (restores AccessLevel to Player).")]
		public async Task UnbanAsync([Remainder] string accountName)
		{
			if (string.IsNullOrWhiteSpace(accountName) || accountName.Length > MaxNameLength)
			{
				await ReplyAsync($"Account name must be between 1 and {MaxNameLength} characters.");
				return;
			}

			try
			{
				using var scope = serviceProvider.CreateScope();
				var dbContext = scope.ServiceProvider.GetRequiredService<NpgsqlDbContext>();

				var account = await dbContext.Accounts.FirstOrDefaultAsync(a => a.Name == accountName);
				if (account == null)
				{
					await ReplyAsync($"Account '{accountName}' not found.");
					return;
				}

				if (account.AccessLevel != 0)
				{
					await ReplyAsync($"Account '{accountName}' is not currently banned (AccessLevel: {account.AccessLevel}).");
					return;
				}

				account.AccessLevel = 1; // Player
				await dbContext.SaveChangesAsync();

				logger.LogInformation(
					"Account '{AccountName}' unbanned by moderator {User}.",
					accountName, Context.User.Username);

				await ReplyAsync($"Account '{accountName}' has been **unbanned** (AccessLevel restored to Player).");
			}
			catch (Exception ex)
			{
				logger.LogError(ex, "Error unbanning account '{AccountName}'.", accountName);
				await ReplyAsync("An error occurred while unbanning the account.");
			}
		}

		/// <summary>
		/// Bans a character or account from the Discord chat bridge.
		/// Messages from bridge-banned names are not forwarded in either direction.
		/// </summary>
		[Command("ban-bridge")]
		[Summary("Bans a character/account from the Discord-game chat bridge.")]
		public async Task BanBridgeAsync(string name, [Remainder] string reason = "No reason provided")
		{
			if (string.IsNullOrWhiteSpace(name) || name.Length > MaxNameLength)
			{
				await ReplyAsync($"Name must be between 1 and {MaxNameLength} characters.");
				return;
			}

			bool added = bridgeBanService.AddBridgeBan(name, isAccountBan: false, Context.User.Username, reason);
			if (!added)
			{
				await ReplyAsync($"'{name}' is already bridge-banned.");
				return;
			}

			await botConfigService.SavePersistentDataAsync();

			logger.LogInformation(
				"Bridge ban added for '{Name}' by {User}. Reason: {Reason}",
				name, Context.User.Username, reason);

			await ReplyAsync($"'{name}' has been **bridge-banned**. Their messages will no longer be forwarded.");
		}

		/// <summary>
		/// Removes a bridge ban from a character or account.
		/// </summary>
		[Command("unban-bridge")]
		[Summary("Removes a bridge ban from a character/account.")]
		public async Task UnbanBridgeAsync([Remainder] string name)
		{
			if (string.IsNullOrWhiteSpace(name) || name.Length > MaxNameLength)
			{
				await ReplyAsync($"Name must be between 1 and {MaxNameLength} characters.");
				return;
			}

			bool removed = bridgeBanService.RemoveBridgeBan(name);
			if (!removed)
			{
				await ReplyAsync($"'{name}' is not currently bridge-banned.");
				return;
			}

			await botConfigService.SavePersistentDataAsync();

			logger.LogInformation(
				"Bridge ban removed for '{Name}' by {User}.",
				name, Context.User.Username);

			await ReplyAsync($"Bridge ban removed for '{name}'.");
		}

		/// <summary>
		/// Lists all current bridge bans.
		/// </summary>
		[Command("bridge-bans")]
		[Summary("Lists all current bridge bans.")]
		public async Task ListBridgeBansAsync()
		{
			var bans = bridgeBanService.GetAllBans();

			if (bans.Count == 0)
			{
				await ReplyAsync("No active bridge bans.");
				return;
			}

			var sb = new StringBuilder();
			sb.AppendLine("**Active Bridge Bans:**");
			foreach (var ban in bans)
			{
				string type = ban.IsAccountBan ? "Account" : "Character";
				sb.AppendLine($"• **{ban.Name}** ({type}) — by {ban.BannedBy} on {ban.CreatedAtUtc:yyyy-MM-dd} — {ban.Reason}");
			}

			string response = sb.ToString();
			if (response.Length > 1900)
			{
				response = response.Substring(0, 1900) + "\n... (truncated)";
			}

			await ReplyAsync(response, allowedMentions: AllowedMentions.None);
		}
	}
}