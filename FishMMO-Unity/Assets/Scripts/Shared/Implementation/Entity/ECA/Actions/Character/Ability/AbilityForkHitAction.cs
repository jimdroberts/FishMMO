using System;
using UnityEngine;
using FishMMO.Logging;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Action that turns an ability object onto a new heading inside a cone when it hits something —
	/// a projectile that scatters or ricochets off its target rather than carrying straight on.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>This used to do nothing.</b> It assigned <c>abilityObject.Transform.rotation</c>, but an
	/// ability object's position is a closed form evaluated from its spawn pose and its integer tick
	/// count (<see cref="AbilityMoveTransformAction"/>), so the next tick recomputed the position from
	/// the original spawn line and threw the turn away. The projectile kept flying dead straight while
	/// its mesh — and the knockback direction, which <see cref="KnockbackHitAction"/> reads off
	/// <c>Transform.forward</c> — spun to a heading nothing travelled along.
	/// <see cref="AbilityObject.Redirect"/> moves the closed form's own inputs instead, which is what
	/// actually starts a new leg.
	/// </para>
	/// <para>
	/// <b>Runs on every peer, like the rest of the OnHit chain.</b> The new heading is drawn from the
	/// ability object's own <see cref="AbilityObject.RNG"/>, whose state is carried in the reconcile,
	/// so the server and the caster's owner turn the same way for the same hit.
	/// </para>
	/// <para>
	/// <b>An observer forks on the server's hit, not on one of its own.</b> This action used to be
	/// the loudest symptom of a third-party observer resolving hits against its own interpolated
	/// world: for most OnHit actions an invented hit cost an effect in the wrong place, but for this
	/// one the observer's copy veered onto a heading the server never took and stayed wrong for the
	/// rest of its life — and, because the trajectory is a closed form re-anchored by
	/// <see cref="AbilityObject.Redirect"/>, the destroy message then ended it somewhere the server's
	/// copy had never been. Observers no longer sweep; they are told which body was hit
	/// (<c>AbilityObjectHitBroadcast</c>) and run this action from that, so the fork they draw is the
	/// one that happened. See <see cref="AbilityObject.ResolveSweptHits"/>.
	/// </para>
	/// </remarks>
	[Serializable]
	public class AbilityForkHitAction : BaseAction
	{
		/// <summary>
		/// The value provider that determines the total arc, in degrees, the new heading is drawn from.
		/// </summary>
		/// <remarks>
		/// A TOTAL spread about the current heading, so 90 means 45 degrees either side. Zero leaves the
		/// heading alone; 360 or more is any direction at all.
		/// </remarks>
		[Tooltip("The value provider that determines the total arc in degrees for the fork spread. 90 means 45 degrees either side of the current heading.")]
		[SerializeReference, SubclassSelector]
		public IFloatValueProvider ArcValue;

		/// <summary>
		/// Turns the ability object onto a new heading drawn from within the configured arc.
		/// </summary>
		/// <param name="initiator">The character initiating the action.</param>
		/// <param name="eventData">The event data containing ability collision information.</param>
		public override void Execute(ICharacter initiator, EventData eventData)
		{
			if (ArcValue == null)
			{
				Log.Warning("AbilityForkHitAction", "ArcValue provider is null.");
				return;
			}

			if (!eventData.TryGet(out AbilityCollisionEventData abilityEventData))
			{
				return;
			}

			AbilityObject abilityObject = abilityEventData.AbilityObject;
			if (abilityObject == null || abilityObject.Transform == null)
			{
				return;
			}

			float arc = ArcValue.GetValue(initiator, eventData);

			/* The object's own generator, never a shared one: this draw has to come out the same on the
			 * server and on the caster's client or the two copies fly apart from the fork onwards. */
			Quaternion heading = abilityObject.Transform.forward.GetRandomConicalDirection(arc, abilityObject.RNG);

			abilityObject.Redirect(heading);
		}
	}
}
