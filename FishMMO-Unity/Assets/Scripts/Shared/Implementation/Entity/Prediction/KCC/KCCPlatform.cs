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
		/// Velocity of the platform during its most recently completed <see cref="Step"/>, in world
		/// units per second. Players riding the platform read this value so the inherited platform
		/// velocity is independent of whether the player's NetworkBehaviour ticks before or after
		/// this platform within the same network tick (FishNet does not guarantee a deterministic
		/// OnTick order across separate NetworkObjects). Lag is at most one tick (acceptable for
		/// moving-platform inheritance) but the value is consistent between server and client
		/// because both compute it from the same deterministic <see cref="Step"/> — the server from
		/// inside its <c>[Replicate]</c>, a client directly from its tick.
		/// </summary>
		public Vector3 LastCompletedTickVelocity { get; private set; }

		/// <summary>Ticks of platform velocity kept for riders replaying a reconcile.</summary>
		private const int VelocityHistoryLength = 64;

		/// <summary>Per-tick velocity ring, indexed by <c>tick % VelocityHistoryLength</c>.</summary>
		private readonly Vector3[] velocityHistory = new Vector3[VelocityHistoryLength];

		/// <summary>The tick each <see cref="velocityHistory"/> slot holds, to detect a stale slot.</summary>
		private readonly uint[] velocityHistoryTicks = new uint[VelocityHistoryLength];

		/// <summary>
		/// The velocity this platform produced on a specific tick.
		/// </summary>
		/// <remarks>
		/// A rider replaying k ticks after a reconcile needs the velocity of each replayed tick, not
		/// the platform's present one. The platform itself never replays — it has no owner and no
		/// reconcile reaches a client — so without this every replayed tick inherited one frozen
		/// value and the rider's replayed path bent at each direction reversal.
		/// </remarks>
		/// <param name="tick">The tick to look up.</param>
		/// <param name="velocity">The velocity produced on that tick, when still held.</param>
		/// <returns>True when the tick is still in the ring.</returns>
		public bool TryGetVelocityForTick(uint tick, out Vector3 velocity)
		{
			int slot = (int)(tick % VelocityHistoryLength);
			if (velocityHistoryTicks[slot] == tick)
			{
				velocity = velocityHistory[slot];
				return true;
			}
			velocity = Vector3.zero;
			return false;
		}

		/// <summary>Records the velocity produced on <paramref name="tick"/>.</summary>
		private void RecordTickVelocity(uint tick, Vector3 velocity)
		{
			int slot = (int)(tick % VelocityHistoryLength);
			velocityHistoryTicks[slot] = tick;
			velocityHistory[slot] = velocity;
		}

		/// <summary>
		/// Network collision component used to detect player entry and exit on the platform.
		/// </summary>
		[SerializeField]
		private NetworkCollision platformCollider;

		/// <inheritdoc/>
		public long ID { get; set; }

		/// <inheritdoc/>
		public GameObject GameObject { get; private set; }

		/// <summary>
		/// Initializes world-space goals from local offsets, subscribes to platform collider events, and registers with SceneObject.
		/// </summary>
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

		/// <summary>
		/// Unsubscribes from platform collider events and unregisters from SceneObject.
		/// </summary>
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
		/// <remarks>
		/// <para>
		/// Position and goal index are the platform's whole simulation state, and they are carried
		/// here rather than by a reconcile because state forwarding is off on every object in this
		/// project. With forwarding off a scene object has no owner to send a reconcile to
		/// (<c>Server_SendReconcileRpc</c> returns immediately when <c>!Owner.IsValid</c>), so a
		/// client that arrives mid-cycle would otherwise start the platform from its authored
		/// position and run a full lap out of phase with the server, for the lifetime of the scene.
		/// </para>
		/// <para>
		/// Per-tick movement needs no wire at all: <see cref="Step"/> is autonomous and deterministic
		/// — <c>MoveTowards</c> by a fixed <c>TimeManager.TickDelta</c> step — and it snaps exactly
		/// onto each waypoint on arrival, so float drift is bounded within one leg and reset at every
		/// corner. Both peers run it: the server from its <c>[Replicate]</c>, a client directly from
		/// <c>TimeManager_OnTick</c>, because FishNet will not invoke an ownerless, non-forwarded
		/// replicate body on a client (which is why the platform stood still on every client until
		/// the 2026-08-28 audit). Fourteen bytes once per observer, instead of a reconcile every tick
		/// to every observer.
		/// </para>
		/// <para>
		/// <c>goals</c> is built in <c>Awake</c> from the authored scene position, which is identical
		/// on every peer, and <c>Awake</c> runs before this — so assigning the live position here
		/// cannot disturb the waypoints it was derived from.
		/// </para>
		/// </remarks>
		public override void ReadPayload(NetworkConnection connection, Reader reader)
		{
			ID = reader.ReadInt64();
			transform.position = reader.ReadVector3();

			byte readGoalIndex = reader.ReadUInt8Unpacked();
			/* A goal index the local goal list cannot address means the scene asset and the server
			 * disagree about this platform's route. Restarting the cycle is wrong by at most one
			 * leg; indexing past the end throws inside the replicate on the very next tick. */
			goalIndex = readGoalIndex < goals.Count ? readGoalIndex : (byte)0;

			SceneObject.Register(this, true);
		}

		/// <inheritdoc/>
		public override void WritePayload(NetworkConnection connection, Writer writer)
		{
			writer.WriteInt64(ID);
			writer.WriteVector3(transform.position);
			writer.WriteUInt8Unpacked(goalIndex);
		}

		/// <inheritdoc/>
		/// <remarks>
		/// The server drives the platform through the predicted <see cref="PerformReplicate"/>.
		/// A client cannot: with state forwarding off and no owner, FishNet's
		/// <c>Replicate_NonAuthoritative</c> returns before it invokes the replicate body, no
		/// reconcile is ever sent (see <see cref="ReadPayload"/>), and this object carries no
		/// NetworkTransform — so left to the replicate the platform never moved on any client
		/// while it moved on the server, and riders diverged every tick. The client runs the
		/// same deterministic step directly, from the state the spawn payload handed it.
		/// </remarks>
		protected override void TimeManager_OnTick()
		{
			if (base.IsServerStarted)
			{
				PerformReplicate(default);
				CreateReconcile();
			}
			else
			{
				Step((float)TimeManager.TickDelta);
			}

			// Keyed by the tick that just ran, so a rider replaying it can ask for the same value.
			if (TimeManager != null)
			{
				RecordTickVelocity(TimeManager.LocalTick, LastCompletedTickVelocity);
			}
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
			Step((float)TimeManager.TickDelta);
		}

		/// <summary>
		/// One deterministic tick of platform movement: <c>MoveTowards</c> the current goal by
		/// <paramref name="delta"/> × <c>moveRate</c>, snap onto the goal on arrival and advance
		/// the goal index. Shared by the server's replicate and the client's direct tick.
		/// </summary>
		/// <param name="delta">Fixed tick step in seconds.</param>
		internal void Step(float delta)
		{
			if (goals.Count == 0)
			{
				return;
			}

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
			/* LastCompletedTickVelocity is intentionally not reconciled — the next Step refreshes it.
			 * Note this reconcile only runs on the server: with no owner and forwarding off nothing
			 * is sent, and no client ever replays this object. */
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