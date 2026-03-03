namespace FishMMO.Shared.Core
{
	/// <summary>
	/// Server-side interface for bindstone interactables.
	/// Marker interface used for type-safe handler resolution and bindstone validation.
	/// </summary>
	public interface IBindstone : IInteractable
	{
		AchievementTemplate AchievementTemplate { get; }
	}
}