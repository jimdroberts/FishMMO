using UnityEngine;
using UnityEngine.AI;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// AI attacking state for caster-focused NPCs (mages, healers, etc.).
	/// Keeps maximum distance from the target, prioritizes the longest-range abilities,
	/// and retreats aggressively when the target closes in. Stops movement completely
	/// while casting for accuracy, and uses wander transitions to reposition when all
	/// abilities are on cooldown.
	/// <para>
	/// Recommended <see cref="BaseAttackingState.PreferredDistance"/> of 15-30,
	/// <see cref="BaseAttackingState.MinComfortDistance"/> of 8-12,
	/// <see cref="BaseAttackingState.AttackCooldown"/> of 2-3.
	/// </para>
	/// </summary>
	[CreateAssetMenu(fileName = "New AI Caster Attacking State", menuName = "FishMMO/Character/NPC/AI/Caster Attacking State", order = 3)]
	public class CasterAttackingState : BaseAttackingState
	{
		/// <summary>
		/// Optional retreat state for emergency backing off when target is dangerously close.
		/// If null, the built-in retreat logic is used.
		/// </summary>
		[Header("Caster Combat Behavior")]
		[Tooltip("Optional retreat state for emergency backing off.")]
		public BaseAIState RetreatState;

		/// <summary>
		/// Optional wander state to reposition while all abilities are on cooldown.
		/// Leave null to simply hold position during cooldowns.
		/// </summary>
		[Tooltip("Optional wander state for cooldown repositioning.")]
		public BaseAIState WanderState;

		/// <summary>
		/// Fraction (0-1) of MinComfortDistance that triggers an emergency retreat.
		/// At this distance the caster abandons everything and flees.
		/// </summary>
		[Range(0f, 1f)]
		[Tooltip("Fraction of MinComfortDistance that triggers emergency retreat.")]
		public float EmergencyRetreatThreshold = 0.4f;

		/// <summary>
		/// Chance (0-1) to reposition (wander) when all abilities are on cooldown,
		/// making the caster harder to pin down.
		/// </summary>
		[Range(0f, 1f)]
		[Tooltip("Chance to wander/reposition when all abilities are on cooldown.")]
		public float CooldownRepositionChance = 0.3f;

		/// <summary>
		/// Called when entering the caster attacking state.
		/// Casters run to maintain distance.
		/// </summary>
		public override void Enter(AIController controller)
		{
			base.Enter(controller);
			controller.Agent.speed = Constants.Character.RunSpeed;
		}

		/// <summary>
		/// Core caster attack logic. Prioritizes staying at maximum range,
		/// picks the longest-range ability available, retreats aggressively
		/// when the target closes in, and repositions during cooldowns.
		/// </summary>
		protected override void TryAttack(AIController controller, ICharacter targetCharacter)
		{
			if (!controller.Character.TryGet(out IAbilityController abilityController))
			{
				controller.TransitionToIdleState();
				return;
			}

			float distance = Mathf.Sqrt(controller.GetSqrDistanceToTarget());

			// If currently casting, hold position completely. Auto-release charged abilities.
			if (HandleActivationInProgress(controller, abilityController))
				return;

			// Emergency retreat — target is dangerously close.
			if (MinComfortDistance > 0f && distance < MinComfortDistance * EmergencyRetreatThreshold)
			{
				// Interrupt any pending cast and flee.
				abilityController.Interrupt(null);

				if (RetreatState != null)
				{
					controller.ChangeState(RetreatState);
					return;
				}
				RetreatFromTarget(controller, PreferredDistance);
				return;
			}

			// Uncomfortable — kite while trying to get off a quick spell.
			if (MinComfortDistance > 0f && distance < MinComfortDistance)
			{
				Ability quickAbility = controller.PickBestAbility(distance);
				if (quickAbility != null && distance <= quickAbility.Range)
				{
					// Fire off ability while retreating.
					bool held = abilityController.RequiresHeld(quickAbility.ID);
					abilityController.Activate(quickAbility.ID, held);
				}
				RetreatFromTarget(controller, PreferredDistance);
				return;
			}

			// Comfortable range — pick the best (preferably longest-range) ability.
			Ability bestAbility = controller.PickBestAbility(PreferredDistance);
			if (bestAbility == null)
			{
				// All abilities on cooldown — consider repositioning.
				DeterministicRNG rng = controller.NpcRNG;
				float roll = (rng ?? DeterministicRNG.Shared).NextFloat();
				if (WanderState != null && roll < CooldownRepositionChance)
				{
					controller.ChangeState(WanderState);
					return;
				}
				ManagePositioning(controller, distance);
				return;
			}

			float abilityRange = bestAbility.Range;

			if (distance <= abilityRange)
			{
				PerformAttack(controller, abilityController, bestAbility, targetCharacter, distance);
			}
			else
			{
				// Close in, but try to stay at the edge of range.
				float targetDistance = abilityRange * 0.9f;
				if (targetDistance > PreferredDistance)
				{
					targetDistance = PreferredDistance;
				}
				MoveTowardTarget(controller, targetDistance);
			}
		}

		/// <summary>
		/// Casters always try to stay at their preferred (maximum) distance.
		/// They back away if the target gets closer than comfort range.
		/// </summary>
		protected override void ManagePositioning(AIController controller, float distance)
		{
			if (controller.Target == null) return;

			if (MinComfortDistance > 0f && distance < MinComfortDistance)
			{
				RetreatFromTarget(controller, PreferredDistance);
			}
			else if (distance > PreferredDistance * 1.3f)
			{
				// Too far — close in to casting range, but not too close.
				MoveTowardTarget(controller, PreferredDistance);
			}
			else
			{
				// At optimal range — hold position.
				controller.Agent.isStopped = true;
			}
		}
	}
}