using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Caster archetype preset (mages, warlocks, crowd controllers). Sits at the far edge of its
	/// spell range, refuses to be meleed, and interrupts its own cast to escape when something
	/// gets inside the panic radius.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Behaviourless by design — a caster is <see cref="BaseAttackingState"/> with a long
	/// <see cref="BaseAttackingState.PreferredDistance"/>, a wide
	/// <see cref="BaseAttackingState.MinComfortDistance"/>, and a slow
	/// <see cref="BaseAttackingState.AttackCooldown"/>.
	/// </para>
	/// <para>
	/// Crowd-control casters use this same class: what makes one a controller rather than a
	/// nuker is the <see cref="AIAbilityRotation"/> on the controller (root when the target is
	/// closing, snare on cooldown, nuke otherwise), not a different state class.
	/// </para>
	/// </remarks>
	[CreateAssetMenu(fileName = "New AI Caster Attacking State", menuName = "FishMMO/Character/NPC/AI/Caster Attacking State", order = 3)]
	public class CasterAttackingState : BaseAttackingState
	{
		/// <summary>
		/// Seeds caster-appropriate defaults when the asset is first created or reset.
		/// </summary>
		private void Reset()
		{
			PreferredDistance = 22f;
			MinComfortDistance = 10f;
			EmergencyRetreatThreshold = 0.4f;
			AttackCooldown = 2.5f;
			MovementVarietyChance = 0f;
		}
	}
}
