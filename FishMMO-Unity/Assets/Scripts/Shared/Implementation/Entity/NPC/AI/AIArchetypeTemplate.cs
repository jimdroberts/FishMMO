using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// A complete, reusable AI brain in one asset: which states an NPC uses, how it picks
	/// abilities, how it behaves in combat, how it accrues threat, and how it is throttled at
	/// distance.
	/// </summary>
	/// <remarks>
	/// <para>
	/// This is the only AI wiring an NPC prefab carries. Assign one to
	/// <see cref="AIController.Archetype"/> and the controller reads every state and tuning value
	/// straight from it. Wiring an NPC previously meant dragging eight state assets plus a
	/// personality, a rotation and a LOD asset onto every prefab by hand, and getting one of them
	/// wrong — a null attacking state, a retreat state on an archetype that never retreats —
	/// produced an NPC that silently did nothing.
	/// </para>
	/// <para>
	/// There is deliberately no per-prefab override of individual slots. Many NPCs share one
	/// brain, and an override layer meant the personality lived in two places and a prefab could
	/// quietly disagree with the archetype it named. A creature that needs one slot different
	/// gets its own archetype. <see cref="Validate"/> reports combinations that cannot work.
	/// </para>
	/// </remarks>
	[CreateAssetMenu(fileName = "New AI Archetype", menuName = "FishMMO/Character/NPC/AI/Archetype", order = -10)]
	public class AIArchetypeTemplate : ScriptableObject
	{
		[Header("Identity")]
		[Tooltip("Human-readable description of this archetype. Editor only.")]
		[TextArea(2, 5)]
		public string Description;

		[Header("Core States")]
		[Tooltip("State the NPC starts in when it spawns.")]
		public BaseAIState InitialState;

		[Tooltip("Combat state. Required for the NPC to be able to fight at all.")]
		public BaseAIState AttackingState;

		[Tooltip("Passive standing-around state.")]
		public BaseAIState IdleState;

		[Header("Movement States")]
		[Tooltip("Random movement around the home position.")]
		public BaseAIState WanderState;

		[Tooltip("Waypoint movement. Requires waypoints on the spawner.")]
		public BaseAIState PatrolState;

		[Tooltip("Leash return. Required for leashing to work.")]
		public BaseAIState ReturnHomeState;

		[Tooltip("Flee state. Required for a personality with a retreat threshold to actually flee.")]
		public BaseAIState RetreatState;

		[Tooltip("Optional state entered on death.")]
		public BaseAIState DeadState;

		[Header("Combat Tuning")]
		[Tooltip("Combat personality: ability preferences, flee threshold, targeting mode.")]
		public AICombatPersonality Personality;

		[Tooltip("Optional condition-driven ability rotation.")]
		public AIAbilityRotation AbilityRotation;

		[Tooltip("Optional high-level behavior tree evaluated above the state machine.")]
		public AIBehaviorTree BehaviorTree;

		[Header("Threat")]
		[Tooltip("Threat per 1 point of damage taken.")]
		public float AggressionDamageWeight = 1.0f;

		[Tooltip("Threat per 1 point of healing witnessed on a combat participant.")]
		public float AggressionHealingWeight = 0.6f;

		[Tooltip("Flat threat added per hit, regardless of damage.")]
		public float AggressionHitBonus = 5.0f;

		[Tooltip("Threat lost per second while no new events arrive.")]
		public float AggressionDecayRate = 3.0f;

		[Tooltip("Seconds before a drained threat entry is forgotten.")]
		public float AggressionStaleTimeout = 30.0f;

		[Range(0f, 1f)]
		[Tooltip("Chance target selection picks the second-highest threat instead of the highest.")]
		public float AggressionVarietyChance = 0.15f;

		[Header("Performance")]
		[Tooltip("Optional distance-based update throttling.")]
		public AILodSettings LodSettings;

		[Tooltip("How often (seconds) the NPC sweeps for nearby enemies while out of combat.")]
		public float EnemySweepRate = 1.5f;

		[Tooltip("NavMeshAgent avoidance priority.")]
		public AgentAvoidancePriority AvoidancePriority = AgentAvoidancePriority.Medium;

		/// <summary>
		/// Checks this archetype for combinations that cannot behave as configured.
		/// </summary>
		/// <remarks>
		/// Exists so the shipped archetype assets can be asserted in an EditMode test rather than
		/// discovered to be broken in play. Every problem returned is one that produces an NPC
		/// which compiles, spawns, and then quietly misbehaves.
		/// </remarks>
		/// <param name="problems">Receives one line per problem found. Cleared first.</param>
		/// <returns>True when the archetype is internally consistent.</returns>
		public bool Validate(System.Collections.Generic.List<string> problems)
		{
			if (problems == null)
			{
				problems = new System.Collections.Generic.List<string>();
			}
			problems.Clear();

			if (InitialState == null)
			{
				problems.Add("InitialState is null — the NPC spawns with no state and never ticks.");
			}

			if (IdleState == null)
			{
				problems.Add("IdleState is null — TransitionToIdleState is a no-op, so any state that falls back to idle sticks where it is.");
			}

			BaseAttackingState attacking = AttackingState as BaseAttackingState;
			if (AttackingState != null && attacking == null)
			{
				problems.Add("AttackingState is assigned but is not a BaseAttackingState — the controller will never treat it as combat.");
			}

			if (Personality != null)
			{
				float threshold = Personality.EffectiveRetreatHealthThreshold;

				if (threshold > 0f && RetreatState == null)
				{
					problems.Add($"Personality '{Personality.name}' flees at {threshold:P0} health but no RetreatState is assigned — it will fight to the death instead.");
				}

				if (Personality.IsFearless && Personality.RetreatHealthThreshold > 0f)
				{
					problems.Add($"Personality '{Personality.name}' is a fearless style ({Personality.Style}) but has RetreatHealthThreshold set — the threshold is ignored.");
				}
			}

			if (attacking != null)
			{
				ValidateAttackingState(attacking, problems);
			}

			if (AggressionStaleTimeout <= 0f)
			{
				problems.Add("AggressionStaleTimeout is 0 — threat entries are pruned the instant they drain, so the NPC forgets who hit it between ticks.");
			}

			if (AttackingState != null && AggressionDamageWeight <= 0f && AggressionHitBonus <= 0f)
			{
				problems.Add("Neither AggressionDamageWeight nor AggressionHitBonus is positive — being attacked generates no threat, so the threat table stays empty and event-driven combat entry never fires.");
			}

			if (ReturnHomeState == null && InitialState != null && InitialState.LeashUpdateRate > 0f)
			{
				problems.Add("InitialState has leashing enabled but no ReturnHomeState is assigned — CheckLeash bails out and the NPC never leashes.");
			}

			return problems.Count == 0;
		}

		/// <summary>
		/// Checks an attacking state's spacing and variety configuration.
		/// </summary>
		/// <param name="attacking">The attacking state to check.</param>
		/// <param name="problems">Receives one line per problem found.</param>
		private static void ValidateAttackingState(BaseAttackingState attacking, System.Collections.Generic.List<string> problems)
		{
			if (attacking.PreferredDistance > 0f &&
				attacking.MinComfortDistance > 0f &&
				attacking.MinComfortDistance >= attacking.PreferredDistance)
			{
				problems.Add($"'{attacking.name}': MinComfortDistance ({attacking.MinComfortDistance}) is not below PreferredDistance ({attacking.PreferredDistance}) — the NPC is permanently in its own kiting band and never stops backing away.");
			}

			if (attacking.MinComfortDistance > 0f &&
				attacking.EmergencyRetreatThreshold <= 0f)
			{
				problems.Add($"'{attacking.name}': MinComfortDistance is set but EmergencyRetreatThreshold is 0 — the NPC will never escalate to an emergency retreat.");
			}

			if (attacking.DetectionRadius <= 0f)
			{
				problems.Add($"'{attacking.name}': DetectionRadius is 0 — the NPC can never find an enemy to fight.");
			}

			if (attacking.MaxLeashRange > 0f && attacking.MaxLeashRange <= attacking.MinLeashRange)
			{
				problems.Add($"'{attacking.name}': MaxLeashRange ({attacking.MaxLeashRange}) is not greater than MinLeashRange ({attacking.MinLeashRange}) — the NPC warps home the instant it would walk home.");
			}

			if (attacking.MovementVarietyChance > 0f)
			{
				if (attacking.VarietyStates == null || attacking.VarietyStates.Count == 0)
				{
					problems.Add($"'{attacking.name}': MovementVarietyChance is set but VarietyStates is empty — the roll can never do anything.");
				}
				else
				{
					for (int i = 0; i < attacking.VarietyStates.Count; i++)
					{
						BaseAIState variety = attacking.VarietyStates[i];
						if (variety == null)
						{
							problems.Add($"'{attacking.name}': VarietyStates[{i}] is null.");
							continue;
						}
						if (!variety.KeepsCombatTarget)
						{
							problems.Add($"'{attacking.name}': variety state '{variety.name}' does not have KeepsCombatTarget enabled — entering it drops the combat target and ends the fight.");
						}
					}
				}
			}

			if (attacking.EmergencyRetreatState != null && !attacking.EmergencyRetreatState.KeepsCombatTarget)
			{
				problems.Add($"'{attacking.name}': EmergencyRetreatState '{attacking.EmergencyRetreatState.name}' does not have KeepsCombatTarget enabled — it needs the target to know which way to run.");
			}
		}
	}
}
