namespace FishMMO.Shared.Core
{
	/// <summary>
	/// Server-side interface for world item interactables.
	/// Exposes the item template, stack amount, and despawn capability needed by the interaction handler.
	/// </summary>
	public interface IWorldItem : IInteractable
	{
		/// <summary>
		/// The item template defining the type of item this world object represents.
		/// </summary>
		BaseItemTemplate Template { get; }

		/// <summary>
		/// Achievement template ID to increment when a player picks up this world item.
		/// </summary>
		int AchievementTemplateID { get; }

		/// <summary>
		/// The number of items in this world item stack.
		/// </summary>
		uint Amount { get; set; }

		/// <summary>
		/// Despawns this world item via its assigned ObjectSpawner.
		/// </summary>
		void Despawn();
	}
}