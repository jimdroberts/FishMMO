namespace FishMMO.Shared.Core
{
	/// <summary>
	/// Server-side interface for container interactables (chests, wardrobes, crates, etc.).
	/// The concrete type also implements <see cref="IItemContainer"/> for item storage operations.
	/// </summary>
	public interface IContainer : IInteractable
	{
		/// <summary>
		/// The container template defining loot tables and spawn settings.
		/// </summary>
		ContainerTemplate Template { get; }
		/// <summary>
		/// Achievement template to increment when a player opens this container.
		/// </summary>
		AchievementTemplate AchievementTemplate { get; }
		/// <summary>
		/// Despawns this container via its assigned ObjectSpawner.
		/// </summary>
		void Despawn();
	}
}
