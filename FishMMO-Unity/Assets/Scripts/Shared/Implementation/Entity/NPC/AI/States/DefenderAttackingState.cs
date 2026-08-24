using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Defender / tank archetype. Fights in melee, keeps its taunts on cooldown to hold threat,
	/// and physically interposes itself between the enemy and whoever it is protecting.
	/// </summary>
	/// <remarks>
	/// <para>
	/// This needs code rather than tuning for two reasons: taunts have to be prioritised over raw
	/// damage regardless of what the scoring picker thinks, and body-blocking means walking to a
	/// position derived from a <em>third</em> character rather than from the target.
	/// </para>
	/// <para>
	/// As a pet this is the "defender" stance made concrete — the pet plants itself between its
	/// owner and whatever is trying to reach them.
	/// </para>
	/// </remarks>
	[CreateAssetMenu(fileName = "New AI Defender Attacking State", menuName = "FishMMO/Character/NPC/AI/Defender Attacking State", order = 5)]
	public class DefenderAttackingState : BaseAttackingState
	{
		/// <summary>
		/// Template IDs of threat-generating abilities. These are used the moment they are
		/// available, ahead of anything the damage picker would otherwise choose.
		/// </summary>
		[Header("Defender Behavior")]
		[Tooltip("AbilityTemplate IDs treated as taunts. Used on cooldown, ahead of damage abilities.")]
		public List<int> TauntAbilityTemplateIDs = new List<int>();

		/// <summary>
		/// When true the defender positions itself on the line between the enemy and the
		/// character it is protecting, rather than simply closing on the enemy.
		/// </summary>
		[Tooltip("Stand between the enemy and the protected character.")]
		public bool BodyBlock = true;

		/// <summary>
		/// How far in front of the protected character the defender plants itself, in world units.
		/// </summary>
		[Tooltip("Distance in front of the protected character to hold the line.")]
		public float BlockStandoffDistance = 2.5f;

		/// <summary>
		/// The defender only bothers repositioning when it is further than this from its ideal
		/// blocking spot. Prevents jitter.
		/// </summary>
		[Tooltip("Only reposition when further than this from the ideal blocking spot.")]
		public float BlockRepositionTolerance = 1.5f;

		/// <summary>
		/// Cached set built from <see cref="TauntAbilityTemplateIDs"/> for O(1) lookup.
		/// </summary>
		private HashSet<int> tauntTemplateSet;

		/// <summary>
		/// Seeds tank-appropriate defaults when the asset is first created or reset.
		/// </summary>
		private void Reset()
		{
			PreferredDistance = 0f;
			MinComfortDistance = 0f;
			AttackCooldown = 1.4f;
			TargetReevaluationRate = 2.0f;
			// A tank that swaps targets on a small threat lead is a tank that loses the pull.
			AggressionSwitchThreshold = 150f;
		}

		/// <inheritdoc />
		public override void Enter(AIController controller)
		{
			base.Enter(controller);
			EnsureTauntSet();
		}

		/// <summary>
		/// Builds the taunt lookup if it has not been built yet.
		/// </summary>
		private void EnsureTauntSet()
		{
			if (tauntTemplateSet == null)
			{
				tauntTemplateSet = new HashSet<int>(TauntAbilityTemplateIDs);
			}
		}

		/// <summary>
		/// True when the ability is one of this defender's configured taunts.
		/// </summary>
		/// <param name="ability">The ability to test.</param>
		/// <returns>True if the ability is a taunt.</returns>
		private bool IsTaunt(Ability ability)
		{
			EnsureTauntSet();
			return ability != null && ability.Template != null && tauntTemplateSet.Contains(ability.Template.ID);
		}

		/// <summary>
		/// Prefers an available taunt over anything else, then falls back to the normal picker.
		/// </summary>
		/// <param name="controller">The AI controller.</param>
		/// <returns>The chosen ability, or null.</returns>
		protected override Ability PickAbility(AIController controller)
		{
			EnsureTauntSet();

			if (tauntTemplateSet.Count > 0)
			{
				Ability taunt = controller.PickScoredAbility(controller.GetSqrDistanceToTarget(), IsTaunt, 0f);
				if (taunt != null)
				{
					return taunt;
				}
			}

			return base.PickAbility(controller);
		}

		/// <summary>
		/// Holds the line in front of the protected character instead of simply walking at the
		/// enemy, when body-blocking is enabled and there is somebody to protect.
		/// </summary>
		/// <inheritdoc />
		protected override void ExecutePlan(
			AIController controller,
			IAbilityController abilityController,
			ICharacter targetCharacter,
			AICombatPlan plan,
			in AICombatContext context,
			Ability chosenAbility)
		{
			if (BodyBlock &&
				(plan.Intent == AICombatIntent.CloseDistance || plan.Intent == AICombatIntent.HoldPosition))
			{
				Transform protectedTransform = ResolveProtectedTransform(controller);
				if (protectedTransform != null && MoveToBlockingPosition(controller, protectedTransform))
				{
					return;
				}
			}

			base.ExecutePlan(controller, abilityController, targetCharacter, plan, context, chosenAbility);
		}

		/// <summary>
		/// Resolves who this defender is protecting: a pet protects its owner, a grouped NPC
		/// protects the group's most wounded member.
		/// </summary>
		/// <param name="controller">The AI controller.</param>
		/// <returns>The protected character's transform, or null when there is nobody to protect.</returns>
		protected virtual Transform ResolveProtectedTransform(AIController controller)
		{
			if (controller.Character is Pet pet && pet.PetOwner != null)
			{
				return pet.PetOwner.Transform;
			}

			if (controller.Group != null &&
				controller.Group.LowestHealthMember != null &&
				controller.Group.LowestHealthMember != controller &&
				controller.Group.LowestHealthMember.Character != null)
			{
				return controller.Group.LowestHealthMember.Character.Transform;
			}

			return null;
		}

		/// <summary>
		/// Walks to the point <see cref="BlockStandoffDistance"/> in front of the protected
		/// character, on the line toward the enemy.
		/// </summary>
		/// <param name="controller">The AI controller.</param>
		/// <param name="protectedTransform">The character being shielded.</param>
		/// <returns>True if the defender took over movement this tick.</returns>
		private bool MoveToBlockingPosition(AIController controller, Transform protectedTransform)
		{
			if (controller.Target == null)
				return false;

			Vector3 protectedPosition = protectedTransform.position;
			Vector3 toEnemy = controller.Target.position - protectedPosition;
			toEnemy.y = 0f;

			if (toEnemy.sqrMagnitude < 0.01f)
				return false;

			Vector3 blockPosition = protectedPosition + toEnemy.normalized * BlockStandoffDistance;

			// Already holding the line — stand still and keep swinging.
			float sqrOffset = (controller.Character.Transform.position - blockPosition).sqrMagnitude;
			if (sqrOffset <= BlockRepositionTolerance * BlockRepositionTolerance)
			{
				controller.Agent.isStopped = true;
				return true;
			}

			controller.Resume();

			/* Failing to reach the blocking spot must hand movement back to the normal combat
			 * logic, not consume the tick. A defender that cannot interpose should still close on
			 * the enemy rather than stand between two points it cannot occupy. */
			if (controller.TryMoveTo(blockPosition) == AIMovementResult.Failed)
			{
				return false;
			}

			return true;
		}
	}
}
