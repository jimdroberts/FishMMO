using FishMMO.Core;

[assembly: GameModule(typeof(FishMMO.Shared.NameGeneration.NamingModule))]

namespace FishMMO.Shared.NameGeneration
{
	/// <summary>
	/// The Naming module: the name generator (character, city, dungeon, point of interest and item names, titles), its
	/// grammar, modifier and title pool templates, and the scene objects' generated names. The naming data on the race and
	/// biome templates stays in the core. See the module's README.
	/// </summary>
	public sealed class NamingModule : GameModule
	{
		/// <summary>The module's id.</summary>
		public const string ModuleId = "naming";

		/// <inheritdoc />
		public override string Id => ModuleId;

		/// <inheritdoc />
		public override void Install(ModuleBuilder builder)
		{
		}
	}
}
