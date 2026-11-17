using FishMMO.Core;

[assembly: GameModule(typeof(FishMMO.Shared.AbilityCraftingModule))]

namespace FishMMO.Shared
{
	/// <summary>
	/// The Ability Crafting module: combining a known ability template with ability events into a new ability at an
	/// ability crafter, paid in currency. Abilities themselves (templates, events, learning, the grant funnel, prediction)
	/// and the crafters placed in the world stay in the core. See the module's README.
	/// </summary>
	public sealed class AbilityCraftingModule : GameModule
	{
		/// <summary>The module's id.</summary>
		public const string ModuleId = "abilitycrafting";

		/// <inheritdoc />
		public override string Id => ModuleId;

		/// <inheritdoc />
		public override void Install(ModuleBuilder builder)
		{
		}
	}
}
