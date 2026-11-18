using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Event data for an ability object's OnDestroy dispatch — the fourth ability event payload.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why this exists.</b> OnDestroy used to dispatch a bare <see cref="EventData"/> carrying
	/// only the caster and a tick. <see cref="AbilityObject.TryResolveFrom"/> reads the collision,
	/// spawn and tick payloads, so every object-scoped action wired to OnDestroy —
	/// <c>AbilityApplyAreaAction</c>, <c>AbilityApplyHitscanAction</c>, <c>AbilityHitCountAction</c>,
	/// <c>AbilityForkHitAction</c>, <c>AbilityMoveTransformAction</c>,
	/// <c>AbilitySpawnMultiplyAction</c>, <c>AbilityApplyTargetAction</c> — resolved nothing and
	/// returned having silently done nothing. That made the most ordinary shape in the genre,
	/// <i>a projectile that detonates in an area when its lifetime runs out</i>, impossible to
	/// author, and it failed quietly rather than warning.
	/// </para>
	/// <para>
	/// <b>The spatial origin is the OBJECT, not the caster.</b> <see cref="EventData.Target"/> is
	/// set to the dying object's GameObject so a selector on an OnDestroy trigger centres on where
	/// the projectile ENDED. Falling back to the initiator, as an empty event does, would centre a
	/// detonation on the caster — the one place it certainly did not happen.
	/// </para>
	/// <para>
	/// <see cref="EventData.TargetCharacter"/> is deliberately left null: an expiring object struck
	/// nobody, and an action that resolves a target strictly should decline rather than invent one.
	/// The per-candidate characters come from the trigger's own selector fan-out, as everywhere else.
	/// </para>
	/// </remarks>
	public class AbilityDestroyEventData : EventData
	{
		/// <summary>The object being destroyed. Still positioned and active while the events run.</summary>
		public AbilityObject AbilityObject { get; }

		/// <summary>
		/// Builds the payload for one object's destroy dispatch.
		/// </summary>
		/// <param name="initiator">The caster, or the snapshot phantom that replaced it.</param>
		/// <param name="abilityObject">The object being destroyed.</param>
		public AbilityDestroyEventData(ICharacter initiator, AbilityObject abilityObject)
			: base(initiator)
		{
			AbilityObject = abilityObject;

			/* The dying object's own GameObject, so GetContext resolves the detonation point. Safe
			 * to read here: DestroyAbilityObjectInternal runs the destroy triggers BEFORE it
			 * deactivates and destroys the instance. */
			if (abilityObject != null)
			{
				Target = abilityObject.GameObject != null ? abilityObject.GameObject : abilityObject.gameObject;
			}
		}
	}
}
