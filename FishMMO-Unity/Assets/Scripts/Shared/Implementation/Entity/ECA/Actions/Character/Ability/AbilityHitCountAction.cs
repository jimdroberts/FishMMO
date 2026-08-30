using System;
using UnityEngine;
using FishMMO.Logging;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Adds to (or subtracts from) an ability object's remaining hit count.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The one action that moves <c>AbilityObject.HitCount</c>, and it covers both directions.
	/// A positive amount is a PIERCE — the object survives that many more impacts instead of ending
	/// on this one, which is how a projectile is made to pass through its targets. A negative amount
	/// consumes hits early, ending the object sooner than its authored count would.
	/// </para>
	/// <para>
	/// <b>There used to be two of these.</b> <c>AbilityPierceHitAction</c> was a second serialized
	/// action type with a byte-identical body under a different field name, so the subclass menu
	/// offered a designer two entries that did the same thing and a fix to one silently missed the
	/// other. It was deleted rather than kept as an alias: this one already expresses pierce, and the
	/// specialised name only described the positive half of what it did.
	/// </para>
	/// <para>
	/// Runs on every peer, deliberately — the caster's predicted copy of the object and the server's
	/// must reach the same hit count or they end at different moments. What an observer's copy does
	/// is allowed to differ, and is documented on <c>AbilityObject.ResolveSweptHits</c>.
	/// </para>
	/// </remarks>
	[Serializable]
	public sealed class AbilityHitCountAction : BaseAction
	{
		/// <summary>
		/// The value provider that determines the amount to add to the AbilityObject's HitCount.
		/// Positive pierces (the object survives that many more impacts); negative consumes hits early.
		/// </summary>
		/// <remarks>
		/// Wired to OnHit, an amount of 1 exactly cancels the decrement the impact itself applies, so
		/// the object never ends on a hit at all — bound it with a lifetime, or with a provider that
		/// runs out.
		/// </remarks>
		[Tooltip("Amount to add to the AbilityObject's HitCount. Positive pierces, negative consumes hits early.")]
		[SerializeReference, SubclassSelector]
		public IIntValueProvider AmountValue;

		/// <summary>
		/// Executes the hit count action, applying the hit count logic to the ability.
		/// </summary>
		/// <param name="initiator">The character initiating the action.</param>
		/// <param name="eventData">The event data containing ability information.</param>
		public override void Execute(ICharacter initiator, EventData eventData)
		{
			if (AmountValue == null)
			{
				Log.Warning("AbilityHitCountAction", "AmountValue provider is null.");
				return;
			}

			if (eventData.TryGet(out AbilityCollisionEventData hitEventData))
			{
				AbilityObject abilityObject = hitEventData.AbilityObject;

				if (abilityObject != null)
				{
					abilityObject.HitCount += AmountValue.GetValue(initiator, eventData);
				}
				else
				{
					Log.Warning("AbilityHitCountAction", $"AbilityCollisionEventData did not contain a valid AbilityObject for initiator {initiator?.Name}.");
				}
			}
			else
			{
				Log.Warning("AbilityHitCountAction", $"EventData does not contain AbilityCollisionEventData for initiator {initiator?.Name}.");
			}
		}
	}
}