using FishMMO.Shared;
using UnityEngine;

namespace FishMMO.Server.Core.World.SceneServer
{
	/// <summary>
	/// Server-side interface for teleporter interactables.
	/// Exposes the target transform needed for same-scene teleportation.
	/// </summary>
	public interface ITeleporter : IInteractable
	{
		/// <summary>
		/// The target transform to teleport the player to. May be null for cross-scene teleportation.
		/// </summary>
		Transform Target { get; }

		/// <summary>
		/// Achievement template to increment when a player uses this teleporter.
		/// </summary>
		AchievementTemplate AchievementTemplate { get; }
	}
}