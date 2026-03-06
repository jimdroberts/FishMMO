using UnityEngine;
using UnityEngine.AI;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// AI attacking state for melee-focused NPCs. Closes to melee range, uses short-range abilities,
	/// and occasionally transitions to orbit or get-behind states for more interesting combat.
	/// <para>
	/// Recommended <see cref="BaseAttackingState.PreferredDistance"/> of 0 (melee range),
	/// <see cref="BaseAttackingState.MinComfortDistance"/> of 0 (never retreat).
	/// </para>
	/// </summary>
	[CreateAssetMenu(fileName = "New AI Melee Attacking State", menuName = "FishMMO/Character/NPC/AI/Melee Attacking State", order = 1)]
	public class MeleeAttackingState : BaseAttackingState
	{
		/// <summary>
		/// Optional orbit state to transition to mid-combat for variety.
		/// Leave null to disable orbit transitions.
		/// </summary>
		[Header("Melee Combat Behavior")]
		[Tooltip("Optional orbit state for mid-combat variety.")]
		public BaseAIState OrbitState;

		/// <summary>
		/// Optional get-behind state to transition to mid-combat for flanking.
		/// Leave null to disable flanking transitions.
		/// </summary>
		[Tooltip("Optional get-behind state for flanking.")]
		public BaseAIState GetBehindState;

		/// <summary>
		/// Chance (0-1) each attack cycle to transition to a movement sub-state
		/// (orbit or get-behind) instead of attacking immediately.
		/// </summary>
		[Range(0f, 1f)]
		[Tooltip("Chance per attack cycle to transition to orbit or get-behind.")]
		public float MovementVarietyChance = 0.15f;

		/// <summary>
		/// When the target is farther than this multiplier times the agent radius,
		/// the NPC will sprint toward the target rather than walk.
		/// </summary>
		[Tooltip("Distance multiplier beyond which the NPC sprints to close the gap.")]
		public float SprintDistanceMultiplier = 3.0f;

		/// <summary>
		/// Called when entering the melee attacking state. Ensures the NPC runs.
		/// </summary>
		public override void Enter(AIController controller)
		{
			base.Enter(controller);
			// Melee fighters always run toward the target.
			controller.Agent.speed = Constants.Character.RunSpeed;
		}

		/// <summary>
		/// Core melee attack logic. Closes to melee range, picks short-range abilities,
		/// and occasionally transitions to orbit or flanking states for variety.
		/// </summary>
		protected override void TryAttack(AIController controller, ICharacter targetCharacter)
		{
			if (!controller.Character.TryGet(out IAbilityController abilityController))
			{
				controller.TransitionToIdleState();
				return;
			}

			float distance = Mathf.Sqrt(controller.GetSqrDistanceToTarget());
			float meleeRange = (controller.Agent.radius * 2.0f).Min(1.0f);

			// If currently casting, stop and wait. Auto-release charged abilities.
			if (abilityController.IsActivating || abilityController.AbilityQueued)
			{
				controller.Agent.isStopped = true;

				if (abilityController.IsActivating && abilityController.RemainingActivationTime <= 0f)
				{
					abilityController.Release();
				}
				return;
			}

			// Occasionally introduce movement variety instead of a straight attack.
			if (distance <= meleeRange * 1.5f && Random.value < MovementVarietyChance)
			{
				BaseAIState varietyState = PickVarietyState();
				if (varietyState != null)
				{
					controller.ChangeState(varietyState);
					return;
				}
			}

			// Pick a short-range ability.
			Ability chosenAbility = controller.PickBestAbility(meleeRange * 2f);

			if (chosenAbility == null)
			{
				// All abilities on cooldown — stay in melee range and wait.
				if (distance > meleeRange)
				{
					MoveTowardTarget(controller, meleeRange);
				}
				else
				{
					controller.Agent.isStopped = true;
				}
				return;
			}

			float abilityRange = chosenAbility.Range;

			if (distance <= abilityRange)
			{
				// In range — stop and attack.
				PerformAttack(controller, abilityController, chosenAbility, targetCharacter, distance);
			}
			else
			{
				// Close the gap.
				MoveTowardTarget(controller, abilityRange * 0.8f);
			}
		}

		/// <summary>
		/// Performs a melee attack. Stops the agent and activates the ability.
		/// </summary>
		public override void PerformAttack(AIController controller, IAbilityController abilityController, Ability ability, ICharacter targetCharacter, float distance)
		{
			controller.Agent.isStopped = true;
			bool held = abilityController.RequiresHeld(ability.ID);
			abilityController.Activate(ability.ID, held);
		}

		/// <summary>
		/// Picks a random variety state (orbit or get-behind) to break up melee attack patterns.
		/// Returns null if no variety states are configured.
		/// </summary>
		private BaseAIState PickVarietyState()
		{
			bool hasOrbit = OrbitState != null;
			bool hasGetBehind = GetBehindState != null;

			if (hasOrbit && hasGetBehind)
			{
				return Random.value < 0.5f ? OrbitState : GetBehindState;
			}
			if (hasOrbit) return OrbitState;
			if (hasGetBehind) return GetBehindState;
			return null;
		}
	}
}