namespace FishMMO.Shared.Core
{
	/// <summary>
	/// Server-side interface for shrine interactables.
	/// Exposes the shrine template needed for healing and buff application.
	/// </summary>
	public interface IShrine : IInteractable
	{
		/// <summary>
		/// The shrine template defining heal percentages, buff grants, and other shrine effects.
		/// </summary>
		ShrineTemplate Template { get; }

		/// <summary>
		/// Achievement template to increment when a player uses this shrine.
		/// </summary>
		AchievementTemplate AchievementTemplate { get; }
	}
}