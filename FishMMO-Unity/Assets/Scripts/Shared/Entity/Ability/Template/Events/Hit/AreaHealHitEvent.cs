using UnityEngine;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Area Heal Hit Event", menuName = "Character/Ability/Hit Event/Area Heal", order = 1)]
	public sealed class AreaHealHitEvent : HitEvent
	{
		private Collider[] colliders = new Collider[100];

		public int HitCount;
		public int Heal;
		public float Radius;
		public LayerMask CollidableLayers = -1;

		public override int Invoke(Character attacker, Character defender, TargetInfo hitTarget, GameObject abilityObject)
		{
			// attacker should exist with a faction controller
			if (attacker == null ||
				!attacker.TryGet(out IFactionController attackerFactionController))
			{
				return 0;
			}

			PhysicsScene physicsScene = attacker.gameObject.scene.GetPhysicsScene();

			int overlapCount = physicsScene.OverlapSphere(//Physics.OverlapCapsuleNonAlloc(
				hitTarget.Target.transform.position,
				Radius,
				colliders,
				CollidableLayers,
				QueryTriggerInteraction.Ignore);

			int hits = 0;
			for (int i = 0; i < overlapCount && hits < HitCount; ++i)
			{
				Character def = colliders[i].gameObject.GetComponent<Character>();
				if (def != null &&
					def.TryGet(out IFactionController defenderFactionController) &&
					def.TryGet(out ICharacterDamageController damageController) &&
					attackerFactionController.GetAllianceLevel(defenderFactionController) == FactionAllianceLevel.Ally)
				{
					damageController.Heal(attacker, Heal);
					++hits;
				}
			}
			return hits;
		}

		public override string GetFormattedDescription()
		{
			return Description.Replace("$HEAL$", "<color=#" + TinyColor.skyBlue.ToHex() + ">" + Heal + "</color>")
							  .Replace("$RADIUS$", Radius.ToString());
		}
	}
}