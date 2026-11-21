using FishMMO.Core;

[assembly: GameModule(typeof(FishMMO.Shared.WorldInteractablesModule))]

namespace FishMMO.Shared
{
	/// <summary>
	/// The World Interactables module (2026-11-20): the furniture of a world a player uses. Containers, NPC dialogue,
	/// gathering nodes, lore objects, shrines, capture points, switches and bindstones, each with its ECA action, its
	/// window and, for dialogue and containers, the server system that answers their follow-up requests.
	/// </summary>
	/// <remarks>
	/// The core keeps the interaction itself: the <c>Interactable</c> base, its ECA triggers, the request with its guard and
	/// scene-object validation (<c>InteractableSystem</c>), the switch target contract the world's doors implement, and
	/// teleporters and portals, which are how scenes connect.
	/// </remarks>
	public sealed class WorldInteractablesModule : GameModule
	{
		/// <summary>The module's id.</summary>
		public const string ModuleId = "world-interactables";

		/// <inheritdoc />
		public override string Id => ModuleId;

		/// <inheritdoc />
		public override void Install(ModuleBuilder builder)
		{
		}
	}
}
