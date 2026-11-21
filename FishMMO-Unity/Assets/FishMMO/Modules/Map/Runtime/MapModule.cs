using FishMMO.Core;

[assembly: GameModule(typeof(FishMMO.Shared.MapModule))]

namespace FishMMO.Shared
{
	/// <summary>
	/// The Map module (2026-11-20): the player's maps. The world map and the minimap, fog of war, map notes, marker
	/// filters, the compass and the client's map state (<c>ClientMapSystem</c>) are the module's, all on the client.
	/// </summary>
	/// <remarks>
	/// The core keeps what is authored into the world and what a build bakes from it: map markers and their registry,
	/// world map definitions, points of interest and region labels in the world scene details, the world map bake, the
	/// <see cref="ExploreMapAction"/> event, and true north. Without this module they are still authored and baked; nothing
	/// draws them.
	/// </remarks>
	public sealed class MapModule : GameModule
	{
		/// <summary>The module's id.</summary>
		public const string ModuleId = "map";

		/// <inheritdoc />
		public override string Id => ModuleId;

		/// <inheritdoc />
		public override void Install(ModuleBuilder builder)
		{
		}
	}
}
