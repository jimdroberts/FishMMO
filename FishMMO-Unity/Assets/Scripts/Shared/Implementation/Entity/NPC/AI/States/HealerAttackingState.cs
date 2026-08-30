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
	/// <b>Heals identify themselves.</b> An ability counts as a heal when
	/// <see cref="AIAbilityClassifier"/> finds a healing action in its ECA graph — no list of
	/// template IDs on the archetype, no naming convention. That matters because the list version
	/// was a second copy of a fact the ability asset already stated, and the two drifted silently:
	/// a designer adding a third heal got a healer that went on casting only the two it had been
	/// told about, with nothing logged and nothing to notice. It also stopped one healer asset
	/// being shared by creatures with different spellbooks, which is most of them.
	/// </para>
	/// <para>
	/// <see cref="HealAbilityTemplateIDs"/> survives as an override for abilities the classifier
	/// reads wrongly. Leave it empty in the normal case.
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
		/// Optional escape hatch: template IDs to treat as heals in addition to whatever
		/// <see cref="AIAbilityClassifier"/> identifies.
		/// </summary>
		/// <remarks>
		/// Normally empty. Heals are recognised from the ability's own ECA actions; this is only
		/// for an ability that heals by some route the classifier cannot see, such as a project
		/// specific action type.
		/// </remarks>
		[Tooltip("Optional. Extra AbilityTemplate IDs to force-treat as heals. Normally empty — heals are detected from the ability's ECA actions.")]
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
		/// <para>
		/// Static and shared: the sweep is fully consumed inside a single synchronous
		/// <see cref="FindMostInjuredAlly"/> call, so no two NPCs can be mid-scan at once.
		/// </para>
		/// <para>
		/// Not <c>readonly</c>, because <see cref="TargetOrdering.TryGrowQueryBuffer{T}"/> replaces
		/// it when a query comes back full. A fixed twenty entries meant the broadphase chose which
		/// allies a healer could see in any group larger than that, in its own order — so the most
		/// wounded ally was silently invisible to the healer whenever it happened to be one of the
		/// ones discarded.
		/// </para>
		/// </remarks>
		private static Collider[] allyHits = new Collider[TargetOrdering.QueryBufferSize(20)];

		/// <summary>Random score jitter applied when choosing between heal abilities.</summary>
		private const float HEAL_ABILITY_JITTER = 30f;

		/// <summary>
		/// Cached set built from the optional <see cref="HealAbilityTemplateIDs"/> override.
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
		/// True when an ability belongs in the damage rotation rather than the heal rotation.
		/// </summary>
		/// <remarks>
		/// Not simply "not a heal". A healer's spellbook contains buffs and cleanses as well, and
		/// none of those should be aimed at the thing it is fighting; the shared
		/// <see cref="BaseAttackingState.IsEnemyAbility"/> test excludes all of them by intent.
		/// The explicit heal override is subtracted on top of that.
		/// </remarks>
		/// <param name="ability">The ability to test.</param>
		/// <returns>True if the ability may be used against an enemy.</returns>
		private bool IsDamageAbility(Ability ability)
		{
			return IsEnemyAbility(ability) && !IsHealAbility(ability);
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

			/* Re-queried until the buffer stops coming back full, the same loop every other spatial
			 * query in the project uses. A non-allocating overlap returns at most buffer.Length
			 * results and says nothing about how many it discarded, and the broadphase chose which
			 * ones — so a healer in a group bigger than its buffer scanned an arbitrary subset of
			 * its allies and never saw the rest, differently on each run. */
			int overlapCount;
			while (true)
			{
				overlapCount = controller.PhysicsScene.OverlapSphere(
					controller.Character.Transform.position,
					AllyScanRadius,
					allyHits,
					AllyLayers,
					QueryTriggerInteraction.Ignore);

				if (!TargetOrdering.TryGrowQueryBuffer(ref allyHits, overlapCount))
				{
					break;
				}
			}

			for (int i = 0; i < overlapCount && i < allyHits.Length; i++)
			{
				Collider col = allyHits[i];
				if (col == null) continue;

				/* Resolved through TargetOrdering rather than with a bare GetComponent on the
				 * collider: an ally whose hitbox hangs off a child transform resolved to no
				 * ICharacter at all and could never be healed. Skipping self is asked of the
				 * resolved body too, so a healer rigged that way does not fail its own CanHealSelf
				 * check by matching on one collider and not another. No dedupe pass is needed —
				 * this loop keeps a single best candidate, and a body's duplicate colliders all
				 * report the same health. */
				GameObject key = TargetOrdering.ResolveHitKey(col, out ICharacter candidate);
				if (key == null || candidate == controller.Character) continue;

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
		/// True when an ability heals, as read from its ECA actions or forced by the override list.
		/// </summary>
		/// <remarks>
		/// Healing only, not <see cref="AIAbilityIntent.Revive"/>. The heal rotation runs against
		/// the most wounded <em>living</em> ally, and offering it a resurrection would either waste
		/// the pick or fail activation outright. Reviving the dead is a separate decision that
		/// needs a separate scan.
		/// </remarks>
		/// <param name="ability">The ability to check.</param>
		/// <returns>True if the ability is a heal ability.</returns>
		private bool IsHealAbility(Ability ability)
		{
			if (ability == null || ability.Template == null)
			{
				return false;
			}

			if (AIAbilityClassifier.HasAny(ability, AIAbilityIntent.Heal))
			{
				return true;
			}

			EnsureHealSet();
			return healTemplateSet.Count > 0 && healTemplateSet.Contains(ability.Template.ID);
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
