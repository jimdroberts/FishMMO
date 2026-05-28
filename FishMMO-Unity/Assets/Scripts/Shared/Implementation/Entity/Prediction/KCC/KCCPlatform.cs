using System.Collections.Generic;
using FishNet.Object.Prediction;
using FishNet.Component.Prediction;
using FishNet.Connection;
using FishNet.Serializing;
using FishNet.Transporting;
using FishNet.Utility.Template;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Predicted moving platform that uses FishNet Prediction V2 for deterministic movement.
	/// Players standing on this platform receive platform velocity through <see cref="KCCPlayer"/>.
	/// </summary>
	public class KCCPlatform : TickNetworkBehaviour, ISceneObject
	{
		#region Types.
		/// <summary>
		/// Replicate data for the platform. Autonomous movement requires no client input.
		/// </summary>
		public struct ReplicateData : IReplicateData
		{
			/// <summary>
			/// Creates default replicate data. Platform movement is autonomous and requires no input fields.
			/// </summary>
			public ReplicateData(uint unused = 0)
			{
				tick = 0;
			}

			private uint tick;

			/// <inheritdoc/>
			public void Dispose() { }

			/// <inheritdoc/>
			public uint GetTick() => tick;

			/// <inheritdoc/>
			public void SetTick(uint value) => tick = value;
		}

		/// <summary>
		/// Reconcile data for the platform. Contains all state read during <see cref="PerformReplicate"/>
		/// to ensure deterministic replay after reconciliation.
		/// </summary>
		public struct ReconcileData : IReconcileData
		{
			/// <summary>
			/// Creates reconcile data capturing the platform's full simulation state.
			/// </summary>
			/// <param name="position">Current world position of the platform.</param>
			/// <param name="goalIndex">Index of the current movement goal.</param>
			public ReconcileData(Vector3 position, byte goalIndex)
			{
				Position = position;
				GoalIndex = goalIndex;
				tick = 0;
			}

			/// <summary>
			/// World position of the platform at this tick.
			/// </summary>
			public Vector3 Position;

			/// <summary>
			/// Index into the goals list indicating which waypoint the platform is moving toward.
			/// </summary>
			public byte GoalIndex;

			private uint tick;

			/// <inheritdoc/>
			public void Dispose() { }

			/// <inheritdoc/>
			public uint GetTick() => tick;

			/// <inheritdoc/>
			public void SetTick(uint value) => tick = value;
		}
		#endregion

		/// <summary>
		/// Movement speed of the platform in units per second.
		/// </summary>
		[SerializeField]
		private float moveRate = 4f;

		/// <summary>
		/// Index of the current goal the platform is moving toward.
		/// </summary>
		private byte goalIndex;

		/// <summary>
		/// Local-space offsets from the platform's initial position.
		/// Converted to world-space goals in Awake.
		/// </summary>
		[SerializeField]
		private List<Vector3> goalOffsets = new()
		{
			new Vector3(0f, 0f, 5f),
			new Vector3(0f, 0f, -5f),
		};

		/// <summary>
		/// Ordered list of world-space waypoints the platform cycles through.
		/// </summary>
		private List<Vector3> goals = new();

		/// <summary>
		/// Velocity of the platform during its most recently completed <see cref="PerformReplicate"/>
		/// tick, in world units per second. Players riding the platform read this value so the
		/// inherited platform velocity is independent of whether the player's NetworkBehaviour
		/// ticks before or after this platform within the same network tick (FishNet does not
		/// guarantee a deterministic OnTick order across separate NetworkObjects). Lag is at most
		/// one tick (acceptable for moving-platform inheritance) but the value is consistent
		/// between server and client because it is computed inside the deterministic [Replicate].
		/// </summary>
		public Vector3 LastCompletedTickVelocity { get; private set; }

		/// <summary>
		/// Network collision component used to detect player entry and exit on the platform.
		/// </summary>
		[SerializeField]
		private NetworkCollision platformCollider;

		/// <inheritdoc/>
		public long ID { get; set; }

		/// <inheritdoc/>
		public GameObject GameObject { get; private set; }

		private void Awake()
		{
			GameObject = gameObject;

			Vector3 position = transform.position;
			goals.Clear();
			for (int i = 0; i < goalOffsets.Count; i++)
			{
				goals.Add(position + goalOffsets[i]);
			}

			if (platformCollider == null)
			{
				platformCollider = GetComponent<NetworkCollision>();
			}
			if (platformCollider != null)
			{
				platformCollider.OnEnter += PlatformCollider_OnEnter;
				platformCollider.OnExit += PlatformCollider_OnExit;
			}

#if UNITY_SERVER
			SceneObject.Register(this);
#endif
		}

		private void OnDestroy()
		{
			if (platformCollider != null)
			{
				platformCollider.OnEnter -= PlatformCollider_OnEnter;
				platformCollider.OnExit -= PlatformCollider_OnExit;
			}
			SceneObject.Unregister(this);
		}

		/// <summary>
		/// Called when a collider enters the platform trigger. Sets this platform on the player.
		/// </summary>
		private void PlatformCollider_OnEnter(Collider other)
		{
			if (other.TryGetComponent(out KCCPlayer player))
			{
				player.SetPlatform(this);
			}
		}

		/// <summary>
		/// Called when a collider exits the platform trigger. Clears the platform from the player.
		/// </summary>
		private void PlatformCollider_OnExit(Collider other)
		{
			if (other.TryGetComponent(out KCCPlayer player))
			{
				player.SetPlatform(null);
			}
		}

		/// <inheritdoc/>
		public override void OnStartNetwork()
		{
			SetTickCallbacks(TickCallback.Tick);
		}

		/// <inheritdoc/>
		public override void ReadPayload(NetworkConnection connection, Reader reader)
		{
			ID = reader.ReadInt64();
			SceneObject.Register(this, true);
		}

		/// <inheritdoc/>
		public override void WritePayload(NetworkConnection connection, Writer writer)
		{
			writer.WriteInt64(ID);
		}

		/// <inheritdoc/>
		protected override void TimeManager_OnTick()
		{
			PerformReplicate(default);
			CreateReconcile();
		}

		/// <inheritdoc/>
		public override void CreateReconcile()
		{
			ReconcileData rd = new(transform.position, goalIndex);
			PerformReconcile(rd);
		}

		/// <summary>
		/// Advances the platform toward its current goal by one tick step.
		/// </summary>
		[Replicate]
		private void PerformReplicate(ReplicateData rd, ReplicateState state = ReplicateState.Invalid, Channel channel = Channel.Unreliable)
		{
			float delta = (float)TimeManager.TickDelta;

			Vector3 from = transform.position;
			Vector3 goal = goals[goalIndex];
			Vector3 next = Vector3.MoveTowards(from, goal, delta * moveRate);

			transform.position = next;

			// Capture the velocity this tick produced. Players read this in their own
			// [Replicate] so they inherit a consistent velocity regardless of cross-
			// NetworkObject tick ordering. delta is guarded against zero in pathological
			// configurations (tick rate misconfiguration).
			LastCompletedTickVelocity = delta > 0f ? (next - from) / delta : Vector3.zero;

			float sqrDistance = (next - goal).sqrMagnitude;
			if (sqrDistance < 0.0001f)
			{
				transform.position = goal;
				goalIndex++;
				if (goalIndex >= goals.Count)
				{
					goalIndex = 0;
				}
			}
		}

		/// <summary>
		/// Restores the platform to the authoritative state for reconcile replay.
		/// </summary>
		[Reconcile]
		private void PerformReconcile(ReconcileData rd, Channel channel = Channel.Unreliable)
		{
			transform.position = rd.Position;
			goalIndex = rd.GoalIndex;
			// LastCompletedTickVelocity is intentionally not reconciled — it will be refreshed
			// on the next PerformReplicate (which runs once per replayed tick).
		}

		/// <inheritdoc/>
		public override void ResetState(bool asServer)
		{
			base.ResetState(asServer);
			// Clear cached per-tick state so a despawn/respawn cycle does not leak velocity
			// from the previous spawn into the next one (which could otherwise launch the
			// first rider that steps on the freshly-respawned platform).
			LastCompletedTickVelocity = Vector3.zero;
			goalIndex = 0;
		}
	}
}