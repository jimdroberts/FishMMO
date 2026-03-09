using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// AI attacking state for healer-focused NPCs. Prioritises healing injured allies
	/// before dealing damage. Scans for nearby allies each update, selects the most
	/// injured one whose health is below <see cref="HealThreshold"/>, and uses a heal
	/// ability on them. Falls back to normal attack behaviour when no ally needs healing.
	/// <para>
	/// Abilities are considered "heal abilities" when their template ID appears in
	/// <see cref="HealAbilityTemplateIDs"/>. All other abilities are treated as damage.
	/// </para>
	/// <para>
	/// Recommended <see cref="BaseAttackingState.PreferredDistance"/> of 15-25,
	/// <see cref="BaseAttackingState.MinComfortDistance"/> of 6-10.
	/// </para>
	/// </summary>
	[CreateAssetMenu(fileName = "New AI Healer Attacking State", menuName = "FishMMO/Character/NPC/AI/Healer Attacking State", order = 4)]
	public class HealerAttackingState : BaseAttackingState
	{
		/// <summary>
		/// The physics layers to check when scanning for allies. Should include the layers
		/// that allied characters (other NPCs, pets, etc.) are on.
		/// </summary>
		[Header("Healer Behavior")]
		[Tooltip("Physics layers to scan for allies.")]
		public LayerMask AllyLayers;

		/// <summary>
		/// Radius within which to scan for injured allies.
		/// </summary>
		[Tooltip("Radius within which to scan for allies.")]
		public float AllyScanRadius = 20f;

		/// <summary>
		/// Health percentage (0-1) below which an ally is considered injured and eligible
		/// for healing. E.g., 0.8 means heal allies below 80% health.
		/// </summary>
		[Range(0f, 1f)]
		[Tooltip("Heal allies whose health is below this fraction of max.")]
		public float HealThreshold = 0.75f;

		/// <summary>
		/// Template IDs of abilities that should be used as heals (on allies).
		/// All other known abilities are treated as damage abilities.
		/// </summary>
		[Tooltip("AbilityTemplate IDs that are considered heal abilities.")]
		public List<int> HealAbilityTemplateIDs = new List<int>();

		/// <summary>
		/// Optional retreat state when the target gets dangerously close.
		/// If null, built-in retreat logic is used.
		/// </summary>
		[Tooltip("Optional retreat state for emergency backing off.")]
		public BaseAIState RetreatState;

		/// <summary>
		/// Fraction of MinComfortDistance that triggers emergency retreat.
		/// </summary>
		[Range(0f, 1f)]
		[Tooltip("Fraction of MinComfortDistance that triggers emergency retreat.")]
		public float EmergencyRetreatThreshold = 0.5f;

		/// <summary>
		/// Buffer for storing colliders hit during ally sweep.
		/// </summary>
		private static readonly Collider[] allyHits = new Collider[20];

		/// <summary>
		/// Cached set built from <see cref="HealAbilityTemplateIDs"/> for O(1) lookup.
		/// Built lazily on first use.
		/// </summary>
		private HashSet<int> healTemplateSet;

		/// <summary>
		/// Called when entering the healer attacking state.
		/// </summary>
		public override void Enter(AIController controller)
		{
			base.Enter(controller);
			controller.Agent.speed = Constants.Character.RunSpeed;

			// Build the lookup set once.
			if (healTemplateSet == null)
			{
				healTemplateSet = new HashSet<int>(HealAbilityTemplateIDs);
			}
		}

		/// <summary>
		/// Core healer logic. Checks for injured allies first; if any need healing and a heal
		/// ability is available, targets and heals the most injured. Otherwise falls back to
		/// normal damage behaviour via base <see cref="BaseAttackingState.TryAttack"/>.
		/// </summary>
		protected override void TryAttack(AIController controller, ICharacter targetCharacter)
		{
			if (!controller.Character.TryGet(out IAbilityController abilityController))
			{
				controller.TransitionToIdleState();
				return;
			}

			float distance = Mathf.Sqrt(controller.GetSqrDistanceToTarget());

			// If currently casting, stop and wait. Auto-release charged abilities.
			if (HandleActivationInProgress(controller, abilityController))
				return;

			// Emergency retreat — enemy too close.
			if (MinComfortDistance > 0f && distance < MinComfortDistance * EmergencyRetreatThreshold)
			{
				if (RetreatState != null)
				{
					controller.ChangeState(RetreatState);
					return;
				}
				RetreatFromTarget(controller, PreferredDistance > 0f ? PreferredDistance : MinComfortDistance);
				return;
			}

			// --- Healing priority ---
			ICharacter injuredAlly = FindMostInjuredAlly(controller);
			if (injuredAlly != null)
			{
				Ability healAbility = PickBestHealAbility(controller, abilityController, injuredAlly);
				if (healAbility != null)
				{
					float allyDistance = Vector3.Distance(
						controller.Character.Transform.position,
						injuredAlly.Transform.position);

					if (allyDistance <= healAbility.Range)
					{
						// Face the ally and heal.
						controller.LookTarget = injuredAlly.Transform;
						controller.Agent.isStopped = true;

						bool held = abilityController.RequiresHeld(healAbility.ID);
						abilityController.Activate(healAbility.ID, held);
						return;
					}
					else
					{
						// Move toward the ally to get in heal range.
						controller.LookTarget = injuredAlly.Transform;
						MoveTowardAlly(controller, injuredAlly, healAbility.Range);
						return;
					}
				}
			}

			// --- No ally needs healing — fall back to damage ---
			// Kiting — target is within discomfort zone.
			if (MinComfortDistance > 0f && distance < MinComfortDistance)
			{
				Ability quickAbility = PickBestDamageAbility(controller, abilityController, distance);
				if (quickAbility != null && distance <= quickAbility.Range)
				{
					bool held = abilityController.RequiresHeld(quickAbility.ID);
					abilityController.Activate(quickAbility.ID, held);
				}
				RetreatFromTarget(controller, PreferredDistance > 0f ? PreferredDistance : MinComfortDistance);
				return;
			}

			// Comfortable range — pick a damage ability.
			Ability bestDamage = PickBestDamageAbility(controller, abilityController, float.MaxValue);
			if (bestDamage == null)
			{
				ManagePositioning(controller, distance);
				return;
			}

			float abilityRange = bestDamage.Range;

			if (distance <= abilityRange)
			{
				PerformAttack(controller, abilityController, bestDamage, targetCharacter, distance);
			}
			else
			{
				float targetDist = Mathf.Min(abilityRange * 0.9f, PreferredDistance > 0f ? PreferredDistance : abilityRange);
				MoveTowardTarget(controller, targetDist);
			}
		}

		/// <summary>
		/// Scans for nearby allies and returns the most injured one whose health is below
		/// <see cref="HealThreshold"/>. Returns null if no ally needs healing.
		/// </summary>
		private ICharacter FindMostInjuredAlly(AIController controller)
		{
			if (!controller.Character.TryGet(out IFactionController ourFaction))
				return null;

			int overlapCount = controller.PhysicsScene.OverlapSphere(
				controller.Character.Transform.position,
				AllyScanRadius,
				allyHits,
				AllyLayers,
				QueryTriggerInteraction.Ignore);

			ICharacter bestAlly = null;
			float bestHealthPct = 1f;

			for (int i = 0; i < overlapCount && i < allyHits.Length; i++)
			{
				Collider col = allyHits[i];
				if (col == null) continue;

				// Skip self.
				if (col == controller.Character.Collider) continue;

				ICharacter candidate = col.GetComponent<ICharacter>();
				if (candidate == null || !candidate.GameObject.activeSelf) continue;

				// Check faction alliance — only heal allies.
				if (!candidate.TryGet(out IFactionController candidateFaction)) continue;
				if (candidateFaction.GetAllianceLevel(ourFaction) != FactionAllianceLevel.Ally) continue;

				// Check if alive and injured.
				if (!candidate.TryGet(out ICharacterDamageController dmg) || !dmg.IsAlive) continue;

				CharacterResourceAttribute health = dmg.ResourceInstance;
				if (health == null || health.FinalValue <= 0f) continue;

				float healthPct = health.CurrentValue / health.FinalValue;
				if (healthPct >= HealThreshold) continue;

				if (healthPct < bestHealthPct)
				{
					bestHealthPct = healthPct;
					bestAlly = candidate;
				}
			}

			return bestAlly;
		}

		/// <summary>
		/// Picks the best heal ability that is off cooldown and can reach the ally.
		/// Prefers abilities whose range covers the distance to the ally. Among those,
		/// picks the one with the longest cooldown (typically stronger).
		/// </summary>
		private Ability PickBestHealAbility(AIController controller, IAbilityController abilityController, ICharacter ally)
		{
			if (!controller.Character.TryGet(out ICooldownController cooldownController))
				return null;

			DeterministicRNG rng = controller.NpcRNG;
			float sqrDist = (ally.Transform.position - controller.Character.Transform.position).sqrMagnitude;

			Ability best = null;
			float bestScore = float.MinValue;

			uint currentTick = controller.TimeManager.LocalTick;

			EventData activationCheckData = null;

			foreach (var kvp in abilityController.KnownAbilities)
			{
				Ability ability = kvp.Value;
				if (ability == null || ability.Template == null) continue;
				if (!IsHealAbility(ability)) continue;
				if (cooldownController.IsOnCooldown(ability.ID, currentTick)) continue;
				if (!ability.MeetsActivationConditions(controller.Character, ref activationCheckData)) continue;

				float score = 0f;
				if (ability.Range * ability.Range >= sqrDist)
				{
					score = 1000f + ability.Cooldown;
				}
				else
				{
					score = ability.Range;
				}

				score += (rng ?? DeterministicRNG.Shared).Range(0f, 30f);

				if (score > bestScore)
				{
					bestScore = score;
					best = ability;
				}
			}

			return best;
		}

		/// <summary>
		/// Picks the best damage (non-heal) ability for attacking enemies.
		/// </summary>
		private Ability PickBestDamageAbility(AIController controller, IAbilityController abilityController, float preferredMaxRange)
		{
			if (!controller.Character.TryGet(out ICooldownController cooldownController))
				return null;

			DeterministicRNG rng = controller.NpcRNG;
			float sqrDist = controller.GetSqrDistanceToTarget();

			Ability best = null;
			float bestScore = float.MinValue;

			uint currentTick = controller.TimeManager.LocalTick;

			EventData activationCheckData = null;

			foreach (var kvp in abilityController.KnownAbilities)
			{
				Ability ability = kvp.Value;
				if (ability == null || ability.Template == null) continue;
				if (IsHealAbility(ability)) continue; // Skip heal abilities.
				if (cooldownController.IsOnCooldown(ability.ID, currentTick)) continue;
				if (!ability.MeetsActivationConditions(controller.Character, ref activationCheckData)) continue;

				float score = 0f;
				if (ability.Range * ability.Range >= sqrDist)
				{
					score = 1000f + ability.Cooldown;
				}
				else
				{
					score = ability.Range;
				}

				score += (rng ?? DeterministicRNG.Shared).Range(0f, 50f);

				if (score > bestScore)
				{
					bestScore = score;
					best = ability;
				}
			}

			return best;
		}

		/// <summary>
		/// Returns true if the ability's template ID is in the configured heal ability set.
		/// </summary>
		private bool IsHealAbility(Ability ability)
		{
			if (healTemplateSet == null)
			{
				healTemplateSet = new HashSet<int>(HealAbilityTemplateIDs);
			}
			return ability.Template != null && healTemplateSet.Contains(ability.Template.ID);
		}

		/// <summary>
		/// Moves the NPC toward an ally, stopping at the specified range.
		/// </summary>
		private void MoveTowardAlly(AIController controller, ICharacter ally, float stopRange)
		{
			if (ally == null || controller.Agent.pathStatus == NavMeshPathStatus.PathInvalid)
				return;

			controller.Agent.isStopped = false;

			if (!controller.Agent.pathPending)
			{
				float sphereRadius = stopRange * 0.9f;

				Vector3 nearestPosition = Vector3Extensions.GetNearestPositionOnSphere(
					controller.Character.Transform.position,
					ally.Transform.position,
					sphereRadius);

				NavMeshHit hit;
				if (NavMesh.SamplePosition(nearestPosition, out hit, 5.0f, NavMesh.AllAreas))
				{
					controller.SetThrottledDestination(hit.position);
				}
			}
		}

		/// <summary>
		/// Healers stay at preferred distance from the enemy, retreating if too close.
		/// </summary>
		protected override void ManagePositioning(AIController controller, float distance)
		{
			if (controller.Target == null) return;

			// Never reposition while casting or channeling.
			if (controller.Character.TryGet(out IAbilityController ac) &&
				(ac.IsActivating || ac.AbilityQueued))
			{
				controller.Agent.isStopped = true;
				return;
			}

			if (MinComfortDistance > 0f && distance < MinComfortDistance)
			{
				RetreatFromTarget(controller, PreferredDistance > 0f ? PreferredDistance : MinComfortDistance);
			}
			else if (PreferredDistance > 0f && distance > PreferredDistance * 1.3f)
			{
				MoveTowardTarget(controller, PreferredDistance);
			}
			else
			{
				controller.Agent.isStopped = true;
			}
		}
	}
}