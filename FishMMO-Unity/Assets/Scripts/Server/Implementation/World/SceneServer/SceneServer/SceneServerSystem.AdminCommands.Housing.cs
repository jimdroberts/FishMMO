using System.Collections.Generic;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Shared.Core;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// The housing commands: <c>/gm plot</c> (look only — GMs may not change gameplay) and
	/// <c>/admin plot</c> (release, reset, move the rent date, list and retire orphans, sweep rent now).
	/// </summary>
	/// <remarks>
	/// A plot is named by key or ID, or left out to mean the plot the operator stands on (or the nearest
	/// deed post). Everything happens in <see cref="IHousingSystem"/>; these only parse and reply.
	/// </remarks>
	public partial class SceneServerSystem
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

		private IEnumerable<OperatorCommand> BuildGameMasterHousingCommands()
		{
			return new List<OperatorCommand>
			{
				new OperatorCommand
				{
					Name = "plot", Category = "World",
					Summary = "Describes the housing plot you stand on, or one named by key or ID.",
					Arguments = "plot:Text?",
					Run = (character, arguments) => DescribePlot(character, arguments),
				},
			};
		}

		private IEnumerable<OperatorCommand> BuildAdminHousingCommands()
		{
			return new List<OperatorCommand>
			{
				new OperatorCommand
				{
					Name = "plot", Aliases = new[] { "plots", "housing" }, Category = "World",
					Summary = "Describes, releases, resets or retires housing plots, moves rent dates, sweeps rent. /admin plot help lists the actions.",
					Arguments = "action:Choice=info,release,reset,setdue,orphans,retire,sweep,help?;rest:Text?",
					Destructive = true,
					Run = RunAdminPlot,
				},
			};
		}

		private bool TryGetHousing(IPlayerCharacter character, out IHousingSystem housing)
		{
			if (Server.BehaviourRegistry.TryGet(out housing) && housing.IsHousingEnabled)
			{
				return true;
			}
			Reply(character, "Housing is off on this server.");
			return false;
		}

		private void DescribePlot(IPlayerCharacter character, string target)
		{
			if (!TryGetHousing(character, out IHousingSystem housing))
			{
				return;
			}
			if (!housing.TryFindPlotForStaff(character, target, out long plotID, out string error))
			{
				Reply(character, error);
				return;
			}
			RunOperatorLines(character, () => housing.DescribePlotForStaffAsync(plotID));
		}

		private void RunAdminPlot(IPlayerCharacter character, string arguments)
		{
			string action = OperatorCommandParsing.SplitFirstWord(arguments, out string rest).ToLowerInvariant();
			rest = rest?.Trim() ?? string.Empty;
			if (action == "help")
			{
				ReplyLines(character, PlotActionHelp);
				return;
			}
			if (!TryGetHousing(character, out IHousingSystem housing))
			{
				return;
			}
			switch (action)
			{
				case "":
				case "info":
					DescribePlot(character, rest);
					return;
				case "release":
				case "reset":
				{
					if (!housing.TryFindPlotForStaff(character, rest, out long plotID, out string error))
					{
						Reply(character, error);
						return;
					}
					bool keep = action == "release";
					RunOperatorAction(character, () => housing.ReleasePlotForStaffAsync(plotID, keep));
					return;
				}
				case "retire":
				{
					if (!long.TryParse(rest, out long plotID) || plotID <= 0)
					{
						Reply(character, "Usage: /admin plot retire <plot ID> (see /admin plot orphans).");
						return;
					}
					RunOperatorAction(character, () => housing.ReleasePlotForStaffAsync(plotID, true));
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
						Reply(character, "Usage: /admin plot setdue [plot] now | +1h | -2d.");
						return;
					}
					if (!housing.TryFindPlotForStaff(character, target, out long plotID, out string error))
					{
						Reply(character, error);
						return;
					}
					RunOperatorAction(character, () => housing.SetRentDueForStaffAsync(plotID, when));
					return;
				}
				case "orphans":
					RunOperatorLines(character, housing.PrepareOrphanListForStaff(character));
					return;
				case "sweep":
					housing.SweepRentNow();
					Reply(character, "This server's rent sweep runs on the next frame.");
					return;
				default:
					Reply(character, $"Unknown plot action '{action}'. /admin plot help lists them.");
					return;
			}
		}
	}
}
