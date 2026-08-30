using FishNet.Object;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Server-side ring buffer of where a character's collider was on each recent tick.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why this exists.</b> Hits resolve on the server against current positions, while a client
	/// aims at peers it renders behind the server by its interpolation buffer plus its own latency.
	/// The gap is the peer's speed times that staleness — measured at 0.64&#160;m for a 40&#160;ms
	/// connection and 2.2&#160;m at 300&#160;ms — against ability hitboxes authored at half a metre.
	/// No amount of tuning closes that. The server has to be able to ask "where was this character
	/// when the shooter saw it", and this is what lets it.
	/// </para>
	/// <para>
	/// <b>Server only, by construction.</b> Recording starts in <see cref="OnStartServer"/> and the
	/// tick subscription is never taken on a client. Rewinding is a server-side query against
	/// authoritative history; it is deliberately not part of the deterministic simulation, so it
	/// cannot influence what clients predict and cannot desynchronise them.
	/// </para>
	/// <para>
	/// <b>Cost.</b> One <see cref="Snapshot"/> per tick per character, in a pre-allocated ring — no
	/// per-tick allocation. At the default window that is a fixed 28&#160;bytes times the buffer
	/// length per character, held for the character's lifetime.
	/// </para>
	/// </remarks>
	public class CharacterPositionHistory : NetworkBehaviour
	{
		/// <summary>One recorded tick.</summary>
		public readonly struct Snapshot
		{
			public readonly uint Tick;
			public readonly Vector3 Position;
			public readonly Quaternion Rotation;

			public Snapshot(uint tick, Vector3 position, Quaternion rotation)
			{
				Tick = tick;
				Position = position;
				Rotation = rotation;
			}
		}

		/// <summary>
		/// How far back the server is willing to rewind, in milliseconds.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>This is the security parameter.</b> It sets the memory cost and, on its own, the
		/// cheating ceiling: no claim can shoot further into the past than the recording reaches, so
		/// an inflated latency report buys an attacker exactly this much rewind and no more. A claim
		/// beyond it is clamped to it rather than refused — see <see cref="TryResolve(uint, out Snapshot)"/>
		/// for why refusing never actually lowered this ceiling.
		/// </para>
		/// <para>
		/// The number to weigh is "how long can a victim have been behind cover and still be shot".
		/// 500&#160;ms at the shipped tick rate is 15 samples, 420&#160;bytes per character, and
		/// covers a round trip of roughly 370&#160;ms plus the interpolation buffer. Players past that
		/// are compensated as far as the recording reaches rather than being cut off.
		/// </para>
		/// </remarks>
		[Tooltip("Maximum rewind window in milliseconds. Also bounds how far a client can claim to have seen into the past.")]
		[Range(100f, 1000f)]
		[SerializeField]
		private float maximumRewindMilliseconds = 500f;

		/// <summary>Maximum rewind window, in milliseconds.</summary>
		public float MaximumRewindMilliseconds => maximumRewindMilliseconds;

		private Snapshot[] buffer;
		private int writeIndex;
		private int count;

		/// <summary>Position and rotation to restore to after a rewind.</summary>
		private Vector3 restorePosition;
		private Quaternion restoreRotation;
		private bool isRewound;

		/// <summary>Scene handle this history is registered under. See <see cref="EnsureSceneRegistration"/>.</summary>
		private int registeredSceneHandle;

		/// <summary>True while this character's collider is displaced by a rewind.</summary>
		public bool IsRewound => isRewound;

		public override void OnStartServer()
		{
			base.OnStartServer();

			double tickDelta = base.TimeManager != null ? base.TimeManager.TickDelta : 1.0 / 30.0;
			AllocateBuffer(Mathf.CeilToInt(maximumRewindMilliseconds / 1000f / (float)tickDelta));

			registeredSceneHandle = gameObject.scene.handle;
			LagCompensationRegistry.Register(this);

			if (base.TimeManager != null)
			{
				base.TimeManager.OnPostTick += RecordTick;
			}
		}

		public override void OnStopServer()
		{
			if (base.TimeManager != null)
			{
				base.TimeManager.OnPostTick -= RecordTick;
			}

			LagCompensationRegistry.Unregister(this);

			buffer = null;
			count = 0;
			writeIndex = 0;
			isRewound = false;

			base.OnStopServer();
		}

		/// <summary>
		/// Records the character's post-simulation transform for this tick.
		/// </summary>
		/// <remarks>
		/// Subscribed to <c>OnPostTick</c> rather than <c>OnTick</c> on purpose: the replicate pass
		/// runs during <c>OnTick</c> and is what moves the character, so recording there would store
		/// the position the character held <i>before</i> this tick's movement and shift the whole
		/// history one tick into the past.
		/// </remarks>
		private void RecordTick()
		{
			// Never record while displaced — that would persist a rewound position as if it were
			// real, and every later rewind would compound the error. Checked first because it is
			// the cheapest guard and the only one evaluable without a TimeManager.
			if (isRewound)
			{
				return;
			}
			if (buffer == null || base.TimeManager == null)
			{
				return;
			}

			EnsureSceneRegistration();

			Transform t = transform;
			/* Keyed through the shared helper, not base.TimeManager.LocalTick directly. The rewind
			 * side measures its target back from the same call, so the two cannot be edited into
			 * different tick domains — the failure that made every rewind decline silently. */
			Record(LagCompensationTick.ServerTickDomain(base.TimeManager), t.position, t.rotation);
		}

		/// <summary>
		/// Re-registers this history when the GameObject has moved to a different Unity scene
		/// since it was registered.
		/// </summary>
		/// <remarks>
		/// The registry buckets by scene handle at registration time because each scene runs its
		/// own PhysicsScene. Every current spawn flow places a character in its final scene before
		/// <c>OnStartServer</c> runs — pets are moved before <c>ServerManager.Spawn</c>, players
		/// load into their scene first, and cross-scene teleports despawn and respawn — but that
		/// is a property of today's call graph, not of the data structure. A future path that
		/// moves a live spawned character between scenes would leave it rewindable in the wrong
		/// scene's queries and invisible to the right one's. One int compare per tick makes the
		/// bucket unconditionally correct instead of correct by convention.
		/// </remarks>
		internal void EnsureSceneRegistration()
		{
			int handle = gameObject.scene.handle;
			if (handle == registeredSceneHandle)
			{
				return;
			}

			// Unregister scans every bucket, so this also heals a registration made under a
			// handle the object no longer occupies.
			LagCompensationRegistry.Unregister(this);
			registeredSceneHandle = handle;
			LagCompensationRegistry.Register(this);
		}

		/// <summary>Writes one snapshot into the ring.</summary>
		/// <remarks>
		/// Split out from <see cref="RecordTick"/> so the ring's wrap and eviction behaviour can be
		/// exercised without standing up a TimeManager, which is what the history tests do.
		/// </remarks>
		private void Record(uint tick, Vector3 position, Quaternion rotation)
		{
			buffer[writeIndex] = new Snapshot(tick, position, rotation);
			writeIndex = (writeIndex + 1) % buffer.Length;
			if (count < buffer.Length)
			{
				count++;
			}
		}

		/// <summary>Allocates the ring directly. Used by the recording path and by tests.</summary>
		private void AllocateBuffer(int ticks)
		{
			buffer = new Snapshot[Mathf.Max(2, ticks)];
			writeIndex = 0;
			count = 0;
			isRewound = false;
		}

		/// <summary>
		/// Resolves where this character was at <paramref name="tick"/>, interpolating between the
		/// two recorded ticks that bracket it.
		/// </summary>
		/// <param name="tick">Tick to resolve, in the server's local tick domain.</param>
		/// <param name="snapshot">The resolved transform.</param>
		/// <returns>True when history covers the requested tick.</returns>
		/// <summary>
		/// Resolves the recorded pose at a fractional point in the past.
		/// </summary>
		/// <remarks>
		/// The whole-tick overload below is what actually reads the ring; this blends the two samples
		/// the target sits between. A client's view of a peer comes from interpolation and lands
		/// between ticks, so resolving only on tick boundaries reproduced a pose it never rendered.
		/// With a zero fraction the bounds collapse and this is exactly the whole-tick result.
		/// </remarks>
		/// <param name="target">The point in the past to resolve.</param>
		/// <param name="snapshot">The pose at that point.</param>
		/// <returns>True when both bounding ticks are still held.</returns>
		public bool TryResolve(RewindTarget target, out Snapshot snapshot)
		{
			snapshot = default;
			if (!target.IsValid)
			{
				return false;
			}

			target.GetBounds(out uint olderTick, out uint newerTick, out float alpha);
			if (olderTick == newerTick)
			{
				return TryResolve(newerTick, out snapshot);
			}

			/* Both bounds must be held. Falling back to the newer one alone would silently return a
			 * pose up to a full tick ahead of the target, which is the error this overload exists to
			 * remove. */
			if (!TryResolve(olderTick, out Snapshot older) ||
				!TryResolve(newerTick, out Snapshot newer))
			{
				return false;
			}

			snapshot = new Snapshot(
				newerTick,
				Vector3.Lerp(older.Position, newer.Position, alpha),
				Quaternion.Slerp(older.Rotation, newer.Rotation, alpha));
			return true;
		}

		public bool TryResolve(uint tick, out Snapshot snapshot)
		{
			snapshot = default;
			if (buffer == null || count == 0)
			{
				return false;
			}

			Snapshot newest = buffer[(writeIndex - 1 + buffer.Length) % buffer.Length];
			Snapshot oldest = buffer[(writeIndex - count + buffer.Length) % buffer.Length];

			// Asking for the present or the future resolves to the present; there is nothing to rewind to.
			if (tick >= newest.Tick)
			{
				snapshot = newest;
				return true;
			}

			if (tick < oldest.Tick)
			{
				/* A LITTLE older than the window clamps to the oldest sample; wildly older is still
				 * refused. The two cases are not the same question.
				 *
				 * Clamping the near case, because refusing it was a cliff and not a defence. The
				 * ceiling on how far into the past anybody can shoot is the RECORDING, which is
				 * bounded by maximumRewindMilliseconds either way; an attacker reaches that ceiling
				 * by claiming a value just INSIDE the window, which refusal always accepted.
				 * Refusing only rejected claims that overshot, and an overshooting claim buys
				 * strictly less than one sitting at the edge — so it deterred nobody and penalised
				 * exactly one population: honest clients whose real latency exceeds the window. With
				 * the view offset corrected to a full round trip that population starts at roughly
				 * 370 ms, and it got full compensation at 360 ms and none at all at 380 ms.
				 *
				 * Refusing the far case, because it is not a latency claim at all. The only thing
				 * that produces a tick thousands out is a TICK DOMAIN error — a target built from
				 * the owning client's replicate counter rather than the server's, which is the bug
				 * LagCompensationTick exists to prevent and which History_AndCompensationAnchor_
				 * ShareOneTickDomain pins. Clamping that would hand back a real-looking pose for a
				 * tick nobody recorded and turn a dead subsystem into a silently WRONG one. The two
				 * are separated by orders of magnitude, and the bound is measured from the PRESENT
				 * rather than from the oldest sample so it does not move with the window length.
				 * LagCompensationTick caps a claim at MaximumCompensationTicks and adds a queue depth
				 * of a couple of ticks, so the deepest honest request sits a few tens of ticks behind
				 * the newest sample; a domain error sits the client's entire uptime behind it —
				 * hundreds of thousands. Twice the maximum claim is far above the first and four
				 * orders of magnitude below the second, so nothing here is a tuned threshold. */
				uint age = newest.Tick - tick;
				if (age > LagCompensationTick.MaximumCompensationTicks * 2u)
				{
					return false;
				}
				snapshot = oldest;
				return true;
			}

			for (int i = 1; i < count; i++)
			{
				int newerIdx = (writeIndex - i + buffer.Length) % buffer.Length;
				int olderIdx = (writeIndex - i - 1 + buffer.Length) % buffer.Length;
				Snapshot newer = buffer[newerIdx];
				Snapshot older = buffer[olderIdx];

				if (older.Tick <= tick && tick <= newer.Tick)
				{
					uint span = newer.Tick - older.Tick;
					float alpha = span == 0 ? 0f : (tick - older.Tick) / (float)span;
					snapshot = new Snapshot(
						tick,
						Vector3.Lerp(older.Position, newer.Position, alpha),
						Quaternion.Slerp(older.Rotation, newer.Rotation, alpha));
					return true;
				}
			}

			return false;
		}

		/// <summary>
		/// Displaces this character's transform to where it was at <paramref name="tick"/>.
		/// </summary>
		/// <returns>True when the character was moved and needs restoring.</returns>
		internal bool Rewind(RewindTarget target)
		{
			if (isRewound)
			{
				return false;
			}
			if (!TryResolve(target, out Snapshot snapshot))
			{
				return false;
			}

			Transform t = transform;
			restorePosition = t.position;
			restoreRotation = t.rotation;

			/* Already where it needs to be; skip the write and the restore bookkeeping. Rotation is
			 * compared as well as position: a character that turned on the spot moves no capsule
			 * centre, but an ability whose shape is a box or a non-upright capsule sweeps a
			 * different volume through it, so treating "did not move" as "nothing to restore" would
			 * quietly leave that one character un-rewound. */
			if ((restorePosition - snapshot.Position).sqrMagnitude < 1e-8f &&
				Quaternion.Angle(restoreRotation, snapshot.Rotation) < 0.01f)
			{
				return false;
			}

			t.SetPositionAndRotation(snapshot.Position, snapshot.Rotation);
			isRewound = true;
			return true;
		}

		/// <summary>Returns this character's transform to its live position.</summary>
		internal void Restore()
		{
			if (!isRewound)
			{
				return;
			}
			transform.SetPositionAndRotation(restorePosition, restoreRotation);
			isRewound = false;
		}

		/// <summary>Number of ticks currently held, for diagnostics and tests.</summary>
		public int RecordedTicks => count;

		/// <summary>Capacity of the ring, for diagnostics and tests.</summary>
		public int Capacity => buffer?.Length ?? 0;
	}
}
