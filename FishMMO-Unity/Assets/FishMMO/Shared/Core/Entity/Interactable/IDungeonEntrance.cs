namespace FishMMO.Shared.Core
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
		/// Template ID of the dungeon this entrance leads to, or 0 when none is configured.
		/// </summary>
		/// <remarks>
		/// Sent to the client so it can resolve the dungeon's description, artwork and difficulty
		/// list out of the shared template cache, and read on the server to validate the
		/// difficulty a request names. An entrance with no template behaves as a dungeon with one
		/// unnamed difficulty and default rules, which is what every entrance authored before
		/// difficulties existed already is.
		/// </remarks>
		int DungeonTemplateID { get; }

		/// <summary>
		/// Achievement template to increment when a player enters this dungeon.
		/// </summary>
		AchievementTemplate AchievementTemplate { get; }
	}
}