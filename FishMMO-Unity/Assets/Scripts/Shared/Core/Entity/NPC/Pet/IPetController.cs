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
		/// Event triggered when a pet is destroyed.
		/// </summary>
		static Action OnPetDestroyed;

		/// <summary>
		/// Event triggered when the server confirms a change to the pet's stance or movement order.
		/// Carries the pet, which may be null if it was dismissed in the meantime.
		/// </summary>
		static Action<Pet> OnPetOrdersChanged;

		/// <summary>
		/// The pet instance managed by this controller.
		/// </summary>
		Pet Pet { get; set; }

		/// <summary>
		/// The pet's combat stance as this peer currently understands it.
		/// Server-authoritative; clients only ever request a change.
		/// </summary>
		PetStance Stance { get; set; }

		/// <summary>
		/// The pet's movement order as this peer currently understands it.
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