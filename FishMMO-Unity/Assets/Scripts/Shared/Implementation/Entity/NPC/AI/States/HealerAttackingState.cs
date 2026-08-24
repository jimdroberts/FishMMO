using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Healer archetype. Keeps caster spacing from the enemy, but interrupts its damage rotation
	/// to top up the most wounded nearby ally.
	/// </summary>
	/// <remarks>
	/// <para>
	/// This is one of the three archetypes that genuinely needs code rather than tuning: it has
	/// to scan for a second, friendly target that the shared combat decision knows nothing about.
	/// Everything after "no ally needs healing" falls through to
	/// <see cref="BaseAttackingState"/>'s shared logic.
	/// </para>
	/// <para>
	/// Abilities count as heals when their template ID appears in
	/// <see cref="HealAbilityTemplateIDs"/>; all others are treated as damage.
	/// </para>
	/// </remarks>
	[CreateAssetMenu(fileName = "New AI Healer Attacking State", menuName = "FishMMO/Character/NPC/AI/Healer Attacking State", order = 4)]
	public class HealerAttackingState : BaseAttackingState
	{
		/// <summary>
		/// The physics layers to check when scanning for allies. Should include the layers
		/// that allied characters (other NPCs, pets, players) are on.
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
		/// Whether the healer counts itself as a heal candidate when it is the most wounded
		/// character in range.
		/// </summary>
		[Tooltip("Allow the healer to heal itself when it is the most wounded character in range.")]
		public bool CanHealSelf = true;

		/// <summary>
		/// Seconds between ally scans.
		/// </summary>
		/// <remarks>
		/// The ally scan is an <c>OverlapSphere</c> plus an interface component lookup per hit, and
		/// it used to run on every single combat tick — so a healer cost roughly twice what any
		/// other archetype cost, purely to re-answer a question whose answer barely changes between
		/// ticks. Health does not move fast enough for a fifth of a second of staleness to matter,
		/// and the result is cached in between.
		/// </remarks>
		[Tooltip("Seconds between ally scans. Higher is cheaper; health changes slowly enough that this can be coarse.")]
		public float AllyScanInterval = 0.5f;

		/// <summary>
		/// Buffer for storing colliders hit during the ally sweep.
		/// </summary>
		/// <remarks>
		/// Static and shared: the sweep is fully consumed inside a single synchronous
		/// <see cref="FindMostInjuredAlly"/> call, so no two NPCs can be mid-scan at once.
		/// </remarks>
		private static readonly Collider[] allyHits = new Collider[20];

		/// <summary>Random score jitter applied when choosing between heal abilities.</summary>
		private const float HEAL_ABILITY_JITTER = 30f;

		/// <summary>
		/// Cached set built from <see cref="HealAbilityTemplateIDs"/> for O(1) lookup.
		/// </summary>
		private HashSet<int> healTemplateSet;

		/// <summary>
		/// Seeds healer-appropriate defaults when the asset is first created or reset.
		/// </summary>
		private void Reset()
		{
			PreferredDistance = 18f;
			MinComfortDistance = 8f;
			AllyScanInterval = 0.5f;
			EmergencyRetreatThreshold = 0.5f;
			AttackCooldown = 2.0f;
		}

		/// <summary>
		/// Called when entering the healer attacking state.
		/// </summary>
		/// <param name="controller">The AI controller.</param>
		public override void Enter(AIController controller)
		{
			base.Enter(controller);
			EnsureHealSet();
		}

		/// <summary>
		/// Rebuilds the heal template lookup if it has not been built yet.
		/// </summary>
		private void EnsureHealSet()
		{
			if (healTemplateSet == null)
			{
				healTemplateSet = new HashSet<int>(HealAbilityTemplateIDs);
			}
		}

		/// <summary>
		/// Heals first, fights second. Falls through to the shared combat logic when nobody
		/// needs healing or no heal is available.
		/// </summary>
		/// <param name="controller">The AI controller.</param>
		/// <param name="targetCharacter">The current enemy target.</param>
		protected override void TryAttack(AIController controller, ICharacter targetCharacter)
		{
			if (!controller.Character.TryGet(out IAbilityController abilityController))
			{
				controller.TransitionToIdleState();
				return;
			}

			if (HandleActivationInProgress(controller, abilityController))
				return;

			float distance = Mathf.Sqrt(controller.GetSqrDistanceToTarget());

			/* Panic first: a healer pinned in melee heals nobody. Checked before the heal scan so
			 * it escapes rather than standing still casting a long heal with a rogue on it. */
			AICombatContext panicContext = BuildContext(controller, distance, null);
			AICombatPlan panicPlan = AICombatDecision.Plan(panicContext);
			if (panicPlan.Intent == AICombatIntent.Flee || panicPlan.Intent == AICombatIntent.EmergencyRetreat)
			{
				ExecutePlan(controller, abilityController, targetCharacter, panicPlan, panicContext, null);
				return;
			}

			// --- Healing priority ---
			if (controller.AttackCooldownTimer <= 0f)
			{
				ICharacter injuredAlly = GetInjuredAlly(controller);
				if (injuredAlly != null && TryHeal(controller, abilityController, injuredAlly))
				{
					return;
				}
			}

			// --- Nobody to heal: behave like any other ranged archetype. ---
			base.TryAttack(controller, targetCharacter);
		}

		/// <summary>
		/// Attempts to heal an ally, moving into range first if necessary.
		/// </summary>
		/// <param name="controller">The AI controller.</param>
		/// <param name="abilityController">The healer's ability controller.</param>
		/// <param name="ally">The ally to heal.</param>
		/// <returns>True if the healer acted on the heal this tick.</returns>
		private bool TryHeal(AIController controller, IAbilityController abilityController, ICharacter ally)
		{
			Ability healAbility = PickBestHealAbility(controller, abilityController, ally);
			if (healAbility == null)
				return false;

			float allyDistance = Vector3.Distance(
				controller.Character.Transform.position,
				ally.Transform.position);

			controller.LookTarget = ally.Transform;

			if (allyDistance <= healAbility.Range)
			{
				controller.Agent.isStopped = true;
				ActivateAbility(controller, abilityController, healAbility);
				return true;
			}

			// Unreachable ally: fall through to the damage rotation instead of stalling on it.
			return MoveTowardAlly(controller, ally, healAbility.Range);
		}

		/// <summary>
		/// Excludes heal abilities from the damage picker so the healer never "attacks" with a heal.
		/// </summary>
		/// <param name="controller">The AI controller.</param>
		/// <returns>The best damage ability, or null.</returns>
		protected override Ability PickAbility(AIController controller)
		{
			return controller.PickBestAbility(
				PreferredDistance > 0f ? PreferredDistance : float.MaxValue,
				IsDamageAbility);
		}

		/// <summary>
		/// True when an ability is not one of this healer's configured heals.
		/// </summary>
		/// <param name="ability">The ability to test.</param>
		/// <returns>True if the ability may be used against an enemy.</returns>
		private bool IsDamageAbility(Ability ability)
		{
			return !IsHealAbility(ability);
		}

		/// <summary>
		/// Returns the ally most in need of healing, rescanning only when the scan interval has
		/// elapsed.
		/// </summary>
		/// <remarks>
		/// The cached candidate is re-validated cheaply on every tick — it may have died, been
		/// despawned, or walked out of range since the scan — so staleness costs a wasted heal at
		/// worst, never a heal cast at a corpse.
		/// </remarks>
		/// <param name="controller">The AI controller.</param>
		/// <returns>The ally to heal, or null.</returns>
		private ICharacter GetInjuredAlly(AIController controller)
		{
			controller.AllyScanTimer -= controller.LastAiDeltaTime;

			if (controller.AllyScanTimer > 0f)
			{
				ICharacter cached = controller.CachedHealTarget;

				if (AITargetSelection.IsValidTarget(cached) &&
					AITargetSelection.GetHealthPercent(cached) < HealThreshold &&
					(cached.Transform.position - controller.Character.Transform.position).sqrMagnitude
						<= AllyScanRadius * AllyScanRadius)
				{
					return cached;
				}
			}

			controller.AllyScanTimer = AllyScanInterval;
			controller.CachedHealTarget = FindMostInjuredAlly(controller);
			return controller.CachedHealTarget;
		}

		/// <summary>
		/// Scans for nearby allies and returns the most injured one whose health is below
		/// <see cref="HealThreshold"/>. Returns null if no ally needs healing.
		/// </summary>
		/// <param name="controller">The AI controller.</param>
		/// <returns>The most injured ally needing healing, or null.</returns>
		private ICharacter FindMostInjuredAlly(AIController controller)
		{
			if (!controller.Character.TryGet(out IFactionController ourFaction))
				return null;

			ICharacter bestAlly = null;
			float bestHealthPct = HealThreshold;

			// The healer is a legitimate heal candidate and is not returned by its own sweep.
			if (CanHealSelf)
			{
				float selfPct = controller.GetHealthPercent();
				if (selfPct < bestHealthPct)
				{
					bestHealthPct = selfPct;
					bestAlly = controller.Character;
				}
			}

			int overlapCount = controller.PhysicsScene.OverlapSphere(
				controller.Character.Transform.position,
				AllyScanRadius,
				allyHits,
				AllyLayers,
				QueryTriggerInteraction.Ignore);

			for (int i = 0; i < overlapCount && i < allyHits.Length; i++)
			{
				Collider col = allyHits[i];
				if (col == null) continue;

				// Skip self.
				if (col == controller.Character.Collider) continue;

				ICharacter candidate = col.GetComponent<ICharacter>();
				if (!AITargetSelection.IsValidTarget(candidate)) continue;

				// Check faction alliance — only heal allies.
				if (!candidate.TryGet(out IFactionController candidateFaction)) continue;
				if (candidateFaction.GetAllianceLevel(ourFaction) != FactionAllianceLevel.Ally) continue;

				float healthPct = AITargetSelection.GetHealthPercent(candidate);
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
		/// </summary>
		/// <param name="controller">The AI controller.</param>
		/// <param name="abilityController">The ability controller for the NPC.</param>
		/// <param name="ally">The ally to heal.</param>
		/// <returns>The best heal ability, or null if none available.</returns>
		private Ability PickBestHealAbility(AIController controller, IAbilityController abilityController, ICharacter ally)
		{
			float sqrDist = (ally.Transform.position - controller.Character.Transform.position).sqrMagnitude;
			return controller.PickScoredAbility(sqrDist, IsHealAbility, HEAL_ABILITY_JITTER);
		}

		/// <summary>
		/// Returns true if the ability's template ID is in the configured heal ability set.
		/// </summary>
		/// <param name="ability">The ability to check.</param>
		/// <returns>True if the ability is a heal ability.</returns>
		private bool IsHealAbility(Ability ability)
		{
			EnsureHealSet();
			return ability != null && ability.Template != null && healTemplateSet.Contains(ability.Template.ID);
		}

		/// <summary>
		/// Moves the NPC toward an ally, stopping at the specified range.
		/// </summary>
		/// <param name="controller">The AI controller.</param>
		/// <param name="ally">The ally to move toward.</param>
		/// <param name="stopRange">Distance from the ally at which to stop.</param>
		private bool MoveTowardAlly(AIController controller, ICharacter ally, float stopRange)
		{
			if (ally == null)
				return false;

			controller.Resume();

			float sphereRadius = Mathf.Max(stopRange * AICombatDecision.RANGE_APPROACH_FACTOR, 0.1f);

			Vector3 approach = Vector3Extensions.GetNearestPositionOnSphere(
				controller.Character.Transform.position,
				ally.Transform.position,
				sphereRadius);

			/* An ally standing somewhere the healer cannot path to is not a heal candidate. Say so
			 * rather than walking to the closest reachable point and reporting the heal handled —
			 * that stalls the healer out of its damage rotation for as long as the ally stays
			 * unreachable. */
			return controller.TryMoveTo(approach) != AIMovementResult.Failed && !controller.LastPathWasPartial;
		}
	}
}
