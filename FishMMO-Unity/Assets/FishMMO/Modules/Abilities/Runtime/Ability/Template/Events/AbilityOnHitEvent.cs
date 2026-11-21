using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// ScriptableObject event triggered when an ability object collides or hits a character.
	/// </summary>
	[CreateAssetMenu(fileName = "New Ability On Hit Event", menuName = "FishMMO/Abilities/Events/Ability On Hit Event", order = 0)]
	public class AbilityOnHitEvent : AbilityEvent
	{
		/// <summary>
		/// Runs the hit's chain inside an attack scope naming the cast and the character struck, so a hostile effect the harm
		/// rule refuses can be counted once per cast (<see cref="HarmRule.BeginAttack"/>). Every hit route (projectile
		/// sweep, target, hitscan, area) reaches its chain through here with the ability object on the event.
		/// </summary>
		public override void Execute(EventData eventData)
		{
			if (!(eventData is AbilityCollisionEventData hit) || hit.AbilityObject == null)
			{
				base.Execute(eventData);
				return;
			}
			HarmRule.AttackScope previous = HarmRule.BeginAttack(CastKey(hit.AbilityObject), hit.TargetCharacter, hit.IsSplash);
			try
			{
				base.Execute(eventData);
			}
			finally
			{
				HarmRule.EndAttack(previous);
			}
		}

		/// <summary>One cast: its container (shared by every object the cast spawned) and its ability.</summary>
		private static long CastKey(AbilityObject abilityObject)
		{
			long ability = abilityObject.Ability != null ? abilityObject.Ability.ID : 0L;
			return unchecked(((long)abilityObject.ContainerID << 32) ^ ability ^ (ability >> 32));
		}
	}
}
