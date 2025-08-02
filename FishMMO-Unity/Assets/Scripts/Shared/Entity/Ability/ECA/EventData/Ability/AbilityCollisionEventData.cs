using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Event data for an ability collision, containing information about the hit character and the ability object involved.
	/// </summary>
	public class AbilityCollisionEventData : CollisionEventData
	{
		/// <summary>
		/// The character that was hit by the ability.
		/// </summary>
		public ICharacter HitCharacter { get; }

		/// <summary>
		/// The ability object involved in the collision (e.g., projectile, area effect).
		/// </summary>
		public AbilityObject AbilityObject { get; }

		/// <summary>
		/// Initializes a new instance of the <see cref="AbilityCollisionEventData"/> class.
		/// </summary>
		/// <param name="initiator">The character who initiated the ability.</param>
		/// <param name="hitCharacter">The character that was hit by the ability.</param>
		/// <param name="abilityObject">The ability object involved in the collision (optional).</param>
		/// <param name="collision">The Unity collision data (optional).</param>
		public AbilityCollisionEventData(ICharacter initiator, ICharacter hitCharacter, AbilityObject abilityObject = null, Collision collision = null)
			: base(initiator, collision)
		{
			HitCharacter = hitCharacter;
			AbilityObject = abilityObject;
		}
	}
}