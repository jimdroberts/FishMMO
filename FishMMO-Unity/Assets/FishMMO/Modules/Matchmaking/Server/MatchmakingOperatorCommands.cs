using System.Collections.Generic;
using System.Globalization;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using FishMMO.Server.Implementation.World.SceneServer.Interactable;

[assembly: FishMMO.Server.Implementation.World.SceneServer.OperatorCommandProvider(typeof(FishMMO.Server.Implementation.World.SceneServer.MatchmakingOperatorCommands))]

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// <c>/admin season</c>: shows the PvP season and the ones before it, or ends it and begins the next.
	/// </summary>
	/// <remarks>
	/// Ending a season names the season being ended (<c>/admin season next 3</c>), not just "next": the command is then
	/// safe to send twice, since the second finds season 3 already over and changes nothing, and it cannot end a season
	/// the administrator did not mean to. See <see cref="MatchmakingSystem.AdvanceSeasonForStaffAsync"/>.
	/// </remarks>
	public sealed class MatchmakingOperatorCommands : OperatorCommandProvider
	{
		private static readonly string[] SeasonHelp =
		{
			"season — the active PvP season and the ones before it",
			"season next <number> [name] — ends season <number> (it must be the active one) and begins the next",
			"A new season starts ranked arena ratings over at 1500; PvP Rank and duel ratings carry on.",
			"Seasons are kept in the database for good; every scene server follows the change on its own.",
		};

		/// <inheritdoc />
		public override IEnumerable<OperatorCommand> AdministratorCommands(IOperatorCommandHost host)
		{
			return new List<OperatorCommand>
			{
				new OperatorCommand
				{
					Name = "season", Aliases = new[] { "pvpseason" }, Category = "World",
					Summary = "Shows the PvP season, or ends it and begins the next. /admin season help lists the actions.",
					Arguments = "action:Choice=show,next,help?;number:Integer?;name:Text?",
					Destructive = true,
					Run = (character, arguments) => RunSeason(host, character, arguments),
				},
			};
		}

		private static void RunSeason(IOperatorCommandHost host, IPlayerCharacter character, string arguments)
		{
			if (character == null)
			{
				return;
			}
			string action = OperatorCommandParsing.SplitFirstWord(arguments, out string rest).ToLowerInvariant();
			if (action == "help")
			{
				host.ReplyLines(character, SeasonHelp);
				return;
			}
			if (action != string.Empty && action != "show" && action != "next")
			{
				host.Reply(character, $"Unknown season action '{OperatorCommandParsing.Truncate(action, 24)}'. /admin season help lists them.");
				return;
			}
			if (!host.TryGetSystem(out MatchmakingSystem matchmaking))
			{
				host.Reply(character, "Matchmaking is not running on this server.");
				return;
			}
			if (action != "next")
			{
				host.RunOperatorLines(character, matchmaking.DescribeSeasonsForStaffAsync);
				return;
			}

			string numberText = OperatorCommandParsing.SplitFirstWord(rest, out string name);
			if (!int.TryParse(numberText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int ending) || ending < 1)
			{
				host.Reply(character, "Usage: /admin season next <number of the season to end> [name of the new one]. /admin season shows the number.");
				return;
			}
			string actorName = character.CharacterName;
			string actorAccount = character.Account;
			string newName = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
			host.RunOperatorLines(character, () => matchmaking.AdvanceSeasonForStaffAsync(ending, newName, actorName, actorAccount));
		}
	}
}
