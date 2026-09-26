using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Serialization;
using FishNet.Managing;
using FishNet.Managing.Server;
using FishNet.Managing.Timing;
using FishNet.Object;
using FishMMO.Shared.Core;
using FishMMO.Logging;
using FishMMO.Shared;
using FishMMO.Shared.Weather;
using FishMMO.Server.Core.World.SceneServer;

namespace FishMMO.Server.Implementation.World.SceneServer.AI
{
	/// <summary>
	/// Controls AI navigation, state transitions, and behavior for NPCs using NavMeshAgent.
	/// Handles movement, enemy detection, leash logic, waypoints, state management, and
	/// provides a virtual camera for aiming abilities at targets during combat.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Server only, and never on a prefab.</b> <see cref="AIBrainHost"/> adds this component
	/// (and, through <see cref="RequireComponent"/>, its <see cref="NavMeshAgent"/>) the first time
	/// the server spawns an NPC, keeps it across pool reuse, and drives it from one network-tick
	/// subscription for every brain. A client never compiles this type, so an NPC prefab carries no
	/// AI component and no AI data.
	/// </para>
	/// <para>
	/// A plain <see cref="MonoBehaviour"/>, not a <c>NetworkBehaviour</c>: FishNet indexes a
	/// NetworkObject's behaviours at edit time and cannot take one added at runtime. It registers
	/// with its character like any <see cref="CharacterBehaviour"/>, so
	/// <c>character.TryGet(out IAIController)</c> and <c>TryGet(out INPCBrain)</c> resolve it.
	/// </para>
	/// </remarks>
	[RequireComponent(typeof(NavMeshAgent))]
	[DisallowMultipleComponent]
	public partial class AIController : MonoBehaviour, IAIController, INPCBrain
	{
		/// <summary>
		/// The character this brain drives.
		/// </summary>
		public ICharacter Character { get; private set; }

		/// <summary>
		/// True once <see cref="InitializeOnce(ICharacter)"/> has bound this brain to its character.
		/// </summary>
		public bool Initialized { get; private set; }

		/// <summary>
		/// The character's network object, or null before the brain is bound.
		/// </summary>
		public NetworkObject NetworkObject => Character != null ? Character.NetworkObject : null;

		/// <summary>
		/// The network manager the character is spawned under, or null.
		/// </summary>
		public NetworkManager NetworkManager
		{
			get
			{
				NetworkObject networkObject = NetworkObject;
				return networkObject != null ? networkObject.NetworkManager : null;
			}
		}

		/// <summary>
		/// The time manager of <see cref="NetworkManager"/>, or null.
		/// </summary>
		public TimeManager TimeManager
		{
			get
			{
				NetworkManager networkManager = NetworkManager;
				return networkManager != null ? networkManager.TimeManager : null;
			}
		}

		/// <summary>
		/// The server manager of <see cref="NetworkManager"/>, or null.
		/// </summary>
		public ServerManager ServerManager
		{
			get
			{
				NetworkManager networkManager = NetworkManager;
				return networkManager != null ? networkManager.ServerManager : null;
			}
		}

		/// <summary>
		/// True between the host preparing this brain for a spawn and the NPC leaving the world.
		/// </summary>
		/// <remarks>
		/// Owned by <see cref="AIBrainHost"/>. A spawner prepares the brain before the network
		/// spawn so the agent is warped onto the mesh first; the host's spawn hook prepares any NPC
		/// nothing else did.
		/// </remarks>
		public bool Prepared { get; internal set; }

		/// <summary>
		/// Binds this brain to its character and registers it for <c>TryGet</c> lookups.
		/// </summary>
		/// <param name="character">The NPC this brain drives.</param>
		public void InitializeOnce(ICharacter character)
		{
			if (Initialized || character == null)
			{
				return;
			}

			Initialized = true;
			Character = character;
			Character.RegisterCharacterBehaviour(this);

			InitializeOnce();
		}

		/// <inheritdoc />
		public void OnStartCharacter() { }

		/// <inheritdoc />
		public void OnStopCharacter() { }

		/// <summary>
		/// Unregisters from the character and releases what the brain holds in shared tables.
		/// </summary>
		private void OnDestroy()
		{
			OnDestroying();

			if (Character != null)
			{
				Character.UnregisterCharacterBehaviour(this);
			}
			Character = null;
		}

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
		/// Assigned by <see cref="AIBrainHost"/> on every spawn — a spawner's override, or the
		/// NPC prefab's entry in the <see cref="AIBrainCatalogue"/> — so a pooled instance can never
		/// carry the previous occupant's brain into its next life. Assigning a different archetype
		/// takes effect immediately: the threat table is retuned and the agent's avoidance priority
		/// is re-applied, and everything else is read live.
		/// </para>
		/// </remarks>
		[Tooltip("The NPC's whole brain. Every state and tuning value comes from this asset.")]
		[FormerlySerializedAs("Archetype")]
		[SerializeField]
		private AIArchetypeTemplate archetype;

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
		/// Whether and how this NPC shelters from the weather, or null when the archetype says
		/// nothing about it. Off unless an archetype turns it on (Q15).
		/// </summary>
		public AIShelterSettings Shelter => archetype != null ? archetype.Shelter : null;

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
		/// Optional LOD settings for distance-based update throttling. When assigned, the brain
		/// runs distance-based tiers by nearest player: Active, Nearby, Far, and Dormant (nobody
		/// within the Far band). Null means always Active: every AI tick runs the full pipeline.
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
		/// Kept per NPC prefab (in the catalogue) rather than on the archetype because it describes
		/// one encounter, not a reusable brain — a boss script on a shared archetype would fire its
		/// phases on every creature that borrowed it.
		/// </remarks>
		[Tooltip("Optional boss script for phased encounters.")]
		[FormerlySerializedAs("BossScript")]
		[SerializeField]
		private BossScript bossScript;

		/// <summary>
		/// The boss script this NPC runs, or null. Assigning one starts it from its first phase.
		/// </summary>
		/// <remarks>
		/// Assigned by <see cref="AIBrainHost"/> from the NPC prefab's catalogue entry on every
		/// spawn, so the runtime state is rebuilt here rather than once at initialisation.
		/// </remarks>
		public BossScript BossScript
		{
			get => bossScript;
			set
			{
				if (bossScript == value)
				{
					return;
				}
				bossScript = value;
				// The old state's adds are nobody's to dismiss once the state is replaced.
				BossState?.ReleaseAdds();
				BossState = value != null ? new BossScriptState(value) : null;
				ClearPhaseOverrides();
			}
		}

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

		/// <summary>
		/// The current look target for the AI (used for facing/rotation).
		/// </summary>
		public Transform LookTarget;

		/// <summary>
		/// If true, the AI will randomize its movement state.
		/// </summary>
		public bool RandomizeState;

		/// <summary>
		/// The world-space point this NPC's abilities fire from.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>Computed, never cached.</b> This returns exactly what
		/// <see cref="CharacterAimOrigin.Resolve(ICharacter)"/> returns on every peer, and it must:
		/// only the aim <em>direction</em> is replicated, so wherever a shot is actually spawned the
		/// origin is re-derived independently. A cached copy of a derived value is the defect this
		/// replaces — the old code solved its aim direction from the character root while the
		/// projectile left from root + 1.45 m, and nothing could detect the disagreement.
		/// </para>
		/// <para>
		/// <b>One point, two consumers.</b> The aim direction is solved from here and
		/// <see cref="BaseAIState.HasLineOfSight"/> rays from here, so the point an NPC shoots from
		/// and the point it sees from are the same by construction. They used to differ — vision
		/// rayed from the ankles, aim was solved from the ankles, the bolt left the eye — and that
		/// 1.45 m of parallel-ray displacement is what made every fireball pass over its target's
		/// head, at any range.
		/// </para>
		/// </remarks>
		public Vector3 AimOrigin => CharacterAimOrigin.Resolve(Character);

		/// <summary>
		/// The rotation whose forward vector is the direction this NPC aims.
		/// </summary>
		/// <remarks>
		/// Written by <see cref="UpdateAim"/> on every network tick, because
		/// <c>AbilityController.PopulateAiAim</c> reads it on every network tick. It is held as
		/// state rather than derived on demand because the per-cast scatter offset is deliberately
		/// fixed for the duration of a cast — see <see cref="AimScatter"/> — so the result depends
		/// on more than the current positions.
		/// </remarks>
		public Quaternion AimRotation { get; private set; }

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

				/* The aim ramp and the velocity estimate are both properties of *this* engagement.
				 * Carrying either across a target change would hand a freshly acquired target the
				 * accuracy earned against the last one, and let the lead inherit a velocity sampled
				 * from a body that is no longer here. Reset here rather than in UpdateAim so that
				 * every path which changes the target gets it, including the ones that never reach
				 * an aim tick. */
				AimLock = 0f;
				TargetVelocity = Vector3.zero;
				hasLastTargetPosition = false;

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
		public float AiTickRate = DEFAULT_AI_TICK_RATE;

		/// <summary>
		/// The brain rate every NPC runs unless its <see cref="AiTickRate"/> is changed, and the rate
		/// <see cref="AIBrainHost"/> ticks packs at.
		/// </summary>
		public const float DEFAULT_AI_TICK_RATE = 8f;

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

		/// <summary>
		/// Seconds between threat-table decay passes.
		/// </summary>
		private const float AGGRESSION_TICK_INTERVAL = 0.5f;

		/// <summary>
		/// Schedules threat decay and measures the time each pass covers. See <see cref="TickAggression"/>.
		/// </summary>
		private AIStateClock aggressionClock;

		/// <summary>
		/// This NPC's slot in the brain-tick stagger. Derived from <see cref="IdentityKey"/>.
		/// </summary>
		private int staggerID;

		/// <summary>
		/// A well-mixed hash of this pooled instance's identity: the one source every per-NPC
		/// phase, stagger and tie-break is drawn from.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Hashed rather than used raw. Unity hands out instance IDs sequentially across every
		/// object and component an instantiation creates, so a camp of one prefab spawned together
		/// tends to have IDs a constant stride apart — and a stride that shares a factor with the
		/// brain interval puts the whole camp on a fraction of the brain ticks, <c>id % 4</c> on
		/// the same one at worst. The hash is a bijection on 32 bits, so distinct instances still
		/// get distinct keys, which is what lets <see cref="AISeparation"/> break a tie with it.
		/// </para>
		/// <para>
		/// Stable for the life of the pooled object, so an NPC keeps its phases across respawns
		/// and nothing draws from its seeded RNG to pick them.
		/// </para>
		/// </remarks>
		public uint IdentityKey { get; private set; }

		/// <summary>
		/// The brain host driving this controller, set by <see cref="AIBrainHost.Prepare"/>. Null
		/// for a brain nothing hosts, which then has no neighbours to separate from.
		/// </summary>
		internal AIBrainHost Host { get; set; }

		/// <summary>
		/// True once the host has stopped ticking this brain because it kept throwing. Cleared
		/// when the NPC is next prepared. See <see cref="AIBrainHost.MaxConsecutiveTickFaults"/>.
		/// </summary>
		public bool Quarantined { get; internal set; }

		/// <summary>
		/// Every collider in the NPC's hierarchy, collected when the brain is first bound.
		/// </summary>
		internal Collider[] BodyColliders { get; private set; }

		/// <summary>
		/// True while the brain is live: prepared for this spawn, not suspended for a corpse, and
		/// not quarantined by the host.
		/// </summary>
		public bool IsRunning => enabled && Prepared && !Quarantined;

		/// <summary>
		/// True while the NPC is on a leash return that began in a fight. Half of
		/// <see cref="IsEvading"/>; the other half is that the return is still the current state.
		/// </summary>
		private bool leashEvade;

		/// <summary>
		/// Reusable condition context for <see cref="Ability.MeetsActivationConditions"/>, so
		/// scoring the spellbook does not allocate one per pick. The ability re-creates it only if
		/// its initiator differs, which it never does for one brain.
		/// </summary>
		[System.NonSerialized]
		internal EventData ActivationCheckData;

		/// <summary>
		/// Seconds the current target has been continuously tracked, which drives the aim accuracy
		/// ramp.
		/// </summary>
		/// <remarks>
		/// Reset whenever <see cref="Target"/> changes, so a target re-acquired after a disengage
		/// must be re-acquired by the aim as well. Advanced only while the controller is in a tier
		/// that runs combat — see <see cref="UpdateAim"/> — because a ramp that measured wall-clock
		/// time with a target rather than time actually spent tracking one would hand an NPC full
		/// accuracy the moment a player walked back into range.
		/// </remarks>
		public float AimLock { get; private set; }

		/// <summary>
		/// The angular error held for the current cast.
		/// </summary>
		/// <remarks>
		/// Rolled once when a cast begins, by <c>BaseAttackingState.ActivateAbility</c>, and held
		/// until the next cast. Held rather than re-rolled per tick on purpose — see
		/// <see cref="AIAimSolver.RollScatter"/> — so an inaccurate NPC tracks its target with a
		/// stable bias instead of spraying around it.
		/// </remarks>
		public Quaternion AimScatter { get; private set; } = Quaternion.identity;

		/// <summary>
		/// Travel speed of the ability being cast, or 0 when nothing is being cast or the ability has
		/// no travel time.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Set by <see cref="RollAimForCast"/> from the ability it is about to cast, and read on every
		/// network tick by <see cref="UpdateAim"/> to derive a lead time from the <em>current</em>
		/// distance.
		/// </para>
		/// <para>
		/// <b>Why the speed is held rather than the lead time.</b> The flight time is distance over
		/// speed, and the distance changes: the bolt does not leave the caster's hand until the windup
		/// finishes — 0.4 s on <c>Orc Firebolt</c> — and the target keeps moving throughout. Baking the
		/// lead seconds at the moment the cast was queued would aim the shot at where the target was
		/// going to be a windup ago. Keeping the speed and recomputing the time each tick means the
		/// lead is correct at the instant the projectile actually spawns, and it costs one subtraction
		/// and a divide on a tick that already solves a look rotation.
		/// </para>
		/// </remarks>
		public float AimLeadSpeed { get; private set; }

		/// <summary>
		/// The current target's position at the previous tick, used to estimate its velocity.
		/// </summary>
		private Vector3 lastTargetPosition;

		/// <summary>
		/// Whether <see cref="lastTargetPosition"/> holds a sample from the previous tick.
		/// </summary>
		private bool hasLastTargetPosition;

		/// <summary>
		/// The current target's estimated velocity in units per second.
		/// </summary>
		/// <remarks>
		/// Sampled rather than asked for. Nothing in the AI currently tracks target motion, and the
		/// alternative — reading the target's own velocity — would mean a different source per
		/// character type: a player's motion lives on the KCC motor, an NPC's on its agent, and a
		/// pet's on whichever of the two it is. The aim needs one number in one unit for every kind
		/// of target, and differencing the replicated position gives exactly that with no new
		/// dependency.
		/// </remarks>
		public Vector3 TargetVelocity { get; private set; }

		/// <summary>
		/// Target speed above which a sampled displacement is treated as a teleport, not motion.
		/// </summary>
		/// <remarks>
		/// A blink, a charge, a pull or a spawner relocating its charge moves a target further in one
		/// tick than any run speed could. Believing that displacement would fling the lead offset
		/// across the scene and guarantee the miss it was meant to prevent, so an implausible sample
		/// is discarded and the previous velocity kept. Well above the fastest legitimate movement —
		/// a sprinting player is a small fraction of this — and reachable only by a genuine jump.
		/// </remarks>
		private const float MaxPlausibleTargetSpeed = 100f;
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
		/// The pack this NPC fights with, or null. Written only by <see cref="NPCGroup"/>.
		/// </summary>
		/// <remarks>
		/// Set when a pack spawner spawns the NPC (<c>SpawnerRuntime.JoinPack</c>) or a boss calls it
		/// in as an add, and cleared when it leaves: on death (<see cref="SuspendForCorpse"/>), on
		/// despawn and pool reset (<see cref="ResetForPool"/>), and on destruction.
		/// </remarks>
		public NPCGroup Group { get; internal set; }

		/// <summary>
		/// This NPC's role within its pack; <see cref="NPCGroupRole.None"/> outside one. Written only
		/// by <see cref="NPCGroup"/>.
		/// </summary>
		public NPCGroupRole GroupRole { get; internal set; }

		/// <summary>
		/// The bearing around the pack's focus this NPC's tactic assigned it, in radians, in the
		/// <see cref="OrbitState"/> convention. Meaningful only while <see cref="HasPackSlot"/>.
		/// </summary>
		public float PackSlotAngle { get; private set; }

		/// <summary>
		/// True while the pack's tactic has a slot for this NPC: it is fighting the pack's focus and
		/// the pack has a tactic.
		/// </summary>
		public bool HasPackSlot { get; private set; }

		/// <summary>
		/// Gives this NPC its tactic slot. Called by <see cref="NPCGroup"/> at each evaluation.
		/// </summary>
		/// <param name="angle">The slot's bearing around the focus, in radians.</param>
		internal void SetPackSlot(float angle)
		{
			PackSlotAngle = angle;
			HasPackSlot = true;
		}

		/// <summary>
		/// Takes this NPC off its tactic slot.
		/// </summary>
		internal void ClearPackSlot()
		{
			PackSlotAngle = 0f;
			HasPackSlot = false;
		}

		/// <summary>
		/// Leaves this NPC's pack, if it is in one. Safe to call at any time and more than once.
		/// </summary>
		internal void LeavePack()
		{
			NPCGroup group = Group;
			if (group != null)
			{
				group.RemoveMember(this);
			}

			// Whatever the pack did or did not know of this brain, it holds no membership now.
			Group = null;
			GroupRole = NPCGroupRole.None;
			ClearPackSlot();
		}

		/// <summary>
		/// The boss that called this NPC in as an add and may send it away again, or null. Written
		/// only by <see cref="BossScriptState"/>.
		/// </summary>
		/// <remarks>
		/// The add's half of <see cref="BossScriptState"/>'s list of live adds, cleared when the add
		/// leaves the fight as a pack member does (<see cref="LeaveSummoner"/>). Not the pack: a boss's
		/// pack may be its spawner's, and those members are not the boss's to despawn.
		/// </remarks>
		internal BossScriptState Summoner { get; set; }

		/// <summary>
		/// Stops being its boss's add, if it is one. Safe to call at any time and more than once.
		/// </summary>
		/// <remarks>
		/// Called where the NPC leaves its pack — on death, on despawn and pool reset, and on
		/// destruction — for the same reason: the brain is pooled and reissued, and a boss still
		/// holding it would, on its next leash reset, despawn whatever NPC the pool made of it next.
		/// </remarks>
		internal void LeaveSummoner()
		{
			BossScriptState summoner = Summoner;
			Summoner = null;
			summoner?.ForgetAdd(this);
		}

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

		/// <summary>Seconds until the next shelter check. See <see cref="CheckShelter"/>.</summary>
		private float nextShelterCheck;

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
		/// Per-NPC retreat memory: how long it has run, how often, and whether it may run again.
		/// See <see cref="AIRetreatBudget"/>.
		/// </summary>
		/// <remarks>
		/// Lives here, on the instance, and not on the retreat state asset. That asset is shared by
		/// every NPC that flees, so a counter stored on it would be one counter for the whole
		/// server — and it holds authored configuration, which runtime bookkeeping has no business
		/// being written into. Non-serialized for the same reason: this is state, not prefab data.
		/// </remarks>
		[System.NonSerialized]
		public AIRetreatBudget Retreat;

		/// <summary>
		/// How fast the current target is closing on this NPC, in units per second.
		/// </summary>
		/// <remarks>
		/// Negative when the target is moving away, zero with no target. What tells a retreating NPC
		/// the difference between "I have escaped" and "it is still coming" — the distinction the
		/// reported behaviour was missing. Measured from the sampled
		/// <see cref="TargetVelocity"/> rather than from a change in distance, so it is available on
		/// the same tick the target moves instead of a tick later, and it does not confuse a target
		/// circling at a steady radius for one closing in.
		/// </remarks>
		public float TargetClosingSpeed { get; private set; }

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

		/// <summary>Scratch list of neighbour positions for <see cref="AISeparation.Resolve"/>.</summary>
		private readonly List<Vector3> separationNeighbours = new List<Vector3>(16);

		/// <summary>
		/// Scratch list of the neighbours' identity keys, in the same order, for the tie-break
		/// when two bodies coincide. One entry per body: the grid holds bodies, not colliders.
		/// </summary>
		private readonly List<uint> separationKeys = new List<uint>(16);

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
		/// Starts the brain on the host's network tick.
		/// </summary>
		/// <remarks>
		/// Driven by the FishNet TimeManager rather than Unity's Update, through
		/// <see cref="AIBrainHost"/>'s single subscription. Everything else authoritative in this
		/// project already runs on ticks — prediction, cooldowns, ability activation — and the AI
		/// reading a variable frame delta made it the one system whose behaviour changed with server
		/// load. It also made the "deterministic" NPC RNG a half-truth: the seeded rolls were
		/// reproducible, but <em>when</em> they were drawn was not.
		/// </remarks>
		/// <param name="tickDelta">Seconds per network tick.</param>
		internal void StartTicking(float tickDelta)
		{
			networkTickDelta = tickDelta;
			ResolveTickRate();
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

			ticksPerAiUpdate = ResolveTicksPerAiUpdate(AiTickRate, networkTickDelta);

			// Stagger the very first brain update so a wave of NPCs spawned together does not all
			// think on the same tick for the rest of their lives.
			aiTickCounter = ticksPerAiUpdate > 1 ? (staggerID % ticksPerAiUpdate) : 0;
		}

		/// <summary>
		/// Network ticks per AI tick for a requested brain rate: the nearest whole divisor of the
		/// network tick. Pure, and shared with the packs <see cref="AIBrainHost"/> ticks, so a pack
		/// thinks on the same cadence as its members.
		/// </summary>
		/// <param name="aiTickRate">The requested rate, in hertz.</param>
		/// <param name="networkTickDelta">Seconds per network tick; 1/30 when not positive.</param>
		/// <returns>Network ticks between AI ticks, at least 1.</returns>
		public static int ResolveTicksPerAiUpdate(float aiTickRate, float networkTickDelta)
		{
			if (networkTickDelta <= 0f)
			{
				networkTickDelta = 1f / 30f;
			}

			float networkTickRate = 1f / networkTickDelta;
			float requested = Mathf.Clamp(aiTickRate, 0.1f, networkTickRate);
			return Mathf.Max(1, Mathf.RoundToInt(networkTickRate / requested));
		}

		/// <summary>
		/// Mixes an instance ID into a key whose every bit depends on every input bit.
		/// </summary>
		/// <remarks>
		/// The "lowbias32" integer hash: xor-shifts and odd multiplies, each invertible, so the whole
		/// is a bijection on 32 bits — two instances never share a key. See <see cref="IdentityKey"/>.
		/// </remarks>
		/// <param name="instanceId">A Unity instance ID.</param>
		/// <returns>The mixed key.</returns>
		public static uint MixIdentity(int instanceId)
		{
			uint x = unchecked((uint)instanceId);
			x ^= x >> 16;
			x = unchecked(x * 0x7feb352dU);
			x ^= x >> 15;
			x = unchecked(x * 0x846ca68bU);
			x ^= x >> 16;
			return x;
		}

		/// <summary>
		/// A fraction in [0, 1) drawn from <paramref name="key"/>, different for each
		/// <paramref name="salt"/>, so one identity can seed several independent phases.
		/// </summary>
		/// <param name="key">An identity key from <see cref="MixIdentity"/>.</param>
		/// <param name="salt">Distinguishes the phases drawn from one key.</param>
		/// <returns>The fraction.</returns>
		public static float PhaseFraction(uint key, uint salt)
		{
			uint mixed = MixIdentity(unchecked((int)(key ^ (salt * 0x9e3779b9U))));
			// The top 24 bits, so the result is exact in a float and strictly below one.
			return (mixed >> 8) * (1f / 16777216f);
		}

		/// <summary>
		/// The phase of the LOD stagger, in AI ticks.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The part of the stagger the brain-tick phase did not use. The brain tick already takes
		/// <c>staggerID % ticksPerAiUpdate</c>; the LOD gate used to add the whole
		/// <c>staggerID</c> again, and when the two moduli share a factor the phases are correlated:
		/// with a brain every 4th network tick and an Active interval of 2, the combinations of the
		/// two phases fill only 4 of the 8 network-tick slots in each cycle, doubling the per-tick
		/// peak. Taking the quotient instead makes the two phases independent, so a population
		/// spreads over every slot.
		/// </para>
		/// <para>Pure, so the spread can be asserted directly.</para>
		/// </remarks>
		/// <param name="staggerID">The NPC's stagger slot.</param>
		/// <param name="ticksPerAiUpdate">Network ticks per brain tick.</param>
		/// <returns>The LOD phase.</returns>
		public static int ResolveLodPhase(int staggerID, int ticksPerAiUpdate)
		{
			return Mathf.Abs(staggerID) / Mathf.Max(1, ticksPerAiUpdate);
		}

		/// <summary>
		/// Starts this NPC's periodic checks at their own point in each period.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The enemy sweep, the LOD re-evaluation, the leash check, the shelter check and threat
		/// decay all used to start at zero and never be offset. A scene's NPCs are spawned together,
		/// so every one of them then swept on the same brain tick every 1.5 s, re-evaluated LOD on
		/// the same one every 2 s, and so on: a camp of two hundred ran all its sweeps — an overlap,
		/// a component lookup per body and a line-of-sight ray per enemy each — on one tick in every
		/// forty-eight. Each period now starts at a fraction drawn from <see cref="IdentityKey"/>,
		/// so the same work is spread evenly across the period instead.
		/// </para>
		/// <para>
		/// Called by <see cref="AIBrainHost.Prepare"/> once the initial state has been entered,
		/// because the leash period belongs to the state.
		/// </para>
		/// </remarks>
		internal void SeedTimerPhases()
		{
			nextEnemySweepUpdate = EnemySweepRate * PhaseFraction(IdentityKey, 1);
			lodReevaluateTimer = LodSettings != null ? LodSettings.ReevaluateInterval * PhaseFraction(IdentityKey, 2) : 0f;
			nextLeashUpdate = CurrentState != null ? Mathf.Max(0f, CurrentState.LeashUpdateRate) * PhaseFraction(IdentityKey, 3) : 0f;
			AIShelterSettings shelter = Shelter;
			nextShelterCheck = shelter != null ? Mathf.Max(0f, shelter.CheckInterval) * PhaseFraction(IdentityKey, 4) : 0f;
			aggressionClock.Rearm(AGGRESSION_TICK_INTERVAL * PhaseFraction(IdentityKey, 5));
		}

		/// <summary>
		/// Initializes the controller and NavMeshAgent. Sets avoidance priority, speed, and movement states.
		/// </summary>
		private void InitializeOnce()
		{
			// Resolved once: Home reads this on every leash check and wander destination.
			cachedPet = Character as Pet;

			/* Derive a stagger ID so NPCs spread their updates across frames.
			 *
			 * Seeded from the GameObject's instance ID rather than Character.ID: the brain is bound
			 * the first time the server spawns the NPC, and a pooled instance keeps the brain for
			 * life, so an ID-derived bucket would be whatever ID the first occupant happened to
			 * draw. The instance ID is stable but NOT well spread — see IdentityKey — so it is
			 * hashed first. */
			IdentityKey = MixIdentity(gameObject.GetInstanceID());
			staggerID = (int)(IdentityKey & 0x7FFFFFFFU);

			/* Collected once: a pooled NPC's hierarchy is fixed for its life, and the host maps each
			 * of these to this brain while it ticks (see AIBrainHost.TryGetBrain). Inactive children
			 * included, so a hitbox enabled later is still known. */
			BodyColliders = GetComponentsInChildren<Collider>(true);

			// One threat table per NPC; ApplyArchetypeTuning below gives it the archetype's numbers.
			AggressionState = new AggressionState(Character);

			/* Event-driven combat entry: every hit is offered to OnThreatReceived, which enters
			 * combat at once when the NPC is not already fighting or evading, instead of waiting
			 * for the next physics sweep. */
			AggressionState.OnHitRecorded = OnThreatReceived;

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
			 * both writes happen once per tick, in StepAgent. A client never has an agent. */
			Agent.updatePosition = false;
			Agent.updateRotation = false;

			/* No crowd avoidance. Unity's crowd is one global simulation, and the scene server
			 * stacks instances of the same scene at the same coordinates, so avoidance made NPCs
			 * steer around NPCs in OTHER instances. AISeparation replaces it, scoped to this
			 * NPC's own PhysicsScene; AICombatSlots spaces attackers around a target. */
			Agent.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance;

			// Initialize boss script runtime state if a boss script is already assigned.
			if (bossScript != null && BossState == null)
			{
				BossState = new BossScriptState(bossScript);
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
		/// <remarks>
		/// <para>
		/// <b>An attacking-state override takes over the fight at once.</b> <see cref="AttackingState"/>
		/// reads the override, but the NPC keeps running whichever attacking state it entered until
		/// something re-enters one. What did, by accident, was the out-of-combat enemy sweep: it
		/// tested only "is the current state the resolved attacking state", so after an override
		/// it ran against the old one and, a sweep interval later and in the Active tier only,
		/// re-entered the new one with a fresh target pick and the cast interrupted. The sweep no
		/// longer runs in any combat state (<see cref="SweepMayRun"/>), so the swap now happens
		/// here, when the phase starts, through <see cref="HandOverFight"/>, which keeps the target
		/// and the cast in progress. A boss not
		/// in its attacking state is left alone: a combat sub-state returns to
		/// <see cref="AttackingState"/> and so picks the override up on its own, and a boss out of
		/// combat enters it the next time it engages.
		/// </para>
		/// </remarks>
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

			if (FightNeedsHandOver(AttackingState != null,
				CurrentState is BaseAttackingState,
				CurrentState != null && ReferenceEquals(CurrentState, AttackingState)))
			{
				HandOverFight();
			}
		}

		/// <summary>
		/// Whether the fight in progress has to move to a different attacking state: the NPC is in
		/// an attacking state, and it is not the one <see cref="AttackingState"/> now resolves to.
		/// </summary>
		/// <remarks>Pure, so the rule can be pinned without a scene.</remarks>
		/// <param name="hasAttackingState">True when an attacking state resolves at all.</param>
		/// <param name="inAttackingState">True when the current state is an attacking state.</param>
		/// <param name="inResolvedAttackingState">True when the current state is the one <see cref="AttackingState"/> resolves to.</param>
		/// <returns>True to hand the fight over now.</returns>
		public static bool FightNeedsHandOver(bool hasAttackingState, bool inAttackingState, bool inResolvedAttackingState)
		{
			return hasAttackingState && inAttackingState && !inResolvedAttackingState;
		}

		/// <summary>
		/// True only while <see cref="HandOverFight"/> is moving an ongoing fight from one attacking
		/// state to another. <see cref="BaseAttackingState.Exit"/> reads it, alongside
		/// <see cref="PendingState"/>, to tell a hand-over from a disengage.
		/// </summary>
		public bool IsHandingOverFight { get; private set; }

		/// <summary>
		/// Moves the fight in progress into the attacking state <see cref="AttackingState"/> now
		/// resolves to, without ending it.
		/// </summary>
		/// <remarks>
		/// A plain <see cref="ChangeState"/> would run the outgoing state's Exit as a disengage — drop
		/// the target, give up the ring slot and interrupt the cast — and the incoming state would
		/// then have to find a target from a fresh sweep, which may not pick the one the boss was
		/// fighting. Marked for the duration of the transition so Exit leaves all three alone.
		/// </remarks>
		private void HandOverFight()
		{
			BaseAIState next = AttackingState;
			if (next == null)
			{
				return;
			}

			IsHandingOverFight = true;
			try
			{
				ChangeState(next);
			}
			finally
			{
				IsHandingOverFight = false;
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
		/// The boss's part of a leash reset: sends the adds it called in back to the pool, then
		/// returns its script to its first phase. Every leash that honours
		/// <see cref="BossScript.ResetOnLeash"/> comes through here.
		/// </summary>
		/// <remarks>
		/// The adds go with the leash rather than with <see cref="ResetBossScript"/>, which a despawn
		/// runs too. A leash promises the encounter as it was before the pull, and adds left standing
		/// met the next pull beside a full-health boss about to call the same adds in again. A boss
		/// that dies or despawns only lets go of its adds (<see cref="BossScriptState.ReleaseAdds"/>),
		/// which fight on as they always have. Only the adds the boss spawned are dismissed, never
		/// the rest of its pack; see <see cref="BossScriptState.DismissAdds"/>.
		/// </remarks>
		private void ResetBossScriptForLeash()
		{
			BossState?.DismissAdds(NetworkManager);
			ResetBossScript();
		}

		/// <summary>
		/// Unsubscribes from global events on destroy to prevent memory leaks.
		/// </summary>
		private void OnDestroying()
		{
			ReleaseCombatSlots();
			AggressionState?.Destroy();

			/* An NPC destroyed with its scene never despawns, so this is the only way out of its
			 * pack; the pack would otherwise count a destroyed brain until its next evaluation. The
			 * same holds for its boss's list of adds, and for its own list if it is a boss. */
			LeavePack();
			LeaveSummoner();
			BossState?.ReleaseAdds();
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
		/// Resets the controller for pool reuse, clearing home, target, look target, timers and
		/// threat. Called by <see cref="AIBrainHost"/> when the NPC leaves the world.
		/// </summary>
		internal void ResetForPool()
		{
			Prepared = false;
			aiTickCounter = 0;
			enabled = true;

			/* Give up any combat ring slot before the ID is recycled. Without this a pooled NPC
			 * leaves a phantom occupant in its old target's ring, which inflates the ring's
			 * occupancy and pushes real attackers out to a further rank than they need. */
			ReleaseCombatSlots();

			home = Vector3.zero;
			Target = null;
			ResetMovementState();
			LookTarget = null;
			AimRotation = Quaternion.identity;
			AimScatter = Quaternion.identity;
			AimLeadSpeed = 0f;
			AimLock = 0f;
			TargetVelocity = Vector3.zero;
			lastTargetPosition = Vector3.zero;
			hasLastTargetPosition = false;
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
			Retreat.Clear();
			TargetClosingSpeed = 0f;
			MaxOffensiveReach = 0f;
			separationVelocity = Vector3.zero;
			offMeshReseatTimer = 0f;
			offMeshWarned = false;
			stateClock = default;
			aggressionClock = default;
			leashEvade = false;
			behaviorTreeTimer = 0f;
			lodReevaluateTimer = 0f;
			currentLodTier = AILodTier.Active;

			/* Out of the pack, not merely forgetting it: the pack still listed this brain, and would
			 * have counted it, read its health and alerted it into its next occupant's life. The
			 * next spawn rejoins through its spawner. */
			LeavePack();

			/* Likewise out of its boss's adds, and, as a boss, done with its own: they are not
			 * dismissed by a despawn, only no longer this brain's, so the next occupant of this pool
			 * slot can neither be sent away by a boss it never met nor send away adds it never
			 * called. */
			LeaveSummoner();
			BossState?.ReleaseAdds();
			PendingState = null;
			CurrentState = null;
			ResetBossScript();

			/* The archetype is left in place: the host assigns one on every spawn, so the next
			 * occupant of this pool slot cannot inherit it. */
			AggressionState?.Clear();
			cachedAbilities.Clear();
			lastKnownAbilityCount = -1;
			repathCooldown = 0f;
		}

		/// <summary>
		/// One network tick of this brain, called by <see cref="AIBrainHost"/>. Steps the body, then
		/// applies LOD-based tick scheduling and dispatches to tier-appropriate update pipelines
		/// for behavior simplification.
		/// <para>
		/// <b>Tick scheduling:</b> The brain runs every <see cref="ticksPerAiUpdate"/> network
		/// ticks, and each LOD tier has an interval in those AI ticks, offset per NPC by its LOD
		/// phase (<see cref="ResolveLodPhase"/>) so updates spread evenly across ticks.
		/// Dormant NPCs use a dedicated high-interval gate so even their wake-up check is cheap.
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
		/// <returns>
		/// True when this network tick ran a tier pipeline to completion. <see cref="AIBrainHost"/>
		/// counts only those as proof the brain is healthy: a throw inside the pipeline recurs once
		/// per pipeline run, and the body-only ticks in between must not reset the count.
		/// </returns>
		internal bool Tick()
		{
			/* The brain is a tick callback, not Update, so disabling the MonoBehaviour does not
			 * stop it by itself: a corpse kept sweeping, leashing (warping home and healing itself)
			 * and driving its agent for the whole of its decay. SuspendForCorpse disables the
			 * controller and halts it; this is what makes the disable mean something. */
			if (!IsRunning)
			{
				return false;
			}

			/* The body runs on every network tick — except for a Dormant NPC with nowhere to go.
			 * Nobody is within the Far band to see it, it has no path to advance, and its brain
			 * does not run, so re-seating an idle agent and re-aiming at nothing thirty times a
			 * second for every dormant NPC in the process was the whole of its cost. One with a
			 * path keeps walking, so it arrives where it was going rather than freezing mid-stride.
			 * The path is only asked about when Dormant; every other tier steps regardless. */
			if (ShouldStepBody(currentLodTier, currentLodTier == AILodTier.Dormant && AgentHasSomewhereToGo()))
			{
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

				/* The aim is written on the same schedule as the facing, and for the same reason. Both
				 * feed AbilityController.PopulateAiAim, which runs on every network tick — so a value
				 * written on the brain tick is between one and four ticks stale by the time it is read.
				 * The brain tick throttles thinking, and aiming is not thinking: it is one subtraction
				 * and a LookRotation against state the controller already holds. */
				UpdateAim(networkTickDelta);
			}
			else
			{
				measuredTickSpeedSqr = 0f;
			}

			aiTickCounter++;

			// --- AI tick gate: only a fraction of network ticks drive the brain. ---
			if (aiTickCounter < ticksPerAiUpdate)
			{
				return false;
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

						/* Nothing refreshes the separation push below the Nearby tier, so it is
						 * dropped at the change rather than whenever the throttled pipeline next
						 * runs: a push left over from the last Active tick would otherwise go on
						 * sliding a Dormant NPC that still has a path, at the push speed, for as long
						 * as it stayed dormant. */
						if (currentLodTier >= AILodTier.Far)
						{
							separationVelocity = Vector3.zero;
						}
					}
				}

				tickInterval = LodSettings.GetTickInterval(currentLodTier);
			}

			/* Stagger gate.
			 *
			 * The LOD phase spreads NPCs across the interval so a thousand of them do not all think
			 * on the same tick and spike one frame in every N. Keyed off a monotonic AI tick index
			 * rather than Time.frameCount, so the spread is identical on a server running at 200
			 * FPS and one running at 30. It is the part of the stagger the brain-tick phase did not
			 * use — see ResolveLodPhase for what reusing the whole of it cost. */
			if (tickInterval > 1 &&
				((AiTickIndex + (uint)ResolveLodPhase(staggerID, ticksPerAiUpdate)) % (uint)tickInterval) != 0)
			{
				return false;
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
				return false;
			}

			/* The retreat budget advances on every AI tick regardless of state, because the refund
			 * and both holds have to run while the NPC is *not* retreating — a budget that only
			 * ticked inside the retreat state could never refill, and the hold imposed after
			 * choosing to fight would never expire. Its authored numbers come off the retreat state,
			 * which may be null on an archetype that never flees. */
			RetreatState retreatAsset = RetreatState as RetreatState;
			Retreat.Tick(ReferenceEquals(CurrentState, retreatAsset),
				dt,
				retreatAsset != null ? retreatAsset.MaxCumulativeRetreatSeconds : 0f,
				retreatAsset != null ? retreatAsset.RetreatRecoverySeconds : 0f);

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
			return true;
		}

		/// <summary>
		/// Whether this network tick should advance the NPC's body — off-mesh recovery, the agent
		/// step, facing and aim.
		/// </summary>
		/// <remarks>
		/// Every tier but Dormant always does. A Dormant NPC does only while it still has a path or
		/// is on an off-mesh link: nobody is within the Far band to see it, its brain does not run,
		/// and an idle agent needs nothing. Pure, so the rule can be pinned without an agent.
		/// </remarks>
		/// <param name="tier">The NPC's current LOD tier.</param>
		/// <param name="hasSomewhereToGo">True when the agent has a path or is traversing a link. Only consulted when Dormant.</param>
		/// <returns>True to step the body this tick.</returns>
		public static bool ShouldStepBody(AILodTier tier, bool hasSomewhereToGo)
		{
			return tier != AILodTier.Dormant || hasSomewhereToGo;
		}

		/// <summary>
		/// True when the agent is live on the NavMesh and still has a path to follow or a link to
		/// cross. An agent off the mesh answers false and is re-seated when the NPC wakes.
		/// </summary>
		private bool AgentHasSomewhereToGo()
		{
			return AgentIsUsable() && (Agent.hasPath || Agent.isOnOffMeshLink);
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
			CheckShelter(dt);

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
			CheckShelter(dt);
			UpdateCurrentState(dt);
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
		/// remain in a damaged/combat state. Whether any player IS nearby is decided by
		/// <see cref="AllowsLeashReset"/> from a measured distance, never from observer membership.
		/// </para>
		/// </summary>
		private void OnLodTierChanged(AILodTier previousTier, AILodTier newTier)
		{
			/* Gated on measured proximity, not on tier membership alone. The reset restores health,
			 * drops the threat table and rewinds boss phases — authoritative simulation, on the
			 * premise that no player is close enough to notice — so it consults the distance
			 * directly. The tier decides how much work to do; this decides whether the fight is
			 * over, and only the second one may be wrong in the player's favour. */
			bool measured = TryGetNearestPlayerSqrDistance(out float nearestSqrDistance);
			if (!AllowsLeashReset(newTier,
					CurrentState is BaseAttackingState,
					measured,
					nearestSqrDistance,
					LodSettings != null ? LodSettings.NearbyDistanceSqr : float.PositiveInfinity))
			{
				return;
			}

			// Interrupt any active ability.
			if (Character.TryGet(out IAbilityController abilityController))
			{
				abilityController.Interrupt(null);
			}

			// Heal to full — no player is close enough to notice.
			if (Character.TryGet(out ICharacterDamageController damageController))
			{
				damageController.CompleteHeal();
			}

			// Clear threat table.
			AggressionState?.Clear();

			// Reset boss script phases and dismiss the boss's adds.
			if (BossState != null && BossScript != null && BossScript.ResetOnLeash)
			{
				ResetBossScriptForLeash();
			}

			TransitionToIdleState();
		}

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

		/// <summary>
		/// True while the current state is part of a fight: the attacking state itself, or a combat
		/// sub-state (orbit, flank, strafe, retreat) that keeps the combat target.
		/// </summary>
		/// <remarks>
		/// Not <c>CurrentState == AttackingState</c>. A hit taken mid-orbit or mid-retreat is part of
		/// the fight already in progress; reading only the attacking state would let every such hit
		/// yank the NPC back into it and onto whoever hit it last, which would end every flee the
		/// moment the pursuer landed a blow.
		/// </remarks>
		public bool IsInCombatState =>
			CurrentState != null && (CurrentState is BaseAttackingState || CurrentState.KeepsCombatTarget);

		/// <inheritdoc />
		/// <remarks>
		/// <para>
		/// Only a return that a leash started out of a fight evades. <see cref="ReturnHomeState"/> is
		/// also one of the random movement states a calm NPC drifts between, and an NPC strolling
		/// home from a wander is as attackable as one wandering. The flag is set by
		/// <see cref="CheckLeash"/> and ANDed here with "the return is still the current state" and
		/// "the brain is running", so every way out of the return — arrival, a warp, a tier reset,
		/// a new fight, a corpse, the pool — ends the evade without having to remember to.
		/// </para>
		/// </remarks>
		public bool IsEvading
		{
			get
			{
				// The flag first: almost no NPC is evading, and this is read on every hit an NPC takes.
				if (!leashEvade)
				{
					return false;
				}
				return IsEvadingRule(leashEvade, IsRunning, CurrentState != null && ReferenceEquals(CurrentState, ReturnHomeState));
			}
		}

		/// <summary>
		/// The evade rule, pure so it can be pinned without a scene: an NPC evades while a
		/// fight-ending leash return is its current state and its brain is running.
		/// </summary>
		/// <param name="leashEvade">True when a leash sent the NPC home out of a fight.</param>
		/// <param name="running">True while the brain is prepared, enabled and not quarantined.</param>
		/// <param name="inReturnHomeState">True while the return-home state is the current state.</param>
		/// <returns>True while the NPC must take no damage.</returns>
		public static bool IsEvadingRule(bool leashEvade, bool running, bool inReturnHomeState)
		{
			return leashEvade && running && inReturnHomeState;
		}

		/// <summary>
		/// Whether a leash that is sending the NPC home should make it evade.
		/// </summary>
		/// <remarks>
		/// Only a leash that ends a fight: the NPC was in a combat state, or still held threat. A
		/// calm NPC that has merely drifted past its leash range walks home as attackable as it was.
		/// A pet never evades: it has no spawn-point leash, and its return is catching up with its
		/// owner. Pure, so it can be pinned without a scene.
		/// </remarks>
		/// <param name="isPet">True for a pet.</param>
		/// <param name="inCombatState">True when the NPC was in a combat state as the leash tripped.</param>
		/// <param name="heldThreat">True when its threat table was not empty as the leash tripped.</param>
		/// <returns>True to evade on the way home.</returns>
		public static bool LeashStartsEvade(bool isPet, bool inCombatState, bool heldThreat)
		{
			return !isPet && (inCombatState || heldThreat);
		}

		/// <summary>
		/// Whether a leash that is walking the NPC home heals it.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Only a leash heals: this is asked by <see cref="CheckLeash"/> and nowhere else, so the
		/// same return state picked as a calm movement (<see cref="TransitionToRandomMovementState"/>)
		/// never heals. Every leash does — a calm drift past the leash as well as one that ends a
		/// fight — exactly as the full-leash warp always has, provided the return actually became the
		/// current state and the archetype's return state is authored to heal
		/// (<see cref="ReturnHomeState.CompleteHealOnReturn"/>).
		/// </para>
		/// <para>Pure, so it can be pinned without a scene.</para>
		/// </remarks>
		/// <param name="enteredReturn">True when the leash's transition put the return state in force.</param>
		/// <param name="completeHealOnReturn">The return state's authored heal flag.</param>
		/// <returns>True to heal the NPC to full.</returns>
		public static bool LeashReturnHeals(bool enteredReturn, bool completeHealOnReturn)
		{
			return enteredReturn && completeHealOnReturn;
		}

		/// <summary>
		/// The state half of the combat-entry rule, pure so it can be pinned without a scene: a
		/// threat event may start a fight only for a running brain that has an attacking state, is
		/// not already fighting, and is not evading.
		/// </summary>
		/// <remarks>
		/// This replaced an edge: combat used to start only when the threat table went from empty
		/// to non-empty, and a fight that ended any way but a kill left the table non-empty, so
		/// every later hit was recorded and ignored. The rule is now asked on every hit and reads
		/// only the brain's own state, so there is no edge to consume.
		/// </remarks>
		/// <param name="hasAttackingState">True when the NPC can fight at all.</param>
		/// <param name="running">True while the brain is prepared, enabled and not quarantined.</param>
		/// <param name="inCombatState">True while the NPC is already fighting (see <see cref="IsInCombatState"/>).</param>
		/// <param name="evading">True while the NPC is on a fight-ending leash return (see <see cref="IsEvading"/>).</param>
		/// <returns>True when the threat may start a fight, subject to the per-character checks.</returns>
		public static bool CanEnterCombatFromThreat(bool hasAttackingState, bool running, bool inCombatState, bool evading)
		{
			return hasAttackingState && running && !inCombatState && !evading;
		}

		/// <summary>
		/// Event-driven combat entry. Called by <see cref="AggressionState"/> on every hit the NPC
		/// takes, and by <see cref="ApplyTaunt"/>. Transitions to combat at once, without waiting
		/// for the next <see cref="SweepForEnemies"/> physics poll, whenever the NPC is not already
		/// fighting.
		/// </summary>
		/// <remarks>
		/// <para>
		/// This eliminates the biggest polling cost for non-Active NPCs: thousands of per-NPC
		/// physics OverlapSphere calls every <see cref="EnemySweepRate"/> seconds. Nearby tier NPCs
		/// rely entirely on this event to detect combat; Active tier NPCs still run
		/// SweepForEnemies for proactive (hostile faction) detection.
		/// </para>
		/// <para>
		/// Called on every hit, so the cheap state checks come first: for an NPC already fighting —
		/// every hit after the first — it returns on a few property reads, before any component
		/// lookup.
		/// </para>
		/// </remarks>
		/// <param name="attacker">The character that generated the threat.</param>
		public void OnThreatReceived(ICharacter attacker)
		{
			if (attacker == null ||
				!CanEnterCombatFromThreat(AttackingState != null, IsRunning, IsInCombatState, IsEvading))
				return;

			// An immortal NPC has no reason to target anything.
			if (IsImmortal)
				return;

			// A passive pet does not fight back; that is the whole meaning of the stance.
			if (!PetStanceAllowsAutoEngage(false))
				return;

			/* A pet does not answer an attacker it could only reach by breaking its owner leash.
			 * The attacking state would send it running and call it back at OwnerLeashRange on its
			 * first update; with combat entry now asked on every hit, the next shot at the owner
			 * would send it out again, and a pet guarding an owner under fire from range would
			 * shuttle between the two for as long as the shooting lasted. */
			if (cachedPet != null &&
				cachedPet.PetOwner != null &&
				cachedPet.PetOwner.Transform != null &&
				attacker.Transform != null &&
				AttackingState is BaseAttackingState petAttacking &&
				!PetCanAnswerAttacker(cachedPet.PetOwner.Transform.position, attacker.Transform.position,
					petAttacking.OwnerLeashRange, Mathf.Max(petAttacking.PreferredDistance, MaxOffensiveReach)))
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
		/// Whether a pack member may join its pack's fight against an enemy a packmate engaged.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The combat-entry rule a hit is asked (<see cref="CanEnterCombatFromThreat"/>), plus the
		/// per-character checks that follow it there: the member must be alive and mortal, and the
		/// enemy a valid target. So an alert never wakes a corpse or a quarantined brain, never drags
		/// an evading member back into the fight it leashed out of, and never re-targets a member
		/// already fighting — mid-orbit and mid-flee included, which the old test of the attacking
		/// state alone yanked back into the attack.
		/// </para>
		/// <para>
		/// A member strolling home as a calm movement is not evading and does answer; only a leash
		/// that ended a fight makes a return an evade. Pure, so it can be pinned.
		/// </para>
		/// </remarks>
		/// <param name="hasAttackingState">True when the member can fight at all.</param>
		/// <param name="running">True while its brain is prepared, enabled and not quarantined.</param>
		/// <param name="inCombatState">True while it is already fighting.</param>
		/// <param name="evading">True while it is on a fight-ending leash return.</param>
		/// <param name="immortal">True while it cannot be hurt.</param>
		/// <param name="alive">True while it is alive.</param>
		/// <param name="enemyValid">True when the enemy is alive and in the world.</param>
		/// <returns>True when the member joins the fight.</returns>
		public static bool MayAnswerPackAlert(bool hasAttackingState, bool running, bool inCombatState, bool evading, bool immortal, bool alive, bool enemyValid)
		{
			return CanEnterCombatFromThreat(hasAttackingState, running, inCombatState, evading) && !immortal && alive && enemyValid;
		}

		/// <summary>
		/// Joins the pack's fight against <paramref name="enemy"/>, if this member is free to.
		/// Called by <see cref="NPCGroup.AlertGroup"/>.
		/// </summary>
		/// <param name="enemy">The character a packmate engaged.</param>
		/// <returns>True when this member entered its attacking state against the enemy.</returns>
		internal bool AnswerPackAlert(ICharacter enemy)
		{
			if (!MayAnswerPackAlert(AttackingState != null, IsRunning, IsInCombatState, IsEvading,
					IsImmortal,
					Character != null && Character.TryGet(out ICharacterDamageController damage) && damage.IsAlive,
					AITargetSelection.IsValidTarget(enemy)))
			{
				return false;
			}

			// A passive pet stays out of it; a packmate's fight is not an owner's order.
			if (!PetStanceAllowsAutoEngage(false))
			{
				return false;
			}

			Target = enemy.Transform;
			LookTarget = enemy.Transform;
			ChangeState(AttackingState);
			return true;
		}

		/// <summary>
		/// Whether a pet may turn on an attacker standing at <paramref name="attackerPosition"/>
		/// without breaking its owner leash to do it. Pure, so the rule can be pinned.
		/// </summary>
		/// <remarks>
		/// Measured from the owner, because the owner leash is: the attacking state calls the pet
		/// back once the PET is <paramref name="ownerLeashRange"/> from its owner. The pet can hit
		/// from <paramref name="reach"/> short of its target, so an attacker within the leash plus
		/// that reach can be answered from inside the leash; one further out cannot, and the pet
		/// would break off before landing a blow. A leash of zero or less means no owner leash.
		/// </remarks>
		/// <param name="ownerPosition">The pet's owner.</param>
		/// <param name="attackerPosition">The character that hit the pet or its owner.</param>
		/// <param name="ownerLeashRange">The attacking state's <see cref="BaseAttackingState.OwnerLeashRange"/>.</param>
		/// <param name="reach">How far from its target the pet can fight: its preferred distance or its longest offensive reach.</param>
		/// <returns>True when the pet may engage.</returns>
		public static bool PetCanAnswerAttacker(Vector3 ownerPosition, Vector3 attackerPosition, float ownerLeashRange, float reach)
		{
			if (ownerLeashRange <= 0f)
			{
				return true;
			}
			float allowed = ownerLeashRange + Mathf.Max(0f, reach);
			return (attackerPosition - ownerPosition).sqrMagnitude <= allowed * allowed;
		}

		/// <summary>
		/// Whether the out-of-combat enemy sweep may run for an NPC in this state.
		/// </summary>
		/// <remarks>
		/// Never while going home, and never in a fight — which is any state that keeps the combat
		/// target (<see cref="IsInCombatState"/>): the attacking state and every combat sub-state.
		/// The sweep is how a calm NPC notices an enemy; an NPC already fighting has a target, a
		/// threat table and a re-evaluation timer for that. Pure, so it can be pinned without a scene.
		/// </remarks>
		/// <param name="hasAttackingState">True when the NPC can fight at all.</param>
		/// <param name="returningHome">True while the return-home state is the current state.</param>
		/// <param name="inCombatState">True while the current state is part of a fight.</param>
		/// <returns>True when the sweep may run.</returns>
		public static bool SweepMayRun(bool hasAttackingState, bool returningHome, bool inCombatState)
		{
			return hasAttackingState && !returningHome && !inCombatState;
		}

		/// <summary>
		/// Sweeps for nearby enemies and transitions to attacking state if any are found.
		/// </summary>
		/// <param name="deltaTime">Seconds elapsed since the previous AI tick.</param>
		private void SweepForEnemies(float deltaTime)
		{
			/* Only sweep when not going home and not already fighting — and "fighting" is every
			 * state that keeps the combat target (IsInCombatState), not just the attacking state.
			 * Testing the attacking state alone let the sweep run mid-orbit, mid-flank and
			 * mid-retreat, and a sweep that saw anybody re-entered the attacking state and re-picked
			 * its target from the sweep's candidates: an orbit or a flank was cut short, a flee
			 * ended the moment its pursuer came into view, and the target was chosen afresh from
			 * whoever the sweep could see. It was also, by accident, the only thing that installed a
			 * boss phase's attacking state mid-fight; SetPhaseOverrides does that on purpose now. */
			if (!SweepMayRun(AttackingState != null,
				CurrentState != null && CurrentState == ReturnHomeState,
				IsInCombatState))
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
		/// Ends a walk home — or a warp that stands in for one: the evade is over, the threat table
		/// is emptied, and the NPC goes back to its calm movement states.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The table is emptied on arrival as well as when the leash trips, because it can refill on
		/// the way: an NPC that owns a pet shares the threat of every hit its pet takes, however far
		/// it has to walk. A table carried home is a fight the NPC no longer knows it is in.
		/// </para>
		/// <para>
		/// Called by <see cref="ReturnHomeState"/> on arrival (walked, or warped because home could
		/// not be pathed to) and by <see cref="CheckLeash"/> after a full-leash warp out of a fight.
		/// </para>
		/// </remarks>
		internal void CompleteReturnHome()
		{
			leashEvade = false;
			AggressionState?.Clear();
			TransitionToRandomMovementState();
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

				// Measured before anything below clears it: did this leash end a fight?
				bool inCombatState = IsInCombatState;
				bool heldThreat = AggressionState?.HasAggression ?? false;
				bool endsFight = inCombatState || heldThreat;

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

					// Reset boss script phases on leash, and dismiss the boss's adds.
					if (BossState != null && BossScript != null && BossScript.ResetOnLeash)
					{
						ResetBossScriptForLeash();
					}

					/* The warp is a reset: the NPC is home, healed and holds no threat, so the fight
					 * is over and the NPC must leave it. It used to stay in its attacking state with
					 * its target still set, run the whole leash distance straight back to the player,
					 * and be warped home again on the next check — healed each time. Leaving through
					 * the same door as a completed walk home also ends any evade. */
					if (endsFight)
					{
						CompleteReturnHome();
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

					/* A boss that walks home at full health (the return heals it) must not come back
					 * fighting with its last phase's overrides: ResetOnLeash promises a heal AND a
					 * phase reset "when leashing back home", and this walk is that leash. Only the
					 * warp above used to honour it. */
					if (endsFight && BossState != null && BossScript != null && BossScript.ResetOnLeash)
					{
						ResetBossScriptForLeash();
					}

					ChangeState(ReturnHomeState);

					/* ChangeState can decline — no return state, or a nested transition out of it — so
					 * both consequences below stand only when the return actually became the current
					 * state. */
					bool returning = CurrentState != null && ReferenceEquals(CurrentState, ReturnHomeState);

					/* The heal is the leash's, not the state's. The return state is also one of the
					 * calm movement states, and while it healed on Enter a damaged NPC that merely
					 * strolled home between fights was topped up to full on the way; only here is it
					 * known that a leash sent it. Every leash heals, as the warp above does — the
					 * authored flag on the return state says whether this archetype's does at all. */
					if (LeashReturnHeals(returning, ReturnHomeState is ReturnHomeState homeAsset && homeAsset.CompleteHealOnReturn) &&
						Character.TryGet(out ICharacterDamageController returningDamageController))
					{
						returningDamageController.CompleteHeal();
					}

					/* Set after the transition, which clears it: a leash that ends a fight makes the
					 * walk home an evade (see IsEvading). */
					leashEvade = LeashStartsEvade(cachedPet != null, inCombatState, heldThreat) && returning;
				}

				nextLeashUpdate = CurrentState.LeashUpdateRate;
			}
			nextLeashUpdate -= deltaTime;
		}

		/// <summary>
		/// Sends the NPC to stand out of the weather, and brings it back out when the weather has
		/// passed (Q15).
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>Never during a fight.</b> An NPC that walked off to a barn mid-combat would be
		/// unkillable in a thunderstorm and would look ridiculous doing it. Combat, leashing and
		/// being dead all outrank the weather.
		/// </para>
		/// <para>
		/// <b>Asked on a slow clock.</b> Weather moves over minutes, so the default interval is
		/// seconds rather than ticks; the sample and the shelter search are the expensive parts and
		/// neither needs to happen often. An archetype with sheltering off pays one boolean.
		/// </para>
		/// <para>
		/// Two thresholds, as with everything else exposure-driven: what it takes to go in is
		/// higher than what it takes to stay, so weather sitting on the line does not have the
		/// creature pacing in and out of a doorway.
		/// </para>
		/// </remarks>
		/// <param name="deltaTime">Seconds elapsed since the previous AI tick.</param>
		private void CheckShelter(float deltaTime)
		{
			AIShelterSettings settings = Shelter;
			if (settings == null || !settings.Enabled || settings.ShelterState == null)
			{
				return;
			}

			nextShelterCheck -= deltaTime;
			if (nextShelterCheck > 0f)
			{
				return;
			}
			nextShelterCheck = settings.CheckInterval;

			// Fighting, going home, or dead: the weather is the least of it.
			bool sheltering = ReferenceEquals(CurrentState, settings.ShelterState);
			if (CurrentState is BaseAttackingState ||
				CurrentState == ReturnHomeState ||
				(AggressionState?.HasAggression ?? false) ||
				Character == null || Character.GameObject == null ||
				Character.IsFlagged(CharacterFlags.IsDead))
			{
				if (sheltering)
				{
					// It was sheltering and something more urgent came up; let go of the state so
					// the NPC is not left standing in a barn after the fight.
					TransitionToRandomMovementState();
				}
				return;
			}

			WeatherSample weather = WeatherQuery.Sample(Character.GameObject.scene, Character.Transform.position);

			if (sheltering)
			{
				if (!settings.WouldStay(weather))
				{
					TransitionToRandomMovementState();
				}
				return;
			}

			if (!settings.WantsShelter(weather))
			{
				return;
			}

			// Only commit to the state when there is somewhere to go. Entering it with no shelter in
			// reach would have the NPC walk nowhere and immediately bounce back out, once per check,
			// for as long as the storm lasted.
			if (WeatherVolumeRegistry.NearestShelter(Character.GameObject.scene, Home,
				settings.SearchRadius, settings.MinimumShelterStrength) == null)
			{
				return;
			}

			ChangeState(settings.ShelterState);
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
		/// Throttles aggression decay to one pass per <see cref="AGGRESSION_TICK_INTERVAL"/> instead
		/// of every tick, crediting each pass with the time that really passed.
		/// Called by <see cref="UpdateActive"/> and <see cref="UpdateNearby"/> but
		/// NOT by <see cref="UpdateFar"/> (Far tier NPCs have no threat table).
		/// </summary>
		/// <remarks>
		/// The pass used to be credited the nominal 0.5 s whatever had elapsed. The brain tick is
		/// coarser than that in the Nearby tier — a pass lands every 0.8 s there — so decay and the
		/// stale timeout ran at 62.5% speed and 30 s of "no events" took 48. The clock is the same
		/// <see cref="AIStateClock"/> the state machine uses for the same reason.
		/// </remarks>
		private void TickAggression(float dt)
		{
			if (aggressionClock.Advance(dt, out float elapsed))
			{
				AggressionState?.Tick(elapsed);
				aggressionClock.Rearm(AGGRESSION_TICK_INTERVAL, dt);
			}

			/* An empty threat table is what "this fight is over" means, and it is the only signal
			 * that can safely say so.
			 *
			 * The obvious alternative — clearing the retreat streak wherever the target is dropped —
			 * is wrong, because a retreat drops its own target on the way out. The streak would
			 * reset on every single retreat and the ramp driven by it would never climb past one,
			 * which is the whole mechanism that makes a long chase end. Deriving the end of the
			 * fight from the threat table instead also means it cannot be forgotten: this is one
			 * place, rather than a line added to each of the six sites that clear that table. */
			if (Retreat.ConsecutiveRetreats > 0 && !(AggressionState?.HasAggression ?? false))
			{
				Retreat.Clear();
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
		/// Squared distance from this NPC to the nearest player in its scene.
		/// </summary>
		/// <remarks>
		/// <para>
		/// From <see cref="ObserverStreamingRegistry"/>, which measures viewer-to-object distance for
		/// every pair it considers anyway, before it applies any range or budget filter. That is a
		/// PROXIMITY question and this is the only place the server answers it honestly.
		/// </para>
		/// <para>
		/// It used to be answered from <c>NetworkObject.Observers</c>. Observer membership is a
		/// BANDWIDTH decision — <c>ObserverBudgetCondition</c> admits the top thirty monsters per
		/// viewer and no more — so a monster evicted from every viewer's budget reported "no player
		/// is anywhere near me" while standing in the middle of a raid, and the tier machinery below
		/// full-healed it, cleared its threat table and reset its boss phases on the strength of it.
		/// </para>
		/// <para>
		/// Returns false when no pass has measured this NPC yet (it registered between two passes, or
		/// the registry is not running at all, as in the simulation harness scenes). Callers must not
		/// read false as "nobody is near" — see <see cref="ResolveLodTier"/> and
		/// <see cref="AllowsLeashReset"/>, both of which treat it as an unanswered question.
		/// </para>
		/// </remarks>
		/// <param name="sqrDistance">Squared distance to the nearest player; infinity when the scene holds none.</param>
		/// <returns>True when the measurement is valid.</returns>
		public bool TryGetNearestPlayerSqrDistance(out float sqrDistance)
		{
			if (NetworkObject != null &&
				ObserverStreamingRegistry.TryGetNearestViewerDistance(NetworkObject, out float distance))
			{
				sqrDistance = distance * distance;
				return true;
			}
			sqrDistance = float.PositiveInfinity;
			return false;
		}

		/// <summary>
		/// True when a player is close enough for this NPC's expensive per-tick polling — the enemy
		/// sweep above all — to be worth doing.
		/// </summary>
		/// <remarks>
		/// Inside the Nearby band, or unmeasured. Never false merely because nothing is streaming
		/// this NPC: an <c>Observers.Count &lt; 1</c> test here meant a budget-evicted monster could
		/// not aggro anybody, including the player who had just pulled it.
		/// </remarks>
		public bool HasNearbyPlayer
		{
			get
			{
				if (!TryGetNearestPlayerSqrDistance(out float sqrDistance))
				{
					return true;
				}
				return LodSettings == null || sqrDistance <= LodSettings.NearbyDistanceSqr;
			}
		}

		/// <summary>
		/// Evaluates the AI LOD tier from how far away the nearest player actually is.
		/// </summary>
		private AILodTier EvaluateLodTier()
		{
			bool measured = TryGetNearestPlayerSqrDistance(out float nearestSqrDistance);
			return ResolveLodTier(LodSettings, measured, nearestSqrDistance);
		}

		/// <summary>
		/// The tier rule, as a pure function so it is testable without a NetworkManager.
		/// </summary>
		/// <remarks>
		/// Truth table, first matching row wins:
		/// <list type="table">
		/// <item><description>no LOD settings → <see cref="AILodTier.Active"/>. Nothing has authored a
		/// throttle for this NPC.</description></item>
		/// <item><description>no proximity measurement → <see cref="AILodTier.Active"/>. An
		/// unanswered question is not permission to suspend a brain; the answer arrives within one
		/// scheduling pass, and running one extra tier for half a second costs far less than a monster
		/// that stops thinking with a player in front of it.</description></item>
		/// <item><description>measured → the authored distance bands, infinity (an empty scene)
		/// landing in <see cref="AILodTier.Dormant"/>.</description></item>
		/// </list>
		/// </remarks>
		/// <param name="settings">Authored LOD bands, or null.</param>
		/// <param name="hasProximityMeasurement">False when nothing has measured the nearest player yet.</param>
		/// <param name="nearestPlayerSqrDistance">Squared distance to the nearest player.</param>
		/// <returns>The tier to run at.</returns>
		public static AILodTier ResolveLodTier(AILodSettings settings, bool hasProximityMeasurement, float nearestPlayerSqrDistance)
		{
			if (settings == null || !hasProximityMeasurement)
			{
				return AILodTier.Active;
			}
			return settings.GetTier(nearestPlayerSqrDistance);
		}

		/// <summary>
		/// Whether a tier change may run the leash reset — interrupt, full heal, threat clear, boss
		/// phase reset — as a pure function so it is testable without a NetworkManager.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The reset is authoritative simulation state, so it asks proximity directly rather than
		/// trusting the tier that triggered it. The tier says how much WORK to do; only distance says
		/// whether a fight is really over.
		/// </para>
		/// <para>
		/// Truth table, first matching row wins:
		/// <list type="table">
		/// <item><description>not in an attacking state → false. There is no fight to end.</description></item>
		/// <item><description>tier below <see cref="AILodTier.Far"/> → false. The NPC is still being
		/// simulated in full.</description></item>
		/// <item><description>no proximity measurement → false. Never hand a monster its health back
		/// on an unanswered question.</description></item>
		/// <item><description>nearest player inside <paramref name="leashResetSqrDistance"/> → false.
		/// Somebody is right there, whatever the network is streaming them.</description></item>
		/// <item><description>otherwise → true.</description></item>
		/// </list>
		/// </para>
		/// </remarks>
		/// <param name="newTier">Tier being transitioned to.</param>
		/// <param name="inAttackingState">True when the NPC is currently in a <see cref="BaseAttackingState"/>.</param>
		/// <param name="hasProximityMeasurement">False when nothing has measured the nearest player yet.</param>
		/// <param name="nearestPlayerSqrDistance">Squared distance to the nearest player.</param>
		/// <param name="leashResetSqrDistance">Squared distance a player must be beyond for the reset to be allowed.</param>
		/// <returns>True when the reset may run.</returns>
		public static bool AllowsLeashReset(AILodTier newTier, bool inAttackingState, bool hasProximityMeasurement, float nearestPlayerSqrDistance, float leashResetSqrDistance)
		{
			if (!inAttackingState || newTier < AILodTier.Far || !hasProximityMeasurement)
			{
				return false;
			}
			return nearestPlayerSqrDistance > leashResetSqrDistance;
		}

		/// <summary>
		/// Recomputes <see cref="AimRotation"/> for the current tick.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>Every network tick, not every brain tick.</b> Called from
		/// <see cref="Tick"/> beside <see cref="FaceLookTarget"/> and for the same
		/// reason: <c>AbilityController.PopulateAiAim</c> reads the result on every network tick, so
		/// it has to be written on every network tick. It used to be called from the brain tick —
		/// 8 Hz in Active, 2.7 Hz in Nearby, never in Far — which left the value between one and
		/// four network ticks stale at the moment a shot resolved. Facing was moved off the brain
		/// tick for exactly this reason; the aim is the other half of the same replicated transform
		/// and belongs on the same schedule.
		/// </para>
		/// <para>
		/// <b>The origin is <see cref="AimOrigin"/>, not the transform.</b> Solving from the
		/// character root while the projectile leaves from root + 1.45 m is the defect this whole
		/// change exists to fix: the two rays are parallel and 1.45 m apart, so the error is constant
		/// at every range and puts the shot over a standing player's head.
		/// </para>
		/// </remarks>
		/// <param name="deltaTime">Seconds elapsed since the previous network tick.</param>
		private void UpdateAim(float deltaTime)
		{
			if (Target == null)
			{
				/* No target, no aim. Body facing is the honest answer, and it is what the melee path
				 * reads anyway — AbilitySpawnTarget.Forward derives its pose from the body, so
				 * writing anything else here would only affect ranged casts that have no target to
				 * range against. */
				AimRotation = Character.Transform.rotation;
				TargetClosingSpeed = 0f;
				return;
			}

			/* Tracked time, not wall-clock time. Combat runs only in the Active and Nearby tiers; a
			 * target held through a drop to Far is not being tracked at all, and a ramp that kept
			 * counting would hand the NPC full accuracy the instant the player came back into range
			 * without it ever having aimed at them. */
			if (currentLodTier == AILodTier.Active || currentLodTier == AILodTier.Nearby)
			{
				AimLock += deltaTime;
			}

			Vector3 targetPosition = Target.position;

			if (hasLastTargetPosition && deltaTime > 0f)
			{
				Vector3 travel = targetPosition - lastTargetPosition;
				float speedSqr = travel.sqrMagnitude / (deltaTime * deltaTime);

				if (speedSqr <= MaxPlausibleTargetSpeed * MaxPlausibleTargetSpeed)
				{
					TargetVelocity = travel / deltaTime;
				}
			}

			lastTargetPosition = targetPosition;
			hasLastTargetPosition = true;

			/* Positive when the target is closing on us. The sign is what a retreating NPC needs:
			 * its own backing away does not move this number, only the target's pursuit does, so a
			 * pursuer is distinguishable from a target that happens to be far away. */
			Vector3 toSelf = Character.Transform.position - targetPosition;
			TargetClosingSpeed = toSelf.sqrMagnitude > AIAimSolver.MinimumAimDistanceSqr
				? Vector3.Dot(TargetVelocity, toSelf.normalized)
				: 0f;

			AIAimProfile profile = Archetype != null ? Archetype.AimProfile : null;
			ICharacter targetCharacter = TargetCharacter;

			Vector3 aimPoint = AIAimSolver.ResolveAimPoint(
				targetPosition,
				targetCharacter != null ? targetCharacter.Collider : null,
				profile != null ? profile.AimPoint : AIAimPoint.Center);

			if (AimLeadSpeed > 0f)
			{
				/* Recomputed every tick from the live distance, so the lead is right at the moment the
				 * projectile spawns rather than right at the moment the cast was queued — see
				 * AimLeadSpeed. The accuracy dial is read per tick too; the profile is authored data and
				 * nothing mutates it at runtime. */
				float leadTime = AIAimSolver.ResolveLeadTime(
					Vector3.Distance(AimOrigin, aimPoint),
					AimLeadSpeed,
					profile != null ? profile.LeadAccuracy : 0f);

				aimPoint += AIAimSolver.ResolveLeadOffset(TargetVelocity, leadTime);
			}

			/* A degenerate solve keeps the previous rotation rather than writing a zero vector:
			 * AimDirectionCompression.Encode substitutes its fallback direction for a degenerate
			 * input, so writing one would silently snap the NPC's aim to a fixed world direction
			 * for a tick. Only reachable when the aim origin is inside the target's collider. */
			if (AIAimSolver.TryComposeAim(AimOrigin, aimPoint, AimScatter, out Vector3 direction))
			{
				AimRotation = Quaternion.LookRotation(direction);
			}
		}

		/// <summary>
		/// Rolls the aim error for a cast that is about to begin, and notes what it is being cast with.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>Once per cast, never per tick.</b> The NPC's seeded RNG is shared with cooldown
		/// jitter, target selection, movement-variety rolls, wander rates and leash timing. Drawing
		/// from it on every network tick would advance that stream thirty times a second and change
		/// the behaviour of every one of those, none of which this feature is tuning. Once per cast
		/// is also what the design wants: the error belongs to the shot, not to the tick, and holding
		/// it is what makes an inaccurate NPC read as aiming badly rather than spraying.
		/// </para>
		/// <para>
		/// A null profile rolls nothing, which is the exact-aim default every archetype without an
		/// aim profile keeps.
		/// </para>
		/// </remarks>
		/// <param name="profile">The archetype's aim profile, or null for exact aim.</param>
		/// <param name="projectileSpeed">The ability's travel speed, or 0 for an instant ability.</param>
		public void RollAimForCast(AIAimProfile profile, float projectileSpeed)
		{
			if (profile == null)
			{
				AimScatter = Quaternion.identity;
				AimLeadSpeed = 0f;
				return;
			}

			float accuracy = AIAimSolver.ResolveAccuracy(AimLock, profile.LockSeconds, profile.BaseAccuracy);
			float spread = AIAimSolver.ResolveSpread(profile.SpreadDegrees, accuracy);

			DeterministicRNG rng = NpcRNG ?? DeterministicRNG.Shared;

			AimScatter = AIAimSolver.RollScatter(spread, rng.Range(-1f, 1f), rng.Range(-1f, 1f));
			AimLeadSpeed = projectileSpeed;
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

			uint currentTick = cooldownController.ResolveAuthoritativeTick(TimeManager.LocalTick);

			for (int i = 0; i < cachedAbilities.Count; i++)
			{
				Ability ability = cachedAbilities[i];

				// Skip abilities the caller is not interested in (e.g. heals during a damage pick).
				if (filter != null && !filter(ability))
					continue;

				// Skip abilities on cooldown.
				if (cooldownController.IsOnCooldown(ability.ID, currentTick))
					continue;

				// Skip abilities the character can't afford. The context is the controller's own,
				// reused across picks rather than allocated per pick.
				if (!ability.MeetsActivationConditions(Character, ref ActivationCheckData))
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
			uint currentTick = cooldownController.ResolveAuthoritativeTick(TimeManager.LocalTick);

			for (int i = 0; i < cachedAbilities.Count; i++)
			{
				Ability ability = cachedAbilities[i];
				if (cooldownController.IsOnCooldown(ability.ID, currentTick))
					continue;
				if (!ability.MeetsActivationConditions(Character, ref ActivationCheckData))
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

			/* Every real transition ends an evade. CheckLeash sets the flag again straight after the
			 * transition that starts one, so this single funnel is what guarantees no other path
			 * into or out of the return can leave an NPC immune. */
			leashEvade = false;

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

				/* Alert the pack when entering combat. The identity-checked character, not the
				 * transform: a pooled target's transform can already belong to somebody else. */
				if (Group != null)
				{
					ICharacter engaged = TargetCharacter;
					if (engaged != null)
					{
						Group.AlertGroup(engaged);
					}
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

			/* An evading NPC cannot be pulled back into the fight it just leashed out of. A taunt
			 * that could would be the same exploit the evade exists to stop, by another route. */
			if (IsEvading)
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

			/* Already facing it: leave the transform alone. The smoothing below only ever
			 * approaches the target, so without this an NPC facing an interactor or a standing
			 * target rewrote its rotation — a transform write and a change notification — on every
			 * network tick for as long as the look target was set. */
			Quaternion current = Character.Transform.rotation;
			if (Quaternion.Angle(current, targetRotation) <= FACING_TOLERANCE_DEGREES)
			{
				return;
			}

			/* Exponential smoothing rather than Slerp(a, b, rate * dt).
			 *
			 * The linear form is frame-rate dependent: doubling the frame rate halves each step
			 * but does not halve the total turn, so an NPC visibly turns at a different speed on a
			 * 30 Hz server than on a 60 Hz one. 1 - e^(-rate * dt) is the closed form of the same
			 * smoothing sampled continuously, so the result is identical at any step size. */
			float t = 1f - Mathf.Exp(-TurnRate * deltaTime);

			Character.Transform.rotation = Quaternion.Slerp(current, targetRotation, t);
		}

		/// <summary>
		/// Angle, in degrees, within which the NPC counts as already facing its look target.
		/// Well under what the rotation compression on the wire can show.
		/// </summary>
		public const float FACING_TOLERANCE_DEGREES = 0.1f;

		/// <summary>
		/// Speed below which the agent's velocity is not worth turning toward, in metres per second.
		/// </summary>
		public const float HEADING_SPEED_THRESHOLD = 0.05f;

		/// <summary>
		/// Recomputes the push away from overlapping NPC bodies in this NPC's own physics scene.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Runs on the brain tick for the Active and Nearby tiers only. Players are not pushed
		/// against — they do not run agents and never took part in crowd avoidance either — and
		/// neither is anything outside this NPC's <see cref="PhysicsScene"/>, which is what keeps
		/// stacked scene instances from touching each other. See <see cref="AISeparation"/>.
		/// </para>
		/// <para>
		/// Neighbours come from the host's per-scene <see cref="AIBodyGrid"/>, built once per
		/// network tick for everyone who asks, not from a physics overlap per NPC. The overlap hit
		/// this NPC's own collider on every call and paid an interface component lookup for it and
		/// for each neighbour; the grid holds every NPC body in the scene already resolved, and
		/// excludes this one by key.
		/// </para>
		/// </remarks>
		private void UpdateSeparation()
		{
			separationVelocity = Vector3.zero;

			if (SeparationSpeed <= 0f || Agent == null || Character == null || Host == null || !PhysicsScene.IsValid())
			{
				return;
			}

			AIBodyGrid grid = Host.GetBodyGrid(PhysicsScene);
			if (grid == null)
			{
				return;
			}

			float radius = SeparationRadius > 0f ? SeparationRadius : Agent.radius * 2f;
			Vector3 position = Character.Transform.position;

			/* The vertical reach stands in for the height the old overlap sphere had against a
			 * standing body's collider: another NPC counts if its capsule could reach this one's
			 * sphere, so one standing on a bridge overhead does not push the one below. */
			separationNeighbours.Clear();
			separationKeys.Clear();
			grid.Query(position, radius, radius + Agent.height, IdentityKey, separationNeighbours, separationKeys);

			separationVelocity = AISeparation.Resolve(position, IdentityKey, separationNeighbours, separationKeys, radius, SeparationSpeed);
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
			 * leave the agent believing it is somewhere else.
			 *
			 * But only when there is something to re-seat. A standing NPC whose agent already sits
			 * on its transform used to pay a NavMesh projection (the nextPosition write) and a
			 * transform write — a static collider move — on every network tick regardless:
			 * thirty a second for every idle NPC in the process. */
			if (NeedsAgentWrite(step, Agent.nextPosition, before))
			{
				Agent.nextPosition = before + step;
				t.position = Agent.nextPosition;

				// What the mesh let through, not what was asked for: the stuck detector reads this.
				measuredTickSpeedSqr = MeasureTickSpeedSqr(before, t.position, tickDelta);
			}
			else
			{
				measuredTickSpeedSqr = 0f;
			}

			// The speed test first, so a standing NPC does not read its rotation just to be told no.
			if (LookTarget == null &&
				velocity.sqrMagnitude >= HEADING_SPEED_THRESHOLD * HEADING_SPEED_THRESHOLD &&
				ResolveTickHeading(t.rotation, velocity, Agent.angularSpeed, tickDelta, out Quaternion heading))
			{
				t.rotation = heading;
			}
		}

		/// <summary>
		/// Whether a tick's step has to be written to the agent and the transform.
		/// </summary>
		/// <remarks>
		/// Only when the NPC is moving, or when something other than the step has moved its
		/// transform away from where its agent believes it is. Both comparisons use Unity's
		/// approximate vector equality, a hundredth of a millimetre: far below anything a tick can
		/// show. Pure, so the rule can be pinned without an agent.
		/// </remarks>
		/// <param name="step">This tick's displacement.</param>
		/// <param name="agentPosition">Where the agent's simulation has the NPC.</param>
		/// <param name="transformPosition">Where the transform has it.</param>
		/// <returns>True to write the step.</returns>
		public static bool NeedsAgentWrite(Vector3 step, Vector3 agentPosition, Vector3 transformPosition)
		{
			return step != Vector3.zero || agentPosition != transformPosition;
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
		/// Called by <see cref="SuspendForCorpse"/> alongside disabling the controller. Disabling stops
		/// the brain; this stops the body: the agent's path is cleared, the target and look target
		/// are dropped so nothing re-engages, and the threat table is emptied. Without it the
		/// corpse's agent kept its destination and, when the brain was later re-enabled for the
		/// next pool occupant, was still heading for wherever its killer had been standing.
		/// </remarks>
		public void HaltMovement()
		{
			// A body that has stopped is not walking home; nothing it was evading for remains.
			leashEvade = false;
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

		/// <summary>
		/// Stops a brain the host has given up on: it no longer runs, acquires targets or evades,
		/// and its body is brought to a halt where it stands.
		/// </summary>
		/// <remarks>
		/// <see cref="IsRunning"/> reads <see cref="Quarantined"/>, so the evade — which would
		/// otherwise leave a quarantined NPC immune for good — and combat entry both end here. The
		/// halt is attempted but not trusted: a brain that threw thirty times in a row may throw
		/// again, and nothing here may take the host's tick down with it.
		/// </remarks>
		internal void Quarantine()
		{
			Quarantined = true;
			leashEvade = false;
			try
			{
				HaltMovement();
			}
			catch (System.Exception ex)
			{
				Log.Error("AIController", $"Halting quarantined brain on {gameObject.name} also threw: {ex}");
			}
		}

		/// <inheritdoc />
		public bool SuspendForCorpse()
		{
			bool wasRunning = enabled;
			enabled = false;
			HaltMovement();

			/* A corpse holds no grudges. Beyond being wrong, a populated threat table keeps
			 * AggressionState.HasAggression true, which is exactly the flag AggressionDispatcher uses
			 * to decide who is worth delivering heal and kill events to — so every corpse in the
			 * scene would be walked and handed every such event for the whole of its decay.
			 * HaltMovement already clears it; repeated here so the rule survives a change there. */
			AggressionState?.Clear();

			/* A corpse is not a packmate. It leaves at death rather than when its body decays, so the
			 * pack stops counting, reading and protecting it at once, and a pack whose last member
			 * falls is released there and then. Its respawn joins its spawner's pack afresh. */
			LeavePack();

			/* Nor a live add. A dead add stops counting towards its boss's cap, and a leash reset
			 * leaves its corpse to decay with its loot rather than despawning it. A boss that dies
			 * lets go of its adds, which fight on as they always have. */
			LeaveSummoner();
			BossState?.ReleaseAdds();

			return wasRunning;
		}

		/// <inheritdoc />
		public void ResumeAfterCorpse()
		{
			enabled = true;
		}

		/// <inheritdoc />
		public void FaceInteractor(Transform interactor)
		{
			LookTarget = interactor;
			TransitionToIdleState();
		}

		/// <inheritdoc />
		/// <remarks>
		/// <para>
		/// <b>The guarantee is computed in SCORE space, not in raw points.</b> An NPC chooses its
		/// target with <see cref="AggressionController.GetThreatScore"/>, which multiplies raw points
		/// by a vulnerability factor for a wounded or out-of-mana character — so a taunt that merely
		/// put the taunter above the highest RAW entry still lost to a wounded ally on the very next
		/// re-evaluation, and the forced switch hid it for exactly one switch.
		/// </para>
		/// <para>
		/// The table holds raw points and no character references, so it cannot evaluate another
		/// entry's score directly — but every entry's score is at most its raw points times
		/// <see cref="AggressionController.MaximumVulnerabilityMultiplier"/>, so clearing that bound
		/// clears every actual score whichever entry carries it. Conservative by up to that factor
		/// and never short, which is the direction a guarantee has to err in.
		/// </para>
		/// <para>
		/// The taunter's OWN multiplier is deliberately NOT used to shrink the requirement, even
		/// though it is known exactly at this instant. It is TRANSIENT — 1.5x below 30% health, gone
		/// the moment a heal lands — while the raw points granted here are permanent. Dividing by it
		/// once granted a wounded tank proportionally fewer points, and the first heal then dropped
		/// their score back under the previous top's ceiling: the boss returned to its old target on
		/// the next re-evaluation, with the forced switch masking the failure for exactly one switch.
		/// Treating the taunter's multiplier as its floor of 1 keeps the guarantee standing for every
		/// later multiplier the taunter can have.
		/// </para>
		/// </remarks>
		public void ApplyTaunt(ICharacter taunter, float threatPoints, bool guaranteeTopThreat, float leadOverHighest, bool forceImmediateTargetSwitch)
		{
			if (taunter == null || Aggression == null)
			{
				return;
			}

			/* An evading NPC takes no threat. Its table was emptied when the leash tripped and is
			 * emptied again on arrival; a taunt landing in between would either pull it back into
			 * the fight it leashed out of or be carried home as a grudge. */
			if (IsEvading)
			{
				return;
			}

			float points = threatPoints;

			if (guaranteeTopThreat)
			{
				float highestRaw = Aggression.GetHighestPoints(taunter.ID);
				float ceilingScore = highestRaw * Aggression.MaximumVulnerabilityMultiplier;

				float requiredPoints = ceilingScore + leadOverHighest;
				float required = requiredPoints - Aggression.GetPoints(taunter.ID);
				if (required > points)
				{
					points = required;
				}
			}

			/* Threat a taunt grants is offered to the same combat-entry rule a hit is. That rule
			 * reads the NPC's state, not the table's, so it does not matter who wrote the table's
			 * first entry: an idle NPC taunted without a forced switch still turns on the taunter,
			 * and a later real hit still starts the fight if this one did not. (Combat entry used
			 * to be an empty-to-non-empty edge on the table, and a taunt that seeded the table
			 * silently consumed it.) */
			if (points > 0f)
			{
				Aggression.AddPoints(taunter.ID, points);
				OnThreatReceived(taunter);
			}

			if (forceImmediateTargetSwitch)
			{
				ForceTarget(taunter);
			}
		}

		/// <inheritdoc />
		/// <remarks>
		/// Only NPCs already in combat gain flat threat: casting near an unaware mob does not pull
		/// it. The resource record is kept regardless, and <see cref="AggressionState.RecordResourceSpent"/>
		/// applies the same in-combat rule to it.
		/// </remarks>
		public void ApplyAreaThreat(ICharacter caster, float threatPoints, int resourceSpent)
		{
			// An evading NPC takes no threat; see ApplyTaunt.
			if (caster == null || AggressionState == null || IsEvading)
			{
				return;
			}

			if (resourceSpent > 0)
			{
				AggressionState.RecordResourceSpent(caster.ID, resourceSpent);
			}

			if (threatPoints > 0f && Aggression != null && Aggression.HasAggression)
			{
				Aggression.AddPoints(caster.ID, threatPoints);
			}
		}
	}
}
