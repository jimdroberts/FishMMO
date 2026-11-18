using FishMMO.Core;

[assembly: GameModule(typeof(FishMMO.Shared.HousingModule))]

namespace FishMMO.Shared
{
	/// <summary>
	/// The Housing module: plots players buy and build on, rent, the house vault, and the staff plot commands. The plot
	/// foundations in the world scenes, their state and their interaction trigger stay in the core; this module answers
	/// them. See the module's README.
	/// </summary>
	public sealed class HousingModule : GameModule
	{
		/// <summary>The module's id.</summary>
		public const string ModuleId = "housing";

		/// <inheritdoc />
		public override string Id => ModuleId;

		/// <inheritdoc />
		public override void Install(ModuleBuilder builder)
		{
		}
	}
}
