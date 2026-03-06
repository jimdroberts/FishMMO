using UnityEngine;
using UnityEngine.AI;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// AI attacking state for ranged-focused NPCs (archers, hunters, etc.).
	/// Maintains a preferred distance from the target, uses ranged abilities, and kites/retreats
	/// when the target closes in. Occasionally strafes (orbits) for more dynamic combat.
	/// <para>
	/// Recommended <see cref="BaseAttackingState.PreferredDistance"/> of 10-20,
	/// <see cref="BaseAttackingState.MinComfortDistance"/> of 4-6.
	/// </para>
	/// </summary>
	[CreateAssetMenu(fileName = "New AI Ranged Attacking State", menuName = "FishMMO/Character/NPC/AI/Ranged Attacking State", order = 2)]
	public class RangedAttackingState : BaseAttackingState
	{
		/// <summary>
		/// Optional orbit state for strafing while attacking.
		/// Leave null to disable strafe transitions.
		/// </summary>
		[Header("Ranged Combat Behavior")]
		[Tooltip("Optional orbit state for strafing while attacking.")]
		public BaseAIState StrafeState;

		/// <summary>
		/// Optional retreat state for emergency retreat when target gets too close.
		/// Leave null to use built-in retreat logic.
		/// </summary>
		[Tooltip("Optional retreat state for emergency backing off.")]
		public BaseAIState RetreatState;

		/// <summary>
		/// Chance (0-1) per attack cycle to transition to a strafe state instead of
		/// standing still and shooting.
		/// </summary>
		[Range(0f, 1f)]
		[Tooltip("Chance per attack cycle to strafe while shooting.")]
		public float StrafeChance = 0.2f;

		/// <summary>
		/// If the target is closer than this fraction of MinComfortDistance,
		/// the NPC will use the RetreatState (if configured) or aggressively back away.
		/// </summary>
		[Range(0f, 1f)]
		[Tooltip("Fraction of MinComfortDistance that triggers emergency retreat.")]
		public float EmergencyRetreatThreshold = 0.5f;

		/// <summary>
		/// Called when entering the ranged attacking state.
		/// </summary>
		public override void Enter(AIController controller)
		{
			base.Enter(controller);
			// Ranged fighters try to maintain distance, run speed to reposition.
			controller.Agent.speed = Constants.Character.RunSpeed;
		}

		/// <summary>
		/// Core ranged attack logic. Maintains preferred distance, kites when target gets close,
		/// and uses ranged abilities when in range.
		/// </summary>
		protected override void TryAttack(AIController controller, ICharacter targetCharacter)
		{
			if (!controller.Character.TryGet(out IAbilityController abilityController))
			{
				controller.TransitionToIdleState();
				return;
			}

			float distance = Mathf.Sqrt(controller.GetSqrDistanceToTarget());

			// If currently casting, hold position. Auto-release charged abilities.
			if (HandleActivationInProgress(controller, abilityController))
				return;

			// Emergency retreat — target is dangerously close.
			if (MinComfortDistance > 0f && distance < MinComfortDistance * EmergencyRetreatThreshold)
			{
				if (RetreatState != null)
				{
					controller.ChangeState(RetreatState);
					return;
				}
				// Fallback: use built-in retreat.
				RetreatFromTarget(controller, MinComfortDistance);
				return;
			}

			// Kiting — target is within discomfort zone but not emergency.
			if (MinComfortDistance > 0f && distance < MinComfortDistance)
			{
				// Try to shoot while moving away.
				Ability chosenAbility = controller.PickBestAbility(PreferredDistance);
				if (chosenAbility != null && distance <= chosenAbility.Range)
				{
					// Fire and back away simultaneously.
					bool held = abilityController.RequiresHeld(chosenAbility.ID);
					abilityController.Activate(chosenAbility.ID, held);
				}
				RetreatFromTarget(controller, MinComfortDistance);
				return;
			}

			// At comfortable distance or farther — pick an ability and fire.
			Ability bestAbility = controller.PickBestAbility(PreferredDistance);
			if (bestAbility == null)
			{
				// All on cooldown — reposition.
				ManagePositioning(controller, distance);
				return;
			}

			float abilityRange = bestAbility.Range;

			if (distance <= abilityRange)
			{
				// Occasionally strafe for variety.
				System.Random rng = controller.NpcRNG;
				if (StrafeState != null && (rng != null ? (float)rng.NextDouble() : Random.value) < StrafeChance)
				{
					// Activate ability then immediately strafe.
					bool held = abilityController.RequiresHeld(bestAbility.ID);
					abilityController.Activate(bestAbility.ID, held);
					controller.ChangeState(StrafeState);
					return;
				}

				PerformAttack(controller, abilityController, bestAbility, targetCharacter, distance);
			}
			else
			{
				// Close distance to get in range — but not closer than preferred.
				float targetDistance = Mathf.Min(abilityRange * 0.85f, PreferredDistance);
				MoveTowardTarget(controller, targetDistance);
			}
		}

		/// <summary>
		/// Ranged NPCs prefer to stay at their preferred distance.
		/// If too close, back away. If too far, close in but never closer than preferred distance.
		/// </summary>
		protected override void ManagePositioning(AIController controller, float distance)
		{
			if (controller.Target == null) return;

			if (MinComfortDistance > 0f && distance < MinComfortDistance)
			{
				RetreatFromTarget(controller, MinComfortDistance);
			}
			else if (distance > PreferredDistance * 1.2f)
			{
				MoveTowardTarget(controller, PreferredDistance);
			}
			else
			{
				// Comfortable distance — hold position.
				controller.Agent.isStopped = true;
			}
		}
	}
}