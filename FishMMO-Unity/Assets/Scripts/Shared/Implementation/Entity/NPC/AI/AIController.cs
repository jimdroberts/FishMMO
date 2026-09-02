using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.AI;
using FishNet.Connection;
using FishNet.Object;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Controls AI navigation, state transitions, and behavior for NPCs using NavMeshAgent.
	/// Handles movement, enemy detection, leash logic, waypoints, state management, and
	/// provides a virtual camera for aiming abilities at targets during combat.
	/// </summary>
	[RequireComponent(typeof(NavMeshAgent))]
	public partial class AIController : CharacterBehaviour, IAIController
	{
		/// <summary>
		/// Buffer for storing colliders hit during enemy sweep. Grown on demand — see
		/// <see cref="BaseAIState.SweepForEnemies"/>.
		/// </summary>
		/// <remarks>
		/// Not a fixed 20. A non-allocating overlap returns at most <c>buffer.Length</c> results and
		/// says nothing about how many it discarded, and the ones it discarded were chosen by the
		/// physics broadphase — so an NPC in a fight larger than its buffer detected an arbitrary,
		/// run-varying subset of its attackers and ignored the rest. The sweep re-queries into a
		/// larger buffer through <c>TargetOrdering.TryGrowQueryBuffer</c> until it stops coming back
		/// full, which is the same treatment every other spatial query in the project gets.
		/// </remarks>
		public Collider[] SweepHits = new Collider[20];

		/// <summary>
		/// How often (in seconds) to sweep for nearby enemies.
		/// </summary>
		public float EnemySweepRate = 1.5f;

		[Header("Archetype")]
		/// <summary>
		/// Optional archetype asset that fills in every state and tuning slot below.
		/// </summary>
		/// <remarks>
		/// Applied in <see cref="InitializeOnce"/>. Fields the archetype leaves null keep whatever
		/// the prefab already had, so a prefab can override one slot without abandoning the
		/// archetype. Assign this instead of wiring a dozen slots by hand.
		/// </remarks>
		[Tooltip("Optional archetype asset that fills in the state and tuning slots below.")]
		public AIArchetypeTemplate Archetype;

		[Header("States")]
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

		/// <summary>
		/// How quickly the NPC turns to face its look target, in radians-ish per second.
		/// </summary>
		/// <remarks>
		/// Feeds an exponential smoothing factor, so the value is a rate rather than a hard
		/// angular speed: higher snaps faster, and the result is identical at any frame rate.
		/// </remarks>
		[Header("Facing")]
		[Tooltip("How quickly the NPC turns to face its target. Higher is snappier.")]
		public float TurnRate = 8.0f;

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
		/// Cached <see cref="Pet"/> view of <see cref="CharacterBehaviour.Character"/>, or null for
		/// a normal NPC. Resolved once in <see cref="InitializeOnce"/>.
		/// </summary>
		private Pet cachedPet;

		/// <summary>
		/// Backing field for <see cref="Home"/>, used by NPCs and by a pet under a Stay order.
		/// </summary>
		private Vector3 home;

		/// <summary>
		/// The anchor this AI leashes and wanders around.
		/// </summary>
		/// <remarks>
		/// <para>
		/// For a normal NPC this is its spawn point. <b>For a pet it is its owner</b> — a pet's
		/// home is a moving target, and every leash check, wander radius and return-home
		/// destination in the AI reads this property, so anchoring it to the owner here fixes all
		/// of them at once. Previously each site that cared had to remember to overwrite the field
		/// with the owner's position, and the ones that forgot dragged the pet back toward
		/// wherever it happened to be summoned.
		/// </para>
		/// <para>
		/// A pet ordered to <see cref="PetMovementOrder.Stay"/> is the exception: it holds the
		/// position it was standing in, which is what the setter stores.
		/// </para>
		/// </remarks>
		public Vector3 Home
		{
			get
			{
				if (cachedPet != null &&
					cachedPet.MovementOrder != PetMovementOrder.Stay &&
					cachedPet.PetOwner != null &&
					cachedPet.PetOwner.Transform != null)
				{
					return cachedPet.PetOwner.Transform.position;
				}
				return home;
			}
			set { home = value; }
		}

		/// <summary>
		/// The pet this controller drives, or null when it drives a normal NPC.
		/// </summary>
		public Pet OwningPet => cachedPet;

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

				/* Resolve the ICharacter once per target change rather than per use.
				 *
				 * GetComponent for an *interface* is markedly more expensive than for a concrete
				 * type — Unity has to walk the GameObject's component list and type-test each one.
				 * The combat path used to re-resolve the same target three to five times every
				 * tick: target validity, ability picking, combat-slot claiming, and the
				 * unreachable check each called it independently. At a few hundred NPCs in combat
				 * that is tens of thousands of interface lookups a second to answer a question
				 * whose answer only changes when the target does. */
				cachedTargetCharacter = value != null ? value.GetComponent<ICharacter>() : null;
				cachedTargetCharacterID = cachedTargetCharacter != null ? cachedTargetCharacter.ID : 0;

				if (!AgentIsUsable())
					return;

				if (value != null)
				{
					// If a target is set, update the agent's destination to the target's position.
					Agent.SetDestination(value.position);
				}
				else
				{
					// If no target, set destination to current position (stop moving).
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
		/// The state that is about to become <see cref="CurrentState"/>, visible to the outgoing
		/// state's <see cref="BaseAIState.Exit"/>.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Exists so an attacking state can tell "combat is over" from "combat is continuing in a
		/// sub-state". <see cref="BaseAttackingState.Exit"/> clears the target and interrupts the
		/// cast, which is right when the NPC disengages and catastrophic when it does not: the
		/// melee archetype's flanking roll called <c>ChangeState(GetBehindState)</c>, Exit wiped
		/// the target on the way out, and GetBehindState then found no target and dropped the NPC
		/// to idle. Every configured orbit / flank / strafe roll silently ended the fight.
		/// </para>
		/// <para>
		/// Null outside of a transition.
		/// </para>
		/// </remarks>
		public BaseAIState PendingState { get; private set; }

		/// <summary>
		/// The waypoints available to this AI controller.
		/// </summary>
		public Vector3[] Waypoints;

		/// <summary>
		/// The current waypoint index.
		/// </summary>
		public int CurrentWaypointIndex { get; private set; }

		private Transform target;

		/// <summary>
		/// The <see cref="ICharacter"/> on <see cref="Target"/>, resolved once when the target
		/// changes. Null when there is no target or it is not a character.
		/// </summary>
		private ICharacter cachedTargetCharacter;

		/// <summary>
		/// <see cref="ICharacter.ID"/> of the cached target at the moment it was targeted.
		/// </summary>
		/// <remarks>
		/// The identity check the Transform cannot provide. A pooled NetworkObject keeps its
		/// Transform and its components across occupants, so when the targeted character despawns
		/// and the pooled object is reactivated as somebody else, <see cref="Target"/> still
		/// compares equal, the setter never re-runs, and every null/active/alive validity check
		/// passes — the NPC silently continues its attack against the new occupant, who never
		/// engaged it. Comparing the character ID recorded at target time detects the swap.
		/// </remarks>
		private long cachedTargetCharacterID;

		/// <summary>
		/// The current combat target as an <see cref="ICharacter"/>, or null.
		/// </summary>
		/// <remarks>
		/// Prefer this over calling <c>Target.GetComponent&lt;ICharacter&gt;()</c>. The result is
		/// cached against the transform, so it costs a field read rather than an interface
		/// component lookup. Returns null when the pooled object behind the transform has been
		/// re-issued to a different character since targeting — see
		/// <see cref="cachedTargetCharacterID"/> — so every consumer's null check drops the stale
		/// target instead of attacking the pool's next occupant.
		/// </remarks>
		public ICharacter TargetCharacter
		{
			get
			{
				// The transform can be destroyed under us without the setter running.
				if (target == null)
				{
					return null;
				}
				if (cachedTargetCharacter != null && cachedTargetCharacter.ID != cachedTargetCharacterID)
				{
					return null;
				}
				return cachedTargetCharacter;
			}
		}

		/// <summary>
		/// How many times per second this NPC's brain runs, in hertz.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Rounded to the nearest whole divisor of the FishNet tick rate, so the brain always
		/// lands on network ticks and never drifts against them. At the project's 30 Hz network
		/// tick, 8 Hz resolves to every 4th tick — 7.5 Hz exactly, forever, on any hardware.
		/// <see cref="EffectiveAiTickRate"/> reports what a requested rate actually resolved to.
		/// </para>
		/// <para>
		/// 5-10 Hz is the useful band for an MMO brain. Decisions below about 5 Hz start to read
		/// as sluggish reaction time to a player; above about 10 Hz the NPC is re-deciding faster
		/// than its own pathing and animation can respond, so the extra ticks buy nothing but CPU.
		/// </para>
		/// </remarks>
		[Header("Tick Rate")]
		[Tooltip("Brain updates per second. 5-10 is the useful band. Rounded to a divisor of the network tick rate.")]
		[Range(1f, 30f)]
		public float AiTickRate = 8f;

		/// <summary>
		/// Network ticks between brain updates, derived from <see cref="AiTickRate"/>.
		/// </summary>
		private int ticksPerAiUpdate = 1;

		/// <summary>
		/// Network ticks elapsed since the last brain update.
		/// </summary>
		private int aiTickCounter;

		/// <summary>
		/// Seconds per network tick, cached from the TimeManager.
		/// </summary>
		private float networkTickDelta = 1f / 30f;

		/// <summary>
		/// Monotonic count of brain updates. Drives the LOD stagger.
		/// </summary>
		public uint AiTickIndex { get; private set; }

		/// <summary>
		/// The brain rate actually achieved, after rounding to a whole number of network ticks.
		/// </summary>
		public float EffectiveAiTickRate => ticksPerAiUpdate > 0 ? (1f / (networkTickDelta * ticksPerAiUpdate)) : 0f;

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
		/// Seconds remaining before this NPC may activate another ability.
		/// </summary>
		/// <remarks>
		/// Lives on the controller, not on the attacking state, because the state is a
		/// ScriptableObject shared by every NPC of that archetype — a timer stored there would be
		/// one global pacing clock for the whole population.
		/// </remarks>
		[System.NonSerialized]
		public float AttackCooldownTimer;

		/// <summary>
		/// Seconds of manoeuvring budget remaining for <see cref="RogueAttackingState"/>'s
		/// flanking attempt. Per-NPC for the same reason as <see cref="AttackCooldownTimer"/>.
		/// </summary>
		[System.NonSerialized]
		public float FlankTimer;

		/// <summary>
		/// Countdown used by bounded combat sub-states such as <see cref="OrbitState"/> to know
		/// when their manoeuvre is finished. Per-NPC, for the same reason as the other timers here.
		/// </summary>
		[System.NonSerialized]
		public float SubStateTimer;

		/// <summary>
		/// Seconds a pet has spent unable to reach its owner. Drives the follow state's teleport
		/// escape hatch. Per-NPC, for the same reason as the other timers here.
		/// </summary>
		[System.NonSerialized]
		public float PetStuckTimer;

		/// <summary>
		/// Seconds the NPC has spent unable to reach its combat target.
		/// </summary>
		[System.NonSerialized]
		public float UnreachableTargetTimer;

		/// <summary>
		/// True when the previous combat tick resolved to attacking or holding position rather
		/// than moving. Feeds the range hysteresis in <see cref="AICombatDecision"/>.
		/// </summary>
		[System.NonSerialized]
		public bool WasAttackingLastTick;

		/// <summary>
		/// Seconds until a healer archetype rescans for wounded allies.
		/// </summary>
		[System.NonSerialized]
		public float AllyScanTimer;

		/// <summary>
		/// The ally a healer archetype last chose to heal, re-validated cheaply between scans.
		/// </summary>
		[System.NonSerialized]
		public ICharacter CachedHealTarget;

		/// <summary>
		/// Seconds elapsed during the AI tick currently executing.
		/// </summary>
		/// <remarks>
		/// Published so helpers reached from deep inside a state's update — which do not receive
		/// deltaTime as a parameter — can still advance per-tick timers on the same clock the
		/// state machine runs on, rather than sampling <see cref="Time.deltaTime"/> and getting one
		/// frame instead of one AI tick.
		/// </remarks>
		public float LastAiDeltaTime { get; private set; }

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
				/* The agent is a server-side driver and must not run on a client at all. Left
				 * enabled, it keeps simulating: every frame it re-maps the transform — which the
				 * NetworkTransform has just interpolated — onto whatever NavMesh this client has,
				 * and runs crowd avoidance against every other visible NPC's agent. That is a
				 * per-frame fight over the transform that reads as jitter, and a snap wherever the
				 * interpolated path leaves the client's mesh. Disabling the component stops the
				 * simulation; the transform is then the NetworkTransform's alone. */
				if (Agent == null)
				{
					Agent = GetComponent<NavMeshAgent>();
				}
				if (Agent != null)
				{
					Agent.enabled = false;
				}
				enabled = false;
				return;
			}

			/* Driven by the FishNet TimeManager rather than Unity's Update.
			 *
			 * Everything else authoritative in this project already runs on ticks — prediction,
			 * cooldowns, ability activation — and the AI reading a variable frame delta made it
			 * the one system whose behaviour changed with server load. It also made the
			 * "deterministic" NPC RNG a half-truth: the seeded rolls were reproducible, but *when*
			 * they were drawn was not. */
			if (base.TimeManager != null)
			{
				networkTickDelta = (float)base.TimeManager.TickDelta;
				ResolveTickRate();

				base.TimeManager.OnTick += TimeManager_OnTick;
			}
		}

		/// <summary>
		/// Releases the tick subscription.
		/// </summary>
		public override void OnStopNetwork()
		{
			if (base.TimeManager != null)
			{
				base.TimeManager.OnTick -= TimeManager_OnTick;
			}

			aiTickCounter = 0;

			base.OnStopNetwork();
		}

		/// <summary>
		/// Converts the requested <see cref="AiTickRate"/> into a whole number of network ticks.
		/// </summary>
		/// <remarks>
		/// Rounding to a divisor is what keeps the brain phase-locked to the network tick. A
		/// fractional interval would mean the brain drifts across tick boundaries and its real
		/// rate wobbles, which is the problem this whole change exists to remove.
		/// </remarks>
		private void ResolveTickRate()
		{
			if (networkTickDelta <= 0f)
			{
				networkTickDelta = 1f / 30f;
			}

			float networkTickRate = 1f / networkTickDelta;
			float requested = Mathf.Clamp(AiTickRate, 0.1f, networkTickRate);

			ticksPerAiUpdate = Mathf.Max(1, Mathf.RoundToInt(networkTickRate / requested));

			// Stagger the very first brain update so a wave of NPCs spawned together does not all
			// think on the same tick for the rest of their lives.
			aiTickCounter = ticksPerAiUpdate > 1 ? (staggerID % ticksPerAiUpdate) : 0;
		}

		/// <summary>
		/// Initializes the controller and NavMeshAgent. Sets avoidance priority, speed, and movement states.
		/// </summary>
		public override void InitializeOnce()
		{
			base.InitializeOnce();

			// Apply the archetype before anything reads the state slots.
			if (Archetype != null)
			{
				Archetype.ApplyTo(this);
			}

			// Resolved once: Home reads this on every leash check and wander destination.
			cachedPet = Character as Pet;

			/* Derive a stagger ID so NPCs spread their updates across frames.
			 *
			 * Seeded from the GameObject's instance ID rather than Character.ID: behaviours are
			 * initialised from BaseCharacter.Awake, which runs before NPC.OnAwake registers the
			 * scene object and assigns an ID. Every NPC therefore read ID 0 here and every NPC
			 * landed on the same stagger bucket, so the whole population ticked on the same
			 * frames — the exact frame spike the stagger exists to prevent. */
			staggerID = Mathf.Abs(gameObject.GetInstanceID());

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
			ReleaseCombatSlots();
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
			ResetMovementState();

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

			/* Warp rather than trusting the transform. A recycled NPC comes out of the pool with
			 * its NavMeshAgent re-enabled at a new position, and the agent's internal NavMesh
			 * location is still wherever the previous occupant was — it then refuses to path, or
			 * paths back toward the old spot. Warp is what actually re-seats it. */
			WarpTo(home);

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

			/* Give up any combat ring slot before the ID is recycled. Without this a pooled NPC
			 * leaves a phantom occupant in its old target's ring, which inflates the ring's
			 * occupancy and pushes real attackers out to a further rank than they need. */
			ReleaseCombatSlots();

			home = Vector3.zero;
			Target = null;
			ResetMovementState();
			LookTarget = null;
			VirtualCameraPosition = Vector3.zero;
			VirtualCameraRotation = Quaternion.identity;
			OrbitAngle = 0f;
			RotationIndex = 0;
			AttackCooldownTimer = 0f;
			FlankTimer = 0f;
			SubStateTimer = 0f;
			PetStuckTimer = 0f;
			UnreachableTargetTimer = 0f;
			WasAttackingLastTick = false;
			AllyScanTimer = 0f;
			CachedHealTarget = null;
			LastAiDeltaTime = 0f;
			aggressionTickTimer = 0f;
			cachedTargetHalfHeight = 0f;
			cachedTargetHeightSource = null;
			behaviorTreeTimer = 0f;
			lodReevaluateTimer = 0f;
			currentLodTier = AILodTier.Active;
			Group = null;
			GroupRole = NPCGroupRole.None;
			PendingState = null;
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
		private void TimeManager_OnTick()
		{
			/* Facing runs on every network tick, the brain on a fraction of them.
			 *
			 * The two rates want different things. Rotation is replicated by the NetworkTransform,
			 * which sends on the network tick, so turning any faster than that is work nobody ever
			 * sees — and turning any slower makes an NPC's head visibly snap between orientations
			 * while its position is being smoothly interpolated. Matching the send rate is exactly
			 * right. The brain, meanwhile, has no reason to run at 30 Hz. */
			if (LookTarget != null)
			{
				FaceLookTarget(networkTickDelta);
			}

			aiTickCounter++;

			// --- AI tick gate: only a fraction of network ticks drive the brain. ---
			if (aiTickCounter < ticksPerAiUpdate)
			{
				return;
			}
			aiTickCounter = 0;

			// From here on, one "AI tick" has elapsed.
			AiTickIndex++;

			float aiTickDelta = networkTickDelta * ticksPerAiUpdate;

			int tickInterval = 1;

			if (LodSettings != null)
			{
				// --- LOD re-evaluation, on its own wall-clock interval. ---
				lodReevaluateTimer -= aiTickDelta;
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
				}

				tickInterval = LodSettings.GetTickInterval(currentLodTier);
			}

			/* Stagger gate.
			 *
			 * staggerID spreads NPCs across the interval so a thousand of them do not all think on
			 * the same tick and spike one frame in every N. Keyed off a monotonic AI tick index
			 * rather than Time.frameCount, so the spread is identical on a server running at 200
			 * FPS and one running at 30. */
			if (tickInterval > 1 && ((AiTickIndex + staggerID) % tickInterval) != 0)
			{
				return;
			}

			/* Exact, not accumulated. Every timer downstream — leash, sweep, threat decay, state
			 * update rates — advances by precisely the wall-clock time that elapsed, computed from
			 * the fixed network tick rather than measured from a variable frame. There is no drift
			 * to correct and no spike to clamp. */
			float dt = aiTickDelta * tickInterval;
			LastAiDeltaTime = dt;

			// Dormant NPCs run nothing but the re-evaluation above.
			if (currentLodTier == AILodTier.Dormant)
			{
				return;
			}

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
			SweepForEnemies(dt);
			CheckLeash(dt);

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
				UpdateCurrentState(dt);
			}

			UpdateVirtualCamera();
			TickAggression(dt);
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
			CheckLeash(dt);
			UpdateCurrentState(dt);
			UpdateVirtualCamera();
			TickAggression(dt);
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

			repathCooldown -= dt;
			CheckLeash(dt);
			UpdateCurrentState(dt);
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

			// A passive pet does not fight back; that is the whole meaning of the stance.
			if (!PetStanceAllowsAutoEngage(false))
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
		/// <param name="deltaTime">Seconds elapsed since the previous AI tick.</param>
		private void SweepForEnemies(float deltaTime)
		{
			// Only sweep for enemies if not returning home or already attacking.
			if (AttackingState == null ||
				CurrentState == ReturnHomeState ||
				CurrentState == AttackingState)
			{
				return;
			}

			/* A pet's engagement is decided by its stance, not by proximity. PetIdleState runs
			 * the aggressive sweep itself and the defensive case is event-driven from the owner
			 * being attacked; letting this generic sweep run as well would make every pet
			 * effectively aggressive regardless of what its owner ordered. */
			if (Character is Pet)
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
			nextEnemySweepUpdate -= deltaTime;
		}

		/// <summary>
		/// Releases this NPC's place in any combat ring, and clears the ring it was the target of.
		/// </summary>
		/// <remarks>
		/// Both directions matter: an NPC can be an attacker holding a slot and simultaneously be
		/// the target other attackers hold slots around.
		/// </remarks>
		private void ReleaseCombatSlots()
		{
			if (Character == null)
			{
				return;
			}

			AICombatSlots.Release(Character.ID);
			AICombatSlots.ReleaseTarget(Character.ID);
		}

		/// <summary>
		/// Returns whether this NPC's pet stance permits engaging on its own.
		/// Always true for anything that is not a pet.
		/// </summary>
		/// <param name="requiresAggressive">
		/// True to require the Aggressive stance (hunting for a fight); false to accept anything
		/// except Passive (fighting back).
		/// </param>
		/// <returns>True if the NPC may engage.</returns>
		public bool PetStanceAllowsAutoEngage(bool requiresAggressive)
		{
			Pet pet = Character as Pet;
			if (pet == null)
			{
				return true;
			}

			return requiresAggressive
				? pet.Stance == PetStance.Aggressive
				: pet.Stance != PetStance.Passive;
		}

		/// <summary>
		/// Checks leash distance and transitions to return home or warps home if leash is exceeded.
		/// </summary>
		/// <param name="deltaTime">Seconds elapsed since the previous AI tick.</param>
		private void CheckLeash(float deltaTime)
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
			nextLeashUpdate -= deltaTime;
		}

		/// <summary>
		/// Updates the current state if needed, calling its UpdateState method.
		/// </summary>
		/// <param name="deltaTime">Seconds elapsed since the previous AI tick.</param>
		private void UpdateCurrentState(float deltaTime)
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
				CurrentState.UpdateState(this, deltaTime);

				nextUpdate = CurrentState.GetUpdateRate(this);
			}
			nextUpdate -= deltaTime;
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
			if (!AgentIsUsable()) return;
			Agent.isStopped = true;
		}

		/// <summary>
		/// True when the NavMeshAgent can accept movement commands.
		/// </summary>
		/// <remarks>
		/// Unity rejects <c>isStopped</c> and <c>SetDestination</c> with an error for an agent that
		/// is disabled or not on a NavMesh. Spawn paths legitimately touch the brain around the
		/// moment an object is activated and placed, so guard rather than log a wall of errors.
		/// </remarks>
		/// <returns>True if the agent is enabled and on a NavMesh.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool AgentIsUsable()
		{
			return Agent != null && Agent.isActiveAndEnabled && Agent.isOnNavMesh;
		}

		/// <summary>
		/// Resumes the agent's movement.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void Resume()
		{
			if (!AgentIsUsable()) return;
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
			if (!AgentIsUsable())
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

			foreach (NetworkConnection conn in Observers)
			{
				/* FirstObject — the connection's player character — rather than walking every
				 * object the connection owns.
				 *
				 * The inner loop over conn.Objects was the expensive half of this method: it is
				 * O(observers x owned objects) per NPC per re-evaluation, and a connection owns
				 * more than its character (its pet, anything else it has been given authority
				 * over). Those are all within metres of the player anyway, so they never changed
				 * the answer — they just multiplied the work. At a thousand NPCs and a hundred
				 * players that inner loop was six figures of distance checks every couple of
				 * seconds, to compute a number the character alone already gives. */
				NetworkObject observerObject = conn?.FirstObject;
				if (observerObject == null)
				{
					continue;
				}

				float sqrDist = (observerObject.transform.position - npcPos).sqrMagnitude;
				if (sqrDist < nearestSqrDist)
				{
					nearestSqrDist = sqrDist;

					// Cannot do better than the closest tier; stop looking.
					if (nearestSqrDist <= LodSettings.ActiveDistanceSqr)
					{
						break;
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
		/// Returns this NPC's health as a fraction (0-1) of its maximum, or 1 when it has no
		/// health resource.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public float GetHealthPercent()
		{
			return AITargetSelection.GetHealthPercent(Character);
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
			return PickBestAbility(preferredMaxRange, null);
		}

		/// <summary>
		/// Selects the best ability to use against the current target, optionally restricted to
		/// abilities matching a predicate.
		/// </summary>
		/// <param name="preferredMaxRange">Maximum desired range.</param>
		/// <param name="filter">
		/// Optional predicate an ability must satisfy to be considered. Used by
		/// <see cref="HealerAttackingState"/> to keep heals out of the damage rotation.
		/// </param>
		/// <returns>The chosen ability, or null if nothing is available.</returns>
		public Ability PickBestAbility(float preferredMaxRange, System.Func<Ability, bool> filter)
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
				ICharacter targetCharacter = TargetCharacter;

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

			// --- Default scoring-based selection ---
			return PickScoredAbility(GetSqrDistanceToTarget(), filter, DEFAULT_ABILITY_JITTER);
		}

		/// <summary>
		/// Random score jitter applied by the default ability picker so an NPC does not always
		/// open with the same ability.
		/// </summary>
		private const float DEFAULT_ABILITY_JITTER = 50f;

		/// <summary>
		/// Scores every usable known ability against a subject at the given squared distance and
		/// returns the highest scorer.
		/// </summary>
		/// <remarks>
		/// Shared by the default enemy picker and by <see cref="HealerAttackingState"/>'s heal
		/// picker, which scores against an <em>ally's</em> distance rather than the target's.
		/// Both previously carried their own near-identical copy of this loop.
		/// </remarks>
		/// <param name="sqrDistanceToSubject">Squared distance to whatever the ability will be aimed at.</param>
		/// <param name="filter">Optional predicate an ability must satisfy. Null accepts all.</param>
		/// <param name="jitter">Maximum random score jitter, for variety.</param>
		/// <returns>The best-scoring usable ability, or null.</returns>
		public Ability PickScoredAbility(float sqrDistanceToSubject, System.Func<Ability, bool> filter, float jitter)
		{
			if (!Character.TryGet(out IAbilityController abilityController))
				return null;
			if (!Character.TryGet(out ICooldownController cooldownController))
				return null;
			if (!Character.TryGet(out ICharacterDamageController damageController) || !damageController.IsAlive)
				return null;

			// --- Rebuild ability cache if abilities changed ---
			RebuildAbilityCacheIfDirty(abilityController);

			float sqrDist = sqrDistanceToSubject;

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

			uint currentTick = cooldownController.ResolveAuthoritativeTick(base.TimeManager.LocalTick);

			EventData activationCheckData = null;

			for (int i = 0; i < cachedAbilities.Count; i++)
			{
				Ability ability = cachedAbilities[i];

				// Skip abilities the caller is not interested in (e.g. heals during a damage pick).
				if (filter != null && !filter(ability))
					continue;

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
				score += (rng ?? DeterministicRNG.Shared).Range(0f, jitter);

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
			uint currentTick = cooldownController.ResolveAuthoritativeTick(base.TimeManager.LocalTick);

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

			/* Re-entering the state already running is churn, not a transition: it runs Exit then
			 * Enter, which for IdleState means Resume() immediately followed by Stop(), every
			 * single tick. TransitionToRandomMovementState can legitimately roll the current state,
			 * so this is reachable in normal play rather than only through mistakes.
			 *
			 * Attacking states are exempt because re-entering with a fresh candidate list is how
			 * the NPC re-targets. */
			if (CurrentState == newState && !(newState is BaseAttackingState))
			{
				return;
			}

			if (CurrentState != null)
			{
				// Published before Exit so the outgoing state can see where the NPC is headed.
				PendingState = newState;
				try
				{
					CurrentState.Exit(this);
				}
				finally
				{
					PendingState = null;
				}
			}

			//Log.Debug($"{this.gameObject.name} Transitioning to: {newState.GetType().Name}");

			CurrentState = newState;
			if (CurrentState != null)
			{
				nextUpdate = CurrentState.GetUpdateRate(this);
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
		/// Exposes the serialized combat state through <see cref="IAIStateMachine"/>.
		/// Explicit because the inspector needs a field here, and a field cannot satisfy a
		/// property on an interface.
		/// </summary>
		BaseAIState IAIStateMachine.AttackingState => AttackingState;

		/// <summary>
		/// Exposes the serialized idle state through <see cref="IAIStateMachine"/>.
		/// </summary>
		BaseAIState IAIStateMachine.IdleState => IdleState;

		/// <summary>
		/// Forces this NPC onto a specific character immediately, entering combat if it is not
		/// already fighting.
		/// </summary>
		/// <remarks>
		/// The scripted-aggro entry point, used by <see cref="ApplyTauntAction"/>. Distinct from
		/// setting <see cref="Target"/> directly, which changes who the NPC is fighting without
		/// putting it into a state that fights.
		/// </remarks>
		/// <param name="character">The character to attack. Ignored when null or dead.</param>
		/// <returns>True if the NPC took the new target.</returns>
		public bool ForceTarget(ICharacter character)
		{
			if (character == null || !AITargetSelection.IsValidTarget(character))
			{
				return false;
			}

			// A passive pet stays out of it; a taunt is not an owner's order.
			if (!PetStanceAllowsAutoEngage(false))
			{
				return false;
			}

			if (!Character.TryGet(out ICharacterDamageController damageController) || !damageController.IsAlive)
			{
				return false;
			}

			Target = character.Transform;
			LookTarget = character.Transform;

			/* Arm the re-evaluation timer rather than zeroing it, so the NPC does not reconsider
			 * on its very next tick. The threat the taunt applied would normally hold it anyway,
			 * but a targeting mode that ignores threat (a rampaging beast) would otherwise shrug
			 * the taunt off one tick later. */
			BaseAttackingState attacking = AttackingState as BaseAttackingState;
			TargetReevaluationTimer = attacking != null ? attacking.TargetReevaluationRate : 0f;

			if (AttackingState != null && CurrentState != AttackingState)
			{
				ChangeState(AttackingState);
			}

			return true;
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
		/// <returns>True if a destination was set.</returns>
		public bool SetRandomHomeDestination(float radius = 5.0f)
		{
			Vector3 position = radius > 0.0f
				? Vector3Extensions.RandomPositionWithinRadius(Home, radius)
				: Home;

			return TryMoveTo(position, throttle: false) != AIMovementResult.Failed;
		}

		/// <summary>
		/// Sets a random destination within a radius around the current position.
		/// </summary>
		/// <param name="radius">Radius to randomize destination.</param>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		/// <returns>True if a destination was set.</returns>
		public bool SetRandomDestination(float radius = 5.0f)
		{
			Vector3 origin = Character.Transform.position;
			Vector3 position = radius > 0.0f
				? Vector3Extensions.RandomPositionWithinRadius(origin, radius)
				: origin;

			return TryMoveTo(position, throttle: false) != AIMovementResult.Failed;
		}

		/// <summary>
		/// Transitions to the next waypoint in the waypoint array.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		/// <returns>True if a waypoint destination was set.</returns>
		public bool TransitionToNextWaypoint()
		{
			if (Waypoints == null || Waypoints.Length < 1 || !AgentIsUsable()) return false;

			CurrentWaypointIndex = (CurrentWaypointIndex + 1) % Waypoints.Length;

			// Unthrottled: a waypoint is a one-shot destination, and a dropped request leaves the
			// NPC standing at the previous one believing it is on its way.
			return TryMoveTo(Waypoints[CurrentWaypointIndex], throttle: false) != AIMovementResult.Failed;
		}

		/// <summary>
		/// Picks the nearest waypoint to the current position and sets it as the destination.
		/// </summary>
		/// <returns>True if a waypoint destination was set.</returns>
		public bool PickNearestWaypoint()
		{
			if (!AgentIsUsable()) return false;
			if (Waypoints == null || Waypoints.Length < 1) return false;

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

			CurrentWaypointIndex = closestIndex;
			return TryMoveTo(Waypoints[closestIndex], throttle: false) != AIMovementResult.Failed;
		}

		/// <summary>
		/// Rotates the character to face the current look target smoothly.
		/// </summary>
		public void FaceLookTarget(float deltaTime)
		{
			if (LookTarget == null)
			{
				return;
			}

			// Get the direction from the agent to the LookTarget
			Vector3 direction = LookTarget.position - Character.Transform.position;
			direction.y = 0f;

			// Squared compare rather than == Vector3.zero: an exact-zero test misses the
			// near-degenerate case where the NPC is all but standing on its target, and
			// LookRotation on a near-zero vector produces a warning and an arbitrary rotation.
			if (direction.sqrMagnitude < 0.0001f)
			{
				return;
			}

			Quaternion targetRotation = Quaternion.LookRotation(direction);

			/* Exponential smoothing rather than Slerp(a, b, rate * dt).
			 *
			 * The linear form is frame-rate dependent: doubling the frame rate halves each step
			 * but does not halve the total turn, so an NPC visibly turns at a different speed on a
			 * 30 Hz server than on a 60 Hz one. 1 - e^(-rate * dt) is the closed form of the same
			 * smoothing sampled continuously, so the result is identical at any step size. */
			float t = 1f - Mathf.Exp(-TurnRate * deltaTime);

			Character.Transform.rotation = Quaternion.Slerp(Character.Transform.rotation, targetRotation, t);
		}
	}
}