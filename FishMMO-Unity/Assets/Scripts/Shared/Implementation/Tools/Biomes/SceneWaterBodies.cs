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
	/// </remarks>
	public sealed class SceneWaterBodies : MonoBehaviour, SurfaceWater.IAreaSource
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
					float reach = 0.5f * Mathf.Max(Width(river, i), Width(river, i + 1)) * 2.5f + 2f;
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
		public bool TryGetSurface(float x, float z, out float level) => TryGetSurfaceNear(x, z, 0f, out level);

		/// <summary>
		/// Whether any lake or perennial river may stand within <paramref name="area"/> (scene xz, <c>yMin</c>/<c>yMax</c>
		/// north), its banks and <see cref="MaxReachMetres"/> included: lets a map over a dry terrain skip asking at every point.
		/// </summary>
		public bool Reaches(Rect area)
		{
			if (Hydrology == null)
			{
				return false;
			}
			foreach (SceneHydrology.Lake lake in Hydrology.Lakes)
			{
				Rect bounds = lake.Bounds;
				if (bounds.xMax + MaxReachMetres >= area.xMin && bounds.xMin - MaxReachMetres <= area.xMax &&
					bounds.yMax + MaxReachMetres >= area.yMin && bounds.yMin - MaxReachMetres <= area.yMax)
				{
					return true;
				}
			}
			foreach (SceneHydrology.River river in Hydrology.Rivers)
			{
				if (!river.Perennial || river.Points == null)
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
		/// The highest water surface over a scene position or within <paramref name="reach"/> metres of it (at most
		/// <see cref="MaxReachMetres"/>): a river's past its banks by that much, a lake's where its mask covers the
		/// point or a point that far off. For maps sampled on a grid coarser than a creek: a 2 m heightmap can have no
		/// sample inside a 1.5 m channel, and the water still has to reach the samples either side of it.
		/// </summary>
		public bool TryGetSurfaceNear(float x, float z, float reach, out float level)
		{
			level = float.NegativeInfinity;
			bool any = false;
			if (Hydrology == null)
			{
				return false;
			}
			reach = Mathf.Clamp(reach, 0f, MaxReachMetres);
			foreach (SceneHydrology.Lake lake in Hydrology.Lakes)
			{
				if (lake.Level > level && LakeCovers(lake, x, z, reach))
				{
					level = lake.Level;
					any = true;
				}
			}
			List<Segment> near = Near(x, z);
			if (near == null)
			{
				return any;
			}
			foreach (Segment segment in near)
			{
				if (segment.Jet)
				{
					continue;
				}
				SceneHydrology.River river = Hydrology.Rivers[segment.River];
				if (!river.Perennial)
				{
					continue;
				}
				Vector3 a = river.Points[segment.Point], b = river.Points[segment.Point + 1];
				float t = Project(a, b, x, z, out float distance);
				float half = 0.5f * Mathf.Lerp(Width(river, segment.Point), Width(river, segment.Point + 1), t);
				if (distance <= half + reach)
				{
					float here = Mathf.Lerp(a.y, b.y, t);
					if (here > level)
					{
						level = here;
						any = true;
					}
				}
			}
			return any;
		}

		/// <summary>
		/// Metres from a scene position out to the nearest perennial river's bank: negative inside a channel, positive
		/// outside it. Exact for any channel within <see cref="IndexCell"/> metres; <see cref="float.PositiveInfinity"/>
		/// where none is. Rivers that start or end in the lake <paramref name="exceptLake"/> names are left out (−1
		/// leaves none out).
		/// </summary>
		/// <remarks>
		/// Asks the index cells round the point's as well as its own: a segment is filed only within about one and a
		/// quarter widths plus two metres of its banks, so a creek's own cells alone would lose it a few metres out.
		/// </remarks>
		public float OutsideChannels(float x, float z, int exceptLake = -1)
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
						if (!river.Perennial || exceptLake >= 0 && (river.StartLake == exceptLake || river.EndLake == exceptLake))
						{
							continue;
						}
						Vector3 a = river.Points[segment.Point], b = river.Points[segment.Point + 1];
						float t = Project(a, b, x, z, out float distance);
						float half = 0.5f * Mathf.Lerp(Width(river, segment.Point), Width(river, segment.Point + 1), t);
						outside = Mathf.Min(outside, distance - half);
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

		/// <summary>True when a point is under a lake's or a river's surface.</summary>
		public bool IsUnder(Vector3 point) => TryGetSurface(point.x, point.z, out float level) && point.y < level;

		/// <summary>The current at a scene position where water stands there; zero elsewhere.</summary>
		public Vector2 CurrentAt(float x, float z) => TryGetSurface(x, z, out _) ? FlowAt(x, z, out _, out _) : Vector2.zero;

		/// <summary>
		/// The scene's flow at a position, m/s: every nearby river's current blended by nearness, its jets
		/// spreading into the lakes and the sea they end in. Also how strongly any river reaches the point,
		/// 0 … 1, and how broken the water is there (0 smooth … 1 a fall).
		/// </summary>
		public Vector2 FlowAt(float x, float z, out float reach, out float broken)
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
				if (!river.Perennial)
				{
					continue;
				}
				int i = segment.Point;
				Vector3 a, b;
				float half, speed, fade = 1f, rough;
				if (segment.Jet)
				{
					a = river.Points[i];
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
