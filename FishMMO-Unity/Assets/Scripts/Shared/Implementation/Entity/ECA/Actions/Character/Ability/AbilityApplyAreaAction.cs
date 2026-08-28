using System;
using UnityEngine;
using FishMMO.Logging;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Action that applies an ability effect to all targets within a specified area.
	/// </summary>
	[Serializable]
	public class AbilityApplyAreaAction : BaseAction
	{
		/// <summary>
		/// The value provider that determines the radius of the area effect.
		/// </summary>
		[Tooltip("The value provider that determines the radius of the area effect.")]
		[SerializeReference, SubclassSelector]
		public IFloatValueProvider RadiusValue;

		/// <summary>
		/// The value provider that determines the maximum number of hits to process in the area.
		/// </summary>
		[Tooltip("The value provider that determines the maximum number of hits.")]
		[SerializeReference, SubclassSelector]
		public IIntValueProvider MaxHitsValue;

		/// <summary>
		/// Layer mask to filter targets in the area.
		/// </summary>
		[Tooltip("Layer mask to filter targets in the area.")]
		public LayerMask TargetLayerMask = ~0;

		[NonSerialized]
		private Collider[] hits;

		/// <summary>
		/// Executes the area effect, applying the ability to all valid targets within the radius.
		/// </summary>
		/// <param name="initiator">The character initiating the action.</param>
		/// <param name="eventData">Event data containing context for the action.</param>
		public override void Execute(ICharacter initiator, EventData eventData)
		{
			if (RadiusValue == null || MaxHitsValue == null)
			{
				Log.Warning("AbilityApplyAreaAction", "RadiusValue or MaxHitsValue provider is null.");
				return;
			}

			if (eventData != null && eventData.TryGet(out AbilityCollisionEventData abilityEventData))
			{
				AbilityObject abilityObject = abilityEventData.AbilityObject;

				if (abilityObject != null)
				{
					/* Server only. Physics queries are not deterministic across peers, so this
					 * must run exactly once, where hits are authoritative. It used to gate on the
					 * attached tick being a replicate-domain tick instead — but the server's own
					 * spawn and self-target dispatches carry replicate ticks too, so any area effect
					 * wired to OnSpawn/OnPreSpawn never ran anywhere. Clients receive the results
					 * through the usual authoritative paths. */
					if (!abilityObject.IsServer)
					{
						return;
					}

					int maxHits = MaxHitsValue.GetValue(initiator, eventData);
					float radius = RadiusValue.GetValue(initiator, eventData);

					if (hits == null || hits.Length != maxHits)
					{
						hits = new Collider[maxHits];
					}

					Vector3 center = abilityObject.Transform.position;
					/* Resolved against where the caster's client saw these characters, not where
					 * they are now. The ability object's own position needs no compensation: its
					 * motion is deterministic, so every peer already agrees on it. */
					int hitCount = LagCompensatedQuery.OverlapSphere(
						eventData, abilityObject.GameObject, center, radius, hits, TargetLayerMask);
					var onHitEvents = abilityObject.OnHitEvents;
					if (onHitEvents == null)
					{
						Log.Warning("AbilityApplyAreaAction", "No OnHitEvents available.");
						return;
					}

					// Extract tick context once from the parent event so each child collision
					// event inherits it. Without this, downstream ApplyBuffAction falls back
					// to TimeManager.LocalTick and loses tick alignment in prediction paths.
					eventData.TryGet(out TickEventData tickToPropagate);

					for (int i = 0; i < hitCount; i++)
					{
						var hit = hits[i];
						if (hit == null) continue;

						var targetCharacter = hit.GetComponent<ICharacter>();
						if (targetCharacter != null)
						{
							AbilityCollisionEventData collisionEvent = new AbilityCollisionEventData(initiator, targetCharacter, abilityObject, null, abilityObject.RNG);
							if (tickToPropagate != null)
							{
								collisionEvent.Add(tickToPropagate);
							}

							foreach (var trigger in onHitEvents.Values)
							{
								trigger?.Execute(collisionEvent);
							}
						}
					}
				}
				else
				{
					Log.Warning("AbilityApplyAreaAction", "AbilityObject is null.");
				}
			}
			else
			{
				Log.Warning("AbilityApplyAreaAction", "Expected AbilityCollisionEventData.");
			}
		}
	}
}