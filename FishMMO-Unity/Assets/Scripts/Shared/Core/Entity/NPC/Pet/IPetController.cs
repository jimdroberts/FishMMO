using System;
using System.Collections.Generic;

namespace FishMMO.Shared.Core
{
	/// <summary>
	/// Interface for controllers that manage pet entities, including summoning and destruction events.
	/// </summary>
	public interface IPetController : ICharacterBehaviour
	{
		/// <summary>
		/// Event triggered when a pet is summoned. Provides the summoned pet instance.
		/// </summary>
		static Action<Pet> OnPetSummoned;

		/// <summary>
		/// Event triggered when a pet is dismissed or destroyed. Carries the pet that left, or null when every pet left at once.
		/// </summary>
		static Action<Pet> OnPetDestroyed;

		/// <summary>
		/// Event triggered when the server confirms a change to the pets' stance or movement order.
		/// Orders apply to every pet, so this carries the first pet, or null when none is out.
		/// </summary>
		static Action<Pet> OnPetOrdersChanged;

		/// <summary>
		/// The pets out, in the order they were summoned. Every order and stance applies to all of them.
		/// </summary>
		IReadOnlyList<Pet> Pets { get; }

		/// <summary>
		/// How many pets this character may keep out at once: the final value of its Pet Slots character attribute, so an
		/// archetype, an item or a buff grants slots as it moves any attribute. A summon spell's own <c>MaxPets</c> can only
		/// lower it. See <see cref="PetSlotRules"/>.
		/// </summary>
		int PetSlots { get; }

		/// <summary>
		/// Raised on the server when the Pet Slots attribute falls under a slot a pet holds, so that pet can be dismissed.
		/// </summary>
		event Action<IPetController> OnPetSlotsReduced;

		/// <summary>Adds a pet. False when it is already listed or null.</summary>
		bool AddPet(Pet pet);

		/// <summary>Removes a pet. False when it was not listed.</summary>
		bool RemovePet(Pet pet);

		/// <summary>Forgets every pet.</summary>
		void ClearPets();

		/// <summary>
		/// The pets' combat stance as this peer currently understands it.
		/// Server-authoritative; clients only ever request a change.
		/// </summary>
		PetStance Stance { get; set; }

		/// <summary>
		/// The pets' movement order as this peer currently understands it.
		/// </summary>
		PetMovementOrder MovementOrder { get; set; }

		/// <summary>
		/// The packed order a pet attack command tries its target choices in; see
		/// <c>PetAttackPriority</c>. Mirrored on this controller so the panel has something to
		/// bind to between a pet being dismissed and re-summoned.
		/// </summary>
		int AttackPriority { get; set; }

		/// <summary>
		/// Raised on the server when this controller's owner is damaged by a hostile character,
		/// so a defensive pet can come to their aid.
		/// </summary>
		/// <remarks>
		/// The first argument is the controller that raised it. Carrying it means a single shared
		/// handler can service every player without having to search the scene for whose pet just
		/// heard about an attack.
		/// </remarks>
		event Action<IPetController, ICharacter> OnOwnerAttacked;

		/// <summary>
		/// Triggers invoked when a pet is summoned.
		/// </summary>
		List<Trigger> OnPetSummonTriggers { get; }
		/// <summary>
		/// Triggers invoked when a pet is dismissed or destroyed.
		/// </summary>
		List<Trigger> OnPetDismissTriggers { get; }
	}
}