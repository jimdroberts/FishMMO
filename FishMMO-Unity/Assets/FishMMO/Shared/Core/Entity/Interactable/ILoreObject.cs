namespace FishMMO.Shared.Core
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

		/// <summary>
		/// Claims this character's one-time item grant, returning false if they already took it.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <see cref="LoreObjectTemplate.GrantItems"/> had no repeat guard of any kind. The ability
		/// and ability-event grants beside it are idempotent because the controller already knows
		/// what the character has learned, but items have no such memory — so re-reading a lore
		/// object handed out the whole item list again, as fast as the one-second interaction
		/// debounce allowed. Any lore object with an item on it was an unbounded item source.
		/// </para>
		/// <para>
		/// <b>Scope: per character, per lore object, for the lifetime of the server process.</b>
		/// The record lives on the lore object in the scene and is not written to the database, so
		/// a restart lets each character claim once more. That is the same latitude every other
		/// piece of interactable world state in this project already takes — gathering node
		/// charges, capture point ownership, shrine cooldowns — and it turns an unbounded faucet
		/// into a single grant per restart, which is not farmable. Making it survive restarts needs
		/// a per-character discovery table; when that exists this is the one call site to change.
		/// </para>
		/// </remarks>
		/// <param name="characterID">The character reading the lore object.</param>
		/// <returns>True when the grant was unclaimed and is now recorded as taken.</returns>
		bool TryConsumeItemGrant(long characterID);
	}
}