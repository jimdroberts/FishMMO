using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Ranged archetype preset (archers, hunters, gunners). Holds a working distance, kites when
	/// the target closes, and breaks away hard once the target is inside the panic radius.
	/// </summary>
	/// <remarks>
	/// Behaviourless by design: ranged combat is <see cref="BaseAttackingState"/> with a non-zero
	/// <see cref="BaseAttackingState.PreferredDistance"/> and
	/// <see cref="BaseAttackingState.MinComfortDistance"/>. Assign a strafe asset with
	/// <see cref="BaseAIState.KeepsCombatTarget"/> enabled to
	/// <see cref="BaseAttackingState.VarietyStates"/> to get shoot-and-move behaviour.
	/// </remarks>
	[CreateAssetMenu(fileName = "New AI Ranged Attacking State", menuName = "FishMMO/Character/NPC/AI/Ranged Attacking State", order = 2)]
	public class RangedAttackingState : BaseAttackingState
	{
		/// <summary>
		/// Seeds archer-appropriate defaults when the asset is first created or reset.
		/// </summary>
		private void Reset()
		{
			PreferredDistance = 15f;
			MinComfortDistance = 5f;
			EmergencyRetreatThreshold = 0.5f;
			AttackCooldown = 1.5f;
			MovementVarietyChance = 0.2f;
		}
	}
}
