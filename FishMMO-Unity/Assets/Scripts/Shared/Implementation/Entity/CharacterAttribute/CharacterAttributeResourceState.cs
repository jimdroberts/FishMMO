namespace FishMMO.Shared
{
	/// <summary>
	/// Represents the current state of a character's resource attributes (health, mana, stamina) and regeneration timer.
	/// Used for synchronizing resource values and regeneration progress between client and server.
	/// </summary>
	public struct CharacterAttributeResourceState
	{
		/// <summary>
		/// The accumulated time delta for resource regeneration ticks.
		/// Used to track partial intervals between regeneration updates.
		/// </summary>
		public float RegenDelta;

		/// <summary>
		/// The current health value of the character.
		/// </summary>
		public float Health;

		/// <summary>
		/// The current maximum health cap (final value) of the character.
		/// </summary>
		public int MaxHealth;

		/// <summary>
		/// The current mana value of the character.
		/// </summary>
		public float Mana;

		/// <summary>
		/// The current maximum mana cap (final value) of the character.
		/// </summary>
		public int MaxMana;

		/// <summary>
		/// The current stamina value of the character.
		/// </summary>
		public float Stamina;

		/// <summary>
		/// The current maximum stamina cap (final value) of the character.
		/// </summary>
		public int MaxStamina;
	}
}