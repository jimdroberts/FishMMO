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
		/// The pet instance managed by this controller.
		/// </summary>
		Pet Pet { get; set; }

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