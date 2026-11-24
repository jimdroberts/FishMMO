using FishMMO.Core;

[assembly: GameModule(typeof(FishMMO.Shared.AuctionHouseModule))]

namespace FishMMO.Shared
{
	/// <summary>
	/// The Auction House module (issue #299): one market for the whole world, reached at the Auctioneer who stands in
	/// friendly towns. Sellers list a stack at a price per unit for 12, 24 or 48 hours; buyers take all of it or part;
	/// what is bought, what it earned and what did not sell arrives by mail. See the module's README.
	/// </summary>
	public sealed class AuctionHouseModule : GameModule
	{
		/// <summary>The module's id.</summary>
		public const string ModuleId = "auctionhouse";

		/// <inheritdoc />
		public override string Id => ModuleId;

		/// <inheritdoc />
		public override void Install(ModuleBuilder builder)
		{
		}
	}
}
