using FishNet.CodeGenerating;

namespace FishMMO.Shared
{
	/// <summary>
	/// Represents the current state of a character's resource attributes (health, mana, stamina) and regeneration timer.
	/// Used for synchronizing resource values and regeneration progress between client and server.
	/// </summary>
	[UseGlobalCustomSerializer]
	public struct CharacterAttributeResourceState
	{
		/// <summary>
		/// Absolute simulation tick at which the next resource-regeneration pulse should fire.
		/// Reconciled so both client and server advance regeneration from the same tick,
		/// eliminating the modulo-scheduling race that occurs when TickDelta changes at runtime
		/// (a new modulo interval can produce an immediate spurious pulse or a long gap).
		/// Zero means regeneration has not been scheduled yet (controller not yet started).
		/// </summary>
		public uint NextRegenTick;
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