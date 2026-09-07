using System.Collections.Generic;
using FishNet.Object.Prediction;
using FishNet.Component.Prediction;
using FishNet.Connection;
using FishNet.Serializing;
using FishNet.Transporting;
using FishNet.Utility.Template;
using FishMMO.Logging;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Predicted moving platform on FishNet's own model: every peer runs the same autonomous,
	/// deterministic replicate, the server reconciles it to every observer, and FishNet rolls it
	/// back and replays it with everything else. Riders inherit its per-tick velocity through
	/// <see cref="KCCPlayer"/>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A fork of FishNet's <c>Demos/Prediction/CharacterController/MovingPlatform</c>, and since
	/// issue #228 deliberately back ON that model. This platform's <c>NetworkObject</c> is the one
	/// kind of scene object in the project with state forwarding ENABLED (pinned by
	/// <c>InterestManagementWiringTests.OnlyPlatforms_ShipWithStateForwardingOn</c>), because the
	/// argument that keeps forwarding off everywhere else does not apply to it: forwarding a
	/// character relays every owner's INPUT to every observer, which is what makes 100-200 players
	/// unaffordable — an ownerless platform has no input. Its replicate is an empty struct, and the
	/// only wire cost is one delta-encoded reconcile (Vector3 + byte) per observer per tick.
	/// </para>
	/// <para>
	/// What that buys, in the demo's own words: with the body run in EVERY replicate state,
	/// <c>IsFuture</c> included, a client's platform runs AHEAD of the server by that client's ping —
	/// where the platform will be by the time the client's input reaches the server — and every
	/// reconcile rolls the platform back to the server's state and replays it in lockstep with the
	/// rider, so a replayed ground probe meets the deck where it actually stood on that tick.
	/// </para>
	/// <para>
	/// The previous model — forwarding off, each client stepping the platform itself from a
	/// one-shot spawn payload — had neither property. With forwarding off FishNet returns from
	/// <c>Replicate_NonAuthoritative</c> before invoking the body and sends no reconcile for an
	/// ownerless object, so the client copy was seeded from the payload and dead-reckoned open-loop
	/// for the rest of the session: seeded BEHIND the server (aligned to the interpolated observer
	/// frame ability objects use, the opposite of what a deck the owner stands on needs), and never
	/// rolled back, so a reconcile replay probed a deck up to a round trip downstream of where the
	/// rider stood. Riders sank through moving decks, worst at high ping, worst near the edges.
	/// </para>
	/// </remarks>
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
				Sequence = 0;
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

			/// <summary>
			/// Server-side send counter, stamped by <c>Server_SendReconcileRpc</c> through
			/// <c>ReconcileSequenceStamper</c> on every reconcile actually written, wrapping at 255.
			/// </summary>
			/// <remarks>
			/// The delta chain's loss detector, the same one <c>CharacterReconcileData.Sequence</c>
			/// carries and for the same reason: reconciles ride the unreliable state datagram and
			/// each delta is difference-encoded against the previous state the server SENT, so a
			/// lost datagram would otherwise have every later delta decode against a baseline this
			/// client never received — a deck standing in the wrong place, and every rider's
			/// footing with it, for up to a second until the periodic absolute snapshot. The reader
			/// requires <c>prev.Sequence + 1</c> and rejects the packet otherwise; a loss then costs
			/// "no correction until the next snapshot", which for a deterministic platform is no
			/// visible cost at all. This matters MORE here than on a character: the platform is the
			/// one object whose reconcile fans out to every observer.
			/// </remarks>
			public byte Sequence;

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
		/// units per second. Refreshed by every run of the replicate body — the live tick on both
		/// peers, and each replayed tick on a client — so it is the velocity of whatever tick the
		/// platform last simulated.
		/// </summary>
		public Vector3 LastCompletedTickVelocity { get; private set; }

		/// <summary>Ticks of platform velocity kept for riders replaying a reconcile.</summary>
		private const int VelocityHistoryLength = 64;

		/// <summary>Per-tick velocity ring, indexed by <c>tick % VelocityHistoryLength</c>.</summary>
		private readonly Vector3[] velocityHistory = new Vector3[VelocityHistoryLength];

		/// <summary>The tick each <see cref="velocityHistory"/> slot holds, to detect a stale slot.</summary>
		private readonly uint[] velocityHistoryTicks = new uint[VelocityHistoryLength];

		/// <summary>
		/// The velocity this platform produced on a specific tick, in the CLIENT tick domain.
		/// </summary>
		/// <remarks>
		/// <para>
		/// FishNet promises no tick order across NetworkObjects, so a rider simulating tick T cannot
		/// know whether this platform has already stepped T or is still on T-1 when it asks for
		/// <see cref="LastCompletedTickVelocity"/>. The ring answers by tick instead, and is filled
		/// wherever the platform runs a tick on a client: from <see cref="TimeManager_OnTick"/> keyed
		/// by <c>LocalTick</c> for the live tick, and from the replayed replicate keyed by
		/// <c>PredictionManager.ClientReplayTick</c> — both the same counter a rider's input carries.
		/// </para>
		/// <para>
		/// The server fills it too, keyed by ITS local tick, but must not consult it with a rider's
		/// input tick: that tick is the owning client's unsynchronised counter, so the lookup is an
		/// arbitrary hit or a miss. The server never replays, and reads the live value instead.
		/// </para>
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

				/* The rider-detection volume MUST see characters, whatever the scene authored.
				 * Characters live on the Player layer (BaseCharacter.Awake moves them there), and
				 * a NetworkCollision whose Layers omits that bit polls forever and detects nobody
				 * — SetPlatform never fires, SetPlatformVelocity stays zero, and a rider stands
				 * still while the deck slides out from under them (reported live 2026-09-01: the
				 * shipped platform's volume was authored Default-only). Forced here rather than
				 * left to scene data, because the failure is silent and the requirement is
				 * intrinsic to being a platform. */
				if (Constants.Layers.Index.Player >= 0)
				{
					LayerMask required = 1 << Constants.Layers.Index.Player;
					if ((platformCollider.QueryLayers & required) != required)
					{
						Log.Debug("KCCPlatform",
							$"'{name}' rider volume layers 0x{(int)platformCollider.QueryLayers:X} " +
							"did not include the Player layer; correcting.");
						platformCollider.QueryLayers |= required;
					}
				}
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
				Log.Debug("KCCPlatform", $"'{name}' picked up rider '{other.name}'.");
				return;
			}

			/* Something entered the rider volume that is not a rider. Worth saying: a character
			 * whose collider sits on a child object resolves no KCCPlayer here, and the failure is
			 * completely silent -- the deck slides away and the rider stands still, which is the
			 * same symptom as the volume not seeing the Player layer at all. */
			Log.Debug("KCCPlatform",
				$"'{name}' rider volume entered by '{other.name}' on layer {other.gameObject.layer}, " +
				"which carries no KCCPlayer.");
		}

		/// <summary>
		/// Called when a collider exits the platform trigger. Clears the platform from the player.
		/// </summary>
		private void PlatformCollider_OnExit(Collider other)
		{
			if (other.TryGetComponent(out KCCPlayer player))
			{
				player.SetPlatform(null);
				Log.Debug("KCCPlatform", $"'{name}' dropped rider '{other.name}'.");
			}
		}

		/// <inheritdoc/>
		public override void OnStartNetwork()
		{
			SetTickCallbacks(TickCallback.Tick);
		}

		/// <inheritdoc/>
		/// <remarks>
		/// A SEED, not a sync. A client arriving mid-cycle would otherwise draw the platform at its
		/// authored pose for the tick or two before its first reconcile lands; the reconcile is what
		/// places it, and every reconcile after that keeps it placed. The catch-up that used to
		/// live here (fast-forwarding the snapshot by its transit) is gone with the reason it
		/// existed — the payload is no longer the only word the client ever hears about this
		/// platform.
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
		/// Identical on every peer, exactly as the FishNet demo has it. The server's call is
		/// authoritative and drives the reconcile; a client's routes through
		/// <c>Replicate_NonAuthoritative</c>, which — because this object forwards state — runs the
		/// body with the server's queued data when it has some and with default, <c>IsFuture</c>
		/// data when it does not. The body moves in either case, which is what puts the client's
		/// platform ahead of the server by its ping rather than behind it.
		/// </remarks>
		protected override void TimeManager_OnTick()
		{
			PerformReplicate(default);
			CreateReconcile();

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

			/* A replayed tick refreshes the ring under the tick being replayed, in the client's
			 * own counter — the value a rider replaying the same tick will ask for. The live tick
			 * is recorded by TimeManager_OnTick instead, because the body may run more than once
			 * (or not at all) inside one PerformReplicate call on a client. NetworkObject first: the
			 * PredictionManager accessor throws on a component that was never spawned. */
			if (state.ContainsReplayed() && base.NetworkObject != null && base.PredictionManager != null)
			{
				RecordTickVelocity(base.PredictionManager.ClientReplayTick, LastCompletedTickVelocity);
			}
		}

		/// <summary>
		/// One deterministic tick of platform movement: <c>MoveTowards</c> the current goal by
		/// <paramref name="delta"/> × <c>moveRate</c>, snap onto the goal on arrival and advance
		/// the goal index. Pure in (position, goalIndex, delta), which is what lets a reconcile
		/// replay reproduce the server's walk exactly, corners included.
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
		/// <remarks>
		/// Runs on every observing client, every tick, because the object forwards state: this is
		/// the rollback that stands the deck where the server had it before the rider's replay
		/// probes it. <c>LastCompletedTickVelocity</c> is intentionally not reconciled — the first
		/// replayed Step refreshes it.
		/// </remarks>
		[Reconcile]
		private void PerformReconcile(ReconcileData rd, Channel channel = Channel.Unreliable)
		{
			transform.position = rd.Position;
			goalIndex = rd.GoalIndex;
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
