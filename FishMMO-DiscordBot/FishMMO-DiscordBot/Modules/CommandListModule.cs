using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using FishMMO.DiscordBot.Services;

namespace FishMMO.DiscordBot.Modules
{
	/// <summary>
	/// Top-level /commands command that lists all commands available to the calling user,
	/// filtered by Discord permissions, role requirements, and per-guild disabled commands.
	/// </summary>
	public class CommandListModule : ModuleBase<SocketCommandContext>
	{
		private readonly CommandService commandService;
		private readonly BotConfigurationService botConfigService;

		public CommandListModule(CommandService commandService, BotConfigurationService botConfigService)
		{
			this.commandService = commandService;
			this.botConfigService = botConfigService;
		}

		/// <summary>
		/// Lists all commands the calling user can execute in this server,
		/// based on their Discord permissions, configured role requirements,
		/// and any commands disabled by moderators.
		/// </summary>
		[Command("commands")]
		[Summary("Lists all commands available to you based on your permissions.")]
		public async Task ListCommandsAsync()
		{
			var guildUser = Context.User as IGuildUser;
			ulong guildId = Context.Guild?.Id ?? 0;
			var config = guildId != 0 ? botConfigService.GetCommandPermissionConfig(guildId) : null;

			var userRoleIds = guildUser?.RoleIds as IReadOnlyCollection<ulong>;

			var sections = new Dictionary<string, List<string>>();

			foreach (var module in commandService.Modules)
			{
				string sectionName = string.IsNullOrEmpty(module.Group)
					? "General"
					: char.ToUpper(module.Group[0]) + module.Group.Substring(1);

				string groupPrefix = string.IsNullOrEmpty(module.Group) ? "" : $"{module.Group} ";

				foreach (var cmd in module.Commands)
				{
					string cmdKey = BuildCommandKey(cmd);

					// Skip commands disabled by guild config
					if (config != null && config.DisabledCommands.Contains(cmdKey))
					{
						continue;
					}

					// Skip commands that require a role the user doesn't have
					if (config != null && config.RoleRequirements.TryGetValue(cmdKey, out ulong requiredRoleId))
					{
						if (userRoleIds == null || !userRoleIds.Contains(requiredRoleId))
						{
							continue;
						}
					}

					// Check Discord permission preconditions
					var preconditionResult = await cmd.CheckPreconditionsAsync(Context);
					if (!preconditionResult.IsSuccess)
					{
						continue;
					}

					string summary = string.IsNullOrEmpty(cmd.Summary) ? "No description" : cmd.Summary;
					string line = $"• `/{groupPrefix}{cmd.Name}` — {summary}";

					if (!sections.TryGetValue(sectionName, out var list))
					{
						list = new List<string>();
						sections[sectionName] = list;
					}
					list.Add(line);
				}
			}

			if (sections.Count == 0)
			{
				await ReplyAsync("No commands are available to you.");
				return;
			}

			var sb = new StringBuilder();
			sb.AppendLine("**Commands Available to You:**\n");

			foreach (var kvp in sections)
			{
				sb.AppendLine($"__**{kvp.Key}**__");
				foreach (string line in kvp.Value)
				{
					sb.AppendLine(line);
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
		/// Builds a lowercase command key from a CommandInfo such as "mod kick" or "link".
		/// </summary>
		internal static string BuildCommandKey(CommandInfo cmd)
		{
			string? group = cmd.Module.Group;
			if (string.IsNullOrEmpty(group))
			{
				return cmd.Name.ToLowerInvariant();
			}
			return $"{group} {cmd.Name}".ToLowerInvariant();
		}
	}
}