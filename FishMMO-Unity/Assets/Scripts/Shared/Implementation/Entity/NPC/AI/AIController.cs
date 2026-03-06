using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.AI;
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
		private List<BaseAIState> movementStates = new List<BaseAIState>();

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

			if (Home != null)
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

			// Initialize the per-NPC aggression state with serialized tuning values.
			AggressionState = new AggressionState(
				Character,
				AggressionDamageWeight,
				AggressionHealingWeight,
				AggressionHitBonus,
				AggressionDecayRate,
				AggressionStaleTimeout,
				AggressionVarietyChance);

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
			AggressionState?.Clear();
		}

		/// <summary>
		/// Unity Update loop. Handles enemy sweeping, leash checks, state updates, virtual camera, aggression decay, and facing look target.
		/// </summary>
		void Update()
		{
			SweepForEnemies();
			CheckLeash();
			UpdateCurrentState();
			UpdateVirtualCamera();
			AggressionState?.Tick(Time.deltaTime);
			FaceLookTarget();
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
				if (AttackingState.SweepForEnemies(this, out List<ICharacter> enemies))
				{
					ChangeState(AttackingState, enemies);
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

					return;
				}
				// If leash is exceeded but not critical, transition to return home state.
				else if (distanceToHome > CurrentState.MinLeashRange * CurrentState.MinLeashRange)
				{
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
				// Aim at the center of the target's collider for accuracy.
				Vector3 targetPoint = Target.position;
				ICharacter targetCharacter = Target.GetComponent<ICharacter>();
				if (targetCharacter != null && targetCharacter.Collider != null)
				{
					targetPoint = targetCharacter.Collider.bounds.center;
				}

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

			float sqrDist = GetSqrDistanceToTarget();

			Ability bestAbility = null;
			float bestScore = float.MinValue;

			foreach (var kvp in abilityController.KnownAbilities)
			{
				Ability ability = kvp.Value;
				if (ability == null || ability.Template == null)
					continue;

				// Skip abilities on cooldown.
				if (cooldownController.IsOnCooldown(ability.ID))
					continue;

				// Skip abilities the character can't afford.
				if (!ability.MeetsActivationConditions(Character))
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

				// Add small random jitter so the NPC doesn't always pick the same ability.
				score += Random.Range(0f, 50f);

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

			float sqrMinRange = minRange * minRange;

			foreach (var kvp in abilityController.KnownAbilities)
			{
				Ability ability = kvp.Value;
				if (ability == null || ability.Template == null)
					continue;
				if (cooldownController.IsOnCooldown(ability.ID))
					continue;
				if (!ability.MeetsActivationConditions(Character))
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