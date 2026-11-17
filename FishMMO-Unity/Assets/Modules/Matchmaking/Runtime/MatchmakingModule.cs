using FishMMO.Core;

[assembly: GameModule(typeof(FishMMO.Shared.MatchmakingModule))]

namespace FishMMO.Shared
{
	/// <summary>
	/// The Matchmaking module: the group finder, the dungeon finder and dungeon instances, arenas and arena matches.
	/// Dungeon and arena templates, the entrances, boards and objectives placed in the world, and arena teams stay in the
	/// core. See the module's README.
	/// </summary>
	public sealed class MatchmakingModule : GameModule
	{
		/// <summary>The module's id.</summary>
		public const string ModuleId = "matchmaking";

		/// <inheritdoc />
		public override string Id => ModuleId;

		/// <inheritdoc />
		public override void Install(ModuleBuilder builder)
		{
		}
	}
}
