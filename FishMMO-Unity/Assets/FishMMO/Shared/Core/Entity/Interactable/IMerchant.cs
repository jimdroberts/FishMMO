using FishMMO.Shared;
namespace FishMMO.Shared.Core
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
		CachedScriptableObject Template { get; }

		/// <summary>
		/// Achievement template to increment when a player purchases from this merchant.
		/// </summary>
		CachedScriptableObject AchievementTemplate { get; }
	}
}