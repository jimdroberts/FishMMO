namespace FishMMO.Shared.Core
{
	/// <summary>
	/// Server-side interface for mailbox interactables.
	/// Marker interface used for type-safe handler resolution and mailbox validation.
	/// </summary>
	public interface IMailbox : IInteractable
	{
		AchievementTemplate AchievementTemplate { get; }
	}
}