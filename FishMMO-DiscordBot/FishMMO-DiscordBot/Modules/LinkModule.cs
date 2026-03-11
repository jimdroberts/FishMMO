using System;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using FishMMO.Database.Npgsql;
using FishMMO.DiscordBot.Services;

namespace FishMMO.DiscordBot.Modules
{
	/// <summary>
	/// Commands for linking a Discord account to a game character.
	/// The flow: user runs /link [character], gets a code, types it in-game,
	/// the bot detects the code in chat polling and confirms the link.
	/// </summary>
	public class LinkModule : ModuleBase<SocketCommandContext>
	{
		private readonly IServiceProvider serviceProvider;
		private readonly AccountLinkingService accountLinkingService;
		private readonly BotConfigurationService botConfigService;
		private readonly ILogger<LinkModule> logger;

		private const int MaxNameLength = 64;

		public LinkModule(
			IServiceProvider serviceProvider,
			AccountLinkingService accountLinkingService,
			BotConfigurationService botConfigService,
			ILogger<LinkModule> logger)
		{
			this.serviceProvider = serviceProvider;
			this.accountLinkingService = accountLinkingService;
			this.botConfigService = botConfigService;
			this.logger = logger;
		}

		/// <summary>
		/// Starts the account linking process. The user provides their character name
		/// and receives a verification code to type in-game chat.
		/// </summary>
		[Command("link")]
		[Summary("Links your Discord account to a game character. Usage: /link [character_name]")]
		public async Task LinkAsync([Remainder] string characterName)
		{
			if (string.IsNullOrWhiteSpace(characterName) || characterName.Length > MaxNameLength)
			{
				await ReplyAsync($"Character name must be between 1 and {MaxNameLength} characters.");
				return;
			}

			// Check if already linked
			var existing = accountLinkingService.GetLinkedAccount(Context.User.Id);
			if (existing != null)
			{
				await ReplyAsync(
					$"Your Discord account is already linked to **{existing.CharacterName}** (Account: {existing.GameAccountName}). " +
					$"Use `/unlink` to remove the link first.");
				return;
			}

			// Check if already has a pending verification
			if (accountLinkingService.HasPendingVerification(Context.User.Id))
			{
				await ReplyAsync("You already have a pending verification. Log in to your character and type the code in any chat channel.");
				return;
			}

			// Verify the character exists in the database
			try
			{
				using var scope = serviceProvider.CreateScope();
				var dbContext = scope.ServiceProvider.GetRequiredService<NpgsqlDbContext>();

				var character = await dbContext.Characters.FirstOrDefaultAsync(c => c.Name == characterName && !c.Deleted);
				if (character == null)
				{
					await ReplyAsync($"Character '{characterName}' not found in the game database.");
					return;
				}

				string? code = accountLinkingService.StartLinkRequest(Context.User.Id, characterName);
				if (code == null)
				{
					await ReplyAsync("Unable to start linking. Your account may already be linked.");
					return;
				}

				logger.LogInformation(
					"Link request started for Discord user {User} ({UserId}) with character '{CharacterName}'.",
					Context.User.Username, Context.User.Id, characterName);

				var embed = new EmbedBuilder()
					.WithTitle("Account Link — Verification Required")
					.WithColor(Color.Blue)
					.WithDescription(
						$"To link your Discord account to **{characterName}**, please do the following:\n\n" +
						$"1. Log in to the game as **{characterName}**\n" +
						$"2. Type the following code in any chat channel:\n\n" +
						$"```{code}```\n\n" +
						$"The code expires in **5 minutes**.")
					.WithTimestamp(DateTimeOffset.UtcNow)
					.Build();

				// Send as DM for security, fall back to channel reply
				try
				{
					var dmChannel = await Context.User.CreateDMChannelAsync();
					await dmChannel.SendMessageAsync(embed: embed);
					await ReplyAsync("Check your DMs for the verification code!");
				}
				catch
				{
					// DMs might be disabled
					await ReplyAsync(embed: embed);
				}
			}
			catch (Exception ex)
			{
				logger.LogError(ex, "Error during link command for '{CharacterName}'.", characterName);
				await ReplyAsync("An error occurred while starting the link process.");
			}
		}

		/// <summary>
		/// Removes the link between the user's Discord account and their game account.
		/// </summary>
		[Command("unlink")]
		[Summary("Removes the link between your Discord and game accounts.")]
		public async Task UnlinkAsync()
		{
			bool removed = accountLinkingService.Unlink(Context.User.Id);
			if (!removed)
			{
				await ReplyAsync("Your Discord account is not linked to any game account.");
				return;
			}

			await botConfigService.SavePersistentDataAsync();

			logger.LogInformation(
				"Discord user {User} ({UserId}) unlinked their game account.",
				Context.User.Username, Context.User.Id);

			await ReplyAsync("Your game account has been unlinked from Discord.");
		}
	}
}