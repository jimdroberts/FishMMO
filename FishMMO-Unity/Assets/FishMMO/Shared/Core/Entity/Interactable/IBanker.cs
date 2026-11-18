namespace FishMMO.Shared.Core
{
	/// <summary>
	/// Server-side interface for banker interactables.
	/// Marker interface used for type-safe handler resolution and banker validation.
	/// </summary>
	public interface IBanker : IInteractable
	{
		/// <summary>
		/// Achievement template to increment when a player uses this banker.
		/// </summary>
		AchievementTemplate AchievementTemplate { get; }
	}
}
