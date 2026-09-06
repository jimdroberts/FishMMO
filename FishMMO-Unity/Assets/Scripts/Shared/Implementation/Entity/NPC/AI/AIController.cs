using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Serialization;
using FishNet.Connection;
using FishNet.Object;
using FishMMO.Shared.Core;
using FishMMO.Logging;

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

		[Header("Archetype")]
		/// <summary>
		/// The NPC's brain: which states it uses, how it picks abilities, how it behaves in combat,
		/// how much threat it feels, and how it is throttled at distance. The only AI wiring on a
		/// prefab.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Every state and tuning property on this controller reads straight through to the
		/// archetype, so an NPC is configured by assigning one asset rather than by filling a
		/// dozen slots — and two NPCs that share an archetype cannot drift apart. There is no
		/// per-prefab override layer on purpose: the old one doubled every assignment, put the
		/// personality in two places, and let a prefab silently disagree with the brain it claimed
		/// to use. To make one creature behave differently, create another archetype.
		/// </para>
		/// <para>
		/// Assigning a different archetype at runtime — a spawner override, a harness — takes
		/// effect immediately: the threat table is retuned and the agent's avoidance priority is
		/// re-applied, and everything else is read live. <see cref="ResetState"/> hands a pooled
		/// instance back with the archetype it was authored with.
		/// </para>
		/// </remarks>
		[Tooltip("The NPC's whole brain. Every state and tuning value comes from this asset.")]
		[FormerlySerializedAs("Archetype")]
		[SerializeField]
		private AIArchetypeTemplate archetype;

		/// <summary>
		/// The archetype the prefab was authored with, captured on first initialisation.
		/// </summary>
		/// <remarks>
		/// A spawner override is a plain field write that outlives the spawn that made it, so a
		/// recycled instance would otherwise carry the previous spawner's brain into a spawner
		/// that expected the prefab's. Restored in <see cref="ResetState"/>.
		/// </remarks>
		private AIArchetypeTemplate prefabArchetype;

		/// <summary>Attacking state a boss phase has put in place of the archetype's, or null.</summary>
		private BaseAIState phaseAttackingState;

		/// <summary>Behavior tree a boss phase has put in place of the archetype's, or null.</summary>
		private AIBehaviorTree phaseBehaviorTree;

		/// <summary>Ability rotation a boss phase has put in place of the archetype's, or null.</summary>
		private AIAbilityRotation phaseAbilityRotation;

		/// <summary>
		/// The archetype this NPC runs. See the field remarks for what assigning one at runtime does.
		/// </summary>
		public AIArchetypeTemplate Archetype
		{
			get => archetype;
			set
			{
				if (archetype == value)
				{
					return;
				}
				archetype = value;
				if (Initialized)
				{
					ApplyArchetypeTuning();
				}
			}
		}

		/// <summary>
		/// The state the NPC starts in when it spawns.
		/// </summary>
		public BaseAIState InitialState => archetype != null ? archetype.InitialState : null;

		/// <summary>
		/// Random movement around the home position, or null when the archetype does not wander.
		/// </summary>
		public BaseAIState WanderState => archetype != null ? archetype.WanderState : null;

		/// <summary>
		/// Waypoint movement, or null when the archetype does not patrol.
		/// </summary>
		public BaseAIState PatrolState => archetype != null ? archetype.PatrolState : null;

		/// <summary>
		/// Leash return, or null when the archetype never leashes.
		/// </summary>
		public BaseAIState ReturnHomeState => archetype != null ? archetype.ReturnHomeState : null;

		/// <summary>
		/// Flee state, or null when the archetype fights to the death.
		/// </summary>
		public BaseAIState RetreatState => archetype != null ? archetype.RetreatState : null;

		/// <summary>
		/// The state this NPC falls back to when it has nothing to do.
		/// </summary>
		public BaseAIState IdleState => archetype != null ? archetype.IdleState : null;

		/// <summary>
		/// The combat state, or null when this NPC cannot fight. A boss phase's override wins over
		/// the archetype's while the phase is in force.
		/// </summary>
		public BaseAIState AttackingState =>
			phaseAttackingState != null ? phaseAttackingState : (archetype != null ? archetype.AttackingState : null);

		/// <summary>
		/// Optional state entered on death.
		/// </summary>
		public BaseAIState DeadState => archetype != null ? archetype.DeadState : null;

		/// <summary>
		/// Optional ability rotation. When assigned, <see cref="PickBestAbility"/> evaluates the
		/// rotation first. If no entry matches and <see cref="AIAbilityRotation.FallbackToDefault"/>
		/// is true, the default scoring-based picker runs as a fallback. A boss phase's override
		/// wins over the archetype's while the phase is in force.
		/// </summary>
		public AIAbilityRotation AbilityRotation =>
			phaseAbilityRotation != null ? phaseAbilityRotation : (archetype != null ? archetype.AbilityRotation : null);

		/// <summary>
		/// Optional combat personality that biases ability selection via per-category score
		/// multipliers. When assigned, <see cref="PickBestAbility"/> applies the personality's
		/// weight and bonus to each ability's score. Two NPCs with the same abilities but different
		/// personalities will favour different abilities in combat.
		/// </summary>
		public AICombatPersonality Personality => archetype != null ? archetype.Personality : null;

		/// <summary>
		/// Optional behavior tree that provides high-level decision making above the state machine.
		/// When assigned, the tree is evaluated each tick before the current state's UpdateState.
		/// If the tree produces a state transition (returns Success), UpdateState is skipped that
		/// tick. A boss phase's override wins over the archetype's while the phase is in force.
		/// </summary>
		public AIBehaviorTree BehaviorTree =>
			phaseBehaviorTree != null ? phaseBehaviorTree : (archetype != null ? archetype.BehaviorTree : null);

		/// <summary>
		/// Optional LOD settings for distance-based update throttling. When assigned, replaces the
		/// fixed 1-in-3 stagger with distance-based tiers: Active (nearby players), Nearby, Far,
		/// and Dormant (no observers). Null means always Active.
		/// </summary>
		public AILodSettings LodSettings => archetype != null ? archetype.LodSettings : null;

		/// <summary>
		/// How often (in seconds) to sweep for nearby enemies while out of combat.
		/// </summary>
		public float EnemySweepRate =>
			archetype != null && archetype.EnemySweepRate > 0f ? archetype.EnemySweepRate : DEFAULT_ENEMY_SWEEP_RATE;

		/// <summary>
		/// The NavMeshAgent avoidance priority (affects how strongly it avoids other agents).
		/// </summary>
		public AgentAvoidancePriority AvoidancePriority =>
			archetype != null ? archetype.AvoidancePriority : AgentAvoidancePriority.Medium;

		/// <summary>Enemy sweep rate for an NPC with no archetype.</summary>
		private const float DEFAULT_ENEMY_SWEEP_RATE = 1.5f;

		[Header("Boss Script")]
		/// <summary>
		/// Optional boss script defining phased encounters and timed mechanics.
		/// When assigned, the controller evaluates phase transitions and mechanic timers each tick.
		/// </summary>
		/// <remarks>
		/// Stays on the prefab rather than the archetype because it describes one encounter, not a
		/// reusable brain — a boss script on a shared archetype would fire its phases on every
		/// creature that borrowed it.
		/// </remarks>
		[Tooltip("Optional boss script for phased encounters.")]
		public BossScript BossScript;

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

		private float nextLeashUpdate = 0.0f;
		private float nextEnemySweepUpdate = 0.0f;
		private float aggressionTickTimer = 0.0f;
		private float cachedTargetHalfHeight = 0.0f;
		private Transform cachedTargetHeightSource;
		private int staggerID;
		/// <summary>Scratch list for <see cref="TransitionToRandomMovementState"/>; refilled per call.</summary>
		private readonly List<BaseAIState> movementStates = new List<BaseAIState>(4);
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
		/// Seconds covered by the state update currently executing.
		/// </summary>
		/// <remarks>
		/// A state updates every <c>updateRate</c> seconds, not every brain tick, so a timer a
		/// state advances must use this rather than <see cref="LastAiDeltaTime"/>. See
		/// <see cref="AIStateClock"/> for what happened when it did not.
		/// </remarks>
		public float StateDeltaTime { get; private set; }

		/// <summary>
		/// Per-NPC kiting allowance. See <see cref="AIKiteBudget"/>.
		/// </summary>
		[System.NonSerialized]
		public AIKiteBudget Kite;

		/// <summary>
		/// The longest reach among this NPC's offensive abilities, in metres. 0 when it knows none.
		/// </summary>
		/// <remarks>
		/// What an archetype's spacing is checked against: a comfort distance no ability can
		/// attack from is not kiting, it is running away. Refreshed with the ability cache.
		/// </remarks>
		public float MaxOffensiveReach { get; private set; }

		[Header("Separation")]
		/// <summary>
		/// Distance at which another NPC body starts pushing this one away. 0 = twice the agent radius.
		/// </summary>
		[Tooltip("Distance at which another NPC starts pushing this one away. 0 = twice the agent radius.")]
		public float SeparationRadius = 0f;

		/// <summary>
		/// Push speed when fully overlapped with another NPC, in metres per second.
		/// </summary>
		[Tooltip("Push speed when fully overlapped with another NPC. 0 disables separation.")]
		public float SeparationSpeed = 1.0f;

		/// <summary>
		/// The separation velocity computed on the last brain tick, applied by <see cref="StepAgent"/>.
		/// </summary>
		private Vector3 separationVelocity;

		/// <summary>
		/// Seconds between attempts to put an agent that has left the NavMesh back on it.
		/// </summary>
		public const float OFF_MESH_RESEAT_INTERVAL = 1.0f;

		/// <summary>
		/// Countdown to the next off-mesh re-seat attempt. See <see cref="RecoverIfOffMesh"/>.
		/// </summary>
		private float offMeshReseatTimer;

		/// <summary>
		/// True once the current off-mesh episode has been logged, so a lost NPC warns once, not once a second.
		/// </summary>
		private bool offMeshWarned;

		/// <summary>
		/// Squared speed the transform actually moved at over the last network tick, in (m/s)².
		/// </summary>
		/// <remarks>
		/// Measured from the displacement <see cref="StepAgent"/> applied after the NavMesh
		/// projection, not from <see cref="NavMeshAgent.velocity"/>. With crowd avoidance off
		/// (<see cref="InitializeOnce"/>) nothing ever blocks the agent's simulated velocity, so it
		/// cannot tell a walking NPC from one whose step the mesh projection keeps clamping to the
		/// same point; the displacement can. Read by <see cref="GetMovementProgress"/>.
		/// </remarks>
		private float measuredTickSpeedSqr;

		/// <summary>Scratch buffer for the separation overlap query. Grown on demand.</summary>
		private Collider[] separationHits = new Collider[16];

		/// <summary>Scratch list of neighbour positions for <see cref="AISeparation.Resolve"/>.</summary>
		private readonly List<Vector3> separationNeighbours = new List<Vector3>(16);

		/// <summary>Scratch list of bodies already counted, so a multi-collider NPC pushes once.</summary>
		private readonly List<GameObject> separationKeys = new List<GameObject>(16);

		/// <summary>Schedules the current state's updates and measures the interval each covers.</summary>
		private AIStateClock stateClock;

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

			// The brain the prefab was authored with; ResetState restores it after a spawner override.
			prefabArchetype = archetype;

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

			// One threat table per NPC; ApplyArchetypeTuning below gives it the archetype's numbers.
			AggressionState = new AggressionState(Character);

			// Wire event-driven combat entry: when the NPC takes damage for the first time,
			// enter combat immediately instead of waiting for the next physics sweep.
			AggressionState.OnCombatInitiated = OnThreatReceived;

			if (Agent == null)
			{
				Agent = GetComponent<NavMeshAgent>();
			}

			ApplyArchetypeTuning();
			Agent.speed = Constants.Character.WalkSpeed;

			/* The agent simulates; the tick moves the transform. See StepAgent.
			 *
			 * With updatePosition on, the NavMeshAgent writes the transform every FRAME while the
			 * NetworkTransform samples it every TICK. The scene server runs 60 FPS against a 30 Hz
			 * tick, so a tick normally covers two frames of agent motion but regularly covers one
			 * or three — a per-tick displacement that swings between 0.5x and 1.5x the true speed.
			 * FishNet's abnormal-rate corrector only recognises exactly 0.5x and 2x, so the 1.5x
			 * case reaches every observer as a stutter. Issue #220.
			 *
			 * With updateRotation on, the agent also turns the transform toward its velocity every
			 * frame while FaceLookTarget turns it toward the target every tick: two writers, and a
			 * chasing NPC's heading flickered between them on the wire. Both flags are cleared and
			 * both writes happen once per tick, in StepAgent. Harmless on a client, where
			 * OnStartNetwork disables the agent outright. */
			Agent.updatePosition = false;
			Agent.updateRotation = false;

			/* No crowd avoidance. Unity's crowd is one global simulation, and the scene server
			 * stacks instances of the same scene at the same coordinates, so avoidance made NPCs
			 * steer around NPCs in OTHER instances. AISeparation replaces it, scoped to this
			 * NPC's own PhysicsScene; AICombatSlots spaces attackers around a target. */
			Agent.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance;

			// Initialize boss script runtime state if a boss script is assigned.
			if (BossScript != null)
			{
				BossState = new BossScriptState(BossScript);
			}
		}

		/// <summary>
		/// Pushes the parts of the archetype that are consumed once, rather than read live, into
		/// the objects that hold them: the threat table's weights and the agent's avoidance priority.
		/// </summary>
		/// <remarks>
		/// Runs from <see cref="InitializeOnce"/> and again whenever <see cref="Archetype"/> changes
		/// on an initialised controller, which is what makes a spawner override take on a recycled
		/// instance rather than only on the first spawn of that pooled object.
		/// </remarks>
		private void ApplyArchetypeTuning()
		{
			if (AggressionState != null)
			{
				if (archetype != null)
				{
					AggressionState.Configure(
						archetype.AggressionDamageWeight,
						archetype.AggressionHealingWeight,
						archetype.AggressionHitBonus,
						archetype.AggressionDecayRate,
						archetype.AggressionStaleTimeout,
						archetype.AggressionVarietyChance);
				}
				else
				{
					AggressionState.ConfigureDefaults();
				}
			}

			if (Agent != null)
			{
				Agent.avoidancePriority = (int)AvoidancePriority;
			}
		}

		/// <summary>
		/// Puts a boss phase's overrides in front of the archetype's slots. A null argument leaves
		/// that slot's current override in place, so a later phase that only replaces the rotation
		/// keeps the attacking state an earlier phase installed.
		/// </summary>
		/// <param name="attackingState">Attacking state for the phase, or null to keep the current one.</param>
		/// <param name="behaviorTree">Behavior tree for the phase, or null to keep the current one.</param>
		/// <param name="abilityRotation">Ability rotation for the phase, or null to keep the current one.</param>
		public void SetPhaseOverrides(BaseAIState attackingState, AIBehaviorTree behaviorTree, AIAbilityRotation abilityRotation)
		{
			if (attackingState != null)
			{
				phaseAttackingState = attackingState;
			}
			if (behaviorTree != null)
			{
				phaseBehaviorTree = behaviorTree;
			}
			if (abilityRotation != null)
			{
				phaseAbilityRotation = abilityRotation;
			}
		}

		/// <summary>
		/// Drops every boss phase override so the archetype's own slots show through again.
		/// </summary>
		public void ClearPhaseOverrides()
		{
			phaseAttackingState = null;
			phaseBehaviorTree = null;
			phaseAbilityRotation = null;
		}

		/// <summary>
		/// Returns the boss script to its first phase and drops the overrides later phases installed.
		/// </summary>
		/// <remarks>
		/// The two have to go together: phase 0 is never "transitioned to", so a reset that only
		/// rewound the phase index left the boss fighting with its final phase's attacking state.
		/// </remarks>
		private void ResetBossScript()
		{
			BossState?.Reset();
			ClearPhaseOverrides();
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
			StateDeltaTime = 0f;
			Kite.Clear();
			MaxOffensiveReach = 0f;
			separationVelocity = Vector3.zero;
			offMeshReseatTimer = 0f;
			offMeshWarned = false;
			stateClock = default;
			aggressionTickTimer = 0f;
			cachedTargetHalfHeight = 0f;
			cachedTargetHeightSource = null;
			behaviorTreeTimer = 0f;
			lodReevaluateTimer = 0f;
			currentLodTier = AILodTier.Active;
			Group = null;
			GroupRole = NPCGroupRole.None;
			PendingState = null;
			ResetBossScript();

			/* Give the instance back with the brain it was authored with. A spawner override is a
			 * plain field write; without this the next spawner to draw this instance — one with no
			 * override, expecting the prefab default — silently inherits the previous brain. Only
			 * once InitializeOnce has captured it: FishNet also calls ResetState from OnDisable and
			 * OnDestroy, which can run before the character has bound its behaviours. */
			if (Initialized)
			{
				Archetype = prefabArchetype;
			}
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
			/* The brain is a tick subscription, not Update, so disabling the MonoBehaviour did not
			 * stop it: a corpse kept sweeping, leashing (warping home and healing itself) and
			 * driving its agent for the whole of its decay. NPC.Despawn disables the controller
			 * and calls HaltMovement; this is what makes the disable mean something. */
			if (!enabled)
			{
				return;
			}

			// An agent off the mesh cannot step, path or arrive. Put it back before anything asks it to.
			RecoverIfOffMesh(networkTickDelta);

			// Apply this tick's slice of agent motion before anything reads the position.
			StepAgent(networkTickDelta);

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
			UpdateSeparation();
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
			UpdateSeparation();
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
			// Nobody is close enough to see two far NPCs overlap; skip the query.
			separationVelocity = Vector3.zero;
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
					ResetBossScript();
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
		/// <summary>
		/// True while this NPC cannot be hurt. An immortal NPC has no reason to target anything, so
		/// neither the enemy sweep nor an incoming hit acquires a target for it.
		/// </summary>
		/// <remarks>
		/// Acquisition only. Both callers already stand down inside the attacking state, so a boss
		/// that turns immortal for a phase mid-fight keeps its target and keeps fighting; what this
		/// stops is an idle training dummy or invulnerable quest giver answering a stray hit by
		/// chasing the player across the map. <see cref="TargetController"/> applies the same rule
		/// to the acquisition trace a cast runs through.
		/// </remarks>
		private bool IsImmortal =>
			Character != null &&
			Character.TryGet(out ICharacterDamageController ownDamage) &&
			ownDamage.Immortal;

		public void OnThreatReceived(ICharacter attacker)
		{
			if (attacker == null || AttackingState == null)
				return;

			// Already in combat or returning home — don't interrupt.
			if (CurrentState == AttackingState || CurrentState == ReturnHomeState)
				return;

			// An immortal NPC has no reason to target anything.
			if (IsImmortal)
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
			// An immortal NPC has no reason to target anything.
			if (IsImmortal)
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
					/* WarpTo, not Agent.Warp(Home). A raw Warp to a point that is not exactly on
					 * the NavMesh fails, and the fallback of writing the transform left the agent
					 * off-mesh: AgentIsUsable was false from then on, every TryMoveTo failed, and
					 * the NPC stood at home for the rest of its life. WarpTo samples first. */
					WarpTo(Home);

					// Clear aggression on full leash reset.
					AggressionState?.Clear();

					// Reset boss script phases on leash.
					if (BossState != null && BossScript != null && BossScript.ResetOnLeash)
					{
						ResetBossScript();
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

			// Update the state when its interval has elapsed, telling it how long that really was.
			if (stateClock.Advance(deltaTime, out float elapsed))
			{
				StateDeltaTime = elapsed;
				CurrentState.UpdateState(this, elapsed);

				stateClock.Rearm(CurrentState.GetUpdateRate(this), deltaTime);
			}
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
			MaxOffensiveReach = 0f;
			foreach (var kvp in abilityController.KnownAbilities)
			{
				if (kvp.Value != null && kvp.Value.Template != null)
				{
					cachedAbilities.Add(kvp.Value);

					if (BaseAttackingState.IsEnemyAbility(kvp.Value))
					{
						MaxOffensiveReach = Mathf.Max(MaxOffensiveReach, ResolveAbilityReach(kvp.Value));
					}
				}
			}
			lastKnownAbilityCount = currentCount;
		}

		/// <summary>
		/// How far <paramref name="ability"/> can hit something from, for this NPC's body size.
		/// </summary>
		/// <remarks>
		/// Always use this rather than <see cref="Ability.Range"/> in AI code: the raw range is
		/// zero for anything that does not travel. See <see cref="AIAbilityReach"/>.
		/// </remarks>
		public float ResolveAbilityReach(Ability ability)
		{
			float casterRadius = Agent != null ? Agent.radius : 0.5f;
			return AIAbilityReach.Resolve(ability, casterRadius);
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

				float abilityRange = ResolveAbilityReach(ability);

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
				float reach = ResolveAbilityReach(ability);
				if (reach * reach >= sqrMinRange)
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
				stateClock.Rearm(CurrentState.GetUpdateRate(this));
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
			/* Refilled on every call rather than cached at initialisation, so a spawner that swaps
			 * the archetype after InitializeOnce gets the new archetype's movement states too. */
			movementStates.Clear();
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
			if (movementStates.Count < 1)
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

		/// <summary>
		/// Speed below which the agent's velocity is not worth turning toward, in metres per second.
		/// </summary>
		public const float HEADING_SPEED_THRESHOLD = 0.05f;

		/// <summary>
		/// Recomputes the push away from overlapping NPC bodies in this NPC's own physics scene.
		/// </summary>
		/// <remarks>
		/// Runs on the brain tick for the Active and Nearby tiers only. Players are not pushed
		/// against — they do not run agents and never took part in crowd avoidance either — and
		/// neither is anything outside this NPC's <see cref="PhysicsScene"/>, which is what keeps
		/// stacked scene instances from touching each other. See <see cref="AISeparation"/>.
		/// </remarks>
		private void UpdateSeparation()
		{
			separationVelocity = Vector3.zero;

			if (SeparationSpeed <= 0f || Agent == null || Character == null || !PhysicsScene.IsValid())
			{
				return;
			}

			float radius = SeparationRadius > 0f ? SeparationRadius : Agent.radius * 2f;
			Vector3 position = Character.Transform.position;

			int hitCount;
			while (true)
			{
				hitCount = PhysicsScene.OverlapSphere(position, radius, separationHits, Constants.Layers.Player, QueryTriggerInteraction.Ignore);
				if (!TargetOrdering.TryGrowQueryBuffer(ref separationHits, hitCount))
				{
					break;
				}
			}

			separationNeighbours.Clear();
			separationKeys.Clear();
			for (int i = 0; i < hitCount && i < separationHits.Length; ++i)
			{
				Collider hit = separationHits[i];
				if (hit == null)
				{
					continue;
				}

				GameObject key = TargetOrdering.ResolveHitKey(hit, out ICharacter other);
				if (key == null || other == null || other == Character || !(other is NPC))
				{
					continue;
				}
				if (TargetOrdering.ContainsBody(separationKeys, key))
				{
					continue;
				}
				separationKeys.Add(key);
				separationNeighbours.Add(other.Transform.position);
			}

			separationVelocity = AISeparation.Resolve(position, separationNeighbours, radius, SeparationSpeed);
		}

		/// <summary>
		/// Applies one network tick of the NavMeshAgent's simulated motion to the transform.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The agent keeps simulating every frame (path following, acceleration, crowd
		/// avoidance) but no longer touches the transform — see <see cref="InitializeOnce"/>.
		/// Each tick the transform advances by exactly <c>velocity × tickDelta</c>, the agent's
		/// internal position is re-seated on that point (which projects it back onto the NavMesh,
		/// so the y follows the mesh), and the heading turns toward the velocity at the agent's
		/// angular speed unless a <see cref="LookTarget"/> owns the facing.
		/// </para>
		/// <para>
		/// The displacement the NetworkTransform samples is therefore identical every tick for a
		/// given speed, regardless of how many frames the server happened to render in between.
		/// </para>
		/// </remarks>
		/// <param name="tickDelta">Seconds per network tick.</param>
		private void StepAgent(float tickDelta)
		{
			if (!AgentIsUsable() || tickDelta <= 0f)
			{
				measuredTickSpeedSqr = 0f;
				return;
			}

			Transform t = Character.Transform;
			Vector3 before = t.position;

			// Off-mesh links are traversed by the agent itself; just follow it.
			if (Agent.isOnOffMeshLink)
			{
				t.position = Agent.nextPosition;
				measuredTickSpeedSqr = MeasureTickSpeedSqr(before, t.position, tickDelta);
				return;
			}

			Vector3 velocity = Agent.velocity;
			// Separation moves the body but never turns it: an NPC nudged sideways keeps facing
			// where it was going.
			Vector3 step = ResolveTickStep(velocity + separationVelocity, tickDelta);

			/* Re-seat the simulation on the transform even when the step is zero: anything that
			 * moved the transform directly (a platform, a scripted placement) would otherwise
			 * leave the agent believing it is somewhere else. */
			Agent.nextPosition = before + step;
			t.position = Agent.nextPosition;

			// What the mesh let through, not what was asked for: the stuck detector reads this.
			measuredTickSpeedSqr = MeasureTickSpeedSqr(before, t.position, tickDelta);

			if (LookTarget == null && ResolveTickHeading(t.rotation, velocity, Agent.angularSpeed, tickDelta, out Quaternion heading))
			{
				t.rotation = heading;
			}
		}

		/// <summary>
		/// The displacement one tick of travel at <paramref name="velocity"/> covers.
		/// </summary>
		/// <remarks>Separated so the tick-uniformity StepAgent relies on can be asserted directly.</remarks>
		public static Vector3 ResolveTickStep(Vector3 velocity, float tickDelta)
		{
			return velocity * tickDelta;
		}

		/// <summary>
		/// The squared speed a displacement over one tick amounts to.
		/// </summary>
		/// <remarks>Separated so the stuck detector's input can be asserted directly.</remarks>
		/// <param name="before">Position at the start of the tick.</param>
		/// <param name="after">Position the tick actually reached, after NavMesh projection.</param>
		/// <param name="tickDelta">Seconds per tick.</param>
		/// <returns>Squared metres per second; zero for a non-positive tick.</returns>
		public static float MeasureTickSpeedSqr(Vector3 before, Vector3 after, float tickDelta)
		{
			if (tickDelta <= 0f)
			{
				return 0f;
			}
			return (after - before).sqrMagnitude / (tickDelta * tickDelta);
		}

		/// <summary>
		/// Re-seats an enabled agent that is no longer on the NavMesh, once per
		/// <see cref="OFF_MESH_RESEAT_INTERVAL"/> until it lands.
		/// </summary>
		/// <remarks>
		/// <para>
		/// With <c>updatePosition</c> off nothing else ever does this. A NavMeshAgent places itself
		/// on the mesh only when it is enabled; after that, a failed <see cref="WarpTo"/> (a spawn
		/// point with no mesh within reach) or the mesh going away underneath it (a stacked instance
		/// of the same scene unloading removes its copy of the NavMeshData, and the agent may have
		/// been standing on that copy) leaves <c>isOnNavMesh</c> false for good. Every guard in this
		/// class then reads <see cref="AgentIsUsable"/> as false: no step, no destination, no
		/// arrival — an NPC frozen mid-stride until the pool recycles it.
		/// </para>
		/// <para>
		/// Where it stands first, so a recovered NPC does not visibly teleport; home only when there
		/// is no mesh anywhere near it, which is what the leash would do anyway.
		/// </para>
		/// </remarks>
		/// <param name="tickDelta">Seconds per network tick.</param>
		private void RecoverIfOffMesh(float tickDelta)
		{
			if (Agent == null || !Agent.isActiveAndEnabled || Agent.isOnNavMesh)
			{
				offMeshReseatTimer = 0f;
				offMeshWarned = false;
				return;
			}

			if (!ShouldAttemptReseat(ref offMeshReseatTimer, tickDelta, OFF_MESH_RESEAT_INTERVAL))
			{
				return;
			}

			Vector3 standing = Character.Transform.position;
			Vector3 fallback = Home;
			bool reseated = WarpTo(standing) || (fallback != Vector3.zero && fallback != standing && WarpTo(fallback));

			if (!reseated && !offMeshWarned)
			{
				offMeshWarned = true;
				Log.Warning("AIController", $"{gameObject.name} is off the NavMesh at {standing} with no mesh within reach of it or its home {fallback}; retrying every {OFF_MESH_RESEAT_INTERVAL:0.#}s.");
			}
		}

		/// <summary>
		/// Counts down between off-mesh re-seat attempts and reports when one is due.
		/// </summary>
		/// <remarks>
		/// Separated so the cadence can be asserted directly. The first call of an episode is due
		/// at once (the timer is zero when the agent is on the mesh); every later one waits
		/// <paramref name="interval"/>, so a hopeless NPC costs one widening NavMesh sample a second,
		/// not thirty.
		/// </remarks>
		/// <param name="timer">Seconds until the next attempt; rearmed to <paramref name="interval"/> when one is due.</param>
		/// <param name="tickDelta">Seconds since the previous call.</param>
		/// <param name="interval">Seconds between attempts.</param>
		/// <returns>True when an attempt should be made now.</returns>
		public static bool ShouldAttemptReseat(ref float timer, float tickDelta, float interval)
		{
			timer -= tickDelta;
			if (timer > 0f)
			{
				return false;
			}
			timer = interval;
			return true;
		}

		/// <summary>
		/// Turns a heading toward the direction of travel, bounded by an angular speed.
		/// </summary>
		/// <remarks>
		/// Mirrors what <c>NavMeshAgent.updateRotation</c> does per frame, on the tick instead.
		/// Vertical velocity is ignored so a slope does not pitch the character, and a velocity
		/// below <see cref="HEADING_SPEED_THRESHOLD"/> leaves the heading alone: an agent braking
		/// to a stop or being nudged by avoidance must not spin to face the nudge.
		/// </remarks>
		/// <param name="current">The current rotation.</param>
		/// <param name="velocity">The agent's velocity.</param>
		/// <param name="angularSpeed">Maximum turn, in degrees per second.</param>
		/// <param name="tickDelta">Seconds per tick.</param>
		/// <param name="result">The rotation to apply.</param>
		/// <returns>True if the heading changed.</returns>
		public static bool ResolveTickHeading(Quaternion current, Vector3 velocity, float angularSpeed, float tickDelta, out Quaternion result)
		{
			result = current;

			velocity.y = 0f;
			if (velocity.sqrMagnitude < HEADING_SPEED_THRESHOLD * HEADING_SPEED_THRESHOLD)
			{
				return false;
			}

			Quaternion target = Quaternion.LookRotation(velocity.normalized, Vector3.up);
			result = Quaternion.RotateTowards(current, target, Mathf.Max(0f, angularSpeed) * tickDelta);
			return result != current;
		}

		/// <summary>
		/// Stops the NPC where it stands and forgets what it was doing, for a death.
		/// </summary>
		/// <remarks>
		/// Called by <see cref="NPC.Despawn"/> alongside disabling the controller. Disabling stops
		/// the brain; this stops the body: the agent's path is cleared, the target and look target
		/// are dropped so nothing re-engages, and the threat table is emptied. Without it the
		/// corpse's agent kept its destination and, when the brain was later re-enabled for the
		/// next pool occupant, was still heading for wherever its killer had been standing.
		/// </remarks>
		public void HaltMovement()
		{
			Target = null;
			LookTarget = null;
			ClearPath();
			Stop();
			AggressionState?.Clear();

			/* The corpse path calls this instead of exiting the attacking state, and the state's
			 * Exit is where a combat slot is normally given back. Without this an NPC killed
			 * mid-attack kept its ring slot around its victim for the whole of its decay: the
			 * pack still counted the corpse as an attacker and spread itself around a body. */
			ReleaseCombatSlots();
		}
	}
}