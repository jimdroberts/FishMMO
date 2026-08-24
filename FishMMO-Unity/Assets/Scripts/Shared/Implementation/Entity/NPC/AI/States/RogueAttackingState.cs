using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Rogue archetype. Melee, but refuses to trade blows face-to-face: it circles into the
	/// target's rear arc before opening, and drifts back around whenever the target turns on it.
	/// </summary>
	/// <remarks>
	/// <para>
	/// This is the third archetype that needs code: "behind the target" is a function of the
	/// target's <em>facing</em>, which no amount of distance tuning can express.
	/// </para>
	/// <para>
	/// It degrades gracefully — a rogue that cannot reach the rear arc (cornered target, bad
	/// navmesh) still attacks from wherever it is rather than standing there circling forever.
	/// </para>
	/// </remarks>
	[CreateAssetMenu(fileName = "New AI Rogue Attacking State", menuName = "FishMMO/Character/NPC/AI/Rogue Attacking State", order = 6)]
	public class RogueAttackingState : BaseAttackingState
	{
		/// <summary>
		/// Half-angle (degrees) of the arc behind the target that counts as "flanked".
		/// 90 means the whole rear hemisphere qualifies.
		/// </summary>
		[Header("Rogue Behavior")]
		[Range(15f, 180f)]
		[Tooltip("Half-angle of the rear arc that counts as flanked.")]
		public float FlankArcDegrees = 75f;

		/// <summary>
		/// How long (seconds) the rogue will keep manoeuvring for a flank before giving up and
		/// attacking from wherever it stands. Prevents an infinite circling stalemate.
		/// </summary>
		[Tooltip("Seconds spent trying to flank before attacking from the current position.")]
		public float MaxFlankSeconds = 3.0f;

		/// <summary>
		/// Distance behind the target the rogue aims for when repositioning.
		/// </summary>
		[Tooltip("Distance behind the target to aim for when repositioning.")]
		public float FlankStandoffDistance = 1.5f;

		/// <summary>
		/// Seeds rogue-appropriate defaults when the asset is first created or reset.
		/// </summary>
		private void Reset()
		{
			PreferredDistance = 0f;
			MinComfortDistance = 0f;
			AttackCooldown = 1.0f;
			AttackCooldownJitter = 0.3f;
			TargetReevaluationRate = 2.0f;
		}

		/// <inheritdoc />
		public override void Enter(AIController controller)
		{
			base.Enter(controller);
			controller.FlankTimer = MaxFlankSeconds;
		}

		/// <summary>
		/// Spends its flank budget circling into the target's rear arc, then fights normally.
		/// </summary>
		/// <inheritdoc />
		protected override void TryAttack(AIController controller, ICharacter targetCharacter)
		{
			if (!controller.Character.TryGet(out IAbilityController abilityController))
			{
				controller.TransitionToIdleState();
				return;
			}

			if (HandleActivationInProgress(controller, abilityController))
				return;

			if (controller.FlankTimer > 0f && !IsFlanking(controller))
			{
				if (MoveBehindTarget(controller))
				{
					return;
				}
			}

			base.TryAttack(controller, targetCharacter);
		}

		/// <inheritdoc />
		protected override void ReevaluateTarget(AIController controller, float deltaTime)
		{
			// Burn the flank budget on the AI tick clock rather than on a second timer.
			if (controller.FlankTimer > 0f)
			{
				controller.FlankTimer -= deltaTime;
			}
			else if (IsFlanking(controller))
			{
				// Successfully behind the target — bank a fresh budget so the rogue re-flanks
				// the next time the target turns to face it.
				controller.FlankTimer = MaxFlankSeconds;
			}

			base.ReevaluateTarget(controller, deltaTime);
		}

		/// <summary>
		/// True when the rogue is currently inside the target's rear arc.
		/// </summary>
		/// <param name="controller">The AI controller.</param>
		/// <returns>True if flanked.</returns>
		public bool IsFlanking(AIController controller)
		{
			if (controller.Target == null)
				return false;

			Vector3 targetToSelf = controller.Character.Transform.position - controller.Target.position;
			targetToSelf.y = 0f;

			if (targetToSelf.sqrMagnitude < 0.0001f)
				return true;

			Vector3 targetForward = controller.Target.forward;
			targetForward.y = 0f;

			if (targetForward.sqrMagnitude < 0.0001f)
				return true;

			// Angle between where the target is looking and where the rogue is standing.
			// 180 means directly behind; the rear arc is everything past (180 - FlankArcDegrees).
			float angle = Vector3.Angle(targetForward, targetToSelf.normalized);
			return angle >= (180f - FlankArcDegrees);
		}

		/// <summary>
		/// Steers toward a point in the target's rear arc.
		/// </summary>
		/// <param name="controller">The AI controller.</param>
		/// <returns>True if the rogue took over movement this tick.</returns>
		private bool MoveBehindTarget(AIController controller)
		{
			if (controller.Target == null)
				return false;

			Vector3 behind = controller.Target.position -
							 controller.Target.forward * (GetMeleeReach(controller) + FlankStandoffDistance);

			controller.Resume();

			/* A partial path counts as failure here, not success. The rear arc of a target backed
			 * against a wall is not reachable, and walking to the closest point to it leaves the
			 * rogue circling next to its target without ever flanking or attacking. */
			AIMovementResult result = controller.TryMoveTo(behind);
			if (result == AIMovementResult.Failed || controller.LastPathWasPartial)
			{
				// Nowhere behind the target to stand — give up on flanking and just fight.
				controller.FlankTimer = 0f;
				return false;
			}

			return true;
		}
	}
}
