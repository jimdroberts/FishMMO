using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Shared.Core;

[assembly: FishMMO.Server.Implementation.World.SceneServer.OperatorCommandProvider(typeof(FishMMO.Server.Implementation.World.SceneServer.HousingOperatorCommands))]

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// The housing commands: <c>/gm plot</c> describes a plot; <c>/admin plot</c> releases, resets, retires, moves rent
	/// dates and sweeps rent. Added to the operator sets by the Housing module (they were a <c>SceneServerSystem</c>
	/// partial before housing became a module).
	/// </summary>
	public sealed class HousingOperatorCommands : OperatorCommandProvider
	{
		private static readonly string[] PlotActionHelp =
		{
			"plot [plot] — describes the plot you stand on, or the one named (a key such as lot_120_-45, or an ID)",
			"plot release [plot] — takes it from its owner; what stood on it goes to their vault",
			"plot reset [plot] — takes it from its owner and demolishes everything on it (no vault)",
			"plot setdue [plot] now|+1h|-2d — moves its rent due date (then /admin plot sweep to bill it)",
			"plot orphans — owned plots in this scene whose foundation a re-cut removed",
			"plot retire <id> — releases an orphaned plot into its owner's vault (refund the price by hand)",
			"plot sweep — runs this server's rent sweep now",
		};

		/// <inheritdoc />
		public override IEnumerable<OperatorCommand> GameMasterCommands(IOperatorCommandHost host)
		{
			return new List<OperatorCommand>
			{
				new OperatorCommand
				{
					Name = "plot", Category = "World",
					Summary = "Describes the housing plot you stand on, or one named by key or ID.",
					Arguments = "plot:Text?",
					Run = (character, arguments) => DescribePlot(host, character, arguments),
				},
			};
		}

		/// <inheritdoc />
		public override IEnumerable<OperatorCommand> AdministratorCommands(IOperatorCommandHost host)
		{
			return new List<OperatorCommand>
			{
				new OperatorCommand
				{
					Name = "plot", Aliases = new[] { "plots", "housing" }, Category = "World",
					Summary = "Describes, releases, resets or retires housing plots, moves rent dates, sweeps rent. /admin plot help lists the actions.",
					Arguments = "action:Choice=info,release,reset,setdue,orphans,retire,sweep,help?;rest:Text?",
					Destructive = true,
					Run = (character, arguments) => RunAdminPlot(host, character, arguments),
				},
			};
		}

		private static bool TryGetHousing(IOperatorCommandHost host, IPlayerCharacter character, out IHousingSystem housing)
		{
			if (host.TryGetSystem(out housing) && housing.IsHousingEnabled)
			{
				return true;
			}
			host.Reply(character, "Housing is off on this server.");
			return false;
		}

		private static void RunOperatorAction(IOperatorCommandHost host, IPlayerCharacter character, Func<Task<string>> action)
		{
			host.RunOperatorLines(character, async () => new[] { await action() });
		}

		private static void DescribePlot(IOperatorCommandHost host, IPlayerCharacter character, string target)
		{
			if (!TryGetHousing(host, character, out IHousingSystem housing))
			{
				return;
			}
			if (!housing.TryFindPlotForStaff(character, target, out long plotID, out string error))
			{
				host.Reply(character, error);
				return;
			}
			host.RunOperatorLines(character, () => housing.DescribePlotForStaffAsync(plotID));
		}

		private static void RunAdminPlot(IOperatorCommandHost host, IPlayerCharacter character, string arguments)
		{
			string action = OperatorCommandParsing.SplitFirstWord(arguments, out string rest).ToLowerInvariant();
			rest = rest?.Trim() ?? string.Empty;
			if (action == "help")
			{
				host.ReplyLines(character, PlotActionHelp);
				return;
			}
			if (!TryGetHousing(host, character, out IHousingSystem housing))
			{
				return;
			}
			switch (action)
			{
				case "":
				case "info":
					DescribePlot(host, character, rest);
					return;
				case "release":
				case "reset":
				{
					if (!housing.TryFindPlotForStaff(character, rest, out long plotID, out string error))
					{
						host.Reply(character, error);
						return;
					}
					bool keep = action == "release";
					RunOperatorAction(host, character, () => housing.ReleasePlotForStaffAsync(plotID, keep));
					return;
				}
				case "retire":
				{
					if (!long.TryParse(rest, out long plotID) || plotID <= 0)
					{
						host.Reply(character, "Usage: /admin plot retire <plot ID> (see /admin plot orphans).");
						return;
					}
					RunOperatorAction(host, character, () => housing.ReleasePlotForStaffAsync(plotID, true));
					return;
				}
				case "setdue":
				{
					// The last word is the change; anything before it names the plot.
					string trimmed = rest.Trim();
					int split = trimmed.LastIndexOf(' ');
					string target = split > 0 ? trimmed.Substring(0, split) : string.Empty;
					string when = split > 0 ? trimmed.Substring(split + 1) : trimmed;
					if (string.IsNullOrEmpty(when))
					{
						host.Reply(character, "Usage: /admin plot setdue [plot] now | +1h | -2d.");
						return;
					}
					if (!housing.TryFindPlotForStaff(character, target, out long plotID, out string error))
					{
						host.Reply(character, error);
						return;
					}
					RunOperatorAction(host, character, () => housing.SetRentDueForStaffAsync(plotID, when));
					return;
				}
				case "orphans":
					host.RunOperatorLines(character, housing.PrepareOrphanListForStaff(character));
					return;
				case "sweep":
					housing.SweepRentNow();
					host.Reply(character, "This server's rent sweep runs on the next frame.");
					return;
				default:
					host.Reply(character, $"Unknown plot action '{action}'. /admin plot help lists them.");
					return;
			}
		}
	}
}
