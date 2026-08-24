using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Pet combat preset. A plain melee-tuned attacking state with pet-appropriate defaults.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Behaviourless by design. Everything that makes a pet's combat different from a wild NPC's —
	/// leashing to a moving owner and returning to heel instead of wandering off — lives in
	/// <see cref="BaseAttackingState"/> and applies to <em>any</em> attacking state a pet is
	/// given. That matters: it means a pet healer can use
	/// <see cref="HealerAttackingState"/> and a pet tank can use
	/// <see cref="DefenderAttackingState"/> and both still behave like pets, which would be
	/// impossible if the pet rules lived in a subclass they would have to inherit from instead.
	/// </para>
	/// <para>
	/// Use this asset type for a straightforward melee pet; use the archetype states directly for
	/// anything else.
	/// </para>
	/// </remarks>
	[CreateAssetMenu(fileName = "New AI Pet Attacking State", menuName = "FishMMO/Character/NPC/AI/Pet Attacking State", order = 7)]
	public class PetAttackingState : BaseAttackingState
	{
		/// <summary>
		/// Seeds pet-appropriate defaults when the asset is first created or reset.
		/// </summary>
		private void Reset()
		{
			PreferredDistance = 0f;
			MinComfortDistance = 0f;
			AttackCooldown = 1.2f;
			TargetReevaluationRate = 2.0f;
			OwnerLeashRange = 30.0f;

			// A pet is not on a spawn-point leash; the owner leash above is what bounds it.
			LeashUpdateRate = 0f;
		}
	}
}
