using FishMMO.Core;
using FishMMO.Shared;

[assembly: GameModule(typeof(FishMMO.Shared.WildlifeModule))]

namespace FishMMO.Shared
{
	/// <summary>The Wildlife module (2026-11-20). See the module's README.</summary>
	public sealed class WildlifeModule : GameModule
	{
		/// <summary>The module's id.</summary>
		public const string ModuleId = "wildlife";

		/// <inheritdoc />
		public override string Id => ModuleId;

		/// <inheritdoc />
		public override void Install(ModuleBuilder builder)
		{
		}
	}
}
