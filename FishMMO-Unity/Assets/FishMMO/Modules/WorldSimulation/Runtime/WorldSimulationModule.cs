using FishMMO.Core;

[assembly: GameModule(typeof(FishMMO.Shared.WorldSimulationModule))]

namespace FishMMO.Shared
{
	/// <summary>
	/// The World Simulation module: the sky and its calendar, the weather and climate, the biomes and the world atlas, on the
	/// shared clock. Its pieces install themselves at load (the weather reading, the client and scene server services, the render
	/// features, the operator commands). See the module's README.
	/// </summary>
	public sealed class WorldSimulationModule : GameModule
	{
		/// <summary>The module's id.</summary>
		public const string ModuleId = "world-simulation";

		/// <inheritdoc />
		public override string Id => ModuleId;

		/// <inheritdoc />
		public override void Install(ModuleBuilder builder)
		{
		}
	}
}
