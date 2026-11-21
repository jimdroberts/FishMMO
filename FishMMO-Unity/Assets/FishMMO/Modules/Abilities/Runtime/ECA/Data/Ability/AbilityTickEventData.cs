using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Event data for an ability tick. The tick subject's transform is reachable via
	/// <see cref="AbilityObject"/>.<see cref="AbilityObject.Transform"/> so no separate
	/// transform field is carried here.
	/// </summary>
	public class AbilityTickEventData : EventData
	{
		/// <summary>
		/// The ability object being updated during the tick event.
		/// </summary>
		public AbilityObject AbilityObject;

		/// <summary>
		/// The time delta for the tick (update interval).
		/// </summary>
		public float DeltaTime;

		/// <summary>
		/// The authoritative server tick at which this tick event fired.
		/// Set by <see cref="AbilityObject"/> each tick before event dispatch.
		/// This is a raw authoritative tick, not a replicate-domain tick. Consumers that
		/// compare against prediction-domain state must route it through their controller's
		/// authoritative fallback rather than wrapping it directly as a <see cref="PredictionTick"/>.
		/// </summary>
		public uint CurrentTick;

		/// <summary>
		/// Initializes a new instance of the <see cref="AbilityTickEventData"/> class.
		/// </summary>
		/// <param name="initiator">The character initiating the tick event.</param>
		/// <param name="deltaTime">The time delta for the tick.</param>
		/// <param name="abilityObject">The ability object being updated.</param>
		public AbilityTickEventData(ICharacter initiator, float deltaTime, AbilityObject abilityObject)
			: base(initiator)
		{
			DeltaTime = deltaTime;
			AbilityObject = abilityObject;
		}
	}
}
