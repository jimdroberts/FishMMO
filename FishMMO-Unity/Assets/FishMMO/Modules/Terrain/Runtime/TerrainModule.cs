using FishMMO.Core;

[assembly: GameModule(typeof(FishMMO.Shared.TerrainModule))]

namespace FishMMO.Shared
{
	/// <summary>
	/// The Terrain module: the client's own drawing of the ground and what grows and lies on it. The GPU-driven trees, rocks
	/// and props (with their HLOD and shadow cache casters), the heightmap draw, the blade grass and the detail scatter, the
	/// rock seam fillets, the cave cover, the trails, and the moss layer's globals. All of it is client presentation of the
	/// scene's baked data; the server neither runs nor knows it. Without it the scene's Unity terrain draws itself. See the
	/// module's README.
	/// </summary>
	public sealed class TerrainModule : GameModule
	{
		/// <summary>The module's id.</summary>
		public const string ModuleId = "terrain";

		/// <inheritdoc />
		public override string Id => ModuleId;

		/// <inheritdoc />
		public override void Install(ModuleBuilder builder)
		{
		}
	}
}
