using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.AI;
using FishNet.Connection;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Controls AI navigation, state transitions, and behavior for NPCs using NavMeshAgent.
	/// Handles movement, enemy detection, leash logic, waypoints, state management, and
	/// provides a virtual camera for aiming abilities at targets during combat.
	/// </summary>
	[RequireComponent(typeof(NavMeshAgent))]
	public class AIController : CharacterBehaviour, IAIController
	{
		/// <summary>
		/// Buffer for storing colliders hit during enemy sweep.
		/// </summary>
		public Collider[] SweepHits = new Collider[20];

		/// <summary>
		/// How often (in seconds) to sweep for nearby enemies.
		/// </summary>
		public float EnemySweepRate = 1.5f;

		/// <summary>
		/// The initial AI state when the controller is started.
		/// </summary>
		public BaseAIState InitialState;

		/// <summary>
		/// The avoidance priority for this agent (affects how strongly it avoids other agents).
		/// </summary>
		public AgentAvoidancePriority AvoidancePriority = AgentAvoidancePriority.Medium;

		/// <summary>
		/// Reference to the wander state for random movement.
		/// </summary>
		public BaseAIState WanderState;

		/// <summary>
		/// Reference to the patrol state for waypoint movement.
		/// </summary>
		public BaseAIState PatrolState;

		/// <summary>
		/// Reference to the return home state for leash logic.
		/// </summary>
		public BaseAIState ReturnHomeState;

		/// <summary>
		/// Reference to the retreat state for fleeing behavior.
		/// </summary>
		public BaseAIState RetreatState;

		/// <summary>
		/// Reference to the idle state for passive behavior.
		/// </summary>
		public BaseAIState IdleState;

		/// <summary>
		/// Reference to the attacking state for combat behavior.
		/// </summary>
		public BaseAIState AttackingState;

		/// <summary>
		/// Reference to the dead state for death logic.
		/// </summary>
		public BaseAIState DeadState;

		[Header("Ability Rotation")]
		/// <summary>
		/// Optional ability rotation asset. When assigned, <see cref="PickBestAbility"/> evaluates
		/// the rotation first. If no entry matches and <see cref="AIAbilityRotation.FallbackToDefault"/>
		/// is true, the default scoring-based picker runs as a fallback.
		/// </summary>
		[Tooltip("Optional ability rotation for condition/sequence-based ability selection.")]
		public AIAbilityRotation AbilityRotation;

		[Header("Combat Personality")]
		/// <summary>
		/// Optional combat personality that biases ability selection via per-category score multipliers.
		/// When assigned, <see cref="PickBestAbility"/> applies the personality's weight and bonus
		/// to each ability's score. Two NPCs with the same abilities but different personalities
		/// will favour different abilities in combat.
		/// </summary>
		[Tooltip("Optional combat personality for data-driven ability preference.")]
		public AICombatPersonality Personality;

		[Header("Behavior Tree")]
		/// <summary>
		/// Optional behavior tree that provides high-level decision making above the state machine.
		/// When assigned, the tree is evaluated each tick before the current state's UpdateState.
		/// If the tree produces a state transition (returns Success), UpdateState is skipped that tick.
		/// </summary>
		[Tooltip("Optional behavior tree for high-level decision making.")]
		public AIBehaviorTree BehaviorTree;

		[Header("AI LOD")]
		/// <summary>
		/// Optional LOD settings for distance-based update throttling.
		/// When assigned, replaces the fixed 1-in-3 stagger with distance-based tiers:
		/// Active (nearby players), Nearby, Far, and Dormant (no observers).
		/// </summary>
		[Tooltip("Optional LOD settings for distance-based AI throttling.")]
		public AILodSettings LodSettings;

		[Header("Boss Script")]
		/// <summary>
		/// Optional boss script defining phased encounters and timed mechanics.
		/// When assigned, the controller evaluates phase transitions and mechanic timers each tick.
		/// </summary>
		[Tooltip("Optional boss script for phased encounters.")]
		public BossScript BossScript;

		[Header("Aggression / Threat")]
		/// <summary>
		/// Points awarded per 1 point of damage dealt to this NPC.
		/// </summary>
		[Tooltip("Aggression points per 1 damage taken.")]
		public float AggressionDamageWeight = 1.0f;

		/// <summary>
		/// Points awarded per 1 point of healing an enemy of the NPC witnesses.
		/// </summary>
		[Tooltip("Aggression points per 1 healing witnessed on a combat participant.")]
		public float AggressionHealingWeight = 0.6f;

		/// <summary>
		/// Flat points added per hit, regardless of damage amount.
		/// </summary>
		[Tooltip("Flat aggression per hit.")]
		public float AggressionHitBonus = 5.0f;

		/// <summary>
		/// Points per second that each entry decays when no new events occur.
		/// </summary>
		[Tooltip("Aggression decay per second.")]
		public float AggressionDecayRate = 3.0f;

		/// <summary>
		/// Seconds after last event before an entry is removed entirely.
		/// </summary>
		[Tooltip("Seconds before a stale aggression entry is pruned.")]
		public float AggressionStaleTimeout = 30.0f;

		/// <summary>
		/// Chance (0-1) that target selection ignores the top-threat target and picks a secondary one.
		/// </summary>
		[Range(0f, 1f)]
		[Tooltip("Chance to pick a non-top-threat target for variety.")]
		public float AggressionVarietyChance = 0.15f;

		[Header("Pathfinding")]
		/// <summary>
		/// Minimum seconds between <see cref="Agent"/>.<see cref="NavMeshAgent.SetDestination"/> calls
		/// made through <see cref="SetThrottledDestination"/>. Prevents path recalculation spam
		/// when a moving target causes frequent repathing.
		/// </summary>
		[Tooltip("Minimum seconds between NavMeshAgent.SetDestination calls via SetThrottledDestination.")]
		public float RepathInterval = 0.5f;

		/// <summary>
		/// The aggression (threat) state for this NPC. Manages the threat table, event
		/// subscriptions, and target re-evaluation timer. One instance per NPC — not shared.
		/// </summary>
		public AggressionState AggressionState { get; private set; }

		/// <summary>
		/// Convenience accessor for the underlying aggression controller.
		/// </summary>
		public AggressionController Aggression => AggressionState?.Controller;

		/// <summary>
		/// Per-NPC timer for mid-combat target re-evaluation. Delegates to
		/// <see cref="AggressionState.TargetReevaluationTimer"/>.
		/// </summary>
		public float TargetReevaluationTimer
		{
			get => AggressionState != null ? AggressionState.TargetReevaluationTimer : 0f;
			set { if (AggressionState != null) AggressionState.TargetReevaluationTimer = value; }
		}

		[SerializeField]
		private Transform eyeTransform;

		/// <summary>
		/// The transform used for vision checks. Defaults to the character's transform if not set.
		/// </summary>
		public Transform EyeTransform => eyeTransform != null ? eyeTransform : Character.Transform;

		/// <summary>
		/// The current look target for the AI (used for facing/rotation).
		/// </summary>
		public Transform LookTarget;

		/// <summary>
		/// If true, the AI will randomize its movement state.
		/// </summary>
		public bool RandomizeState;

		/// <summary>
		/// Virtual camera position used by the ability system to aim projectiles.
		/// Computed from the eye transform, aimed toward the current target's center.
		/// Mirrors the role of KCCController.VirtualCameraPosition for player characters.
		/// </summary>
		public Vector3 VirtualCameraPosition { get; private set; }

		/// <summary>
		/// Virtual camera rotation used by the ability system to aim projectiles.
		/// Points from the eye transform toward the current target's center.
		/// Mirrors the role of KCCController.VirtualCameraRotation for player characters.
		/// </summary>
		public Quaternion VirtualCameraRotation { get; private set; }

		//public List<AIState> AllowedRandomStates;

		/// <summary>
		/// The physics scene associated with this AI controller.
		/// </summary>
		public PhysicsScene PhysicsScene { get; private set; }

		/// <summary>
		/// The home position for this AI (used for leash and wandering).
		/// </summary>
		public Vector3 Home { get; set; }

		/// <summary>
		/// The current target for the AI (e.g., enemy, destination).
		/// Setting this property updates the agent's destination.
		/// </summary>
		public Transform Target
		{
			get { return target; }
			set
			{
				if (target == value)
					return;

				target = value;
				if (value != null)
				{
					// If a target is set, update the agent's destination to the target's position.
					if (Agent.isOnNavMesh)
						Agent.SetDestination(value.position);
				}
				else
				{
					// If no target, set destination to current position (stop moving).
					if (Agent.isOnNavMesh)
						Agent.SetDestination(transform.position);
				}
			}
		}

		/// <summary>
		/// The NavMeshAgent component used for navigation.
		/// </summary>
		public NavMeshAgent Agent { get; private set; }

		/// <summary>
		/// The current AI state.
		/// </summary>
		public BaseAIState CurrentState { get; private set; }

		/// <summary>
		/// The waypoints available to this AI controller.
		/// </summary>
		public Vector3[] Waypoints;

		/// <summary>
		/// The current waypoint index.
		/// </summary>
		public int CurrentWaypointIndex { get; private set; }

		private Transform target;
		private float nextUpdate = 0.0f;
		private float nextLeashUpdate = 0.0f;
		private float nextEnemySweepUpdate = 0.0f;
		private float aggressionTickTimer = 0.0f;
		private float cachedTargetHalfHeight = 0.0f;
		private Transform cachedTargetHeightSource;
		private int staggerID;
		private List<BaseAIState> movementStates = new List<BaseAIState>();
		private List<ICharacter> sweepResults = new List<ICharacter>(10);

		// --- Ability cache ---
		// Flat list rebuilt from IAbilityController.KnownAbilities when count changes.
		// Avoids dictionary enumeration overhead in PickBestAbility / HasAbilityInRange.
		private readonly List<Ability> cachedAbilities = new List<Ability>(8);
		private int lastKnownAbilityCount = -1;

		// --- Repath throttle ---
		private float repathCooldown;

		/// <summary>
		/// Reusable buffer for collecting targets during combat state updates.
		/// Used by attacking states to avoid per-frame GC allocations.
		/// </summary>
		public List<ICharacter> CombatTargetBuffer { get; } = new List<ICharacter>(10);

		private float behaviorTreeTimer;
		private float lodReevaluateTimer;
		private AILodTier currentLodTier = AILodTier.Active;

		/// <summary>
		/// The NPC group this controller belongs to. Set by <see cref="NPCGroup"/>.
		/// </summary>
		[System.NonSerialized]
		public NPCGroup Group;

		/// <summary>
		/// This NPC's role within its group. Set by <see cref="NPCGroup"/>.
		/// </summary>
		[System.NonSerialized]
		public NPCGroupRole GroupRole;

		/// <summary>
		/// Runtime state for the boss script. Null when no <see cref="BossScript"/> is assigned.
		/// </summary>
		public BossScriptState BossState { get; private set; }

		/// <summary>
		/// Current AI LOD tier. Determines how frequently this NPC's brain ticks.
		/// </summary>
		public AILodTier CurrentLodTier => currentLodTier;

		/// <summary>
		/// Per-NPC orbit angle (radians) used by <see cref="OrbitState"/>.
		/// Stored here instead of on the ScriptableObject to avoid the shared-instance
		/// mutable state problem.
		/// </summary>
		[System.NonSerialized]
		public float OrbitAngle;

		/// <summary>
		/// Per-NPC rotation index used by <see cref="AIAbilityRotation"/> in Sequence mode.
		/// Tracks which entry in the rotation to try next.
		/// </summary>
		[System.NonSerialized]
		public int RotationIndex;

		/// <summary>
		/// The seeded RNG from the owning <see cref="NPC"/>.
		/// All AI randomisation should use this instead of <c>DeterministicRNG.Shared</c>
		/// so that NPC behaviour is fully deterministic given the same seed.
		/// Returns null for non-NPC characters.
		/// </summary>
		public DeterministicRNG NpcRNG
		{
			get
			{
				NPC npc = Character as NPC;
				return npc?.RNG;
			}
		}

#if UNITY_EDITOR
		/// <summary>
		/// Draws gizmos in the editor to visualize agent radius and home position.
		/// </summary>
		void OnDrawGizmos()
		{
			if (Agent == null)
			{
				return;
			}
			Gizmos.color = Color.red;
			Gizmos.DrawWireSphere(transform.position, Agent.radius);

			if (Home != Vector3.zero)
			{
				if (WanderState != null && WanderState is WanderState wanderState)
				{
					Gizmos.color = Color.green;
					Gizmos.DrawWireSphere(Home, wanderState.WanderRadius);
				}
				Gizmos.color = Color.blue;
				Gizmos.DrawWireSphere(Home, 0.5f);
			}
		}
#endif

		/// <summary>
		/// Called when the network starts. Disables the controller if not running on the server.
		/// </summary>
		public override void OnStartNetwork()
		{
			base.OnStartNetwork();

			if (!base.IsServerStarted)
			{
				enabled = false;
				return;
			}
		}

		/// <summary>
		/// Initializes the controller and NavMeshAgent. Sets avoidance priority, speed, and movement states.
		/// </summary>
		public override void InitializeOnce()
		{
			base.InitializeOnce();

			// Derive a stagger ID so NPCs spread their updates across frames.
			staggerID = Mathf.Abs((int)(Character.ID % int.MaxValue));

			// Initialize the per-NPC aggression state with serialized tuning values.
			AggressionState = new AggressionState(
				Character,
				AggressionDamageWeight,
				AggressionHealingWeight,
				AggressionHitBonus,
				AggressionDecayRate,
				AggressionStaleTimeout,
				AggressionVarietyChance);

			// Wire event-driven combat entry: when the NPC takes damage for the first time,
			// enter combat immediately instead of waiting for the next physics sweep.
			AggressionState.OnCombatInitiated = OnThreatReceived;

			if (Agent == null)
			{
				Agent = GetComponent<NavMeshAgent>();
			}

			Agent.avoidancePriority = (int)AvoidancePriority;
			Agent.speed = Constants.Character.WalkSpeed;

			// Add available movement states to the list for random selection.
			if (WanderState != null)
			{
				movementStates.Add(WanderState);
			}
			if (PatrolState != null)
			{
				movementStates.Add(PatrolState);
			}
			if (ReturnHomeState != null)
			{
				movementStates.Add(ReturnHomeState);
			}
			if (IdleState != null)
			{
				movementStates.Add(IdleState);
			}

			// Initialize boss script runtime state if a boss script is assigned.
			if (BossScript != null)
			{
				BossState = new BossScriptState(BossScript);
			}
		}

		/// <summary>
		/// Unsubscribes from global events on destroy to prevent memory leaks.
		/// </summary>
		public override void OnDestroying()
		{
			AggressionState?.Destroy();

			base.OnDestroying();
		}

		/// <summary>
		/// Initializes the controller with a home position and waypoints. Sets agent dimensions and initial state.
		/// </summary>
		/// <param name="home">The home position for the AI.</param>
		/// <param name="waypoints">Optional waypoints for patrol.</param>
		public void Initialize(Vector3 home, Vector3[] waypoints = null)
		{
			Home = home;
			Waypoints = waypoints;

			PhysicsScene = Character.GameObject.scene.GetPhysicsScene();

			Collider collider = Character.Transform.GetComponent<Collider>();
			if (collider != null && collider.TryGetDimensions(out float height, out float radius))
			{
				Agent.height = height;
				Agent.radius = radius;
			}
			else // default height and radius
			{
				Agent.height = 2.0f;
				Agent.radius = 0.5f;
			}

			// Set initial state
			ChangeState(InitialState);
		}

		/// <summary>
		/// Resets the controller's state, clearing home, target, look target, and virtual camera.
		/// </summary>
		/// <param name="asServer">Whether the reset is performed on the server.</param>
		public override void ResetState(bool asServer)
		{
			base.ResetState(asServer);

			Home = Vector3.zero;
			Target = null;
			LookTarget = null;
			VirtualCameraPosition = Vector3.zero;
			VirtualCameraRotation = Quaternion.identity;
			OrbitAngle = 0f;
			RotationIndex = 0;
			aggressionTickTimer = 0f;
			cachedTargetHalfHeight = 0f;
			cachedTargetHeightSource = null;
			behaviorTreeTimer = 0f;
			lodReevaluateTimer = 0f;
			currentLodTier = AILodTier.Active;
			Group = null;
			GroupRole = NPCGroupRole.None;
			BossState?.Reset();
			AggressionState?.Clear();
			cachedAbilities.Clear();
			lastKnownAbilityCount = -1;
			repathCooldown = 0f;
		}

		/// <summary>
		/// Unity Update loop. Applies LOD-based tick scheduling and dispatches to
		/// tier-appropriate update pipelines for behavior simplification.
		/// <para>
		/// <b>Tick scheduling:</b> Each LOD tier has a frame stagger modulus that spreads
		/// NPC updates evenly across frames (e.g., Active: every 3rd frame ≈ 50ms at 60 FPS).
		/// Dormant NPCs use a dedicated high-modulus gate so even their wake-up check is cheap.
		/// </para>
		/// <para>
		/// <b>Behavior simplification:</b>
		/// <list type="bullet">
		///   <item><b>Active</b> — Full pipeline: sweep, leash, BT, boss, state machine, virtual camera, aggression, facing.</item>
		///   <item><b>Nearby</b> — Simplified: no enemy sweep (event-driven), no BT, no boss scripts. Combat still works via state machine.</item>
		///   <item><b>Far</b> — Minimal: no combat AI, no sweep, no aggression. Only wander/idle/return home.</item>
		///   <item><b>Dormant</b> — Suspended: only periodic LOD re-evaluation to wake up when a player approaches.</item>
		/// </list>
		/// </para>
		/// </summary>
		void Update()
		{
			if (LodSettings != null)
			{
				// --- Dormant Quick Bail ---
				// Dormant NPCs only need periodic LOD re-evaluation to wake up.
				// Use a dedicated high-modulus gate to minimize per-frame overhead.
				// At 60 FPS with DormantCheckModulus=120, a dormant NPC only runs
				// this check ~once every 2 seconds.
				if (currentLodTier == AILodTier.Dormant)
				{
					if ((Time.frameCount + staggerID) % LodSettings.DormantCheckModulus != 0)
						return;

					currentLodTier = EvaluateLodTier();
					if (currentLodTier == AILodTier.Dormant)
						return;

					// Woke up — reset the LOD timer and fall through to normal processing.
					lodReevaluateTimer = LodSettings.ReevaluateInterval;
				}

				// --- LOD Re-evaluation (non-dormant tiers) ---
				lodReevaluateTimer -= Time.deltaTime;
				if (lodReevaluateTimer <= 0f)
				{
					AILodTier previousTier = currentLodTier;
					currentLodTier = EvaluateLodTier();
					lodReevaluateTimer = LodSettings.ReevaluateInterval;

					// Handle tier transitions (e.g., disengage combat when going to Far).
					if (previousTier != currentLodTier)
					{
						OnLodTierChanged(previousTier, currentLodTier);
					}

					// If we just transitioned to Dormant, bail immediately.
					if (currentLodTier == AILodTier.Dormant)
						return;
				}

				// --- Frame Stagger Gate ---
				// Spreads NPC updates evenly across frames to avoid spikes.
				// Active: mod 3 ≈ 50ms, Nearby: mod 12 ≈ 200ms, Far: mod 60 ≈ 1s.
				int modulus = LodSettings.GetStaggerModulus(currentLodTier);
				if ((Time.frameCount + staggerID) % modulus != 0)
					return;
			}
			else
			{
				// No LOD settings — simple 1-in-3 frame stagger.
				if ((Time.frameCount + staggerID) % 3 != 0)
					return;
			}

			float dt = Time.deltaTime;

			// --- Dispatch to tier-appropriate update pipeline ---
			switch (currentLodTier)
			{
				case AILodTier.Active:
					UpdateActive(dt);
					break;
				case AILodTier.Nearby:
					UpdateNearby(dt);
					break;
				case AILodTier.Far:
					UpdateFar(dt);
					break;
			}
		}

		/// <summary>
		/// Full AI pipeline for Active tier NPCs (close to players).
		/// Runs all subsystems: enemy sweep, leash, behavior tree, boss scripts,
		/// state machine, virtual camera, aggression decay, and facing.
		/// </summary>
		private void UpdateActive(float dt)
		{
			repathCooldown -= dt;
			SweepForEnemies();
			CheckLeash();

			// --- Behavior Tree (decision layer) ---
			bool btHandled = false;
			if (BehaviorTree != null)
			{
				behaviorTreeTimer -= dt;
				if (behaviorTreeTimer <= 0f)
				{
					AINodeResult btResult = BehaviorTree.Evaluate(this);
					btHandled = (btResult == AINodeResult.Success);
					behaviorTreeTimer = BehaviorTree.TickRate;
				}
			}

			// --- Boss Script (phase & mechanic evaluation) ---
			if (BossState != null)
			{
				BossState.EvaluatePhases(this);
				BossState.TickMechanics(this, dt);
			}

			// --- State Machine (execution layer) ---
			if (!btHandled)
			{
				UpdateCurrentState();
			}

			UpdateVirtualCamera();
			TickAggression(dt);
			FaceLookTarget();
		}

		/// <summary>
		/// Simplified pipeline for Nearby tier NPCs (within medium range of players).
		/// Skips: enemy sweep (relies on event-driven <see cref="OnThreatReceived"/>),
		/// behavior tree, and boss scripts.
		/// Runs: leash, state machine, virtual camera, aggression decay, facing.
		/// Combat still functions via the state machine and event-driven damage entry.
		/// </summary>
		private void UpdateNearby(float dt)
		{
			repathCooldown -= dt;
			CheckLeash();
			UpdateCurrentState();
			UpdateVirtualCamera();
			TickAggression(dt);
			FaceLookTarget();
		}

		/// <summary>
		/// Minimal pipeline for Far tier NPCs (far from all players).
		/// No combat AI, no enemy sweep, no boss scripts, no aggression, no virtual camera.
		/// Only runs: leash check and basic state machine (wander/idle/return home).
		/// If the NPC is in a combat state, it transitions to idle.
		/// </summary>
		private void UpdateFar(float dt)
		{
			// Far tier NPCs should not be in combat — disengage if they are.
			if (CurrentState is BaseAttackingState)
			{
				AggressionState?.Clear();
				TransitionToIdleState();
				return;
			}

			CheckLeash();
			UpdateCurrentState();
		}

		/// <summary>
		/// Handles LOD tier transitions. Cleans up combat state when transitioning to
		/// lower tiers, and restores readiness when transitioning to higher tiers.
		/// <para>
		/// Transitioning to <see cref="AILodTier.Far"/> or <see cref="AILodTier.Dormant"/>:
		/// interrupts abilities, heals to full, clears aggression, and transitions to idle.
		/// This acts as a soft-leash reset — if no players are nearby, the NPC shouldn't
		/// remain in a damaged/combat state.
		/// </para>
		/// </summary>
		private void OnLodTierChanged(AILodTier previousTier, AILodTier newTier)
		{
			// Transitioning to Far or Dormant — full combat disengage.
			if (newTier >= AILodTier.Far && CurrentState is BaseAttackingState)
			{
				// Interrupt any active ability.
				if (Character.TryGet(out IAbilityController abilityController))
				{
					abilityController.Interrupt(null);
				}

				// Heal to full — no players are close enough to notice.
				if (Character.TryGet(out ICharacterDamageController damageController))
				{
					damageController.CompleteHeal();
				}

				// Clear threat table.
				AggressionState?.Clear();

				// Reset boss script phases.
				if (BossState != null && BossScript != null && BossScript.ResetOnLeash)
				{
					BossState.Reset();
				}

				TransitionToIdleState();
			}
		}

		/// <summary>
		/// Event-driven combat entry. Called by <see cref="AggressionState"/> when the
		/// NPC receives its first threat event (damage from a player/NPC). Immediately
		/// transitions to combat without waiting for the next <see cref="SweepForEnemies"/>
		/// physics poll.
		/// <para>
		/// This eliminates the biggest polling cost for non-Active NPCs: thousands of
		/// per-NPC physics OverlapSphere calls every <see cref="EnemySweepRate"/> seconds.
		/// Nearby/Far tier NPCs rely entirely on this event to detect combat.
		/// Active tier NPCs still run SweepForEnemies for proactive (hostile faction) detection.
		/// </para>
		/// </summary>
		/// <param name="attacker">The character that generated the first threat event.</param>
		public void OnThreatReceived(ICharacter attacker)
		{
			if (attacker == null || AttackingState == null)
				return;

			// Already in combat or returning home — don't interrupt.
			if (CurrentState == AttackingState || CurrentState == ReturnHomeState)
				return;

			// Verify the attacker is alive.
			if (!attacker.TryGet(out ICharacterDamageController dmg) || !dmg.IsAlive)
				return;

			// Enter combat immediately.
			Target = attacker.Transform;
			LookTarget = attacker.Transform;
			ChangeState(AttackingState);
		}

		/// <summary>
		/// Sweeps for nearby enemies and transitions to attacking state if any are found.
		/// </summary>
		private void SweepForEnemies()
		{
			// Only sweep for enemies if not returning home or already attacking.
			if (AttackingState == null ||
				CurrentState == ReturnHomeState ||
				CurrentState == AttackingState)
			{
				return;
			}
			if (nextEnemySweepUpdate < 0.0f)
			{
				// Check for nearby enemies if not in combat.
				sweepResults.Clear();
				if (AttackingState.SweepForEnemies(this, sweepResults))
				{
					ChangeState(AttackingState, sweepResults);
				}
				nextEnemySweepUpdate = EnemySweepRate;
			}
			nextEnemySweepUpdate -= Time.deltaTime;
		}

		/// <summary>
		/// Checks leash distance and transitions to return home or warps home if leash is exceeded.
		/// </summary>
		private void CheckLeash()
		{
			// Only check leash if leash logic is enabled and not already returning home.
			if (ReturnHomeState == null ||
				CurrentState == null ||
				CurrentState.LeashUpdateRate <= 0.0f ||
				CurrentState == ReturnHomeState)
			{
				return;
			}
			if (nextLeashUpdate < 0.0f)
			{
				float distanceToHome = (Home - Character.Transform.position).sqrMagnitude;

				// Warp back to home if leash is greatly exceeded.
				if (distanceToHome > CurrentState.MaxLeashRange * CurrentState.MaxLeashRange)
				{
					// Cancel any active ability before warping.
					if (Character.TryGet(out IAbilityController abilityController))
					{
						abilityController.Interrupt(null);
					}

					// Heal on returning home.
					if (Character.TryGet(out ICharacterDamageController characterDamageController))
					{
						characterDamageController.CompleteHeal();
					}
					// Attempt to warp home, fallback to setting position if warp fails.
					if (!Agent.Warp(Home))
					{
						Character.Transform.position = Home;
					}

					// Clear aggression on full leash reset.
					AggressionState?.Clear();

					// Reset boss script phases on leash.
					if (BossState != null && BossScript != null && BossScript.ResetOnLeash)
					{
						BossState.Reset();
					}

					return;
				}
				// If leash is exceeded but not critical, transition to return home state.
				else if (distanceToHome > CurrentState.MinLeashRange * CurrentState.MinLeashRange)
				{
					// Clear aggression to prevent pingpong — without this the threat
					// table persists and event-driven combat can immediately pull the
					// NPC back into attacking after it arrives home.
					AggressionState?.Clear();

					ChangeState(ReturnHomeState);
				}

				nextLeashUpdate = CurrentState.LeashUpdateRate;
			}
			nextLeashUpdate -= Time.deltaTime;
		}

		/// <summary>
		/// Updates the current state if needed, calling its UpdateState method.
		/// </summary>
		private void UpdateCurrentState()
		{
			if (Agent == null)
			{
				return;
			}
			if (CurrentState == null)
			{
				return;
			}

			// Update state if timer has elapsed.
			if (nextUpdate < 0.0f)
			{
				CurrentState.UpdateState(this, Time.deltaTime);

				nextUpdate = CurrentState.GetUpdateRate();
			}
			nextUpdate -= Time.deltaTime;
		}

		/// <summary>
		/// Throttles aggression decay to fixed 0.5s intervals instead of every tick.
		/// Called by <see cref="UpdateActive"/> and <see cref="UpdateNearby"/> but
		/// NOT by <see cref="UpdateFar"/> (Far tier NPCs have no threat table).
		/// </summary>
		private void TickAggression(float dt)
		{
			aggressionTickTimer -= dt;
			if (aggressionTickTimer <= 0f)
			{
				const float AGGRESSION_TICK_INTERVAL = 0.5f;
				AggressionState?.Tick(AGGRESSION_TICK_INTERVAL);
				aggressionTickTimer = AGGRESSION_TICK_INTERVAL;
			}
		}

		/// <summary>
		/// Stops the agent's movement.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void Stop()
		{
			Agent.isStopped = true;
		}

		/// <summary>
		/// Resumes the agent's movement.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void Resume()
		{
			Agent.isStopped = false;
		}

		/// <summary>
		/// Throttled destination setter. Only calls <see cref="NavMeshAgent.SetDestination"/>
		/// if enough time has elapsed since the last repath (controlled by <see cref="RepathInterval"/>).
		/// Use this for ongoing movement toward a moving target (chase, orbit, retreat) to prevent
		/// path recalculation spam. For one-time destinations (waypoint arrival, warp), use
		/// <see cref="NavMeshAgent.SetDestination"/> directly.
		/// </summary>
		/// <param name="position">The world position to navigate toward.</param>
		/// <returns>True if the destination was updated, false if throttled.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool SetThrottledDestination(Vector3 position)
		{
			if (repathCooldown > 0f)
				return false;
			if (!Agent.isOnNavMesh)
				return false;

			Agent.SetDestination(position);
			repathCooldown = RepathInterval;
			return true;
		}

		/// <summary>
		/// Rebuilds the cached ability list from <see cref="IAbilityController.KnownAbilities"/>
		/// if the ability count has changed. This replaces dictionary enumeration with flat list
		/// iteration in <see cref="PickBestAbility"/> and <see cref="HasAbilityInRange"/>.
		/// </summary>
		private void RebuildAbilityCacheIfDirty(IAbilityController abilityController)
		{
			int currentCount = abilityController.KnownAbilities.Count;
			if (currentCount == lastKnownAbilityCount)
				return;

			cachedAbilities.Clear();
			foreach (var kvp in abilityController.KnownAbilities)
			{
				if (kvp.Value != null && kvp.Value.Template != null)
				{
					cachedAbilities.Add(kvp.Value);
				}
			}
			lastKnownAbilityCount = currentCount;
		}

		/// <summary>
		/// Evaluates the AI LOD tier based on the nearest observer's distance.
		/// Uses the FishNet <c>NetworkObject.Observers</c> collection to find player connections,
		/// then checks the squared distance to each observer's character.
		/// Returns <see cref="AILodTier.Dormant"/> when no observers exist.
		/// </summary>
		private AILodTier EvaluateLodTier()
		{
			if (LodSettings == null)
				return AILodTier.Active;

			// No observers → dormant.
			if (Observers.Count < 1)
				return AILodTier.Dormant;

			// Find the nearest observer's squared distance.
			float nearestSqrDist = float.MaxValue;
			Vector3 npcPos = Character.Transform.position;

			foreach (var conn in Observers)
			{
				// Each observer connection has objects — find the one closest.
				foreach (var nob in conn.Objects)
				{
					if (nob == null || nob.transform == null) continue;

					float sqrDist = (nob.transform.position - npcPos).sqrMagnitude;
					if (sqrDist < nearestSqrDist)
					{
						nearestSqrDist = sqrDist;
					}
				}
			}

			return LodSettings.GetTier(nearestSqrDist);
		}

		/// <summary>
		/// Updates the virtual camera position and rotation to aim from the eye
		/// transform toward the current target. When no target is present, the
		/// camera simply looks along the character's forward direction.
		/// Called every frame so the ability system always has fresh aim data.
		/// </summary>
		private void UpdateVirtualCamera()
		{
			VirtualCameraPosition = EyeTransform.position;

			if (Target != null)
			{
				// Cache the target's collider half-height to avoid querying bounds every frame.
				if (cachedTargetHeightSource != Target)
				{
					cachedTargetHeightSource = Target;
					cachedTargetHalfHeight = 0f;

					ICharacter targetCharacter = Target.GetComponent<ICharacter>();
					if (targetCharacter != null && targetCharacter.Collider != null)
					{
						cachedTargetHalfHeight = targetCharacter.Collider.bounds.extents.y;
					}
				}

				Vector3 targetPoint = Target.position + Vector3.up * cachedTargetHalfHeight;

				Vector3 direction = (targetPoint - VirtualCameraPosition).normalized;
				if (direction.sqrMagnitude > 0.0001f)
				{
					VirtualCameraRotation = Quaternion.LookRotation(direction);
				}
			}
			else
			{
				VirtualCameraRotation = Character.Transform.rotation;
			}
		}

		/// <summary>
		/// Returns the squared distance from this NPC to its current target.
		/// Returns float.MaxValue if there is no target.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public float GetSqrDistanceToTarget()
		{
			if (Target == null) return float.MaxValue;
			return (Target.position - Character.Transform.position).sqrMagnitude;
		}

		/// <summary>
		/// Selects the best ability to use against the current target from the NPC's known abilities.
		/// <para>
		/// When an <see cref="AbilityRotation"/> is assigned, it is evaluated first. If it returns
		/// an ability, that ability is used. If no rotation entry matches and
		/// <see cref="AIAbilityRotation.FallbackToDefault"/> is true, the default scoring-based
		/// picker runs as a fallback.
		/// </para>
		/// Prefers abilities whose range covers the current distance. Among those, picks one at random
		/// weighted toward longer-cooldown (typically stronger) abilities. Returns null if no ability
		/// is usable (all on cooldown, out of resources, or no abilities known).
		/// </summary>
		/// <param name="preferredMaxRange">Maximum desired range. Abilities with range beyond this are still considered but deprioritized.</param>
		/// <returns>The chosen ability, or null if nothing is available.</returns>
		public Ability PickBestAbility(float preferredMaxRange = float.MaxValue)
		{
			if (!Character.TryGet(out IAbilityController abilityController))
				return null;
			if (!Character.TryGet(out ICooldownController cooldownController))
				return null;
			if (!Character.TryGet(out ICharacterDamageController damageController) || !damageController.IsAlive)
				return null;

			// --- Rotation-based selection (designer-driven) ---
			if (AbilityRotation != null)
			{
				// Resolve the target character for condition evaluation.
				ICharacter targetCharacter = Target != null ? Target.GetComponent<ICharacter>() : null;

				Ability rotationPick = AbilityRotation.Evaluate(
					this,
					abilityController,
					cooldownController,
					Character,
					targetCharacter);

				if (rotationPick != null)
					return rotationPick;

				// Rotation produced no match — check if we should fall back.
				if (!AbilityRotation.FallbackToDefault)
					return null;
			}

			// --- Rebuild ability cache if abilities changed ---
			RebuildAbilityCacheIfDirty(abilityController);

			// --- Default scoring-based selection ---
			float sqrDist = GetSqrDistanceToTarget();

			// Pre-compute health percentage for personality bonuses.
			float healthPercent = 1f;
			if (Personality != null && damageController.ResourceInstance != null &&
				damageController.ResourceInstance.FinalValue > 0f)
			{
				healthPercent = damageController.ResourceInstance.CurrentValue /
								damageController.ResourceInstance.FinalValue;
			}

			Ability bestAbility = null;
			float bestScore = float.MinValue;

			uint currentTick = base.TimeManager.LocalTick;

			EventData activationCheckData = null;

			for (int i = 0; i < cachedAbilities.Count; i++)
			{
				Ability ability = cachedAbilities[i];

				// Skip abilities on cooldown.
				if (cooldownController.IsOnCooldown(ability.ID, currentTick))
					continue;

				// Skip abilities the character can't afford.
				if (!ability.MeetsActivationConditions(Character, ref activationCheckData))
					continue;

				float abilityRange = ability.Range;

				// Score: prefer abilities that can reach the target.
				float score = 0f;
				if (abilityRange * abilityRange >= sqrDist)
				{
					// In range: strong bonus. Tiebreak by cooldown (longer cooldown = stronger ability).
					score = 1000f + ability.Cooldown;
				}
				else
				{
					// Out of range: low score, still a fallback.
					score = abilityRange;
				}

				// --- Personality-weighted scoring ---
				// Apply the personality's category weight as a multiplier, then add
				// any health-dependent bonus. This makes two NPCs with the same abilities
				// but different personalities favour different ability categories.
				if (Personality != null)
				{
					score *= Personality.GetWeight(ability);
					score += Personality.GetBonusScore(ability, healthPercent);
				}

				// Add small random jitter so the NPC doesn't always pick the same ability.
				// Uses the seeded NPC RNG for deterministic behaviour.
				DeterministicRNG rng = NpcRNG;
				score += (rng ?? DeterministicRNG.Shared).Range(0f, 50f);

				if (score > bestScore)
				{
					bestScore = score;
					bestAbility = ability;
				}
			}

			return bestAbility;
		}

		/// <summary>
		/// Returns true if the NPC has at least one ability with range >= the given distance
		/// that is off cooldown and meets activation conditions.
		/// </summary>
		/// <param name="minRange">Minimum ability range required.</param>
		public bool HasAbilityInRange(float minRange)
		{
			if (!Character.TryGet(out IAbilityController abilityController))
				return false;
			if (!Character.TryGet(out ICooldownController cooldownController))
				return false;

			RebuildAbilityCacheIfDirty(abilityController);

			float sqrMinRange = minRange * minRange;
			uint currentTick = base.TimeManager.LocalTick;

			EventData activationCheckData = null;

			for (int i = 0; i < cachedAbilities.Count; i++)
			{
				Ability ability = cachedAbilities[i];
				if (cooldownController.IsOnCooldown(ability.ID, currentTick))
					continue;
				if (!ability.MeetsActivationConditions(Character, ref activationCheckData))
					continue;
				if (ability.Range * ability.Range >= sqrMinRange)
					return true;
			}
			return false;
		}

		/// <summary>
		/// Changes the AI state, optionally providing targets for attacking states. Handles speed and state transitions.
		/// </summary>
		/// <param name="newState">The new state to transition to.</param>
		/// <param name="targets">Optional list of targets for attacking states.</param>
		public void ChangeState(BaseAIState newState, List<ICharacter> targets = null)
		{
			if (newState == null)
			{
				return;
			}

			if (CurrentState != null)
			{
				CurrentState.Exit(this);
			}

			//Log.Debug($"{this.gameObject.name} Transitioning to: {newState.GetType().Name}");

			CurrentState = newState;
			if (CurrentState != null)
			{
				nextUpdate = CurrentState.GetUpdateRate();
			}

			if (newState is BaseAttackingState attackingState)
			{
				// Set agent speed to run speed for attacking.
				Agent.speed = Constants.Character.RunSpeed;

				if (targets != null)
				{
					attackingState.PickTarget(this, targets);
				}

				// Alert the NPC group when entering combat.
				if (Group != null && Target != null)
				{
					Group.AlertGroup(Target);
				}
			}
			else
			{
				// Set agent speed to walk speed for non-attacking states.
				Agent.speed = Constants.Character.WalkSpeed;
			}
			CurrentState?.Enter(this);
		}

		/// <summary>
		/// Transitions to the idle state.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void TransitionToIdleState()
		{
			ChangeState(IdleState, null);
		}

		/// <summary>
		/// Transitions to a random movement state from the available movement states.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public virtual void TransitionToRandomMovementState()
		{
			if (movementStates == null || movementStates.Count < 1)
			{
				return;
			}

			BaseAIState randomState = movementStates.GetRandom();
			if (randomState != null)
			{
				ChangeState(randomState);
			}
		}

		/// <summary>
		/// Sets a random destination within a radius around the home position.
		/// </summary>
		/// <param name="radius">Radius to randomize destination.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void SetRandomHomeDestination(float radius = 5.0f)
		{
			Vector3 position = Home;
			if (radius > 0.0f)
			{
				position = Vector3Extensions.RandomPositionWithinRadius(Home, radius);
			}
			NavMeshHit hit;
			if (NavMesh.SamplePosition(position, out hit, radius, NavMesh.AllAreas))
			{
				Agent.SetDestination(hit.position);
			}
		}

		/// <summary>
		/// Sets a random destination within a radius around the current position.
		/// </summary>
		/// <param name="radius">Radius to randomize destination.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void SetRandomDestination(float radius = 5.0f)
		{
			Vector3 position = Character.Transform.position;
			if (radius > 0.0f)
			{
				position = Vector3Extensions.RandomPositionWithinRadius(position, radius);
			}
			NavMeshHit hit;
			if (NavMesh.SamplePosition(position, out hit, radius, NavMesh.AllAreas))
			{
				Agent.SetDestination(hit.position);
			}
		}

		/// <summary>
		/// Transitions to the next waypoint in the waypoint array.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void TransitionToNextWaypoint()
		{
			CurrentWaypointIndex = (CurrentWaypointIndex + 1) % Waypoints.Length;
			Agent.SetDestination(Waypoints[CurrentWaypointIndex]);
		}

		/// <summary>
		/// Picks the nearest waypoint to the current position and sets it as the destination.
		/// </summary>
		public void PickNearestWaypoint()
		{
			if (Waypoints != null && Waypoints.Length > 0)
			{
				float lastSqrDistance = 0.0f;
				int closestIndex = -1;
				// Find the nearest waypoint
				for (int i = 0; i < Waypoints.Length; ++i)
				{
					Vector3 waypoint = Waypoints[i];

					float sqrDistance = (Character.Transform.position - waypoint).sqrMagnitude;
					if (closestIndex < 0 || sqrDistance < lastSqrDistance)
					{
						lastSqrDistance = sqrDistance;
						closestIndex = i;
					}
				}
				Agent.SetDestination(Waypoints[closestIndex]);
				CurrentWaypointIndex = closestIndex;
			}
		}

		/// <summary>
		/// Rotates the character to face the current look target smoothly.
		/// </summary>
		public void FaceLookTarget()
		{
			if (LookTarget == null)
			{
				return;
			}

			// Get the direction from the agent to the LookTarget
			Vector3 direction = LookTarget.position - Character.Transform.position;
			direction.y = 0;

			if (direction == Vector3.zero)
			{
				return;
			}

			// Calculate the rotation needed to face the target
			Quaternion targetRotation = Quaternion.LookRotation(direction);

			// Apply a smooth rotation (adjust speed as needed)
			Character.Transform.rotation = Quaternion.Slerp(Character.Transform.rotation, targetRotation, Time.deltaTime * 5f);
		}
	}
}