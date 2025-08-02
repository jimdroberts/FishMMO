using UnityEngine;
using FishMMO.Logging;

namespace FishMMO.Shared
{
	/// <summary>
	/// Action that applies an ability effect to all targets within a specified area.
	/// </summary>
	[CreateAssetMenu(fileName = "New Ability Apply Area Action", menuName = "FishMMO/Triggers/Actions/Ability/Target/Apply Area")]
	public class AbilityApplyAreaAction : BaseAction
	{
		/// <summary>
		/// Radius of the area effect.
		/// </summary>
		[Tooltip("Radius of the area effect.")]
		public float Radius = 5f;

		/// <summary>
		/// Maximum number of hits to process in the area.
		/// </summary>
		[Tooltip("Maximum number of hits to process in the area.")]
		public int MaxHits = 5;

		/// <summary>
		/// Layer mask to filter targets in the area.
		/// </summary>
		[Tooltip("Layer mask to filter targets in the area.")]
		public LayerMask TargetLayerMask = ~0; // All layers by default

		private Collider[] hits;

		/// <inheritdoc/>
		public override void OnLoad(string typeName, string resourceName, int resourceID)
		{
			base.OnLoad(typeName, resourceName, resourceID);
			hits = new Collider[MaxHits];
		}

		/// <summary>
		/// Executes the area effect, applying the ability to all valid targets within the radius.
		/// </summary>
		/// <param name="initiator">The character initiating the action.</param>
		/// <param name="eventData">Event data containing context for the action.</param>
		public override void Execute(ICharacter initiator, EventData eventData)
		{
			if (eventData.TryGet(out AbilityCollisionEventData abilityEventData))
			{
				AbilityObject abilityObject = abilityEventData.AbilityObject;

				if (abilityObject != null)
				{
					PhysicsScene physicsScene = abilityObject.GameObject.scene.GetPhysicsScene();

					Vector3 center = abilityObject.Transform.position;
					int hitCount = physicsScene.OverlapSphere(center, Radius, hits, TargetLayerMask, QueryTriggerInteraction.UseGlobal);
					for (int i = 0; i < hitCount; i++)
					{
						var hit = hits[i];
						if (hit == null) continue;

						var targetCharacter = hit.GetComponent<ICharacter>();
						if (targetCharacter != null)
						{
							AbilityCollisionEventData collisionEvent = new AbilityCollisionEventData(initiator, targetCharacter);
							collisionEvent.Add(new CharacterHitEventData(initiator, targetCharacter, abilityObject.RNG));

							foreach (var trigger in abilityObject.Ability.OnHitEvents.Values)
							{
								trigger?.Execute(eventData);
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

		/// <inheritdoc/>
		public override string GetFormattedDescription()
		{
			return $"Applies area effect in a <color=#FFD700>radius of {Radius}</color> units, affecting up to <color=#FFD700>{MaxHits}</color> targets.";
		}
	}
}