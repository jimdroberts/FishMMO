using FishMMO.Shared;

namespace FishMMO.Server.Core.World.SceneServer
{
	/// <summary>
	/// Server-side interface for lore object interactables.
	/// Exposes the lore template needed for granting abilities, events, and items on interaction.
	/// </summary>
	public interface ILoreObject : IInteractable
	{
		/// <summary>
		/// The lore object template defining text content, granted abilities, events, and items.
		/// </summary>
		LoreObjectTemplate Template { get; }

		/// <summary>
		/// Achievement template to increment when a player discovers this lore object.
		/// </summary>
		AchievementTemplate AchievementTemplate { get; }
	}
}