using FishMMO.Shared;

namespace FishMMO.Server.Core.World.SceneServer
{
	/// <summary>
	/// Server-side interface for ability crafter interactables.
	/// Marker interface used for type-safe handler resolution and ability crafter validation.
	/// </summary>
	public interface IAbilityCrafter : IInteractable
	{
		AchievementTemplate AchievementTemplate { get; }
	}
}