using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.AI;

namespace FishMMO.Shared
{
	public class AIController : CharacterBehaviour
	{
		public BaseAIState WanderState;
		public BaseAIState PatrolState;
		public BaseAIState ReturnHomeState;
		public BaseAIState RetreatState;
		public BaseAIState IdleState;
		public BaseAIState AttackingState;
		public BaseAIState DeadState;

		//public List<AIState> AllowedRandomStates;

		public Transform Transform { get; private set; }
		public PhysicsScene PhysicsScene { get; private set; }
		public Transform Home { get; private set; }
		public Transform Target
		{
			get
			{
				return target;
			}
			set
			{
				target = value;
				if (value != null)
				{
					Agent.SetDestination(value.position);
				}
				else
				{
					Agent.SetDestination(Transform.position);
				}
			}
		}
		public NavMeshAgent Agent { get; private set; }
		public BaseAIState CurrentState { get; private set; }
		/// <summary>
		/// Minimum distance at which the AI will forget the current target and return home
		/// </summary>
		public float MinLeashRange = 100.0f;
		/// <summary>
		/// Maximum distance at which the AI will forget the current target and teleport return home
		/// </summary>
		public float MaxLeashRange = 500.0f;
		/// <summary>
		/// The waypoints available to this AI controller
		/// </summary>
		public Vector3[] Waypoints;
		/// <summary>
		/// The current waypoint index
		/// </summary>
		public int CurrentWaypointIndex { get; private set; }
		/// <summary>
		/// Distance at which an interaction can be initiated
		/// </summary>
		public float InteractionDistance = 1.5f;

		public readonly List<BaseAIState> MovementStates = new List<BaseAIState>();

		private Transform target;
		private float nextUpdate = 0;

#if UNITY_EDITOR
		void OnDrawGizmos()
		{
			DrawDebugCircle(transform.position, InteractionDistance, Color.red);

			if (Home != null)
			{
				if (WanderState != null && WanderState is WanderState wanderState)
				{
					DrawDebugCircle(Home.position, wanderState.WanderRadius, Color.white);
				}
				DrawDebugCircle(Home.position, 0.5f, Color.white);
			}
		}
#endif

		public override void OnAwake()
		{
			base.OnAwake();

			if (Agent == null)
			{
				Agent = GetComponent<NavMeshAgent>();
				if (Agent == null)
				{
					gameObject.SetActive(false);
					return;
				}
			}

			Transform = transform;
			PhysicsScene = gameObject.scene.GetPhysicsScene();

			if (WanderState != null)
			{
				MovementStates.Add(WanderState);
			}
			if (PatrolState != null)
			{
				MovementStates.Add(PatrolState);
			}
			if (ReturnHomeState != null)
			{
				MovementStates.Add(ReturnHomeState);
			}
			if (IdleState != null)
			{
				MovementStates.Add(IdleState);
			}
		}

		public void Initialize(Transform home, BaseAIState initialState, Vector3[] waypoints = null)
		{
			Home = home;
			Waypoints = waypoints;

			// Set initial state
			ChangeState(initialState);
		}

		private void Update()
		{
			if (CurrentState != null)
			{
				if (nextUpdate < 0.0f)
				{
					// If the target is too far away from home, return home and forget the target
					float distanceToHome = Vector3.Distance(Transform.position, Home.position);
					if (distanceToHome >= MinLeashRange)
					{
						// Warp back to home if we have somehow reached a significant leash range
						if (distanceToHome >= MaxLeashRange)
						{
							// Complete heal on returning home
							if (Character.TryGet(out ICharacterDamageController characterDamageController))
							{
								characterDamageController.CompleteHeal();
							}
							// Warp
							Transform.position = Home.position;
							Target = null;
						}
						// Otherwise run back
						else if (ReturnHomeState != null)
						{
							ChangeState(ReturnHomeState);
							return;
						}
					}

					CurrentState.UpdateState(this);
					nextUpdate = CurrentState.UpdateRate;
				}
				nextUpdate -= Time.deltaTime;
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void Stop()
		{
			Agent.isStopped = true;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void Resume()
		{
			Agent.isStopped = false;
		}

		public void ChangeState(BaseAIState newState, List<ICharacter> targets = null)
		{
			if (newState == null)
			{
				return;
			}

			if (CurrentState != null)
			{
				CurrentState?.Exit(this);
			}

			CurrentState = newState;
			if (CurrentState != null)
			{
				nextUpdate = CurrentState.UpdateRate;

				Agent.height = newState.Height;
				Agent.radius = newState.Radius;
			}

			if (targets != null && newState is BaseAttackingState attackingState)
			{
				attackingState.PickTarget(this, targets);
			}
			CurrentState?.Enter(this);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void TransitionToDefaultState()
		{
			ChangeState(IdleState, null);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public virtual void TransitionToRandomMovementState()
		{
			if (MovementStates == null ||
				MovementStates.Count < 1)
			{
				return;
			}

			BaseAIState randomState = MovementStates.GetRandom();
			if (randomState != null)
			{
				ChangeState(randomState);
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void SetRandomHomeDestination(float radius = 0.0f)
		{
			Vector3 randomDirection = Home.position;
			if (radius > 0.0f)
			{
				randomDirection += Vector3Extensions.RandomOnUnitSphere() * radius;
			}
			NavMeshHit hit;
			if (NavMesh.SamplePosition(randomDirection, out hit, radius, NavMesh.AllAreas))
			{
				Agent.SetDestination(hit.position);
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void SetRandomDestination(float radius = 0.0f)
		{
			Vector3 randomDirection = Transform.position;
			if (radius > 0.0f)
			{
				randomDirection += Vector3Extensions.RandomOnUnitSphere() * radius;
			}
			NavMeshHit hit;
			if (NavMesh.SamplePosition(randomDirection, out hit, radius, NavMesh.AllAreas))
			{
				Agent.SetDestination(hit.position);
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void TransitionToNextWaypoint()
		{
			CurrentWaypointIndex = (CurrentWaypointIndex + 1) % Waypoints.Length;
			Agent.SetDestination(Waypoints[CurrentWaypointIndex]);
		}

		public void PickNearestWaypoint()
		{
			if (Waypoints != null &&
				Waypoints.Length > 0)
			{
				float lastSqrDistance = 0.0f;
				int closestIndex = -1;
				// Find the nearest waypoint
				for (int i = 0; i < Waypoints.Length; ++i)
				{
					Vector3 waypoint = Waypoints[i];

					float sqrDistance = (Transform.position - waypoint).sqrMagnitude;
					if (closestIndex < 0 ||
						sqrDistance < lastSqrDistance)
					{
						lastSqrDistance = sqrDistance;
						closestIndex = i;
					}
				}
				Agent.SetDestination(Waypoints[closestIndex]);
				CurrentWaypointIndex = 0;
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void DrawDebugCircle(Vector3 position, float radius, Color color)
		{
			Gizmos.color = color;
			Gizmos.DrawWireSphere(position, radius);
		}
	}
}