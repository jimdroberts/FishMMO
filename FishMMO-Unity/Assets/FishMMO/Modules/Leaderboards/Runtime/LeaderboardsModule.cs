using FishMMO.Core;

[assembly: GameModule(typeof(FishMMO.Shared.LeaderboardsModule))]

namespace FishMMO.Shared
{
	/// <summary>
	/// The Leaderboards module (the pilot module): PvP and PvE boards read from the database by the scene server's
	/// <c>LeaderboardSystem</c>, shown in the Leaderboards window and the arena board's standings.
	/// </summary>
	/// <remarks>
	/// Everything it is lives in <c>Assets/FishMMO/Modules/Leaderboards</c>: the board templates, the broadcasts, the server
	/// system and its asset, the window (a prefab placed by <c>ModulePanelHost</c>), its game menu entry and the arena
	/// standings source. Deleting the folder removes the feature; the game builds and runs without it, and the
	/// database tables it reads (owned by other features) stay as they are. It adds no Core systems yet: declaring it
	/// lists it among the installed modules.
	/// </remarks>
	public sealed class LeaderboardsModule : GameModule
	{
		public const string ModuleId = "leaderboards";

		public override string Id => ModuleId;

		public override void Install(ModuleBuilder builder)
		{
		}
	}
}
