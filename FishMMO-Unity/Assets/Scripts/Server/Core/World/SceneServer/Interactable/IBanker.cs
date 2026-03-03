using FishMMO.Shared;

namespace FishMMO.Server.Core.World.SceneServer
{
	/// <summary>
	/// Server-side interface for banker interactables.
	/// Marker interface used for type-safe handler resolution and banker validation.
	/// </summary>
	public interface IBanker : IInteractable
	{
		AchievementTemplate AchievementTemplate { get; }
	}
}