namespace FishMMO.Shared
{
	/// <summary>
	/// Specifies the types of consumable items available in the game, used for categorization and logic.
	/// </summary>
	public enum ConsumableType : byte
	{
		/// <summary>
		/// A potion item, typically used for healing or buffs.
		/// </summary>
		Potion,

		/// <summary>
		/// A food item, typically used for restoring hunger or providing temporary effects.
		/// </summary>
		Food,

		/// <summary>
		/// A mount item, used to summon or activate a mount.
		/// </summary>
		Mount,

		/// <summary>
		/// A scroll item, typically used for casting spells or triggering special effects.
		/// </summary>
		Scroll,
	}
}