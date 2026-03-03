namespace FishMMO.Shared.Core
{
	/// <summary>
	/// Server-side interface for container interactables (chests, wardrobes, crates, etc.).
	/// The concrete type also implements <see cref="IItemContainer"/> for item storage operations.
	/// </summary>
	public interface IContainer : IInteractable
	{
		ContainerTemplate Template { get; }
		AchievementTemplate AchievementTemplate { get; }
		void Despawn();
	}
}