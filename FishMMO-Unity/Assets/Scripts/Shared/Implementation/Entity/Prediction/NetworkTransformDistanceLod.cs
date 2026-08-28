using System.Collections.Generic;
using FishNet.Component.Transforming;
using FishNet.Connection;
using FishNet.Object;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Scales a <see cref="NetworkTransform"/>'s send interval by how far away the nearest observer
	/// is, so an object nobody is standing near stops paying full rate.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why interval and not something finer.</b> Once payloads are delta-encoded, a transform
	/// update is around eleven bytes behind a ten-byte RPC header — the header is roughly half the
	/// cost. At that point the only lever left is sending fewer messages, and
	/// <see cref="NetworkTransform.SetInterval"/> is the one FishNet already exposes for it.
	/// </para>
	/// <para>
	/// <b>What this cannot do.</b> The interval lives on the object, not on the observer, so every
	/// observer of an object gets the same rate. The distance that matters is therefore the
	/// <i>nearest</i> observer's: if anyone is close enough to notice, everyone keeps the fast rate.
	/// That makes this very effective in sparse zones — a distant NPC nobody is near drops to a
	/// fraction of its traffic — and close to useless in a packed capital, where somebody is always
	/// within a few metres of everything. Dense scenes need <see cref="IntervalScale"/> raised
	/// instead, which is a per-zone decision rather than a per-object one.
	/// </para>
	/// <para>
	/// <b>Changing the interval is not free.</b> <see cref="NetworkTransform.SetInterval"/> is a
	/// buffered observers RPC, so a value that oscillates across a band edge would cost more than
	/// the rate it saves. Bands are therefore evaluated on a slow timer and applied with a hysteresis
	/// margin, so an object hovering on a boundary settles rather than flapping.
	/// </para>
	/// </remarks>
	[RequireComponent(typeof(NetworkTransform))]
	public class NetworkTransformDistanceLod : NetworkBehaviour
	{
		/// <summary>
		/// One distance band: everything nearer than <see cref="MaximumDistance"/> and not covered by
		/// a nearer band synchronises every <see cref="Interval"/> ticks.
		/// </summary>
		[System.Serializable]
		public struct Band
		{
			[Tooltip("Nearest observer must be within this distance for this band to apply.")]
			public float MaximumDistance;

			[Tooltip("Ticks between transform sends while this band applies. 1 is every tick.")]
			[Range(1, 60)]
			public byte Interval;
		}

		/// <summary>
		/// Bands in ascending distance order. The first whose <see cref="Band.MaximumDistance"/> the
		/// nearest observer is inside wins; beyond the last band, the last band's interval is used.
		/// </summary>
		/// <remarks>
		/// Defaults are deliberately conservative — full rate out to twenty metres, which covers
		/// melee range and most of what a player actually watches, then progressively coarser. Tune
		/// against how your camera frames the world rather than against the byte counts.
		/// </remarks>
		[Header("Level of detail")]
		[Tooltip("Ascending distance bands. Nearest observer inside a band sets that band's interval.")]
		[SerializeField]
		private Band[] bands =
		{
			new Band { MaximumDistance = 20f, Interval = 1 },
			new Band { MaximumDistance = 40f, Interval = 3 },
			new Band { MaximumDistance = 80f, Interval = 6 },
		};

		/// <summary>
		/// Multiplies whichever band interval is selected. Raise it for dense scenes.
		/// </summary>
		/// <remarks>
		/// The escape hatch for the case the per-object distance rule cannot see. In a capital every
		/// object has a near observer, so every object sits in the fastest band and this component
		/// saves nothing; raising the scale for that scene trades some smoothness for a proportional
		/// cut across every object in it. Settable at runtime so a zone can raise it on entry.
		/// </remarks>
		[Tooltip("Multiplier applied to the selected band's interval. Raise for crowded scenes.")]
		[Range(1, 8)]
		[SerializeField]
		private int intervalScale = 1;

		/// <summary>Seconds between band evaluations.</summary>
		/// <remarks>
		/// Deliberately slow. Each applied change is a buffered observers RPC, and the thing being
		/// decided — roughly how far away the nearest player is — does not move meaningfully within
		/// a few hundred milliseconds.
		/// </remarks>
		[Tooltip("Seconds between evaluations. Low values cost RPCs without improving the decision.")]
		[Range(0.1f, 5f)]
		[SerializeField]
		private float evaluateInterval = 0.5f;

		/// <summary>
		/// Fraction a band edge is extended by before the object is allowed to leave that band.
		/// </summary>
		/// <remarks>
		/// Without this an object sitting exactly on an edge would emit an RPC every evaluation as
		/// it crossed back and forth — spending bandwidth to save bandwidth.
		/// </remarks>
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

		private NetworkTransform networkTransform;
		private float nextEvaluateTime;
		private int currentBand = -1;
		private byte appliedInterval;

		private void Awake()
		{
			networkTransform = GetComponent<NetworkTransform>();
		}

		public override void OnStartServer()
		{
			base.OnStartServer();

			// Start at the fastest band so an object is never briefly coarse right after spawning,
			// which is exactly when a player is most likely to be looking at it.
			currentBand = -1;
			appliedInterval = 0;
			nextEvaluateTime = 0f;
		}

		private void Update()
		{
			if (!base.IsServerStarted || networkTransform == null || bands == null || bands.Length < 1)
			{
				return;
			}
			if (Time.time < nextEvaluateTime)
			{
				return;
			}
			nextEvaluateTime = Time.time + evaluateInterval;

			int band = ResolveBand(NearestObserverSqrDistance());
			if (band < 0)
			{
				return;
			}

			byte interval = (byte)Mathf.Clamp(bands[band].Interval * Mathf.Max(1, intervalScale), 1, 255);
			if (band == currentBand && interval == appliedInterval)
			{
				return;
			}

			currentBand = band;
			appliedInterval = interval;
			networkTransform.SetInterval(interval);
		}

		/// <summary>
		/// Squared distance to the closest observing player, or <see cref="float.MaxValue"/> when
		/// nobody is observing.
		/// </summary>
		/// <remarks>
		/// Squared throughout — this runs per object per evaluation and the comparison does not need
		/// the square root. An observer whose connection has no first object yet (still loading in)
		/// is skipped rather than treated as infinitely far, so an object does not drop to its
		/// coarsest band because someone is mid-handshake beside it.
		/// </remarks>
		private float NearestObserverSqrDistance()
		{
			NetworkObject nob = base.NetworkObject;
			if (nob == null)
			{
				return float.MaxValue;
			}

			HashSet<NetworkConnection> observers = nob.Observers;
			if (observers == null || observers.Count < 1)
			{
				return float.MaxValue;
			}

			Vector3 position = transform.position;
			float nearest = float.MaxValue;

			foreach (NetworkConnection connection in observers)
			{
				NetworkObject observerObject = connection?.FirstObject;
				if (observerObject == null)
				{
					continue;
				}

				float sqr = (observerObject.transform.position - position).sqrMagnitude;
				if (sqr < nearest)
				{
					nearest = sqr;
				}
			}

			return nearest;
		}

		/// <summary>
		/// Picks the band for a nearest-observer distance, holding the current band until the
		/// distance clears its edge by <see cref="hysteresis"/>.
		/// </summary>
		/// <param name="sqrDistance">Squared distance to the nearest observer.</param>
		/// <returns>Band index, or -1 when nothing is observing and the rate is irrelevant.</returns>
		private int ResolveBand(float sqrDistance)
		{
			if (sqrDistance == float.MaxValue)
			{
				// Unobserved. Whatever interval is set costs nothing, so leave it alone rather than
				// spending an RPC on a decision no one can see.
				return -1;
			}

			for (int i = 0; i < bands.Length; i++)
			{
				float edge = bands[i].MaximumDistance;

				// Widen the edge of the band we are already in, so sitting on a boundary settles
				// instead of emitting an RPC every evaluation.
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
	}
}
