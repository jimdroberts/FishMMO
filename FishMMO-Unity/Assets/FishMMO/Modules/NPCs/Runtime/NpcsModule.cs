using FishMMO.Core;
using FishMMO.Shared;

[assembly: GameModule(typeof(FishMMO.Shared.NpcsModule))]

namespace FishMMO.Shared
{
	/// <summary>The NPCs module. See the module's README.</summary>
	public sealed class NpcsModule : GameModule
	{
		/// <summary>The module's id.</summary>
		public const string ModuleId = "npcs";

		/// <inheritdoc />
		public override string Id => ModuleId;

		/// <inheritdoc />
		public override void Install(ModuleBuilder builder)
		{
		}
	}
}
