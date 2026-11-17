using FishMMO.Core;

[assembly: GameModule(typeof(FishMMO.Shared.CommerceModule))]

namespace FishMMO.Shared
{
	/// <summary>
	/// The Commerce module: merchants (buying and selling), mail, corpse looting and player-to-player trade. Merchant
	/// and mailbox templates and the interactables placed in the world, the messages their actions send to open the
	/// windows, and the loot an NPC carries stay in the core. See the module's README.
	/// </summary>
	public sealed class CommerceModule : GameModule
	{
		/// <summary>The module's id.</summary>
		public const string ModuleId = "commerce";

		/// <inheritdoc />
		public override string Id => ModuleId;

		/// <inheritdoc />
		public override void Install(ModuleBuilder builder)
		{
		}
	}
}
