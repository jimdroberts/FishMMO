#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Where sites stand IN fresh water (decision 35, 2026-10-18, phase I2l "lakes and rivers as places"): the pure geometry of an
	/// in-stream site moored in a river's current (a ship mill, a salmon lift-net, a toll keep on its rock), a quay straddling a
	/// river's bank (a river staple), a pile site over a lake's shallows with its causeway to the strand, and a sunken site's
	/// depth below a lake's own level.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>What is wired.</b> A kind rule says it may stand in water (<see cref="PointOfInterestKindRule.InChannel"/>,
	/// <see cref="PointOfInterestKindRule.StraddlesBank"/>, <see cref="PointOfInterestKindRule.OverShallows"/>,
	/// <see cref="PointOfInterestKindRule.InLakes"/>); none of the catalogue's rules says so yet, so every scene is sited as
	/// before. Sunken kinds that say <c>InLakes</c> are sited under a lake by its own depth (PointOfInterestPlanner.Placement);
	/// the in-stream, bank-straddling and over-shallows modes are answered here and are switched on in the planner together with
	/// the pieces their sites are built from (the ship mill's hulls and wheel, the lift-net stage, the pile house and its Steg:
	/// the Lake and River rounds' signature pieces, I7), since a land building set on a river's current is worse than none.
	/// </para>
	/// <para>
	/// Every answer is a pure function of the river's line and the ground and water functions it is given, so a re-cut sites the
	/// same, and each is tested without a scene.
	/// </para>
	/// </remarks>
	public static class PointOfInterestWaterSites
	{
		/// <summary>An in-stream spot on a river: where its moorings stand, its heading (bow upstream) and the water there.</summary>
		public struct ChannelSpot
		{
			public Vector2 Position;
			/// <summary>Degrees about +y the site faces: its bow into the current (upstream).</summary>
			public float Yaw;
			/// <summary>The river's surface there (a moored hull floats on it).</summary>
			public float Surface;
			public float Depth;
			public float Speed;
			/// <summary>Metres from the spot to the nearer bank, which its chain or gangplank reaches.</summary>
			public float BankDistance;
			/// <summary>−1 the river's right bank (looking downstream), +1 its left.</summary>
			public int Side;
		}

		/// <summary>
		/// The in-stream spot at point <paramref name="k"/> of a river, on one side of its line (decision 35 (g)): moored
		/// <paramref name="bankMetres"/> off the bank (never past the middle), where the channel is at least
		/// <paramref name="minDepth"/> deep and the current at least <paramref name="minSpeed"/> (a ship mill wants 1.5 m and 0.8 m/s:
		/// a wheel needs the stream, its hulls the draught), on a steady reach (a run; never a riffle, rapid or fall). False where
		/// the river cannot hold it.
		/// </summary>
		public static bool TryChannelSpot(RiverPath river, int k, int side, float minDepth, float minSpeed, float bankMetres, out ChannelSpot spot)
		{
			spot = default;
			if (river == null || !river.Perennial || river.Count < 3 || k < 1 || k >= river.Count - 1 || side == 0)
			{
				return false;
			}
			if (river.Reach != null && k < river.Reach.Length && river.Reach[k] != RiverReach.Run)
			{
				return false;
			}
			float depth = river.Depth != null && k < river.Depth.Length ? river.Depth[k] : 0f;
			float speed = river.SpeedAt(k);
			if (depth < minDepth || speed < minSpeed)
			{
				return false;
			}
			float half = 0.5f * river.Width[k];
			float fromBank = Mathf.Min(Mathf.Max(1f, bankMetres), half);
			float tx = river.X[k + 1] - river.X[k - 1], tz = river.Z[k + 1] - river.Z[k - 1];
			float tl = Mathf.Sqrt(tx * tx + tz * tz);
			if (tl < 1e-3f)
			{
				return false;
			}
			tx /= tl;
			tz /= tl;
			// Left of the flow is (−tz, tx).
			float nx = -tz * Mathf.Sign(side), nz = tx * Mathf.Sign(side);
			float off = half - fromBank;
			spot = new ChannelSpot
			{
				Position = new Vector2(river.X[k] + nx * off, river.Z[k] + nz * off),
				Yaw = PointOfInterestPlanner.YawOf(-tx, -tz),
				Surface = river.Surface != null && k < river.Surface.Length ? river.Surface[k] : float.NaN,
				Depth = depth,
				Speed = speed,
				BankDistance = fromBank,
				Side = side > 0 ? 1 : -1,
			};
			return true;
		}

		/// <summary>A quay straddling a river's bank: its centre ON the water's edge, half its footprint over the water, facing it.</summary>
		public struct BankQuay
		{
			public Vector2 Position;
			/// <summary>Degrees about +y the quay faces: across the river, toward the water.</summary>
			public float Yaw;
			public float Surface;
		}

		/// <summary>
		/// The bank-straddling spot at point <paramref name="k"/> of a river on one side (decision 35 (g), a river staple's quay and
		/// crane): centred on the water's edge so its quay wall runs along the bank with half its footprint over the water, facing
		/// across the river. False on a fall-adjacent or narrow reach (a staple wants a river ships use: 12 m and wider).
		/// </summary>
		public static bool TryBankQuay(RiverPath river, int k, int side, float minWidth, out BankQuay quay)
		{
			quay = default;
			if (river == null || !river.Perennial || river.Count < 3 || k < 1 || k >= river.Count - 1 || side == 0 || river.Width[k] < minWidth)
			{
				return false;
			}
			if (river.Reach != null && k < river.Reach.Length && river.Reach[k] != RiverReach.Run && river.Reach[k] != RiverReach.Pool)
			{
				return false;
			}
			float tx = river.X[k + 1] - river.X[k - 1], tz = river.Z[k + 1] - river.Z[k - 1];
			float tl = Mathf.Sqrt(tx * tx + tz * tz);
			if (tl < 1e-3f)
			{
				return false;
			}
			tx /= tl;
			tz /= tl;
			float nx = -tz * Mathf.Sign(side), nz = tx * Mathf.Sign(side);
			float half = 0.5f * river.Width[k];
			quay = new BankQuay
			{
				Position = new Vector2(river.X[k] + nx * half, river.Z[k] + nz * half),
				Yaw = PointOfInterestPlanner.YawOf(-nx, -nz),
				Surface = river.Surface != null && k < river.Surface.Length ? river.Surface[k] : float.NaN,
			};
			return true;
		}

		/// <summary>
		/// How deep a point lies below the water a sunken site stands in (decision 35 (d)): below the lake's own level where a lake
		/// covers it, else below the sea's; NaN where neither does (no lake there and no sea).
		/// </summary>
		public static float SunkenDepth(float ground, float lakeLevel, bool hasSea, float seaLevel)
		{
			if (!float.IsNaN(lakeLevel) && !float.IsInfinity(lakeLevel))
			{
				return lakeLevel - ground;
			}
			return hasSea ? seaLevel - ground : float.NaN;
		}

		/// <summary>A pile site over a lake's shallows and the causeway that joins it to the strand.</summary>
		public struct ShallowsSite
		{
			public Vector2 Position;
			public float Depth;
			/// <summary>The causeway's line, site first, the dry strand last (empty when the site stands within reach of the shore).</summary>
			public List<Vector2> Causeway;
			public float CausewayLength;
		}

		/// <summary>
		/// Whether a point over a lake can hold a pile site (decision 35 (c)): its water <paramref name="minDepth"/> … <paramref name="maxDepth"/>
		/// deep (piles in 0.5–3 m), within <paramref name="maxShoreMetres"/> of dry ground along the causeway <see cref="Causeway"/> finds,
		/// the footprint (<paramref name="radius"/>) all over water no deeper than the piles reach. The bed is never levelled: the site
		/// stands on piles to its own depth.
		/// </summary>
		/// <param name="depthAt">The water's depth at a point (negative on dry ground).</param>
		public static bool TryShallows(Vector2 at, float radius, float minDepth, float maxDepth, float maxShoreMetres,
			Func<float, float, float> depthAt, out ShallowsSite site)
		{
			site = default;
			if (depthAt == null)
			{
				return false;
			}
			float depth = depthAt(at.x, at.y);
			if (!(depth >= minDepth) || depth > maxDepth)
			{
				return false;
			}
			for (int k = 0; k < 8; k++)
			{
				float a = k * Mathf.PI * 0.25f;
				float d = depthAt(at.x + radius * Mathf.Sin(a), at.y + radius * Mathf.Cos(a));
				if (!(d > 0.15f) || d > maxDepth + 1f)
				{
					return false;
				}
			}
			List<Vector2> line = Causeway(at, radius, maxShoreMetres, depthAt, out float length);
			if (line == null)
			{
				return false;
			}
			site = new ShallowsSite { Position = at, Depth = depth, Causeway = line, CausewayLength = length };
			return true;
		}

		/// <summary>The step a causeway is laid in, metres (a boardwalk module).</summary>
		public const float CausewayStepMetres = 4f;

		/// <summary>
		/// The causeway from a pile site to the strand: the straightest way off the footprint's edge to dry ground, chosen among
		/// sixteen headings as the shortest that reaches ground within <paramref name="maxMetres"/> (ties to the shallower water on
		/// the way, then the lower heading), laid in <see cref="CausewayStepMetres"/> steps. Null when no heading reaches the shore.
		/// </summary>
		public static List<Vector2> Causeway(Vector2 from, float radius, float maxMetres, Func<float, float, float> depthAt, out float length)
		{
			length = 0f;
			int bestHeading = -1;
			float bestRun = float.PositiveInfinity, bestDeep = float.PositiveInfinity;
			for (int h = 0; h < 16; h++)
			{
				float a = h * Mathf.PI / 8f;
				var dir = new Vector2(Mathf.Sin(a), Mathf.Cos(a));
				float deepest = 0f;
				for (float s = radius; s <= radius + maxMetres + 1e-3f; s += CausewayStepMetres * 0.5f)
				{
					Vector2 p = from + dir * s;
					float d = depthAt(p.x, p.y);
					if (d <= 0f)
					{
						if (s < bestRun - 1e-3f || Mathf.Abs(s - bestRun) <= 1e-3f && deepest < bestDeep)
						{
							bestRun = s;
							bestDeep = deepest;
							bestHeading = h;
						}
						break;
					}
					deepest = Mathf.Max(deepest, d);
				}
			}
			if (bestHeading < 0)
			{
				return null;
			}
			float angle = bestHeading * Mathf.PI / 8f;
			var heading = new Vector2(Mathf.Sin(angle), Mathf.Cos(angle));
			var line = new List<Vector2>();
			int steps = Mathf.Max(1, Mathf.CeilToInt((bestRun - radius) / CausewayStepMetres));
			for (int i = 0; i <= steps; i++)
			{
				line.Add(from + heading * Mathf.Lerp(radius, bestRun, i / (float)steps));
			}
			length = bestRun - radius;
			return line;
		}
	}
}
#endif
