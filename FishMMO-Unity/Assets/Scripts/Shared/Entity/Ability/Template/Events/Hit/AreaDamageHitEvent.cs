using UnityEngine;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Area Damage Hit Event", menuName = "Character/Ability/Hit Event/Area Damage", order = 1)]
	public sealed class AreaDamageHitEvent : HitEvent
	{
		private static Collider[] Hits = new Collider[512];

		public int HitCount;
		public int Damage;
		public float Radius;
		public DamageAttributeTemplate DamageAttributeTemplate;
		public LayerMask CollidableLayers = -1;

		public override int Invoke(ICharacter attacker, ICharacter defender, TargetInfo hitTarget, GameObject abilityObject)
		{
			if (attacker == defender ||
				attacker == null ||
				!attacker.TryGet(out IFactionController attackerFactionController))
			{
				return 0;
			}

			PhysicsScene physicsScene = attacker.GameObject.scene.GetPhysicsScene();

			int overlapCount = physicsScene.OverlapSphere(
								hitTarget.Target.transform.position,
								Radius,
								Hits,
								CollidableLayers,
								QueryTriggerInteraction.Ignore);

			int hits = 0;
			for (int i = 0; i < overlapCount && hits < HitCount; ++i)
			{
				if (Hits[i] != attacker.Collider)
				{
					ICharacter def = Hits[i].gameObject.GetComponent<ICharacter>();
					if (def != null &&
						def.TryGet(out IFactionController defenderFactionController) &&
						def.TryGet(out ICharacterDamageController damageController) &&
						attackerFactionController.GetAllianceLevel(defenderFactionController) == FactionAllianceLevel.Enemy)
					{
						damageController.Damage(attacker, Damage, DamageAttributeTemplate);
						++hits;
					}
				}
			}
			return hits;
		}

		public override string GetFormattedDescription()
		{
			return Description.Replace("$ELEMENT$", "<color=#" + DamageAttributeTemplate.DisplayColor.ToHex() + ">" + DamageAttributeTemplate.Name + "</color>")
							  .Replace("$DAMAGE$", "<color=#" + DamageAttributeTemplate.DisplayColor.ToHex() + ">" + Damage + "</color>")
							  .Replace("$RADIUS$", Radius.ToString());
		}
	}
}