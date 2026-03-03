using FishMMO.Shared;

namespace FishMMO.Server.Core.World.SceneServer
{
	/// <summary>
	/// Server-side interface for dungeon entrance interactables.
	/// Exposes the dungeon name needed by the dungeon finder system.
	/// </summary>
	public interface IDungeonEntrance : IInteractable
	{
		/// <summary>
		/// The name of the dungeon scene this entrance leads to.
		/// </summary>
		string DungeonName { get; }

		/// <summary>
		/// Achievement template to increment when a player enters this dungeon.
		/// </summary>
		AchievementTemplate AchievementTemplate { get; }
	}
}