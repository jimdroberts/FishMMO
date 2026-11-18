namespace FishMMO.Shared.Core
{
	/// <summary>
	/// Server-side interface for gathering node interactables.
	/// Exposes the template, remaining uses, and despawn capability needed by the interaction handler.
	/// </summary>
	public interface IGatheringNode : IInteractable
	{
		/// <summary>
		/// The gathering node template defining drop tables, gather time, and maximum uses.
		/// </summary>
		GatheringNodeTemplate Template { get; }

		/// <summary>
		/// Achievement template to increment when a player gathers from this node.
		/// </summary>
		AchievementTemplate AchievementTemplate { get; }

		/// <summary>
		/// The number of remaining uses before this node is depleted and despawned.
		/// </summary>
		int RemainingUses { get; set; }

		/// <summary>
		/// Despawns this gathering node via its assigned ObjectSpawner.
		/// </summary>
		void Despawn();
	}
}