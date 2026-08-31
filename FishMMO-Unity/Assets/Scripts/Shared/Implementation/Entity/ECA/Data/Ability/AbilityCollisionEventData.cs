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
		/// True when this hit is the SERVER'S answer arriving on a peer that did not resolve it,
		/// rather than something this peer worked out for itself.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Set only by <see cref="AbilityObject.ApplyObservedHit"/>, which is how
		/// <c>AbilityObjectHitBroadcast</c> reaches the OnHit events. Every other producer — the
		/// server's own sweep, the caster's predicted sweep, the area and hitscan queries — leaves
		/// it false, because those peers decided the hit themselves.
		/// </para>
		/// <para>
		/// <b>What reads it, and why it has to exist.</b> The caster's own client is sent the hit
		/// broadcast as well as everybody else, so that an owner which mispredicted a MISS still
		/// plays its impact. That correction runs the same OnHit events a predicted hit runs, and
		/// <c>ApplyDamageAction</c> could not tell the two apart — so it drew a PREDICTED damage
		/// label for a hit the server had already reported. The server's own
		/// <c>CombatEventBroadcast</c> for that hit is unreliable while the hit broadcast is
		/// reliable, and the two have no ordering relationship, so whenever the unreliable one won
		/// the race <c>PredictedCombatEvents.TryConfirm</c> found nothing pending, the display drew
		/// the server's number, and the prediction drawn moments later became a second label for
		/// one hit.
		/// </para>
		/// <para>
		/// A flag on the event rather than a check in the display: the display cannot distinguish
		/// them either — both arrive as an ordinary prediction — and the number that should not be
		/// drawn is better not produced than filtered afterwards.
		/// </para>
		/// </remarks>
		public bool IsAuthoritativeEcho { get; }

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
		/// <param name="isAuthoritativeEcho">True when the server resolved this hit and this peer is being told. See <see cref="IsAuthoritativeEcho"/>.</param>
		public AbilityCollisionEventData(ICharacter initiator, ICharacter hitCharacter, AbilityObject abilityObject, Vector3 hitPoint, Vector3 hitNormal, DeterministicRNG rng = null, bool isAuthoritativeEcho = false)
			: base(initiator, hitPoint, hitNormal)
		{
			AbilityObject = abilityObject;
			IsAuthoritativeEcho = isAuthoritativeEcho;
			TargetCharacter = hitCharacter;
			Target = hitCharacter?.GameObject;
			RNG = rng;
		}
	}
}
