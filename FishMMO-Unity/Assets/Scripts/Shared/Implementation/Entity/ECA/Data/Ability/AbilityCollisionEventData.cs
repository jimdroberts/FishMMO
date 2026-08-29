using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Event data for an ability hit, carrying the ability object and where the impact landed. The
	/// hit character is stored on the base <see cref="EventData.TargetCharacter"/> so all consumers
	/// can access it the same way.
	/// </summary>
	public class AbilityCollisionEventData : CollisionEventData
	{
		/// <summary>
		/// The ability object involved in the collision (e.g., projectile, area effect).
		/// </summary>
		public AbilityObject AbilityObject { get; }

		/// <summary>
		/// Initializes event data for a hit with no single impact point — a self-target dispatch or
		/// an area effect, where the whole overlap resolves at once.
		/// </summary>
		/// <param name="initiator">The character who initiated the ability.</param>
		/// <param name="hitCharacter">The character that was hit by the ability (assigned to base <see cref="EventData.TargetCharacter"/>).</param>
		/// <param name="abilityObject">The ability object involved in the hit (optional).</param>
		/// <param name="rng">Optional deterministic RNG (assigned to base <see cref="EventData.RNG"/>).</param>
		public AbilityCollisionEventData(ICharacter initiator, ICharacter hitCharacter, AbilityObject abilityObject = null, DeterministicRNG rng = null)
			: base(initiator)
		{
			AbilityObject = abilityObject;
			TargetCharacter = hitCharacter;
			Target = hitCharacter?.GameObject;
			RNG = rng;
		}

		/// <summary>
		/// Initializes event data for a hit resolved at a known point — one entry of an
		/// <see cref="AbilityObject"/>'s swept query.
		/// </summary>
		/// <param name="initiator">The character who initiated the ability.</param>
		/// <param name="hitCharacter">The character that was hit by the ability (assigned to base <see cref="EventData.TargetCharacter"/>).</param>
		/// <param name="abilityObject">The ability object involved in the hit.</param>
		/// <param name="hitPoint">World point of impact.</param>
		/// <param name="hitNormal">Surface normal at the impact.</param>
		/// <param name="rng">Optional deterministic RNG (assigned to base <see cref="EventData.RNG"/>).</param>
		public AbilityCollisionEventData(ICharacter initiator, ICharacter hitCharacter, AbilityObject abilityObject, Vector3 hitPoint, Vector3 hitNormal, DeterministicRNG rng = null)
			: base(initiator, hitPoint, hitNormal)
		{
			AbilityObject = abilityObject;
			TargetCharacter = hitCharacter;
			Target = hitCharacter?.GameObject;
			RNG = rng;
		}
	}
}
