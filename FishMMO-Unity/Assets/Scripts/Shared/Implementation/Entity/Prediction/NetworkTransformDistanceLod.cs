using System.Collections.Generic;
using FishNet.Component.Transforming;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Observing;
using FishNet.Transporting;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Rate limits a <see cref="NetworkTransform"/>'s unreliable updates <b>per observer</b> by how far
	/// that observer is from the object, so a spectator across the zone stops paying full rate for
	/// something it can barely see while a spectator standing next to it still gets every tick.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why per observer and not <see cref="NetworkTransform.SetInterval"/>.</b> The transform's
	/// own interval is one value for the whole object, changed through a buffered observers RPC, so
	/// the best it could do was follow the <i>nearest</i> observer — useless in a crowd, where
	/// somebody is always close to everything. It also fought the per-observer streaming filter:
	/// with the transform sending every Nth tick and the filter passing every Mth tick per
	/// observer, an observer only heard from the object when both gates coincided, which for
	/// coprime intervals happens once in N×M ticks. This component therefore never touches the
	/// transform's interval (it stays at 1) and instead answers, per observer and per send,
	/// through FishNet's <see cref="IObserverSendFilter"/> hook. Skipping an unreliable update is
	/// indistinguishable from packet loss to the receiver, and reliable sends (the settle after a
	/// stop, a teleport) are never filtered, so every observer still converges on the true pose.
	/// </para>
	/// <para>
	/// <b>Composition with the observer cap.</b> Characters registered with
	/// <see cref="ObserverStreamingRegistry"/> already have a per-observer filter,
	/// <see cref="ObserverStreamingEntry"/>, that rate limits everything beyond a viewer's
	/// full-rate cap. That entry reads this component and applies the <i>larger</i> of the two
	/// intervals, so the two never multiply and never starve anyone. Objects without an entry —
	/// world items, static interactables — install this component as the object's filter directly.
	/// </para>
	/// <para>
	/// <b>Owner.</b> The owner never counts. Its own character is at distance zero from itself and
	/// would have pinned every object at full rate; more to the point a server-authoritative
	/// transform with SendToOwner off does not send to its owner at all
	/// (<see cref="NetworkBehaviour.ExcludeOwnerFromUnbufferedObserversRpcs"/>).
	/// </para>
	/// <para>
	/// <b>Hysteresis.</b> Bands are evaluated on a slow timer and each observer holds its band
	/// until it clears the edge by <see cref="hysteresis"/>, so an observer hovering on a boundary
	/// sees a steady sample spacing rather than one that flips every evaluation. Changing an
	/// observer's interval is free on the wire now (it is a dictionary write, not an RPC), but an
	/// interpolator fed at an abruptly changing rate can hitch, which is what the margin protects.
	/// </para>
	/// </remarks>
	[RequireComponent(typeof(NetworkTransform))]
	public class NetworkTransformDistanceLod : NetworkBehaviour, IObserverSendFilter
	{
		/// <summary>
		/// One distance band: everything nearer than <see cref="MaximumDistance"/> and not covered by
		/// a nearer band synchronises every <see cref="Interval"/> ticks.
		/// </summary>
		[System.Serializable]
		public struct Band
		{
			[Tooltip("Observer must be within this distance for this band to apply.")]
			public float MaximumDistance;

			[Tooltip("Ticks between transform sends to an observer in this band. 1 is every tick.")]
			[Range(1, 60)]
			public byte Interval;
		}

		/// <summary>
		/// Bands in ascending distance order. The first whose <see cref="Band.MaximumDistance"/> the
		/// observer is inside wins; beyond the last band, the last band's interval is used.
		/// </summary>
		/// <remarks>
		/// Defaults are deliberately conservative — full rate out to twenty metres, which covers
		/// melee range and most of what a player actually watches, then progressively coarser. Tune
		/// against how your camera frames the world rather than against the byte counts.
		/// </remarks>
		[Header("Level of detail")]
		[Tooltip("Ascending distance bands. An observer inside a band receives that band's interval.")]
		[SerializeField]
		/* Retuned 2026-09-01 from 20/1, 40/3, 80/6 after a live report of NPCs "teleporting or
		 * rubber banding" while moving. The transform's interpolation buffers TWO ticks of
		 * received data, sized for per-tick sends — an interval of 3 starves it (updates every
		 * 100ms into a ~66ms buffer), so beyond 20m every moving NPC played out its buffer,
		 * stalled, and snapped to the next sample. Full rate now covers the range where motion
		 * quality is actually judged; interval 2 stays within what the buffer can bridge; the
		 * far band's stepping is a few pixels at 80m+. intervalScale remains the crowd lever. */
		private Band[] bands =
		{
			new Band { MaximumDistance = 40f, Interval = 1 },
			new Band { MaximumDistance = 80f, Interval = 2 },
			new Band { MaximumDistance = 140f, Interval = 4 },
		};

		/// <summary>
		/// Multiplies whichever band interval is selected. Raise it for dense scenes.
		/// </summary>
		/// <remarks>
		/// Per-observer distance already handles a crowd far better than the old nearest-observer
		/// rule did, but a capital can still have dozens of observers inside the first band of
		/// every object. Raising the scale for that scene trades some smoothness for a
		/// proportional cut across every object in it. Settable at runtime so a zone can raise it
		/// on entry.
		/// </remarks>
		[Tooltip("Multiplier applied to the selected band's interval. Raise for crowded scenes.")]
		[Range(1, 8)]
		[SerializeField]
		private int intervalScale = 1;

		/// <summary>Seconds between band evaluations.</summary>
		/// <remarks>
		/// Deliberately slow. The thing being decided — roughly how far away each observer is —
		/// does not move meaningfully within a few hundred milliseconds, and the evaluation walks
		/// every observer of every object carrying this component.
		/// </remarks>
		[Tooltip("Seconds between evaluations, rounded to whole ticks when the server starts. Low values cost CPU without improving the decision.")]
		[Range(0.1f, 5f)]
		[SerializeField]
		private float evaluateInterval = 0.5f;

		/// <summary>
		/// Fraction a band edge is extended by before an observer is allowed to leave that band.
		/// </summary>
		[Tooltip("Hysteresis on band edges, as a fraction of the edge distance.")]
		[Range(0f, 0.5f)]
		[SerializeField]
		private float hysteresis = 0.15f;

		/// <summary>Multiplier applied to the selected band. See <see cref="intervalScale"/>.</summary>
		public int IntervalScale
		{
			get => intervalScale;
			set => intervalScale = Mathf.Clamp(value, 1, 8);
		}

		/// <summary>Number of observers currently held below full rate by distance.</summary>
		public int LimitedObserverCount
		{
			get
			{
				int count = 0;
				foreach (KeyValuePair<int, ObserverLod> pair in lodByClientId)
				{
					if (pair.Value.Interval > 1)
					{
						count++;
					}
				}
				return count;
			}
		}

		/// <summary>Per-observer state: the band it is currently held in and the interval that maps to.</summary>
		private struct ObserverLod
		{
			public int Band;
			public byte Interval;
			/// <summary>Pass number this observer was last seen on, for pruning departed observers.</summary>
			public uint Pass;
			/// <summary>
			/// True while this observer is close enough to attack the object, so its transform is
			/// sent every tick and lag compensation can rewind to a real tick sample.
			/// </summary>
			public bool Engaged;
		}

		private readonly Dictionary<int, ObserverLod> lodByClientId = new Dictionary<int, ObserverLod>();
		private readonly List<int> departed = new List<int>();

		/// <summary>
		/// <see cref="evaluateInterval"/> converted to whole ticks, resolved once from
		/// <c>TimeManager.TickDelta</c> when the server starts.
		/// </summary>
		/// <remarks>
		/// The inspector field stays in seconds because that is the unit the decision is authored
		/// in, but nothing at runtime measures elapsed seconds: the conversion happens once and
		/// every comparison after it is integer tick arithmetic. Same pattern as
		/// <c>BuffController.tickDelta</c>.
		/// </remarks>
		private uint evaluateIntervalTicks = 1;

		/// <summary>Tick the next evaluation pass is due on.</summary>
		private uint nextEvaluateTick;

		private uint pass;

		public override void OnStartServer()
		{
			base.OnStartServer();

			lodByClientId.Clear();

			/* Tick driven, not frame driven.
			 *
			 * This used to run from Update() against Time.time. Nothing about the decision is
			 * wrong on a wall clock -- it only chooses how often each observer hears from this
			 * transform -- but it made the cadence depend on the server's frame rate, which on a
			 * headless build is neither fixed nor related to the tick rate the sends are actually
			 * gated on (ShouldSend is `tick % interval`). Driving it from OnPostTick puts the
			 * scheduler and the thing it schedules in the same clock, and matches
			 * ObserverStreamingRegistry, which reschedules from the same event. */
			evaluateIntervalTicks = ResolveEvaluateIntervalTicks();
			nextEvaluateTick = 0u;

			if (base.TimeManager != null)
			{
				base.TimeManager.OnPostTick += TimeManager_OnPostTick;
			}

			/* Objects that are not characters never get an ObserverStreamingEntry, so this is the
			 * only per-observer filter they will have. Characters may register their entry before
			 * or after this runs; either way the entry reads this component, so whichever filter
			 * ends up installed produces the same decision. */
			if (base.NetworkObject != null && base.NetworkObject.ObserverSendFilter == null)
			{
				base.NetworkObject.ObserverSendFilter = this;
			}
		}

		public override void OnStopServer()
		{
			if (base.TimeManager != null)
			{
				base.TimeManager.OnPostTick -= TimeManager_OnPostTick;
			}
			if (base.NetworkObject != null && ReferenceEquals(base.NetworkObject.ObserverSendFilter, this))
			{
				base.NetworkObject.ObserverSendFilter = null;
			}
			lodByClientId.Clear();
			base.OnStopServer();
		}

		/// <summary>
		/// Converts the authored <see cref="evaluateInterval"/> into whole ticks, never below one.
		/// </summary>
		/// <remarks>
		/// Falls back to one tick when the TimeManager is unavailable or reports a non-positive
		/// tick delta: evaluating every tick is wasteful but correct, where a zero interval would
		/// divide by zero and a large one would freeze every observer in whatever band it first
		/// landed in.
		/// </remarks>
		private uint ResolveEvaluateIntervalTicks()
		{
			double tickDelta = base.TimeManager != null ? base.TimeManager.TickDelta : 0d;
			if (tickDelta <= 0d)
			{
				return 1u;
			}
			int ticks = Mathf.RoundToInt(evaluateInterval / (float)tickDelta);
			return ticks < 1 ? 1u : (uint)ticks;
		}

		/// <summary>
		/// Re-bands observers on a fixed tick cadence.
		/// </summary>
		/// <remarks>
		/// <see cref="TimeManager.OnPostTick"/> rather than <c>OnTick</c>: the pass reads every
		/// observer's transform position, and post-tick is after the motor has moved characters for
		/// this tick, so a band is chosen from where things actually are rather than from where
		/// they were when the tick began.
		/// </remarks>
		private void TimeManager_OnPostTick()
		{
			if (!base.IsServerStarted || bands == null || bands.Length < 1 || base.TimeManager == null)
			{
				return;
			}

			uint localTick = base.TimeManager.LocalTick;
			/* Unsigned wrap-safe comparison: (int)(a - b) < 0 stays correct across uint overflow,
			 * where `a < b` would stall the scheduler for the rest of the session at wrap. Same
			 * form ObservedResourcePushScheduler uses. */
			if (nextEvaluateTick != 0u && (int)(localTick - nextEvaluateTick) < 0)
			{
				return;
			}
			nextEvaluateTick = localTick + evaluateIntervalTicks;

			Evaluate();
		}

		/// <summary>
		/// Re-bands every current observer. Public so a server can force a pass after a bulk
		/// change (a teleport, a load boundary) and so tests can drive it.
		/// </summary>
		public void Evaluate()
		{
			NetworkObject nob = base.NetworkObject;
			if (nob == null || bands == null || bands.Length < 1)
			{
				return;
			}

			pass++;
			Vector3 position = transform.position;
			NetworkConnection owner = nob.Owner;

			HashSet<NetworkConnection> observers = nob.Observers;
			if (observers != null)
			{
				foreach (NetworkConnection connection in observers)
				{
					if (connection == null)
					{
						continue;
					}
					if (owner != null && owner.IsValid && connection == owner)
					{
						// The owner is never rate limited, and would otherwise sit at distance zero.
						continue;
					}

					NetworkObject observerObject = connection.FirstObject;
					if (observerObject == null)
					{
						/* Still loading in. Keep whatever it had (full rate if nothing) rather than
						 * banding it on a position it does not have yet. */
						continue;
					}

					/* The radius comes from the OBSERVER's reach, not this object's: the question is
					 * whether that client could attack this character, and so whether its view of it
					 * has to be tick-exact. */
					float engagementRange = ObserverStreamingPolicy.ResolveEngagementRange(
						observerObject.TryGetComponent(out AbilityController observerAbilities)
							? observerAbilities.LongestKnownAbilityRange
							: 0f);

					BandObserver(connection.ClientId,
						(observerObject.transform.position - position).sqrMagnitude,
						engagementRange * engagementRange);
				}
			}

			// Forget observers that left, so a reconnecting client starts fresh.
			departed.Clear();
			foreach (KeyValuePair<int, ObserverLod> pair in lodByClientId)
			{
				if (pair.Value.Pass != pass)
				{
					departed.Add(pair.Key);
				}
			}
			for (int i = 0; i < departed.Count; i++)
			{
				lodByClientId.Remove(departed[i]);
			}
		}

		/// <summary>
		/// Bands one observer for the current pass. Split out of <see cref="Evaluate"/> so tests can
		/// drive it without a spawned NetworkObject and live connections.
		/// </summary>
		internal void BandObserver(int clientId, float sqrDistance, float engagementSqrDistance = 0f)
		{
			lodByClientId.TryGetValue(clientId, out ObserverLod lod);
			int previousBand = lod.Pass == 0 ? -1 : lod.Band;
			int band = ResolveBand(bands, hysteresis, sqrDistance, previousBand);

			/* Inside the observer's engagement radius nothing is throttled.
			 *
			 * A throttled transform arrives every 3, 6 or 8 ticks and the client interpolates across
			 * the gap, so the pose it renders existed on no server tick and lag compensation cannot
			 * reproduce it however precise the rewind. Full rate makes the rendered pose a tick
			 * sample again. Deliberately checked against the raw distance rather than the banded one:
			 * the bands carry hysteresis, and a target flickering between engaged and throttled at
			 * the boundary is exactly the case where the compensation has to be right.
			 * See ObserverStreamingPolicy.EngagementRange. */
			bool engaged = engagementSqrDistance > 0f && sqrDistance <= engagementSqrDistance;

			lod.Band = band;
			lod.Interval = engaged ? (byte)1 : IntervalForBand(bands, band, intervalScale);
			lod.Engaged = engaged;
			lod.Pass = pass;
			lodByClientId[clientId] = lod;
		}

		/// <summary>
		/// True while this object is close enough to <paramref name="connection"/> for that client to
		/// attack it, so no throttle of any kind may apply.
		/// </summary>
		/// <remarks>
		/// Read by <c>ObserverStreamingEntry</c> as well, because the per-observer CAP throttles
		/// independently of distance and would otherwise reintroduce the gap this closes.
		/// </remarks>
		public bool IsEngaged(NetworkConnection connection)
		{
			return connection != null &&
				lodByClientId.TryGetValue(connection.ClientId, out ObserverLod lod) &&
				lod.Engaged;
		}

		/// <summary>Band an observer is currently held in, or -1 when it is not tracked.</summary>
		public int GetBand(NetworkConnection connection)
		{
			return connection != null && lodByClientId.TryGetValue(connection.ClientId, out ObserverLod lod) ? lod.Band : -1;
		}

		/// <summary>Send interval currently applied to an observer by distance; 1 when unlimited.</summary>
		public byte GetInterval(NetworkConnection connection)
		{
			if (connection == null || !lodByClientId.TryGetValue(connection.ClientId, out ObserverLod lod))
			{
				return 1;
			}
			return lod.Interval < 1 ? (byte)1 : lod.Interval;
		}

		/// <inheritdoc/>
		/// <remarks>
		/// Used directly only when the object has no <see cref="ObserverStreamingEntry"/>; the entry
		/// otherwise folds <see cref="GetInterval"/> into its own decision. Reliable sends are never
		/// declined — the reliable settle after a stop must reach every observer.
		/// </remarks>
		public bool ShouldSend(NetworkObject networkObject, NetworkConnection connection, Channel channel)
		{
			if (channel != Channel.Unreliable || connection == null || networkObject == null)
			{
				return true;
			}
			if (connection == networkObject.Owner)
			{
				return true;
			}
			byte interval = GetInterval(connection);
			if (interval <= 1)
			{
				return true;
			}
			uint tick = networkObject.TimeManager != null ? networkObject.TimeManager.LocalTick : 0u;
			return ObserverStreamingPolicy.ShouldSendThisTick(tick, interval, connection.ClientId);
		}

		/// <summary>
		/// Picks the band for one observer's squared distance, holding <paramref name="currentBand"/>
		/// until the distance clears that band's edge by <paramref name="hysteresis"/>.
		/// </summary>
		/// <param name="bands">Ascending bands; must have at least one element.</param>
		/// <param name="hysteresis">Fraction each held band's edge is widened by.</param>
		/// <param name="sqrDistance">Squared distance from the object to the observer.</param>
		/// <param name="currentBand">Band the observer is currently held in, or -1 for none.</param>
		/// <returns>Band index. Distances beyond the last band clamp to it.</returns>
		public static int ResolveBand(Band[] bands, float hysteresis, float sqrDistance, int currentBand)
		{
			if (bands == null || bands.Length < 1)
			{
				return -1;
			}

			for (int i = 0; i < bands.Length; i++)
			{
				float edge = bands[i].MaximumDistance;

				// Widen the edge of the band the observer is already in, so sitting on a boundary
				// settles instead of changing every evaluation.
				if (i == currentBand)
				{
					edge *= 1f + hysteresis;
				}

				if (sqrDistance <= edge * edge)
				{
					return i;
				}
			}

			return bands.Length - 1;
		}

		/// <summary>Interval for a band after scaling, clamped to the byte range; 1 for no band.</summary>
		public static byte IntervalForBand(Band[] bands, int band, int scale)
		{
			if (bands == null || band < 0 || band >= bands.Length)
			{
				return 1;
			}
			return (byte)Mathf.Clamp(bands[band].Interval * Mathf.Max(1, scale), 1, 255);
		}
	}
}
