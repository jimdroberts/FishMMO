using FishMMO.Shared;

namespace FishMMO.Server.Core.World.SceneServer
{
	/// <summary>
	/// Server-side interface for merchant interactables.
	/// Exposes the merchant template needed for purchase validation and item/ability sales.
	/// </summary>
	public interface IMerchant : IInteractable
	{
		/// <summary>
		/// The merchant template defining available items, abilities, and ability events for sale.
		/// </summary>
		MerchantTemplate Template { get; }

		/// <summary>
		/// Achievement template to increment when a player purchases from this merchant.
		/// </summary>
		AchievementTemplate AchievementTemplate { get; }
	}
}