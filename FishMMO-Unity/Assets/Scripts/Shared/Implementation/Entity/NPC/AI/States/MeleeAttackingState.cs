using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Melee archetype preset. Closes to weapon reach, never backs away, and occasionally steps
	/// into a flanking or orbiting sub-state for variety.
	/// </summary>
	/// <remarks>
	/// <para>
	/// This class carries no behaviour of its own — melee is simply
	/// <see cref="BaseAttackingState"/> with <see cref="BaseAttackingState.PreferredDistance"/>
	/// and <see cref="BaseAttackingState.MinComfortDistance"/> at zero. It exists so the asset
	/// menu still offers a named starting point, and so <see cref="Reset"/> can seed sensible
	/// numbers when a designer creates one.
	/// </para>
	/// <para>
	/// It previously carried a full <c>TryAttack</c> override that duplicated the base logic and
	/// had drifted from it. Notably its flanking roll called <c>ChangeState</c> on a sub-state
	/// whose target the base <c>Exit</c> then wiped, so every roll ended the fight instead of
	/// varying it; wiring variety through <see cref="BaseAttackingState.VarietyStates"/> and
	/// <see cref="BaseAIState.KeepsCombatTarget"/> fixes that for every archetype at once.
	/// </para>
	/// </remarks>
	[CreateAssetMenu(fileName = "New AI Melee Attacking State", menuName = "FishMMO/Character/NPC/AI/Melee Attacking State", order = 1)]
	public class MeleeAttackingState : BaseAttackingState
	{
		/// <summary>
		/// Seeds melee-appropriate defaults when the asset is first created or reset in the
		/// inspector.
		/// </summary>
		private void Reset()
		{
			PreferredDistance = 0f;
			MinComfortDistance = 0f;
			AttackCooldown = 1.2f;
			MovementVarietyChance = 0.15f;
			TargetReevaluationRate = 3.0f;
		}
	}
}
