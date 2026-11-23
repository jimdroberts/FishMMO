using System.Collections.Generic;

namespace FishMMO.Shared.Core
{
	/// <summary>
	/// What the core character save reads of a live pet (pets are the Pets module's). The save writes a pet's
	/// row, attributes and buffs with the owner's own rows, under the owner's version, so the capture stays in the core.
	/// </summary>
	public interface IPersistedPet : ICharacter
	{
		/// <summary>The owner's pet slot this pet occupies.</summary>
		int Slot { get; }

		/// <summary>The pet ability template that summoned it; 0 for none (such a pet is not saved).</summary>
		int PersistedTemplateID { get; }

		/// <summary>The ability IDs the pet knows, as saved.</summary>
		IReadOnlyList<int> PersistedAbilityIDs { get; }

		/// <summary>Moves abilities granted at summon time from the controller into <see cref="PersistedAbilityIDs"/>.</summary>
		void CaptureKnownAbilities();
	}

	/// <summary>The pets a character has out, as the core character save and the Abilities module's summon pre-filter read them (the Pets module's controller implements it).</summary>
	public interface IPetRoster : ICharacterBehaviour
	{
		/// <summary>How many slots the roster has (some may be empty).</summary>
		int PetSlotCount { get; }

		/// <summary>The pet in slot <paramref name="index"/>, or null.</summary>
		IPersistedPet PetAt(int index);

		/// <summary>True when every pet slot the character has is taken (a summon would replace the oldest pet).</summary>
		bool AllPetSlotsTaken { get; }
	}
}
