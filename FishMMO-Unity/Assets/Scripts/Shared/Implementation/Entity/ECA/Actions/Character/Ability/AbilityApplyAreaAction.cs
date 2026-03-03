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

			if (eventData.TryGet(out AbilityCollisionEventData abilityEventData))
			{
				AbilityObject abilityObject = abilityEventData.AbilityObject;

				if (abilityObject != null)
				{
					int maxHits = MaxHitsValue.GetValue(initiator, eventData);
					float radius = RadiusValue.GetValue(initiator, eventData);

					if (hits == null || hits.Length != maxHits)
					{
						hits = new Collider[maxHits];
					}

					PhysicsScene physicsScene = abilityObject.GameObject.scene.GetPhysicsScene();

					Vector3 center = abilityObject.Transform.position;
					int hitCount = physicsScene.OverlapSphere(center, radius, hits, TargetLayerMask, QueryTriggerInteraction.UseGlobal);
					var onHitEvents = abilityObject.OnHitEvents;
					if (onHitEvents == null)
					{
						Log.Warning("AbilityApplyAreaAction", "No OnHitEvents available.");
						return;
					}

					for (int i = 0; i < hitCount; i++)
					{
						var hit = hits[i];
						if (hit == null) continue;

						var targetCharacter = hit.GetComponent<ICharacter>();
						if (targetCharacter != null)
						{
							AbilityCollisionEventData collisionEvent = new AbilityCollisionEventData(initiator, targetCharacter, abilityObject);
							collisionEvent.Add(new CharacterHitEventData(initiator, targetCharacter, abilityObject.RNG));

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