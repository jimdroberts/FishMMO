using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using FishMMO.Database.Npgsql;

namespace FishMMO.DiscordBot.Modules
{
	/// <summary>
	/// Commands for querying the game database. Restricted to server administrators.
	/// </summary>
	[RequireUserPermission(GuildPermission.Administrator)]
	public class DatabaseModule : ModuleBase<SocketCommandContext>
	{
		private readonly NpgsqlDbContext dbContext;
		private readonly ILogger<DatabaseModule> logger;

		/// <summary>
		/// Maximum allowed length for query parameters to prevent abuse.
		/// </summary>
		private const int MaxQueryLength = 64;

		/// <summary>
		/// Initializes a new instance of the <see cref="DatabaseModule"/> class.
		/// </summary>
		/// <param name="dbContext">The database context for this command scope.</param>
		/// <param name="logger">Logger instance.</param>
		public DatabaseModule(NpgsqlDbContext dbContext, ILogger<DatabaseModule> logger)
		{
			this.dbContext = dbContext;
			this.logger = logger;
		}

		/// <summary>
		/// Retrieves an account by name from the database. Requires Administrator permission.
		/// </summary>
		/// <param name="accountName">The account name to look up.</param>
		[Command("getaccount")]
		[Summary("Retrieves an account by its name from the database.")]
		public async Task GetAccountAsync([Remainder] string accountName)
		{
			if (string.IsNullOrWhiteSpace(accountName) || accountName.Length > MaxQueryLength)
			{
				await ReplyAsync($"Account name must be between 1 and {MaxQueryLength} characters.");
				return;
			}

			try
			{
				logger.LogInformation(
					"GetAccount command executed by {User} in {Guild} for account '{AccountName}'.",
					Context.User.Username, Context.Guild?.Name ?? "DM", accountName);

				var account = await dbContext.Accounts.AsQueryable()
					.FirstOrDefaultAsync(a => a.Name == accountName);

				if (account != null)
				{
					string accessLevel = account.AccessLevel switch
					{
						0 => "Banned",
						1 => "Player",
						2 => "GameMaster",
						3 => "Admin",
						_ => $"Unknown ({account.AccessLevel})"
					};

					var embed = new EmbedBuilder()
						.WithTitle($"Account: {account.Name}")
						.WithColor(account.AccessLevel == 0 ? Color.DarkRed : Color.Blue)
						.AddField("Access Level", accessLevel, true)
						.AddField("Email", account.Email ?? "—", true)
						.AddField("2FA Enabled", account.TotpEnabled ? "Yes" : "No", true)
						.AddField("Discord Link", string.IsNullOrEmpty(account.DiscordLinkCode) ? "—" : "Pending", true)
						.AddField("Created", account.TimeCreated.ToString("yyyy-MM-dd HH:mm"), true)
						.AddField("Last Login", account.LastLogin.ToString("yyyy-MM-dd HH:mm"), true)
						.WithTimestamp(DateTimeOffset.UtcNow)
						.Build();

					await ReplyAsync(embed: embed);
				}
				else
				{
					await ReplyAsync($"Account '{accountName}' not found.");
				}
			}
			catch (Exception ex)
			{
				logger.LogError(ex, "Database error during GetAccount for '{AccountName}'.", accountName);
				await ReplyAsync("An error occurred while fetching the account.");
			}
		}

		/// <summary>
		/// Retrieves a character by name from the database. Requires Administrator permission.
		/// </summary>
		/// <param name="characterName">The character name to look up.</param>
		[Command("getcharacter")]
		[Summary("Retrieves a character by its name from the database.")]
		public async Task GetCharacterAsync([Remainder] string characterName)
		{
			if (string.IsNullOrWhiteSpace(characterName) || characterName.Length > MaxQueryLength)
			{
				await ReplyAsync($"Character name must be between 1 and {MaxQueryLength} characters.");
				return;
			}

			try
			{
				logger.LogInformation(
					"GetCharacter command executed by {User} in {Guild} for character '{CharacterName}'.",
					Context.User.Username, Context.Guild?.Name ?? "DM", characterName);

				var character = await dbContext.Characters.AsQueryable()
					.FirstOrDefaultAsync(c => c.Name == characterName);

				if (character != null)
				{
					await ReplyAsync($"Character Found: Name: {character.Name}, ID: {character.ID}");
				}
				else
				{
					await ReplyAsync($"Character '{characterName}' not found.");
				}
			}
			catch (Exception ex)
			{
				logger.LogError(ex, "Database error during GetCharacter for '{CharacterName}'.", characterName);
				await ReplyAsync("An error occurred while fetching the character.");
			}
		}

		/// <summary>
		/// Lists all world servers currently in the database. Requires Administrator permission.
		/// </summary>
		[Command("worldservers")]
		[Summary("Lists all world servers from the database.")]
		public async Task ListWorldServersAsync()
		{
			try
			{
				logger.LogInformation(
					"WorldServers command executed by {User} in {Guild}.",
					Context.User.Username, Context.Guild?.Name ?? "DM");

				var servers = await dbContext.WorldServers.AsQueryable().ToListAsync();

				if (servers.Count == 0)
				{
					await ReplyAsync("No world servers found in the database.");
					return;
				}

				var sb = new StringBuilder();
				sb.AppendLine("**World Servers:**");
				foreach (var server in servers)
				{
					string locked = server.Locked ? " **[LOCKED]**" : "";
					string pulse = server.LastPulse != default
						? server.LastPulse.ToString("yyyy-MM-dd HH:mm:ss")
						: "—";
					sb.AppendLine(
						$"• **{server.Name}** (ID: {server.ID}) — {server.Address}:{server.Port} — Players: {server.CharacterCount} — Pulse: {pulse}{locked}");
				}

				string response = sb.ToString();
				if (response.Length > 1900)
				{
					response = response.Substring(0, 1900) + "\n... (truncated)";
				}

				await ReplyAsync(response, allowedMentions: AllowedMentions.None);
			}
			catch (Exception ex)
			{
				logger.LogError(ex, "Database error during WorldServers command.");
				await ReplyAsync("An error occurred while fetching world servers.");
			}
		}

		/// <summary>
		/// Lists all scene servers currently in the database. Requires Administrator permission.
		/// </summary>
		[Command("sceneservers")]
		[Summary("Lists all scene servers from the database.")]
		public async Task ListSceneServersAsync()
		{
			try
			{
				logger.LogInformation(
					"SceneServers command executed by {User} in {Guild}.",
					Context.User.Username, Context.Guild?.Name ?? "DM");

				var servers = await dbContext.SceneServers.AsQueryable().ToListAsync();

				if (servers.Count == 0)
				{
					await ReplyAsync("No scene servers found in the database.");
					return;
				}

				var sb = new StringBuilder();
				sb.AppendLine("**Scene Servers:**");
				foreach (var server in servers)
				{
					string locked = server.Locked ? " **[LOCKED]**" : "";
					string pulse = server.LastPulse != default
						? server.LastPulse.ToString("yyyy-MM-dd HH:mm:ss")
						: "—";
					sb.AppendLine(
						$"• **{server.Name}** (ID: {server.ID}) — {server.Address}:{server.Port} — Players: {server.CharacterCount} — Pulse: {pulse}{locked}");
				}

				string response = sb.ToString();
				if (response.Length > 1900)
				{
					response = response.Substring(0, 1900) + "\n... (truncated)";
				}

				await ReplyAsync(response, allowedMentions: AllowedMentions.None);
			}
			catch (Exception ex)
			{
				logger.LogError(ex, "Database error during SceneServers command.");
				await ReplyAsync("An error occurred while fetching scene servers.");
			}
		}

		/// <summary>
		/// Lists all active scenes currently in the database. Requires Administrator permission.
		/// </summary>
		[Command("scenes")]
		[Summary("Lists all active scenes from the database.")]
		public async Task ListScenesAsync()
		{
			try
			{
				logger.LogInformation(
					"Scenes command executed by {User} in {Guild}.",
					Context.User.Username, Context.Guild?.Name ?? "DM");

				var scenes = await dbContext.Scenes.AsQueryable()
					.OrderBy(s => s.WorldServerID)
					.ThenBy(s => s.SceneName)
					.Take(50)
					.ToListAsync();

				if (scenes.Count == 0)
				{
					await ReplyAsync("No active scenes found in the database.");
					return;
				}

				var sb = new StringBuilder();
				sb.AppendLine($"**Active Scenes ({scenes.Count}):**");
				foreach (var scene in scenes)
				{
					string sceneType = scene.SceneType switch
					{
						0 => "Public",
						1 => "Instance",
						2 => "Guild",
						_ => $"Type({scene.SceneType})"
					};
					string sceneStatus = scene.SceneStatus switch
					{
						0 => "Pending",
						1 => "Loading",
						2 => "Ready",
						3 => "Failed",
						_ => $"Status({scene.SceneStatus})"
					};
					sb.AppendLine(
						$"• **{scene.SceneName}** — {sceneType} [{sceneStatus}] — Players: {scene.CharacterCount} — World: {scene.WorldServerID} / Scene: {scene.SceneServerID}");
				}

				string response = sb.ToString();
				if (response.Length > 1900)
				{
					response = response.Substring(0, 1900) + "\n... (truncated)";
				}

				await ReplyAsync(response, allowedMentions: AllowedMentions.None);
			}
			catch (Exception ex)
			{
				logger.LogError(ex, "Database error during Scenes command.");
				await ReplyAsync("An error occurred while fetching scenes.");
			}
		}
	}
}