using UnityEngine;

namespace FishMMO.Shared
{
	public abstract class HitEvent : AbilityEvent
	{
		private static Collider[] Hits = new Collider[512];
		
		public int MaxHitCount;
		public bool ApplyToSelf;
		public bool ApplyToEnemy = true;
		public bool ApplyToNeutral;
		public bool ApplyToAllies;
		public float Radius;
		public LayerMask CollidableLayers = -1;

		public GameObject FXPrefab;

		/// <summary>
		/// Returns the number of hits the event has issued,
		/// </summary>
		public int Invoke(ICharacter attacker, ICharacter defender, TargetInfo hitTarget, AbilityObject abilityObject)
		{
			// Attacker should exist with a faction controller
			if (attacker == null ||
				!attacker.TryGet(out IFactionController attackerFactionController))
			{
				return 0;
			}

			// Hit Event applies directly to the defender
			if (Radius <= 0.0f)
			{
				// Defender can't be null
				if (defender == null)
				{
					return 0;
				}

				// Skip Alliance check if we are targeting ourself
				if (defender.ID == attacker.ID)
				{
					return ApplyToSelf ? OnInvoke(attacker, defender, hitTarget, abilityObject) : 0;
				}
				else if (defender.TryGet(out IFactionController defenderFactionController))
				{
					FactionAllianceLevel allianceLevel = attackerFactionController.GetAllianceLevel(defenderFactionController);

					return FactionInvoke(attacker, defender, hitTarget, abilityObject, allianceLevel);
				}
			}
			else
			{
				PhysicsScene physicsScene = attacker.GameObject.scene.GetPhysicsScene();

				int overlapCount = physicsScene.OverlapSphere(
					hitTarget.Target.position,
					Radius,
					Hits,
					CollidableLayers,
					QueryTriggerInteraction.Ignore);

				int hits = 0;
				for (int i = 0; i < overlapCount && hits < MaxHitCount; ++i)
				{
					// Get the current defenders Character
					ICharacter def = Hits[i].gameObject.GetComponent<ICharacter>();
					if (def == null)
					{
						continue;
					}

					// Skip Alliance check if we are targeting ourself
					if (def.ID == attacker.ID)
					{
						if (ApplyToSelf)
						{
							hits += OnInvoke(attacker, def, hitTarget, abilityObject);
						}
						else
						{
							continue;
						}
					}
					else if (def.TryGet(out IFactionController defFactionController))
					{
						FactionAllianceLevel allianceLevel = attackerFactionController.GetAllianceLevel(defFactionController);

						hits += FactionInvoke(attacker, defender, hitTarget, abilityObject, allianceLevel);
					}
				}
				return hits;
			}

			return 1;
		}

		protected abstract int OnInvoke(ICharacter attacker, ICharacter defender, TargetInfo hitTarget, AbilityObject abilityObject);

		private int FactionInvoke(ICharacter attacker, ICharacter defender, TargetInfo hitTarget, AbilityObject abilityObject, FactionAllianceLevel allianceLevel)
		{
			// Directly check and return the result if the conditions match
			if ((allianceLevel == FactionAllianceLevel.Enemy && ApplyToEnemy) ||
				(allianceLevel == FactionAllianceLevel.Neutral && ApplyToNeutral) ||
				(allianceLevel == FactionAllianceLevel.Ally && ApplyToAllies))
			{
				return OnInvoke(attacker, defender, hitTarget, abilityObject);
			}
			return 0;
		}

		public void OnApplyFX(Vector3 position)
		{
#if !UNITY_SERVER
			if (FXPrefab != null)
			{
				GameObject fxPrefab = Instantiate(FXPrefab, position, Quaternion.identity);
			}
#endif
		}
	}
}