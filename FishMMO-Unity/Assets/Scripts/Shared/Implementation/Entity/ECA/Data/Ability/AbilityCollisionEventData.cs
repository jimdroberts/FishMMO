using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Event data for an ability collision, carrying the ability object and Unity collision
	/// data. The hit character is stored on the base <see cref="EventData.TargetCharacter"/>
	/// so all consumers can access it the same way.
	/// </summary>
	public class AbilityCollisionEventData : CollisionEventData
	{
		/// <summary>
		/// The ability object involved in the collision (e.g., projectile, area effect).
		/// </summary>
		public AbilityObject AbilityObject { get; }

		/// <summary>
		/// Initializes a new instance of the <see cref="AbilityCollisionEventData"/> class.
		/// </summary>
		/// <param name="initiator">The character who initiated the ability.</param>
		/// <param name="hitCharacter">The character that was hit by the ability (assigned to base <see cref="EventData.TargetCharacter"/>).</param>
		/// <param name="abilityObject">The ability object involved in the collision (optional).</param>
		/// <param name="collision">The Unity collision data (optional).</param>
		/// <param name="rng">Optional deterministic RNG (assigned to base <see cref="EventData.RNG"/>).</param>
		public AbilityCollisionEventData(ICharacter initiator, ICharacter hitCharacter, AbilityObject abilityObject = null, Collision collision = null, DeterministicRNG rng = null)
			: base(initiator, collision)
		{
			AbilityObject = abilityObject;
			TargetCharacter = hitCharacter;
			Target = hitCharacter?.GameObject;
			RNG = rng;
		}
	}
}
