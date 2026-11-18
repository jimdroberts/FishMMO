using FishMMO.Core;

[assembly: GameModule(typeof(FishMMO.Shared.AmbientLifeModule))]

namespace FishMMO.Shared
{
	/// <summary>
	/// The Ambient Life module: the land's birds, bats and small animals, sea life and what floats on the sea, the motes in
	/// the air, falling leaves, and the life of the world's sites (embers, flames, lighthouse beams, cave drips and water
	/// life, sea hall vents, rockfalls). All of it is client presentation, a pure function of the world and the shared
	/// clock; nothing is simulated on the server or sent. See the module's README.
	/// </summary>
	public sealed class AmbientLifeModule : GameModule
	{
		/// <summary>The module's id.</summary>
		public const string ModuleId = "ambient-life";

		/// <inheritdoc />
		public override string Id => ModuleId;

		/// <inheritdoc />
		public override void Install(ModuleBuilder builder)
		{
		}
	}
}
