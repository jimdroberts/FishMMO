using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
	/// Commands for inspecting game characters and looking up linked accounts.
	/// </summary>
	[Group("char")]
	public class CharacterModule : ModuleBase<SocketCommandContext>
	{
		private readonly IServiceProvider serviceProvider;
		private readonly AccountLinkingService accountLinkingService;
		private readonly ILogger<CharacterModule> logger;

		private const int MaxNameLength = 64;

		public CharacterModule(
			IServiceProvider serviceProvider,
			AccountLinkingService accountLinkingService,
			ILogger<CharacterModule> logger)
		{
			this.serviceProvider = serviceProvider;
			this.accountLinkingService = accountLinkingService;
			this.logger = logger;
		}

		/// <summary>
		/// Looks up which game account/character is linked to a Discord user.
		/// Admins can look up any user; regular users can only check themselves.
		/// </summary>
		[Command("whois")]
		[Summary("Shows the game account linked to a Discord user.")]
		public async Task WhoisAsync(IUser? targetUser = null)
		{
			var user = targetUser ?? Context.User;

			// Non-admins can only look up themselves
			if (targetUser != null && targetUser.Id != Context.User.Id)
			{
				var guildUser = Context.User as IGuildUser;
				if (guildUser == null || !guildUser.GuildPermissions.ManageGuild)
				{
					await ReplyAsync("You can only look up your own linked account.");
					return;
				}
			}

			var linked = accountLinkingService.GetLinkedAccount(user.Id);
			if (linked == null)
			{
				string target = user.Id == Context.User.Id ? "You don't" : $"{user.Username} doesn't";
				await ReplyAsync($"{target} have a linked game account.");
				return;
			}

			var embed = new EmbedBuilder()
				.WithTitle("Linked Account")
				.WithColor(Color.Blue)
				.AddField("Discord User", $"{user.Username} ({user.Id})", true)
				.AddField("Game Account", linked.GameAccountName, true)
				.AddField("Character", linked.CharacterName, true)
				.AddField("Linked Since", linked.LinkedAtUtc.ToString("yyyy-MM-dd HH:mm:ss UTC"), true)
				.WithTimestamp(DateTimeOffset.UtcNow)
				.Build();

			await ReplyAsync(embed: embed);
		}

		/// <summary>
		/// Loads an entire character from the database and displays a detailed embed
		/// with stats, equipment, inventory, skills, guild, quests, achievements, and more.
		/// Requires ManageGuild permission.
		/// </summary>
		[Command("inspect")]
		[RequireUserPermission(GuildPermission.ManageGuild)]
		[Summary("Loads a full character profile from the database (admin).")]
		public async Task InspectAsync([Remainder] string characterName)
		{
			if (string.IsNullOrWhiteSpace(characterName) || characterName.Length > MaxNameLength)
			{
				await ReplyAsync($"Character name must be between 1 and {MaxNameLength} characters.");
				return;
			}

			try
			{
				logger.LogInformation(
					"Inspect command for '{CharacterName}' by {User}.",
					characterName, Context.User.Username);

				using var scope = serviceProvider.CreateScope();
				var dbContext = scope.ServiceProvider.GetRequiredService<NpgsqlDbContext>();

				var character = await dbContext.Characters
					.Include(c => c.Attributes)
					.Include(c => c.Equipment)
					.Include(c => c.Inventory)
					.Include(c => c.Bank)
					.Include(c => c.Skills)
					.Include(c => c.Buffs)
					.Include(c => c.Achievements)
					.Include(c => c.Quests)
					.Include(c => c.Guild)
						.ThenInclude(g => g != null ? g.Guild : null)
					.Include(c => c.Party)
						.ThenInclude(p => p != null ? p.Party : null)
					.Include(c => c.Friends)
					.Include(c => c.Faction)
					.Include(c => c.Abilities)
					.Include(c => c.KnownAbilities)
					.Include(c => c.Archetypes)
					.Include(c => c.Pet)
					.FirstOrDefaultAsync(c => c.Name == characterName);

				if (character == null)
				{
					await ReplyAsync($"Character '{characterName}' not found.");
					return;
				}

				// Build the main embed
				string sessionState = character.SessionState.ToString();
				string accessLevel = GetAccessLevelName(character.AccessLevel);

				var mainEmbed = new EmbedBuilder()
					.WithTitle($"Character: {character.Name}")
					.WithColor(character.Deleted ? Color.DarkRed : Color.Gold)
					.AddField("Account", character.Account ?? "—", true)
					.AddField("ID", character.ID.ToString(), true)
					.AddField("Race ID", character.RaceID.ToString(), true)
					.AddField("Access Level", accessLevel, true)
					.AddField("Status", sessionState, true)
					.AddField("Created", character.TimeCreated.ToString("yyyy-MM-dd HH:mm"), true)
					.AddField("Last Saved", character.LastSaved.ToString("yyyy-MM-dd HH:mm"), true)
					.AddField("Scene", character.SceneName ?? "—", true)
					.AddField("Position", $"X:{character.X:F1} Y:{character.Y:F1} Z:{character.Z:F1}", true);

				// Session ownership info (useful for admin debugging)
				if (character.SessionOwnerServerId != 0)
				{
					mainEmbed.AddField("Session Owner", $"Server {character.SessionOwnerServerId}", true);
					mainEmbed.AddField("Lease Expires", character.SessionLeaseExpiresUtc.ToString("yyyy-MM-dd HH:mm:ss UTC"), true);
				}

				if (character.Deleted)
				{
					mainEmbed.AddField("DELETED", character.TimeDeleted?.ToString("yyyy-MM-dd HH:mm") ?? "Yes", true);
				}

				// Guild info
				if (character.Guild != null && character.Guild.Guild != null)
				{
					mainEmbed.AddField("Guild", $"{character.Guild.Guild.Name} (Rank: {character.Guild.Rank})", true);
				}

				// Party info
				if (character.Party != null && character.Party.Party != null)
				{
					mainEmbed.AddField("Party", $"ID: {character.Party.PartyID} (Rank: {character.Party.Rank})", true);
				}

				// Pet info
				if (character.Pet != null && !character.Pet.Deleted)
				{
					string petStatus = character.Pet.Spawned ? "Spawned" : "Stored";
					mainEmbed.AddField("Pet", $"Template={character.Pet.TemplateID} ({petStatus})", true);
				}

				mainEmbed.WithTimestamp(DateTimeOffset.UtcNow);

				// Equipment embed
				EmbedBuilder? equipEmbed = null;
				if (character.Equipment != null && character.Equipment.Count > 0)
				{
					var activeEquip = character.Equipment.Where(e => !e.Deleted).OrderBy(e => e.Slot).ToList();
					if (activeEquip.Count > 0)
					{
						equipEmbed = new EmbedBuilder()
							.WithTitle($"{character.Name} — Equipment ({activeEquip.Count} items)")
							.WithColor(Color.Orange);

						var sb = new StringBuilder();
						foreach (var item in activeEquip)
						{
							sb.AppendLine($"Slot {item.Slot}: Template={item.TemplateID} x{item.Amount} (Seed: {item.Seed})");
						}
						equipEmbed.WithDescription(Truncate(sb.ToString(), 4096));
					}
				}

				// Inventory embed
				EmbedBuilder? invEmbed = null;
				if (character.Inventory != null && character.Inventory.Count > 0)
				{
					var activeInv = character.Inventory.Where(i => !i.Deleted).OrderBy(i => i.Slot).ToList();
					if (activeInv.Count > 0)
					{
						invEmbed = new EmbedBuilder()
							.WithTitle($"{character.Name} — Inventory ({activeInv.Count} items)")
							.WithColor(Color.Green);

						var sb = new StringBuilder();
						foreach (var item in activeInv)
						{
							sb.AppendLine($"Slot {item.Slot}: Template={item.TemplateID} x{item.Amount} (Seed: {item.Seed})");
						}
						invEmbed.WithDescription(Truncate(sb.ToString(), 4096));
					}
				}

				// Attributes embed
				EmbedBuilder? attrEmbed = null;
				if (character.Attributes != null && character.Attributes.Count > 0)
				{
					var activeAttrs = character.Attributes.Where(a => !a.Deleted).ToList();
					if (activeAttrs.Count > 0)
					{
						attrEmbed = new EmbedBuilder()
							.WithTitle($"{character.Name} — Attributes ({activeAttrs.Count})")
							.WithColor(Color.Purple);

						var sb = new StringBuilder();
						foreach (var attr in activeAttrs)
						{
							sb.AppendLine($"Template={attr.TemplateID}: {attr.CurrentValue:F1}/{attr.Value}");
						}
						attrEmbed.WithDescription(Truncate(sb.ToString(), 4096));
					}
				}

				// Skills embed
				EmbedBuilder? skillsEmbed = null;
				if (character.Skills != null && character.Skills.Count > 0)
				{
					var activeSkills = character.Skills.Where(s => !s.Deleted).ToList();
					if (activeSkills.Count > 0)
					{
						skillsEmbed = new EmbedBuilder()
							.WithTitle($"{character.Name} — Skills ({activeSkills.Count})")
							.WithColor(Color.Teal);

						var sb = new StringBuilder();
						foreach (var skill in activeSkills)
						{
							sb.AppendLine($"Hash={skill.Hash}: Level {skill.Level}");
						}
						skillsEmbed.WithDescription(Truncate(sb.ToString(), 4096));
					}
				}

				// Buffs embed
				EmbedBuilder? buffsEmbed = null;
				if (character.Buffs != null && character.Buffs.Count > 0)
				{
					var activeBuffs = character.Buffs.Where(b => !b.Deleted).ToList();
					if (activeBuffs.Count > 0)
					{
						buffsEmbed = new EmbedBuilder()
							.WithTitle($"{character.Name} — Buffs ({activeBuffs.Count})")
							.WithColor(Color.LightOrange);

						var sb = new StringBuilder();
						foreach (var buff in activeBuffs)
						{
							sb.AppendLine($"Template={buff.TemplateID}: {buff.Stacks} stacks, {buff.RemainingTime:F1}s remaining");
						}
						buffsEmbed.WithDescription(Truncate(sb.ToString(), 4096));
					}
				}

				// Quests embed
				EmbedBuilder? questsEmbed = null;
				if (character.Quests != null && character.Quests.Count > 0)
				{
					var activeQuests = character.Quests.Where(q => !q.Deleted).ToList();
					if (activeQuests.Count > 0)
					{
						questsEmbed = new EmbedBuilder()
							.WithTitle($"{character.Name} — Quests ({activeQuests.Count})")
							.WithColor(Color.DarkBlue);

						var sb = new StringBuilder();
						foreach (var quest in activeQuests)
						{
							string status = quest.Status switch
							{
								0 => "Inactive",
								1 => "Active",
								2 => "Complete",
								3 => "TurnedIn",
								4 => "Failed",
								_ => $"Unknown ({quest.Status})"
							};
							string objectives = string.IsNullOrWhiteSpace(quest.ObjectiveValues) ? "—" : quest.ObjectiveValues;
							sb.AppendLine($"• Quest #{quest.TemplateID} — {status}; Objectives: {objectives}");
						}
						questsEmbed.WithDescription(Truncate(sb.ToString(), 4096));
					}
				}

				// Achievements embed
				EmbedBuilder? achieveEmbed = null;
				if (character.Achievements != null && character.Achievements.Count > 0)
				{
					var activeAch = character.Achievements.Where(a => !a.Deleted).ToList();
					if (activeAch.Count > 0)
					{
						achieveEmbed = new EmbedBuilder()
							.WithTitle($"{character.Name} — Achievements ({activeAch.Count})")
							.WithColor(Color.Gold);

						var sb = new StringBuilder();
						foreach (var ach in activeAch)
						{
							sb.AppendLine($"Template={ach.TemplateID}: Tier {ach.Tier}, Value {ach.Value}");
						}
						achieveEmbed.WithDescription(Truncate(sb.ToString(), 4096));
					}
				}

				// Friends summary (distinguish friends from blocked)
				if (character.Friends != null && character.Friends.Count > 0)
				{
					var activeFriends = character.Friends.Where(f => !f.Deleted).ToList();
					if (activeFriends.Count > 0)
					{
						int friendCount = activeFriends.Count(f => !f.IsBlocked);
						int blockedCount = activeFriends.Count(f => f.IsBlocked);
						string friendsSummary = $"{friendCount} friend(s)";
						if (blockedCount > 0)
						{
							friendsSummary += $", {blockedCount} blocked";
						}
						mainEmbed.AddField("Friends", friendsSummary, true);
					}
				}

				// Factions summary
				if (character.Faction != null && character.Faction.Count > 0)
				{
					mainEmbed.AddField("Factions", $"{character.Faction.Count} faction(s)", true);
				}

				// Archetypes embed
				EmbedBuilder? archetypesEmbed = null;
				if (character.Archetypes != null && character.Archetypes.Count > 0)
				{
					var activeArchetypes = character.Archetypes.Where(a => !a.Deleted).ToList();
					if (activeArchetypes.Count > 0)
					{
						archetypesEmbed = new EmbedBuilder()
							.WithTitle($"{character.Name} — Archetypes ({activeArchetypes.Count})")
							.WithColor(Color.DarkMagenta);

						var sb = new StringBuilder();
						foreach (var archetype in activeArchetypes)
						{
							sb.AppendLine($"Template={archetype.TemplateID}");
						}
						archetypesEmbed.WithDescription(Truncate(sb.ToString(), 4096));
					}
				}

				// Abilities embed
				EmbedBuilder? abilitiesEmbed = null;
				if (character.Abilities != null && character.Abilities.Count > 0)
				{
					var activeAbilities = character.Abilities.Where(a => !a.Deleted).ToList();
					if (activeAbilities.Count > 0)
					{
						abilitiesEmbed = new EmbedBuilder()
							.WithTitle($"{character.Name} — Abilities ({activeAbilities.Count})")
							.WithColor(Color.Blue);

						var sb = new StringBuilder();
						foreach (var ability in activeAbilities)
						{
							sb.AppendLine($"Template={ability.TemplateID} (Cooldown: {ability.Cooldown:F1}s)");
						}
						abilitiesEmbed.WithDescription(Truncate(sb.ToString(), 4096));
					}
				}

				// Bank embed
				EmbedBuilder? bankEmbed = null;
				if (character.Bank != null && character.Bank.Count > 0)
				{
					var activeBank = character.Bank.Where(b => !b.Deleted).OrderBy(b => b.Slot).ToList();
					if (activeBank.Count > 0)
					{
						bankEmbed = new EmbedBuilder()
							.WithTitle($"{character.Name} — Bank ({activeBank.Count} items)")
							.WithColor(Color.DarkGreen);

						var sb = new StringBuilder();
						foreach (var item in activeBank)
						{
							sb.AppendLine($"Slot {item.Slot}: Template={item.TemplateID} x{item.Amount} (Seed: {item.Seed})");
						}
						bankEmbed.WithDescription(Truncate(sb.ToString(), 4096));
					}
				}

				// Send embeds (Discord max 10 embeds per message)
				var embeds = new List<Embed> { mainEmbed.Build() };
				if (archetypesEmbed != null) embeds.Add(archetypesEmbed.Build());
				if (equipEmbed != null) embeds.Add(equipEmbed.Build());
				if (invEmbed != null) embeds.Add(invEmbed.Build());
				if (bankEmbed != null) embeds.Add(bankEmbed.Build());
				if (attrEmbed != null) embeds.Add(attrEmbed.Build());
				if (abilitiesEmbed != null) embeds.Add(abilitiesEmbed.Build());
				if (skillsEmbed != null) embeds.Add(skillsEmbed.Build());
				if (buffsEmbed != null) embeds.Add(buffsEmbed.Build());
				if (questsEmbed != null) embeds.Add(questsEmbed.Build());
				if (achieveEmbed != null) embeds.Add(achieveEmbed.Build());

				// Discord limit: 10 embeds per message
				for (int i = 0; i < embeds.Count; i += 10)
				{
					var batch = embeds.GetRange(i, Math.Min(10, embeds.Count - i));
					await ReplyAsync(embeds: batch.ToArray());
				}
			}
			catch (Exception ex)
			{
				logger.LogError(ex, "Error inspecting character '{CharacterName}'.", characterName);
				await ReplyAsync("An error occurred while loading the character.");
			}
		}

		/// <summary>
		/// Searches for characters matching a partial name (admin only).
		/// </summary>
		[Command("search")]
		[RequireUserPermission(GuildPermission.ManageGuild)]
		[Summary("Searches for characters by partial name (admin).")]
		public async Task SearchAsync([Remainder] string partialName)
		{
			if (string.IsNullOrWhiteSpace(partialName) || partialName.Length > MaxNameLength)
			{
				await ReplyAsync($"Search term must be between 1 and {MaxNameLength} characters.");
				return;
			}

			if (partialName.Length < 2)
			{
				await ReplyAsync("Search term must be at least 2 characters.");
				return;
			}

			try
			{
				using var scope = serviceProvider.CreateScope();
				var dbContext = scope.ServiceProvider.GetRequiredService<NpgsqlDbContext>();

				string lowerPartial = partialName.ToLowerInvariant();
				var characters = await dbContext.Characters.AsQueryable()
					.Where(c => !c.Deleted && c.NameLowercase.Contains(lowerPartial))
					.OrderBy(c => c.Name)
					.Take(20)
					.ToListAsync();

				if (characters.Count == 0)
				{
					await ReplyAsync($"No characters found matching '{partialName}'.");
					return;
				}

				var sb = new StringBuilder();
				sb.AppendLine($"**Characters matching '{partialName}'** ({characters.Count} results):");
				foreach (var c in characters)
				{
					string status = c.SessionState.ToString();
					sb.AppendLine($"• **{c.Name}** (ID: {c.ID}) — {status} — Account: {c.Account}");
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
				logger.LogError(ex, "Error searching characters with '{PartialName}'.", partialName);
				await ReplyAsync("An error occurred while searching for characters.");
			}
		}

		/// <summary>
		/// Displays online player count and list.
		/// </summary>
		[Command("online")]
		[RequireUserPermission(GuildPermission.ManageGuild)]
		[Summary("Shows currently online characters (admin).")]
		public async Task OnlineAsync()
		{
			try
			{
				using var scope = serviceProvider.CreateScope();
				var dbContext = scope.ServiceProvider.GetRequiredService<NpgsqlDbContext>();

				// SessionState value 1 = Online (based on CharacterSessionState enum)
				var onlinePlayers = await dbContext.Characters.AsQueryable()
					.Where(c => !c.Deleted && (int)c.SessionState == 1)
					.OrderBy(c => c.Name)
					.Take(50)
					.ToListAsync();

				if (onlinePlayers.Count == 0)
				{
					await ReplyAsync("No players currently online.");
					return;
				}

				var sb = new StringBuilder();
				sb.AppendLine($"**Online Players ({onlinePlayers.Count}):**");
				foreach (var p in onlinePlayers)
				{
					sb.AppendLine($"• **{p.Name}** — Scene: {p.SceneName ?? "—"} — Account: {p.Account}");
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
				logger.LogError(ex, "Error fetching online players.");
				await ReplyAsync("An error occurred while fetching online players.");
			}
		}

		/// <summary>
		/// Displays guild information from the database.
		/// </summary>
		[Command("guild")]
		[RequireUserPermission(GuildPermission.ManageGuild)]
		[Summary("Shows guild information by name (admin).")]
		public async Task GuildInfoAsync([Remainder] string guildName)
		{
			if (string.IsNullOrWhiteSpace(guildName) || guildName.Length > MaxNameLength)
			{
				await ReplyAsync($"Guild name must be between 1 and {MaxNameLength} characters.");
				return;
			}

			try
			{
				using var scope = serviceProvider.CreateScope();
				var dbContext = scope.ServiceProvider.GetRequiredService<NpgsqlDbContext>();

				var guild = await dbContext.Guilds
					.Include(g => g.Characters)
					.FirstOrDefaultAsync(g => g.Name == guildName);

				if (guild == null)
				{
					await ReplyAsync($"Guild '{guildName}' not found.");
					return;
				}

				var embed = new EmbedBuilder()
					.WithTitle($"Guild: {guild.Name}")
					.WithColor(Color.DarkPurple)
					.AddField("ID", guild.ID.ToString(), true)
					.AddField("Members", (guild.Characters?.Count ?? 0).ToString(), true)
					.AddField("Created", guild.TimeCreated.ToString("yyyy-MM-dd HH:mm"), true)
					.AddField("Notice", string.IsNullOrEmpty(guild.Notice) ? "—" : guild.Notice)
					.AddField("MOTD", string.IsNullOrEmpty(guild.MessageOfTheDay) ? "—" : guild.MessageOfTheDay)
					.WithTimestamp(DateTimeOffset.UtcNow)
					.Build();

				await ReplyAsync(embed: embed);
			}
			catch (Exception ex)
			{
				logger.LogError(ex, "Error fetching guild '{GuildName}'.", guildName);
				await ReplyAsync("An error occurred while fetching guild info.");
			}
		}

		private static string GetAccessLevelName(byte level)
		{
			return level switch
			{
				0 => "Banned",
				1 => "Player",
				2 => "GameMaster",
				3 => "Admin",
				_ => $"Unknown ({level})"
			};
		}

		private static string Truncate(string text, int maxLength)
		{
			if (text.Length <= maxLength) return text;
			return text.Substring(0, maxLength - 20) + "\n... (truncated)";
		}
	}
}