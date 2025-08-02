using System;

namespace FishMMO.Shared
{
	/// <summary>
	/// Interface for character damage controllers, providing damage, healing, and death logic for a character.
	/// Includes static events for global damage/heal/kill notifications and resource management properties.
	/// </summary>
	public interface ICharacterDamageController : ICharacterBehaviour, IDamageable, IHealable
	{
		/// <summary>
		/// Event invoked when a character is damaged. Parameters: attacker, defender, amount, damage type.
		/// </summary>
		static Action<ICharacter, ICharacter, int, DamageAttributeTemplate> OnDamaged;

		/// <summary>
		/// Event invoked when a character is killed. Parameters: killer, victim.
		/// </summary>
		static Action<ICharacter, ICharacter> OnKilled;

		/// <summary>
		/// Event invoked when a character is healed. Parameters: healer, healed, amount.
		/// </summary>
		static Action<ICharacter, ICharacter, int> OnHealed;

		/// <summary>
		/// Gets or sets whether the character is immortal (cannot be damaged or killed).
		/// </summary>
		bool Immortal { get; set; }

		/// <summary>
		/// Returns true if the character is alive (resource attribute's current value is above zero).
		/// </summary>
		bool IsAlive { get; }

		/// <summary>
		/// Gets the cached health resource attribute for this character.
		/// </summary>
		CharacterResourceAttribute ResourceInstance { get; }

		/// <summary>
		/// Kills the character, optionally specifying the killer.
		/// </summary>
		/// <param name="killer">The character responsible for the kill (can be null).</param>
		void Kill(ICharacter killer);

		/// <summary>
		/// Fully heals the character to maximum resource value.
		/// </summary>
		void CompleteHeal();
	}
}