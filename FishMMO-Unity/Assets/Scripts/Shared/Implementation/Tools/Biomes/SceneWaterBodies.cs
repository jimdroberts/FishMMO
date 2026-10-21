using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared.Weather;

namespace FishMMO.Shared.Biomes
{
	/// <summary>
	/// A scene's lakes and rivers as the running game asks about them: where the water stands, how high,
	/// and how it moves. Read from the scene's <see cref="SceneHydrology"/>; registered with
	/// <see cref="SurfaceWater"/> so the weather stops at it and knows when the camera is under it.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>One flow over the whole scene.</b> <see cref="FlowAt"/> is a single smooth field, every river's
	/// current at every point blended by how near it is: so where a tributary meets a river both carry
	/// the same blended current, and where a river runs into a lake its jet spreads and dies away across
	/// the lake. Whatever draws the water reads its current from here, at its own points, which is what
	/// lets two pieces of water meet without a seam.
	/// </para>
	/// <para>
	/// Shared by the client and the server: the queries are arithmetic on the asset, no rendering.
	/// </para>
	/// <para>
	/// <b>Lava too.</b> A lake or river flagged <see cref="SceneHydrology.Lake.Lava"/> (a lava tube's) stands, flows and is
	/// asked about like any other, and says it is lava (<see cref="TryGetSurface(float, float, out float, out bool)"/>), so
	/// <see cref="WaterQuery"/> reports it as <see cref="WaterBody.Lava"/>: burned in, swum slowly by the fire-proof.
	/// </para>
	/// </remarks>
	public sealed class SceneWaterBodies : MonoBehaviour, SurfaceWater.IStackedAreaSource
	{
		[Tooltip("The scene's rivers and lakes, written when it was generated.")]
		public SceneHydrology Hydrology;

		/// <summary>Cells of the index over the river lines, metres.</summary>
		private const float IndexCell = 32f;

		/// <summary>How many river widths past its mouth a river's current carries on into a lake or the sea.</summary>
		public const float JetWidths = 6f;

		private struct Segment
		{
			public int River;
			public int Point;
			/// <summary>A jet: the river's current carried on into the water it ends in, fading.</summary>
			public bool Jet;
		}

		private readonly Dictionary<long, List<Segment>> index = new Dictionary<long, List<Segment>>();
		private bool built;

		public float Level => float.NegativeInfinity;
		public float WaveHeight => 0f;

		private void OnEnable()
		{
			Build();
			SurfaceWater.Register(this);
			WaterQuery.RegisterInland(gameObject.scene, this);
		}

		private void OnDisable()
		{
			SurfaceWater.Unregister(this);
			WaterQuery.UnregisterInland(gameObject.scene, this);
		}

		/// <summary>Builds the index over the river lines; again when the asset changes.</summary>
		public void Build()
		{
			index.Clear();
			built = true;
			if (Hydrology == null)
			{
				return;
			}
			for (int r = 0; r < Hydrology.Rivers.Count; r++)
			{
				SceneHydrology.River river = Hydrology.Rivers[r];
				if (river.Points == null || river.Points.Length < 2)
				{
					continue;
				}
				for (int i = 0; i + 1 < river.Points.Length; i++)
				{
					float reach = 0.5f * Mathf.Max(Width(river, i), Width(river, i + 1)) * 2.5f + 2f
						+ Mathf.Max(Mathf.Max(Shore(river.ShoreLeft, i), Shore(river.ShoreLeft, i + 1)), Mathf.Max(Shore(river.ShoreRight, i), Shore(river.ShoreRight, i + 1)));
					AddSegment(river.Points[i], river.Points[i + 1], reach, new Segment { River = r, Point = i });
				}
				if (river.Perennial && (river.Finish == SceneHydrology.End.Lake || river.Finish == SceneHydrology.End.Sea))
				{
					int last = river.Points.Length - 1;
					Vector3 end = river.Points[last];
					Vector3 along = (end - river.Points[last - 1]);
					along.y = 0f;
					along = along.sqrMagnitude > 1e-6f ? along.normalized : Vector3.forward;
					float length = JetWidths * Width(river, last);
					AddSegment(end, end + along * length, length + Width(river, last) * 2f, new Segment { River = r, Point = last, Jet = true });
				}
			}
		}

		private void AddSegment(Vector3 a, Vector3 b, float reach, Segment segment)
		{
			int x0 = Mathf.FloorToInt((Mathf.Min(a.x, b.x) - reach) / IndexCell), x1 = Mathf.FloorToInt((Mathf.Max(a.x, b.x) + reach) / IndexCell);
			int z0 = Mathf.FloorToInt((Mathf.Min(a.z, b.z) - reach) / IndexCell), z1 = Mathf.FloorToInt((Mathf.Max(a.z, b.z) + reach) / IndexCell);
			for (int z = z0; z <= z1; z++)
			{
				for (int x = x0; x <= x1; x++)
				{
					long key = ((long)x << 32) ^ (uint)z;
					if (!index.TryGetValue(key, out List<Segment> list))
					{
						list = new List<Segment>(4);
						index[key] = list;
					}
					list.Add(segment);
				}
			}
		}

		private static float Shore(float[] shore, int i) => shore != null && i < shore.Length ? Mathf.Max(0f, shore[i]) : 0f;

		/// <summary>
		/// How far out from a river's line between points <paramref name="i"/> and i + 1 its water reaches at (x, z), metres: half
		/// its width, and past that where the cut ground lies under it on that side (<see cref="SceneHydrology.River.ShoreLeft"/>).
		/// </summary>
		private static float HalfAt(SceneHydrology.River river, int i, float t, Vector3 a, Vector3 b, float x, float z)
		{
			float half = 0.5f * Mathf.Lerp(Width(river, i), Width(river, i + 1), t);
			bool left = (b.x - a.x) * (z - a.z) - (b.z - a.z) * (x - a.x) > 0f;
			float[] shore = left ? river.ShoreLeft : river.ShoreRight;
			return half + Mathf.Lerp(Shore(shore, i), Shore(shore, i + 1), t);
		}

		private static float Width(SceneHydrology.River river, int i) => river.Width != null && i < river.Width.Length ? Mathf.Max(0.5f, river.Width[i]) : 4f;
		private static float Speed(SceneHydrology.River river, int i) => river.Speed != null && i < river.Speed.Length ? river.Speed[i] : 0f;

		private List<Segment> Near(float x, float z)
		{
			if (!built)
			{
				Build();
			}
			long key = ((long)Mathf.FloorToInt(x / IndexCell) << 32) ^ (uint)Mathf.FloorToInt(z / IndexCell);
			return index.TryGetValue(key, out List<Segment> list) ? list : null;
		}

		/// <summary>The water's surface over a scene position: a lake's level, or a river's surface where its channel is.</summary>
		public bool TryGetSurface(float x, float z, out float level) => TryGetSurfaceNear(x, z, 0f, out level, out _);

		/// <summary>The surface over a scene position, as <see cref="TryGetSurface(float, float, out float)"/>, and whether it is molten lava.</summary>
		public bool TryGetSurface(float x, float z, out float level, out bool lava) => TryGetSurfaceNear(x, z, 0f, out level, out lava);

		/// <summary>
		/// The surface a height stands in or over at a scene position (2026-10-21): only lakes and rivers whose floor lies no more
		/// than <see cref="SurfaceWater.FloorReach"/> over <paramref name="y"/> answer, so a cave's stream is not drowned by a lava
		/// tube's river on the level over it, nor by a lake on the hill. A lake whose floor is unknown reaches down without end.
		/// </summary>
		public bool TryGetSurface(float x, float y, float z, out float level, out bool lava) => Surface(x, z, 0f, y, out level, out lava);

		/// <summary>True when any of the scene's lakes or rivers is lava.</summary>
		public bool HasLava
		{
			get
			{
				if (Hydrology == null)
				{
					return false;
				}
				foreach (SceneHydrology.Lake lake in Hydrology.Lakes)
				{
					if (lake != null && lake.Lava)
					{
						return true;
					}
				}
				foreach (SceneHydrology.River river in Hydrology.Rivers)
				{
					if (river != null && river.Lava && river.Perennial)
					{
						return true;
					}
				}
				return false;
			}
		}

		/// <summary>
		/// Whether any lake or perennial river may stand within <paramref name="area"/> (scene xz, <c>yMin</c>/<c>yMax</c>
		/// north), its banks and <see cref="MaxReachMetres"/> included: lets a map over a dry terrain skip asking at every point.
		/// With <paramref name="lava"/> false, lava lakes and rivers are left out (they are opaque, drawn like ground).
		/// </summary>
		public bool Reaches(Rect area, bool lava = true)
		{
			if (Hydrology == null)
			{
				return false;
			}
			foreach (SceneHydrology.Lake lake in Hydrology.Lakes)
			{
				if (!lava && lake.Lava)
				{
					continue;
				}
				Rect bounds = lake.Bounds;
				if (bounds.xMax + MaxReachMetres >= area.xMin && bounds.xMin - MaxReachMetres <= area.xMax &&
					bounds.yMax + MaxReachMetres >= area.yMin && bounds.yMin - MaxReachMetres <= area.yMax)
				{
					return true;
				}
			}
			foreach (SceneHydrology.River river in Hydrology.Rivers)
			{
				if (!river.Perennial || river.Points == null || !lava && river.Lava)
				{
					continue;
				}
				for (int i = 0; i + 1 < river.Points.Length; i++)
				{
					Vector3 a = river.Points[i], b = river.Points[i + 1];
					float margin = 0.5f * Mathf.Max(Width(river, i), Width(river, i + 1)) + MaxReachMetres;
					if (Mathf.Max(a.x, b.x) + margin >= area.xMin && Mathf.Min(a.x, b.x) - margin <= area.xMax &&
						Mathf.Max(a.z, b.z) + margin >= area.yMin && Mathf.Min(a.z, b.z) - margin <= area.yMax)
					{
						return true;
					}
				}
			}
			return false;
		}

		/// <summary>The most <see cref="TryGetSurfaceNear"/> reaches past a river's banks: the index over the river lines covers no further.</summary>
		public const float MaxReachMetres = 2f;

		/// <summary>
		/// The water surface over a scene position or within <paramref name="reach"/> metres of it (at most
		/// <see cref="MaxReachMetres"/>): the nearest river's (where its centre line comes nearest) past its banks by that
		/// much, or a lake's, whichever stands higher; a lake's where its mask covers the
		/// point or a point that far off. For maps sampled on a grid coarser than a creek: a 2 m heightmap can have no
		/// sample inside a 1.5 m channel, and the water still has to reach the samples either side of it.
		/// </summary>
		public bool TryGetSurfaceNear(float x, float z, float reach, out float level) => TryGetSurfaceNear(x, z, reach, out level, out _);

		/// <summary><see cref="TryGetSurfaceNear(float, float, float, out float)"/>, and whether the surface found is molten lava.</summary>
		public bool TryGetSurfaceNear(float x, float z, float reach, out float level, out bool lava) => Surface(x, z, reach, float.PositiveInfinity, out level, out lava);

		/// <summary>Whether water whose floor lies at <paramref name="floor"/> is a height's (any for positive infinity).</summary>
		private static bool Holds(float floor, float height) => !(floor > height + SurfaceWater.FloorReach);

		/// <summary>A river's floor where its line comes nearest: its bed, else its surface less its depth there.</summary>
		private static float FloorOf(SceneHydrology.River river, int i, float t, Vector3 a, Vector3 b)
		{
			if (river.Bed != null && i + 1 < river.Bed.Length)
			{
				return Mathf.Lerp(river.Bed[i], river.Bed[i + 1], t);
			}
			float depth = river.Depth != null && i + 1 < river.Depth.Length ? Mathf.Lerp(river.Depth[i], river.Depth[i + 1], t) : 1f;
			return Mathf.Lerp(a.y, b.y, t) - depth;
		}

		/// <summary>The surface over a position, within <paramref name="reach"/>, of the water a height stands in or over.</summary>
		private bool Surface(float x, float z, float reach, float height, out float level, out bool lava)
		{
			level = float.NegativeInfinity;
			lava = false;
			bool any = false;
			if (Hydrology == null)
			{
				return false;
			}
			reach = Mathf.Clamp(reach, 0f, MaxReachMetres);
			foreach (SceneHydrology.Lake lake in Hydrology.Lakes)
			{
				if (lake.Level > level && Holds(lake.Floor, height) && LakeCovers(lake, x, z, reach))
				{
					level = lake.Level;
					lava = lake.Lava;
					any = true;
				}
			}
			List<Segment> near = Near(x, z);
			if (near == null)
			{
				return any;
			}
			/* The river's surface where its centre line comes nearest, not the highest of every segment in reach: down a
			 * falling river the segment upstream reaches a little past its end and stands higher, so the highest stepped
			 * at every segment, and the grass's waterline and the banks' bed paint came out as a row of teeth pointing
			 * downstream (2026-10-11). The nearest varies smoothly along the river. */
			// The nearest of each liquid's apart: a lava tube's river under a river on the hill is its own, and the higher answers.
			float nearestWater = float.PositiveInfinity, water = float.NegativeInfinity;
			float nearestLava = float.PositiveInfinity, molten = float.NegativeInfinity;
			foreach (Segment segment in near)
			{
				if (segment.Jet)
				{
					continue;
				}
				SceneHydrology.River path = Hydrology.Rivers[segment.River];
				if (!path.Perennial)
				{
					continue;
				}
				Vector3 a = path.Points[segment.Point], b = path.Points[segment.Point + 1];
				float t = Project(a, b, x, z, out float distance);
				float half = HalfAt(path, segment.Point, t, a, b, x, z);
				if (distance > half + reach || !Holds(FloorOf(path, segment.Point, t, a, b), height))
				{
					continue;
				}
				if (path.Lava ? distance < nearestLava : distance < nearestWater)
				{
					if (path.Lava)
					{
						nearestLava = distance;
						molten = Mathf.Lerp(a.y, b.y, t);
					}
					else
					{
						nearestWater = distance;
						water = Mathf.Lerp(a.y, b.y, t);
					}
				}
			}
			if (water > level)
			{
				level = water;
				lava = false;
				any = true;
			}
			if (molten > level)
			{
				level = molten;
				lava = true;
				any = true;
			}
			return any;
		}

		/// <summary>
		/// Metres from a scene position out to the nearest perennial river's bank: negative inside a channel, positive
		/// outside it. Exact for any channel within <see cref="IndexCell"/> metres; <see cref="float.PositiveInfinity"/>
		/// where none is. Rivers that start or end in the lake <paramref name="exceptLake"/> names are left out (−1
		/// leaves none out), and so are rivers of the other liquid: for <paramref name="lava"/> false only water's channels
		/// count, for true only lava's (a lava tube's river under a lake on the hill is no channel through that lake).
		/// </summary>
		/// <remarks>
		/// Asks the index cells round the point's as well as its own: a segment is filed only within about one and a
		/// quarter widths plus two metres of its banks, so a creek's own cells alone would lose it a few metres out.
		/// </remarks>
		public float OutsideChannels(float x, float z, int exceptLake = -1, bool lava = false)
		{
			float outside = float.PositiveInfinity;
			if (Hydrology == null)
			{
				return outside;
			}
			if (!built)
			{
				Build();
			}
			int cx = Mathf.FloorToInt(x / IndexCell), cz = Mathf.FloorToInt(z / IndexCell);
			for (int dz = -1; dz <= 1; dz++)
			{
				for (int dx = -1; dx <= 1; dx++)
				{
					long key = ((long)(cx + dx) << 32) ^ (uint)(cz + dz);
					if (!index.TryGetValue(key, out List<Segment> near))
					{
						continue;
					}
					foreach (Segment segment in near)
					{
						if (segment.Jet)
						{
							continue;
						}
						SceneHydrology.River river = Hydrology.Rivers[segment.River];
						if (!river.Perennial || river.Lava != lava || exceptLake >= 0 && (river.StartLake == exceptLake || river.EndLake == exceptLake))
						{
							continue;
						}
						Vector3 a = river.Points[segment.Point], b = river.Points[segment.Point + 1];
						float t = Project(a, b, x, z, out float distance);
						outside = Mathf.Min(outside, distance - HalfAt(river, segment.Point, t, a, b, x, z));
					}
				}
			}
			return outside;
		}

		/// <summary>Eight unit directions round a point.</summary>
		private static readonly Vector2[] Around =
		{
			new Vector2(1f, 0f), new Vector2(-1f, 0f), new Vector2(0f, 1f), new Vector2(0f, -1f),
			new Vector2(0.70710678f, 0.70710678f), new Vector2(-0.70710678f, 0.70710678f),
			new Vector2(0.70710678f, -0.70710678f), new Vector2(-0.70710678f, -0.70710678f),
		};

		/// <summary>Whether a lake's mask covers a point, or (with a reach) any of the eight points that far round it.</summary>
		private static bool LakeCovers(SceneHydrology.Lake lake, float x, float z, float reach)
		{
			Rect bounds = lake.Bounds;
			if (x < bounds.xMin - reach || x > bounds.xMax + reach || z < bounds.yMin - reach || z > bounds.yMax + reach)
			{
				return false;
			}
			if (bounds.Contains(new Vector2(x, z)) && lake.Covers(x, z))
			{
				return true;
			}
			if (reach <= 0f)
			{
				return false;
			}
			for (int k = 0; k < 8; k++)
			{
				float px = x + Around[k].x * reach, pz = z + Around[k].y * reach;
				if (bounds.Contains(new Vector2(px, pz)) && lake.Covers(px, pz))
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>True when a point is under the surface of a lake or a river it stands in (not one on a level above it).</summary>
		public bool IsUnder(Vector3 point) => TryGetSurface(point.x, point.y, point.z, out float level, out _) && point.y < level;

		/// <summary>The current at a scene position where water stands there; zero elsewhere.</summary>
		public Vector2 CurrentAt(float x, float z) => TryGetSurface(x, z, out _, out bool lava) ? FlowAt(x, z, out _, out _, lava) : Vector2.zero;

		/// <summary>
		/// The scene's flow at a position, m/s: every nearby river's current blended by nearness, its jets
		/// spreading into the lakes and the sea they end in. Also how strongly any river reaches the point,
		/// 0 … 1, and how broken the water is there (0 smooth … 1 a fall).
		/// </summary>
		public Vector2 FlowAt(float x, float z, out float reach, out float broken) => FlowAt(x, z, out reach, out broken, false);

		/// <summary>
		/// <see cref="FlowAt(float, float, out float, out float)"/> of one liquid: the lava rivers' flow for <paramref name="lava"/>
		/// true, the water's for false. Each is its own: a lava tube's river under a lake on the hill moves none of its water.
		/// </summary>
		public Vector2 FlowAt(float x, float z, out float reach, out float broken, bool lava) => FlowAt(x, z, out reach, out broken, lava, float.PositiveInfinity);

		/// <summary>
		/// <see cref="FlowAt(float, float, out float, out float, bool)"/> of the water a height stands in (2026-10-21): rivers whose
		/// floor lies over it (a level above) move none of it.
		/// </summary>
		public Vector2 FlowAt(float x, float z, out float reach, out float broken, bool lava, float height)
		{
			reach = 0f;
			broken = 0f;
			List<Segment> near = Near(x, z);
			if (near == null || Hydrology == null)
			{
				return Vector2.zero;
			}
			Vector2 sum = Vector2.zero;
			float weights = 0f, brokenSum = 0f, strongest = 0f;
			foreach (Segment segment in near)
			{
				SceneHydrology.River river = Hydrology.Rivers[segment.River];
				if (!river.Perennial || river.Lava != lava)
				{
					continue;
				}
				int i = segment.Point;
				Vector3 a, b;
				float half, speed, fade = 1f, rough;
				if (segment.Jet)
				{
					a = river.Points[i];
					// Into the water at its own end, a level above or not.
					if (!Holds(river.Bed != null && i < river.Bed.Length ? river.Bed[i] : a.y - 1f, height))
					{
						continue;
					}
					Vector3 along = a - river.Points[i - 1];
					along.y = 0f;
					along = along.sqrMagnitude > 1e-6f ? along.normalized : Vector3.forward;
					float length = JetWidths * Width(river, i);
					b = a + along * length;
					float t0 = Project(a, b, x, z, out float d0);
					// A jet slows and widens as it goes: half its speed by a third of the way.
					fade = Mathf.Exp(-3f * t0);
					half = 0.5f * Width(river, i) * (1f + 2f * t0);
					speed = Speed(river, i) * fade;
					rough = 0f;
					float w0 = Kernel(d0, half) * fade;
					if (w0 <= 1e-4f)
					{
						continue;
					}
					sum += new Vector2(along.x, along.z) * speed * w0;
					weights += w0;
					strongest = Mathf.Max(strongest, w0);
					continue;
				}
				a = river.Points[i];
				b = river.Points[i + 1];
				float t = Project(a, b, x, z, out float distance);
				if (!Holds(FloorOf(river, i, t, a, b), height))
				{
					continue;
				}
				half = 0.5f * Mathf.Lerp(Width(river, i), Width(river, i + 1), t);
				speed = Mathf.Lerp(Speed(river, i), Speed(river, i + 1), t);
				rough = Broken(river, t < 0.5f ? i : i + 1);
				float w = Kernel(distance, half);
				if (w <= 1e-4f)
				{
					continue;
				}
				var direction = new Vector2(b.x - a.x, b.z - a.z);
				direction = direction.sqrMagnitude > 1e-8f ? direction.normalized : Vector2.zero;
				sum += direction * speed * w;
				brokenSum += rough * w;
				weights += w;
				strongest = Mathf.Max(strongest, w);
			}
			if (weights <= 1e-5f)
			{
				return Vector2.zero;
			}
			reach = Mathf.Clamp01(strongest);
			broken = brokenSum / weights;
			return sum / weights;
		}

		/// <summary>How much a point is a river's own, from its distance off the line and the half-width there: 1 inside the channel, falling off over its banks.</summary>
		private static float Kernel(float distance, float half)
		{
			float u = distance / Mathf.Max(0.5f, half);
			return u <= 1f ? 1f : Mathf.Exp(-(u - 1f) * (u - 1f) * 2f);
		}

		/// <summary>How broken the water is at a river point, from what it is doing there.</summary>
		private static float Broken(SceneHydrology.River river, int i)
		{
			if (river.Reach == null || i >= river.Reach.Length)
			{
				return 0f;
			}
			switch (river.Reach[i])
			{
				case 4: return 1f;
				case 3: return 0.6f;
				case 1: return 0.15f;
				default: return 0f;
			}
		}

		/// <summary>The share along a→b of the point nearest (x, z), and the horizontal distance to it.</summary>
		private static float Project(Vector3 a, Vector3 b, float x, float z, out float distance)
		{
			float ex = b.x - a.x, ez = b.z - a.z;
			float lengthSq = ex * ex + ez * ez;
			float t = lengthSq > 1e-8f ? Mathf.Clamp01(((x - a.x) * ex + (z - a.z) * ez) / lengthSq) : 0f;
			float dx = x - (a.x + ex * t), dz = z - (a.z + ez * t);
			distance = Mathf.Sqrt(dx * dx + dz * dz);
			return t;
		}
	}
}
