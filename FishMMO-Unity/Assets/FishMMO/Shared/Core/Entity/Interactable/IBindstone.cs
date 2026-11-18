namespace FishMMO.Shared.Core
{
	/// <summary>
	/// Server-side interface for bindstone interactables.
	/// Marker interface used for type-safe handler resolution and bindstone validation.
	/// </summary>
	public interface IBindstone : IInteractable
	{
		/// <summary>
		/// Achievement template to increment when a player binds at this bindstone.
		/// </summary>
		AchievementTemplate AchievementTemplate { get; }
	}
}
