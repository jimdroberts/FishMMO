using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace FishMMO.Shared.Biomes
{
	/// <summary>
	/// The sculpted landforms' shapes (2026-10-18, phase I2g): each family a pure function of where a landform was placed
	/// (<see cref="BiomeLandformSite"/>) and the point asked, writing the ground's new height there, and the paint masks of its
	/// parts; and the carve of a set of them into a height grid with the record that takes it out again exactly.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Deterministic and bounded.</b> A shape reads only its site, the point, the ground's height there and the ground's slope
	/// before any landform was carved (so the order the cut carves them in is the only thing that matters, and it is fixed). Every
	/// shape is zero outside its site's <see cref="BiomeLandformSite.Bounds"/> and meets the untouched ground there without a step:
	/// a mound's flank, a crater's ejecta, a ridge's taper and a field's biome weight all fall to nothing before the edge.
	/// </para>
	/// <para>
	/// <b>Record and restore.</b> <see cref="Carve"/> keeps the original height of every sample it changed; <see cref="Restore"/>
	/// writes them back bit for bit, so restoring and carving again gives exactly one carve, however often it is repeated.
	/// </para>
	/// </remarks>
	public static partial class BiomeLandforms
	{
		/// <summary>The steepest a dune's slip face stands, degrees (dry sand's angle of repose).</summary>
		public const float ReposeDegrees = 34f;

		/// <summary>The slope over which dune sand gives way to rock in the paint, degrees (sand held to about here, rock past it).</summary>
		public const float SandRockDegrees = 40f;

		/// <summary>An empty list: no landform (every biome's default). A fresh list each call.</summary>
		public static List<BiomeLandform> None() => new List<BiomeLandform>();

		/// <summary>The family a form belongs to (its high nibble).</summary>
		public static BiomeLandformFamily FamilyOf(BiomeLandformForm form) => (BiomeLandformFamily)((byte)form >> 4);

		/// <summary>True when a list carves anything.</summary>
		public static bool Any(IReadOnlyList<BiomeLandform> forms)
		{
			if (forms != null)
			{
				foreach (BiomeLandform form in forms)
				{
					if (form.Carves)
					{
						return true;
					}
				}
			}
			return false;
		}

		/// <summary>True when a form is drawn over a whole patch (dunes, waves, flow steps, spill fill) whatever its entry says.</summary>
		public static bool IsField(BiomeLandformForm form)
		{
			BiomeLandformFamily family = FamilyOf(form);
			return family == BiomeLandformFamily.Dunes || family == BiomeLandformFamily.Waves || form == BiomeLandformForm.FlowSteps || form == BiomeLandformForm.SpillFill
				|| form == BiomeLandformForm.Bench;
		}

		/// <summary>
		/// An ageing crater's class by its seed (2026-10-25, decision 72, pure): 0 fresh (6 %), 1 young (14 %), 2 mature (45 %), 3 old (35 %).
		/// </summary>
		public static int CraterAge(uint seed)
		{
			float u = Unit(Hash(seed, 0xA6Eu));
			return u < 0.06f ? 0 : u < 0.2f ? 1 : u < 0.65f ? 2 : 3;
		}

		/// <summary>An ageing crater's depth as a share of its diameter by class (fresh 0.20 … old 0.04).</summary>
		public static float CraterAgeDepth(int age) => age <= 0 ? 0.2f : age == 1 ? 0.15f : age == 2 ? 0.1f : 0.04f;

		/// <summary>A power-law draw over a size range (pure): N(> D) ∝ D^−power between its ends; an even draw when power is 0.</summary>
		public static float PowerLawPick(Vector2 range, float power, float roll)
		{
			float low = Mathf.Max(0.01f, Mathf.Min(range.x, range.y)), high = Mathf.Max(range.x, range.y);
			if (power <= 0f || high <= low)
			{
				return Pick(range, roll);
			}
			float tail = MathF.Pow(low / high, power);
			return Mathf.Min(high, low * MathF.Pow(1f - Mathf.Clamp01(roll) * (1f - tail), -1f / power));
		}

		/// <summary>True when a form is drawn along a stored line (ridges but the ring, channels).</summary>
		public static bool IsLine(BiomeLandformForm form)
		{
			BiomeLandformFamily family = FamilyOf(form);
			return family == BiomeLandformFamily.Channels || family == BiomeLandformFamily.Ridge && form != BiomeLandformForm.Ring;
		}

		// ── Small pure helpers ────────────────────────────────────────

		/// <summary>True when <paramref name="value"/> lies in a (min, max) band; a band of (0, 0) is open.</summary>
		public static bool InBand(Vector2 band, float value)
		{
			if (band.x == 0f && band.y == 0f)
			{
				return true;
			}
			float lo = Mathf.Min(band.x, band.y), hi = Mathf.Max(band.x, band.y);
			return value >= lo && value <= hi;
		}

		/// <summary>A value in a (min, max) range by a roll in [0, 1).</summary>
		public static float Pick(Vector2 range, float roll)
		{
			float lo = Mathf.Min(range.x, range.y), hi = Mathf.Max(range.x, range.y);
			return lo + (hi - lo) * Mathf.Clamp01(roll);
		}

		/// <summary>A 32-bit mix of up to four words.</summary>
		public static uint Hash(uint a, uint b, uint c = 0u, uint d = 0u)
		{
			uint h = a * 0x8da6b343u ^ b * 0xd8163841u ^ c * 0xcb1ab31fu ^ d * 0x165667b1u;
			h ^= h >> 16;
			h *= 0x7feb352du;
			h ^= h >> 15;
			h *= 0x846ca68bu;
			h ^= h >> 16;
			return h;
		}

		/// <summary>A hash as a value in [0, 1).</summary>
		public static float Unit(uint h) => (h >> 8) * (1f / 16777216f);

		/// <summary>Smooth value noise in [0, 1) (pure: no native noise, the same on every machine).</summary>
		public static float Noise(float x, float y, uint seed)
		{
			int ix = (int)MathF.Floor(x), iy = (int)MathF.Floor(y);
			float fx = x - ix, fy = y - iy;
			fx = fx * fx * (3f - 2f * fx);
			fy = fy * fy * (3f - 2f * fy);
			float a = Unit(Hash((uint)ix, (uint)iy, seed)), b = Unit(Hash((uint)(ix + 1), (uint)iy, seed));
			float c = Unit(Hash((uint)ix, (uint)(iy + 1), seed)), d = Unit(Hash((uint)(ix + 1), (uint)(iy + 1), seed));
			float south = a + (b - a) * fx, north = c + (d - c) * fx;
			return south + (north - south) * fy;
		}

		private static float S01(float t)
		{
			t = t < 0f ? 0f : t > 1f ? 1f : t;
			return t * t * (3f - 2f * t);
		}

		/// <summary>A smooth bump: 1 at 0, 0 at 1 and past it, flat at both ends.</summary>
		public static float Bump(float t)
		{
			t = MathF.Abs(t);
			if (t >= 1f)
			{
				return 0f;
			}
			float s = 1f - t * t;
			return s * s;
		}

		private static float Frac(float x) => x - MathF.Floor(x);

		// ── The ground a shape reads ──────────────────────────────────

		/// <summary>The ground at a point before any landform was carved: its slope (degrees) and the way down it (unit, xz).</summary>
		public struct GroundInfo
		{
			public float Slope;
			public Vector2 Downhill;
		}

		/// <summary>What every shape of a cut shares: the sea's surface and each biome's weight at a point (for field landforms).</summary>
		public sealed class Context
		{
			/// <summary>The sea's surface, scene metres; negative infinity for none.</summary>
			public float SeaLevel = float.NegativeInfinity;

			/// <summary>
			/// A biome's weight at a world point (biome id, x, z), 0 … 1: what a field landform covers. Null: 1 everywhere in its bounds.
			/// Called from worker threads.
			/// </summary>
			public Func<int, float, float, float> BiomeWeight;

			public float WeightOf(int biomeID, float x, float z) => BiomeWeight != null ? Mathf.Clamp01(BiomeWeight(biomeID, x, z)) : 1f;
		}

		// ── Bounds ────────────────────────────────────────────────────

		/// <summary>The world box (min x, min z, max x, max z) a site changes, from its place and shape alone.</summary>
		public static Vector4 BoundsOf(in BiomeLandformSite s)
		{
			BiomeLandform f = s.Form;
			if (IsField(f.Form) && (s.Bounds.z > s.Bounds.x || s.Bounds.w > s.Bounds.y))
			{
				return s.Bounds;
			}
			if (f.Form == BiomeLandformForm.Impact && f.RayReach > 0f && (s.Bounds.z > s.Bounds.x || s.Bounds.w > s.Bounds.y))
			{
				// Long rays clipped to the scene by the planner (2026-10-26, I2g-f): the box it kept, never inside the bowl's 2 radii.
				float c = 2f * s.Radius;
				return new Vector4(Mathf.Min(s.Bounds.x, s.Centre.x - c), Mathf.Min(s.Bounds.y, s.Centre.y - c), Mathf.Max(s.Bounds.z, s.Centre.x + c), Mathf.Max(s.Bounds.w, s.Centre.y + c));
			}
			if (IsLine(f.Form) && s.Segments != null && s.Segments.Count > 0)
			{
				float reach = 0f;
				foreach (BiomeLandformSegment seg in s.Segments)
				{
					reach = Mathf.Max(reach, seg.Half);
				}
				// A graben's Moat is its set's spacing, not a ditch beside it.
				float grow = FamilyOf(f.Form) == BiomeLandformFamily.Channels ? reach * 3.2f : reach * (1f + Mathf.Abs(f.Lean)) + (f.Form == BiomeLandformForm.Graben ? 0f : f.Moat);
				if (f.Form == BiomeLandformForm.Scarp)
				{
					grow = reach + Mathf.Max(1f, s.Radius);
				}
				float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
				foreach (BiomeLandformSegment seg in s.Segments)
				{
					minX = Mathf.Min(minX, Mathf.Min(seg.A.x, seg.B.x));
					maxX = Mathf.Max(maxX, Mathf.Max(seg.A.x, seg.B.x));
					minZ = Mathf.Min(minZ, Mathf.Min(seg.A.y, seg.B.y));
					maxZ = Mathf.Max(maxZ, Mathf.Max(seg.A.y, seg.B.y));
				}
				return new Vector4(minX - grow, minZ - grow, maxX + grow, maxZ + grow);
			}
			float r = Mathf.Max(s.Radius, s.HalfLength);
			switch (FamilyOf(f.Form))
			{
				case BiomeLandformFamily.Mound:
				{
					// The moat is measured across the minor radius, so on a long mound (and a keyhole's front) it reaches further along its length.
					float scale = f.Form == BiomeLandformForm.Keyhole ? Mathf.Max(r, 2f * s.HalfLength - s.Radius) : r;
					// A square plan's corners reach past its half sides (2026-10-25, I2g-b): a sixth-power square's by 0.89 √2.
					scale *= f.Form == BiomeLandformForm.Pyramid ? 1.3f : 1f;
					r = scale * (1.02f + f.Moat / Mathf.Max(0.01f, s.Radius));
				}
					break;
				case BiomeLandformFamily.Crater:
					// A rampart crater's lobes reach past twice its radius (decision 72).
					r = s.Radius * (f.Form == BiomeLandformForm.Rampart ? Mathf.Max(2f, RampartReach(f) * 1.15f + 0.3f) : 2f);
					// Long rays and a halo's parabola reach their RayReach (2026-10-26, I2g-f); they paint only, the carve ends by 2 radii.
					r = f.Form == BiomeLandformForm.Impact && f.RayReach > 0f ? Mathf.Max(r, s.Radius * Mathf.Min(f.RayReach, 12f) * 1.02f) : r;
					break;
				case BiomeLandformFamily.Depression:
					r = f.Form == BiomeLandformForm.Hollow && f.Count > 1
						? Mathf.Max(r * 1.4f, s.Radius * 0.72f + 0.55f * Mathf.Max(f.Width.x, f.Width.y) + 0.5f)
						: f.Form == BiomeLandformForm.Tank ? r * 1.65f : r * 1.4f;
					break;
				case BiomeLandformFamily.Ridge:
					// The ring: its radius, rim and ditch.
					r = s.Radius + s.HalfWidth * (1f + Mathf.Abs(f.Lean)) + f.Moat;
					break;
				case BiomeLandformFamily.Mesa when f.Form == BiomeLandformForm.Table && s.Segments != null && s.Segments.Count > 0:
				{
					// A sea-standing table's box takes in its neck to the shore (decision 74).
					Vector4 box = new Vector4(s.Centre.x - r * 1.02f, s.Centre.y - r * 1.02f, s.Centre.x + r * 1.02f, s.Centre.y + r * 1.02f);
					foreach (BiomeLandformSegment seg in s.Segments)
					{
						float reach = seg.Half * 1.6f;
						box = new Vector4(Mathf.Min(box.x, Mathf.Min(seg.A.x, seg.B.x) - reach), Mathf.Min(box.y, Mathf.Min(seg.A.y, seg.B.y) - reach),
							Mathf.Max(box.z, Mathf.Max(seg.A.x, seg.B.x) + reach), Mathf.Max(box.w, Mathf.Max(seg.A.y, seg.B.y) + reach));
					}
					return box;
				}
				default:
					// A tongue, slide or rimstone flight is a rectangle along its axis: its corners reach the diagonal.
					r = f.Form == BiomeLandformForm.Tongue || f.Form == BiomeLandformForm.Slide || f.Form == BiomeLandformForm.Rimstone
						? 1.02f * Mathf.Sqrt(s.HalfLength * s.HalfLength + s.Radius * s.Radius)
						: r * 1.02f;
					break;
			}
			return new Vector4(s.Centre.x - r, s.Centre.y - r, s.Centre.x + r, s.Centre.y + r);
		}

		// ── A placed shape ────────────────────────────────────────────

		/// <summary>
		/// One site made ready to be asked many points (its trig, its line's buckets, its crater's rays and secondaries worked out once).
		/// Thread-safe once built: <see cref="Apply"/> and <see cref="Mask"/> only read it.
		/// </summary>
		public sealed partial class Shape
		{
			public readonly BiomeLandformSite Site;
			private readonly Context context;
			private readonly float cos, sin;
			private readonly float radius, halfLength;
			private readonly float[] rayAngles;
			private readonly Vector3[] secondaries;
			private readonly Vector3[] cluster;
			private readonly Dictionary<long, List<int>> buckets;
			private readonly float bucketMetres;
			private readonly float lineReach;
			private readonly Dictionary<long, Vector2> rillHeadings;

			public Shape(in BiomeLandformSite site, Context context)
			{
				Site = site;
				this.context = context ?? new Context();
				float yaw = site.Yaw * Mathf.Deg2Rad;
				cos = MathF.Cos(yaw);
				sin = MathF.Sin(yaw);
				radius = Mathf.Max(0.01f, site.Radius);
				halfLength = Mathf.Max(radius, site.HalfLength);
				BiomeLandform f = site.Form;

				// An ageing crater past fresh has lost its rays and its secondaries (decision 72); a fresh one keeps 5–9 rays.
				int age = f.Ages ? CraterAge(site.Seed) : 0;
				if (f.Form == BiomeLandformForm.Impact && age == 0)
				{
					// A chain's Count is its members', not each crater's rays.
					int rays = f.Ages ? 5 + (int)(Hash(site.Seed, 0xA6Fu) % 5u) : f.Count > 0 && f.Placement != BiomeLandformPlacement.Chain ? f.Count : 7;
					rayAngles = new float[rays];
					for (int i = 0; i < rays; i++)
					{
						rayAngles[i] = (i + 0.7f * (Unit(Hash(site.Seed, 0x5A1u, (uint)i)) - 0.5f)) * (MathF.PI * 2f / rays);
					}
					int n = 1 + (int)(Hash(site.Seed, 0x5EC0u) % 3u);
					secondaries = new Vector3[n];
					for (int i = 0; i < n; i++)
					{
						float a = Unit(Hash(site.Seed, 0x5EC1u, (uint)i)) * MathF.PI * 2f;
						float d = radius * (1.3f + 0.45f * Unit(Hash(site.Seed, 0x5EC2u, (uint)i)));
						secondaries[i] = new Vector3(site.Centre.x + MathF.Cos(a) * d, site.Centre.y + MathF.Sin(a) * d, radius * 0.1f);
					}
				}
				if (f.Form == BiomeLandformForm.MultiRing)
				{
					PrepareBasin();
				}
				if (f.Form == BiomeLandformForm.RingGraben)
				{
					PrepareGraben();
				}
				if (FamilyOf(f.Form) == BiomeLandformFamily.Dunes && f.Wake != Vector2.zero)
				{
					PrepareWakes();
				}
				if (f.Form == BiomeLandformForm.Hollow && f.Count > 1)
				{
					cluster = new Vector3[f.Count];
					for (int i = 0; i < f.Count; i++)
					{
						float a = Unit(Hash(site.Seed, 0xC1u, (uint)i)) * MathF.PI * 2f;
						float d = radius * 0.72f * MathF.Sqrt(Unit(Hash(site.Seed, 0xC2u, (uint)i)));
						float r = 0.5f * Pick(f.Width.x > 0f || f.Width.y > 0f ? f.Width : new Vector2(radius * 0.3f, radius * 0.3f), Unit(Hash(site.Seed, 0xC3u, (uint)i)));
						cluster[i] = new Vector3(site.Centre.x + MathF.Cos(a) * d, site.Centre.y + MathF.Sin(a) * d, Mathf.Max(0.5f, r));
					}
				}
				if (f.Form == BiomeLandformForm.Rills && site.Segments != null && site.Segments.Count > 0)
				{
					float cell = RillCell(Mathf.Max(1f, site.HalfWidth * 2f));
					rillHeadings = new Dictionary<long, Vector2>();
					foreach (BiomeLandformSegment seg in site.Segments)
					{
						int ci = Mathf.FloorToInt(seg.A.x / cell), cj = Mathf.FloorToInt(seg.A.y / cell);
						rillHeadings[((long)cj << 32) ^ (uint)ci] = seg.B;
					}
				}
				if (IsLine(f.Form) && site.Segments != null && site.Segments.Count > 0)
				{
					float reach = 0f;
					foreach (BiomeLandformSegment seg in site.Segments)
					{
						reach = Mathf.Max(reach, seg.Half);
					}
					lineReach = FamilyOf(f.Form) == BiomeLandformFamily.Channels ? reach * 3.2f
						: f.Form == BiomeLandformForm.Scarp ? reach + Mathf.Max(1f, site.Radius) : reach * (1f + Mathf.Abs(f.Lean)) + (f.Form == BiomeLandformForm.Graben ? 0f : f.Moat);
					bucketMetres = Mathf.Max(8f, lineReach);
					buckets = new Dictionary<long, List<int>>();
					for (int i = 0; i < site.Segments.Count; i++)
					{
						BiomeLandformSegment seg = site.Segments[i];
						int x0 = Mathf.FloorToInt((Mathf.Min(seg.A.x, seg.B.x) - lineReach) / bucketMetres), x1 = Mathf.FloorToInt((Mathf.Max(seg.A.x, seg.B.x) + lineReach) / bucketMetres);
						int z0 = Mathf.FloorToInt((Mathf.Min(seg.A.y, seg.B.y) - lineReach) / bucketMetres), z1 = Mathf.FloorToInt((Mathf.Max(seg.A.y, seg.B.y) + lineReach) / bucketMetres);
						for (int z = z0; z <= z1; z++)
						{
							for (int x = x0; x <= x1; x++)
							{
								long key = ((long)z << 32) ^ (uint)x;
								if (!buckets.TryGetValue(key, out List<int> list))
								{
									list = new List<int>();
									buckets[key] = list;
								}
								list.Add(i);
							}
						}
					}
				}
			}

			/// <summary>The site's box (<see cref="BoundsOf"/>).</summary>
			public Vector4 Bounds => BoundsOf(Site);

			private void Local(float x, float z, out float u, out float v)
			{
				float dx = x - Site.Centre.x, dz = z - Site.Centre.y;
				u = dx * cos + dz * sin;
				v = -dx * sin + dz * cos;
			}

			private float FieldWeight(float x, float z, float original, float slope)
			{
				BiomeLandform f = Site.Form;
				Vector4 b = Site.Bounds;
				if (x < b.x || x > b.z || z < b.y || z > b.w)
				{
					return 0f;
				}
				float w = context.WeightOf(Site.BiomeID, x, z);
				if (w <= 0f)
				{
					return 0f;
				}
				// The slope and depth bands as soft gates over a field (rills only on steep ground, sediment waves only so deep).
				if (!(f.Slope.x == 0f && f.Slope.y == 0f))
				{
					float lo = Mathf.Min(f.Slope.x, f.Slope.y), hi = Mathf.Max(f.Slope.x, f.Slope.y);
					w *= S01((slope - lo + 3f) / 6f) * (1f - S01((slope - hi + 3f) / 6f));
				}
				if (!(f.Depth.x == 0f && f.Depth.y == 0f) && !float.IsNegativeInfinity(context.SeaLevel))
				{
					float depth = context.SeaLevel - original;
					float lo = Mathf.Min(f.Depth.x, f.Depth.y), hi = Mathf.Max(f.Depth.x, f.Depth.y);
					float soft = Mathf.Max(1f, 0.1f * (hi - lo));
					w *= S01((depth - lo + soft) / (2f * soft)) * (1f - S01((depth - hi + soft) / (2f * soft)));
				}
				if (f.Upper > 0f && !float.IsNaN(Site.Level))
				{
					// Only the highest share of its patch (2026-10-26, I2g-f: the bladed ridges on the top fifth), its level the
					// planner's quantile of the patch's ground, faded in over half the field's height either side of it.
					float soft = Mathf.Max(2f, 0.5f * Site.Height);
					w *= S01((original - Site.Level + soft) / (2f * soft));
				}
				if (FamilyOf(f.Form) == BiomeLandformFamily.Dunes && Site.MixHigh > Site.MixLow && !(Site.MixLow <= 0f && Site.MixHigh >= 1f))
				{
					w *= MixBand(Noise(x / Mathf.Max(64f, Site.HalfWidth * 6f), z / Mathf.Max(64f, Site.HalfWidth * 6f), Site.MixSeed), Site.MixLow, Site.MixHigh);
				}
				return w;
			}

			/// <summary>The share of a mix noise value a band [low, high) of it takes, blended over 0.06 at each inner edge (pure).</summary>
			public static float MixBand(float n, float low, float high)
			{
				const float blend = 0.06f;
				float a = low <= 0f ? 1f : S01((n - low + blend * 0.5f) / blend);
				float b = high >= 1f ? 1f : 1f - S01((n - high + blend * 0.5f) / blend);
				return a * b;
			}

			/// <summary>
			/// The ground's new height at (x, z): the site carved into <paramref name="current"/> (what earlier sites left there).
			/// <paramref name="original"/> and <paramref name="ground"/> are the ground's height and slope before any landform.
			/// </summary>
			public float Apply(float x, float z, float current, float original, in GroundInfo ground)
			{
				BiomeLandform f = Site.Form;
				switch (FamilyOf(f.Form))
				{
					case BiomeLandformFamily.Mound: return Mound(x, z, current);
					case BiomeLandformFamily.Crater: return Crater(x, z, current);
					case BiomeLandformFamily.Depression: return Depression(x, z, current);
					case BiomeLandformFamily.Mesa:
						return f.Form == BiomeLandformForm.Bench ? Bench(x, z, current, original, ground.Slope) : Mesa(x, z, current);
					case BiomeLandformFamily.Ridge: return Ridge(x, z, current);
					case BiomeLandformFamily.Channels: return Channels(x, z, current);
					case BiomeLandformFamily.Lobe: return Lobe(x, z, current, original, ground);
					case BiomeLandformFamily.Dunes:
					{
						float w = FieldWeight(x, z, original, ground.Slope);
						return w > 0f ? current + w * DuneHeight(x, z) : current;
					}
					case BiomeLandformFamily.Waves:
					{
						float w = FieldWeight(x, z, original, ground.Slope);
						return w > 0f ? current + w * WaveHeight(x, z, ground) : current;
					}
					default: return current;
				}
			}

			// ── Mound ─────────────────────────────────────────────────

			private float MoundQ(float x, float z)
			{
				Local(x, z, out float u, out float v);
				BiomeLandform f = Site.Form;
				switch (f.Form)
				{
					case BiomeLandformForm.Keyhole:
					{
						float uc = -halfLength + radius;
						float qr = MathF.Sqrt((u - uc) * (u - uc) + v * v) / radius;
						float qf = float.MaxValue;
						if (u >= uc && halfLength > radius)
						{
							float t = (u - uc) / (halfLength - uc);
							float half = radius * (0.62f + 0.3f * Mathf.Clamp01(t));
							float a = t, b = MathF.Abs(v) / half;
							qf = MathF.Pow(a * a * a * a + b * b * b * b, 0.25f);
						}
						return Mathf.Min(qr, qf);
					}
					case BiomeLandformForm.Tower:
						return TowerQ(u, v);
					case BiomeLandformForm.Pyramid:
						return SquareQ(u / halfLength, v / radius, 6f);
					case BiomeLandformForm.Star:
					{
						float r = MathF.Sqrt(u * u + v * v);
						float theta = MathF.Atan2(v, u);
						int arms = Mathf.Max(3, f.Count);
						float warp = 0.3f * MathF.Sin(3f * r / radius + Unit(Site.Seed) * 6.28f);
						float arm = 0.5f + 0.5f * MathF.Cos(arms * (theta + warp));
						float scale = 0.42f + 0.58f * MathF.Pow(arm, 2.5f);
						return r / (radius * scale);
					}
					default:
						return MathF.Sqrt(u / halfLength * (u / halfLength) + v / radius * (v / radius));
				}
			}

			private float MoundProfile(float q)
			{
				BiomeLandform f = Site.Form;
				if (f.Form == BiomeLandformForm.Tower)
				{
					return TowerProfile(q);
				}
				float top = Mathf.Clamp(f.Top, 0f, 0.95f);
				float qq = top > 0f ? Mathf.Max(0f, (q - top) / (1f - top)) : q;
				if (qq >= 1f)
				{
					return 0f;
				}
				switch (f.Form)
				{
					case BiomeLandformForm.Cone:
						return MathF.Pow(1f - qq, 1.25f);
					case BiomeLandformForm.Star:
						return MathF.Pow(1f - qq, 1.5f);
					case BiomeLandformForm.Pyramid:
						return PyramidSteps(qq);
					case BiomeLandformForm.Platform:
					{
						float p = 1f - S01(qq);
						int steps = f.Count;
						if (steps > 1)
						{
							float level = p * steps;
							float k = MathF.Floor(level);
							p = Mathf.Min(1f, (k + S01((level - k - 0.75f) / 0.25f)) / steps);
						}
						return p;
					}
					default:
						return Bump(qq);
				}
			}

			private float Mound(float x, float z, float current)
			{
				BiomeLandform f = Site.Form;
				float q = MoundQ(x, z);
				float p = MoundProfile(q);
				if (f.Radials > 0 && p > 0f && p < 1f)
				{
					// Radial grooves down its flank (2026-10-26, I2g-f: a pancake dome's cooling cracks).
					p *= 1f - MoundRadial(x, z, p);
				}
				if (f.Form == BiomeLandformForm.Pyramid && p > 0f)
				{
					// The axial stairs run the tiers' height as one ramp (2026-10-25, I2g-b).
					Local(x, z, out float su, out float sv);
					float top = Mathf.Clamp(f.Top, 0f, 0.95f);
					float ramp = 1f - S01(top > 0f ? Mathf.Max(0f, (q - top) / (1f - top)) : q);
					p += (ramp - p) * StairAt(su, sv);
				}
				if (p > 0f && f.Roughness > 0f)
				{
					float scale = Mathf.Max(1f, radius * 0.35f);
					p *= 1f + f.Roughness * (Noise(x / scale, z / scale, Site.Seed ^ 0x40u) - 0.5f);
				}
				float target = !float.IsNaN(Site.Level) && f.Form == BiomeLandformForm.Platform
					? current + (Site.Level - current) * p
					// A tower or cone with a level stands to it from the sea's surface, raised only (2026-10-25, I2g-d: a pinnacle).
					: !float.IsNaN(Site.Level) ? Mathf.Max(current, current + (Site.Level - current) * Mathf.Min(1f, p))
					: current + Site.Height * p;
				if (f.Crown > 0f && f.CrownDepth != 0f)
				{
					float qc = q / f.Crown;
					if (qc < 1f)
					{
						target -= f.CrownDepth * Bump(qc);
					}
				}
				if (f.Breach != 0f && q < 1f)
				{
					target = MoundBreach(x, z, q, current, target);
				}
				if (f.Moat > 0f && f.MoatDepth > 0f && q > 1f)
				{
					float d = (q - 1f) * radius;
					if (d < f.Moat)
					{
						target -= f.MoatDepth * Bump(2f * d / f.Moat - 1f) * MoatShare(x, z);
					}
				}
				return target;
			}

			// ── Crater ────────────────────────────────────────────────

			private float RayAt(float theta)
			{
				if (rayAngles == null)
				{
					return 0f;
				}
				float best = 0f;
				foreach (float a in rayAngles)
				{
					float d = MathF.Abs(Mathf.DeltaAngle(theta * Mathf.Rad2Deg, a * Mathf.Rad2Deg)) * Mathf.Deg2Rad;
					best = Mathf.Max(best, MathF.Exp(-(d / 0.06f) * (d / 0.06f)));
				}
				return best;
			}

			private float Crater(float x, float z, float current)
			{
				BiomeLandform f = Site.Form;
				float dx = x - Site.Centre.x, dz = z - Site.Centre.y;
				float r = MathF.Sqrt(dx * dx + dz * dz) / radius;
				if (f.Form == BiomeLandformForm.MultiRing)
				{
					return Basin(x, z, current);
				}
				if (f.Form == BiomeLandformForm.Caldera)
				{
					return current + Breached(CalderaDelta(dx, dz, r), dx, dz, r);
				}
				if (f.Form == BiomeLandformForm.RingGraben)
				{
					return Graben(dx, dz, r, current);
				}
				float rim = Mathf.Max(0f, f.Rim) * 2f * radius;
				if (f.Form == BiomeLandformForm.Ghost)
				{
					float target = current;
					if (r < 1f && !float.IsNaN(Site.Level))
					{
						target += (Site.Level - current) * (1f - S01((r - 0.82f) / 0.18f));
					}
					return target + rim * MathF.Exp(-((r - 1f) / 0.08f) * ((r - 1f) / 0.08f)) * (1f - S01((r - 1.3f) / 0.2f));
				}
				float top = CraterTop();
				float delta = 0f;
				if (r < 1f)
				{
					float wall = 1f - S01((r - top) / (1f - top));
					// Terraced walls (2026-10-26, I2g-f: a young complex crater's slumped benches), the wall drawn as treads.
					delta -= Site.Height * (f.Steps > 0 && f.Form == BiomeLandformForm.Impact ? Stepped(wall, f.Steps + 1) : wall);
				}
				// An ageing crater's rim is lower and rounder by its class (decision 72).
				int age = f.Ages ? CraterAge(Site.Seed) : 0;
				float rimWidth = 0.13f + 0.04f * age;
				rim *= age == 0 ? 1f : age == 1 ? 0.8f : age == 2 ? 0.55f : 0.3f;
				delta += rim * MathF.Exp(-((r - 1f) / rimWidth) * ((r - 1f) / rimWidth)) * (1f - S01((r - 1.6f) / 0.4f));
				if (f.Form == BiomeLandformForm.Rampart)
				{
					return current + Breached(delta + RampartLobes(dx, dz, r), dx, dz, r);
				}
				if (r >= 1f && r < 2f)
				{
					float blanket = (1f / (r * r * r) - 0.125f) / 0.875f;
					delta += rim * 0.5f * blanket * (1f + 0.35f * RayAt(MathF.Atan2(dz, dx)));
				}
				// A central peak once the crater is large (complex: 3 km and over, or its own PeakFrom; 2026-10-26, I2g-f), or a
				// ring of peaks at its PeakRing share of the radius (a double-ring crater), Crown its half width.
				if (f.Crown > 0f && f.CrownDepth > 0f && radius * 2f >= (f.PeakFrom > 0f ? f.PeakFrom : 3000f))
				{
					delta += f.CrownDepth * 2f * radius * (f.PeakRing > 0f ? Bump((r - f.PeakRing) / f.Crown) : Bump(r / f.Crown));
				}
				if (secondaries != null)
				{
					foreach (Vector3 c in secondaries)
					{
						float ds = MathF.Sqrt((x - c.x) * (x - c.x) + (z - c.y) * (z - c.y)) / c.z;
						if (ds < 1.6f)
						{
							delta += c.z * 2f * (-0.18f * Bump(ds) + 0.03f * MathF.Exp(-((ds - 1f) / 0.2f) * ((ds - 1f) / 0.2f)) * (1f - S01((ds - 1.3f) / 0.3f)));
						}
					}
				}
				return current + Breached(delta, dx, dz, r);
			}

			// ── Depression ────────────────────────────────────────────

			private float Depression(float x, float z, float current)
			{
				BiomeLandform f = Site.Form;
				Local(x, z, out float u, out float v);
				float q = MathF.Sqrt(u / halfLength * (u / halfLength) + v / radius * (v / radius));
				float target = current;
				switch (f.Form)
				{
					case BiomeLandformForm.Pan:
					{
						float top = Mathf.Clamp(f.Top, 0f, 0.95f);
						float w = q >= 1f ? 0f : 1f - S01((q - top) / (1f - top));
						float floor = !float.IsNaN(Site.Level) ? Site.Level : Site.Ground - Site.Height;
						if (f.Roughness > 0f)
						{
							// A hummocky floor (2026-10-25, I2g-d: a chaos field's matrix), a share of its depth either way.
							float scale = Mathf.Max(4f, 0.08f * radius);
							floor += f.Roughness * 0.6f * Site.Height * (Noise(x / scale, z / scale, Site.Seed ^ 0x4Bu) - 0.5f);
						}
						target = current - w * Mathf.Max(0f, current - floor);
						break;
					}
					case BiomeLandformForm.Pit:
					{
						float w = 1f - S01((q - 0.9f) / 0.1f);
						if (f.Datum != BiomeLandformDatum.Ground && !float.IsNaN(Site.Level))
						{
							// Floored at a level under the sea's surface (2026-10-25, decision 68: a rift, a polynya open to the sea).
							target = current - w * Mathf.Max(0f, current - Site.Level);
							break;
						}
						target = current - w * Site.Height * (1f - 0.15f * q * q);
						break;
					}
					case BiomeLandformForm.Tank:
						return Tank(u, v, current);
					case BiomeLandformForm.Hollow:
					{
						if (cluster == null)
						{
							target = current - Site.Height * HollowProfile(q);
						}
						else
						{
							float cut = 0f;
							for (int i = 0; i < cluster.Length; i++)
							{
								Vector3 c = cluster[i];
								float qi = MathF.Sqrt((x - c.x) * (x - c.x) + (z - c.y) * (z - c.y)) / c.z;
								if (qi < 1f)
								{
									cut = Mathf.Max(cut, Pick(f.Height, Unit(Hash(Site.Seed, 0xC4u, (uint)i))) * HollowProfile(qi));
								}
							}
							target = current - cut;
						}
						break;
					}
				}
				if (f.Rim > 0f && f.Form != BiomeLandformForm.SpillFill)
				{
					target += f.Rim * Bump((q - 1.12f) / 0.23f);
				}
				return target;
			}

			/// <summary>A spill fill's swell over the filled ground (a few centimetres of loose surface), metres.</summary>
			public float Swell(float x, float z) => Site.Height * Noise(x / 30f, z / 30f, Site.Seed ^ 0x5u);

			// ── Mesa ──────────────────────────────────────────────────

			private float Mesa(float x, float z, float current)
			{
				BiomeLandform f = Site.Form;
				Local(x, z, out float u, out float v);
				if (f.Form == BiomeLandformForm.Rimstone)
				{
					return Rimstone(u, v, current);
				}
				// A sea-standing table's neck to the shore (decision 74: a tidal brough's causeway), raised only.
				current = Neck(x, z, current);
				float q = MathF.Sqrt(u / halfLength * (u / halfLength) + v / radius * (v / radius));
				if (q >= 1f)
				{
					return current;
				}
				float top = MesaTop();
				float qq = Mathf.Max(0f, (q - top) / (1f - top));
				float w = 1f - S01(qq);
				w = w * w * (3f - 2f * w);
				if (f.Steps > 0)
				{
					// Treads cut into the flank (2026-10-25, I2g-d: a guyot's drowned shorelines).
					w = Stepped(w, f.Steps + 1);
				}
				if (f.Roughness > 0f && f.Count > 0 && q > top)
				{
					float theta = MathF.Atan2(v, u);
					float groove = MathF.Pow(MathF.Abs(MathF.Sin(theta * f.Count * 0.5f + 2f * Noise(q * 3f, theta, Site.Seed))), 8f);
					w *= 1f - f.Roughness * 0.6f * groove * 4f * w * (1f - w);
				}
				float level = !float.IsNaN(Site.Level) ? Site.Level : Site.Ground + Site.Height;
				if (f.Tilt > 0f && f.Form == BiomeLandformForm.Table)
				{
					// A tilted top (2026-10-26, I2g-f: a chaos raft rotated as it broke away), about its centre.
					level += TableTilt(x, z);
				}
				float target = current + (level - current) * w;
				if (f.Datum != BiomeLandformDatum.Ground && !float.IsNaN(Site.Level))
				{
					// A table standing from the sea is raised only (decision 80: a guyot's flat top at its depth under the swell).
					target = Mathf.Max(current, target);
				}
				if (f.Crown > 0f && f.CrownDepth > 0f)
				{
					// An axial cleft along the table (2026-10-25, I2g-b: a lava rise's), its walls the table's own.
					target -= f.CrownDepth * Bump(v / Mathf.Max(0.25f, f.Crown * radius)) * w;
				}
				if (f.Rim > 0f)
				{
					float e = (q - top) / 0.035f;
					target += f.Rim * MathF.Exp(-e * e) * (q <= top ? 1f : 1f - S01((q - top) / 0.06f));
				}
				return target;
			}

			private int Treads => Mathf.Max(2, Site.Form.Count);

			private float Rimstone(float u, float v, float current)
			{
				BiomeLandform f = Site.Form;
				if (MathF.Abs(u) >= halfLength || MathF.Abs(v) >= radius)
				{
					return current;
				}
				int treads = Treads;
				float stepLength = 2f * halfLength / treads;
				float along = (u + halfLength) / stepLength;
				int k = Mathf.Clamp((int)MathF.Floor(along), 0, treads - 1);
				float frac = along - k;
				float head = !float.IsNaN(Site.Level) ? Site.Level : Site.Ground;
				float levelK = head - k * Site.Height;
				float levelNext = head - (k + 1) * Site.Height;
				float tread = k < treads - 1 ? levelK + (levelNext - levelK) * S01((frac - 0.9f) / 0.1f) : levelK;
				// The rim: a lip along each tread's downslope edge, behind which the pool stands.
				float lip = f.Rim * MathF.Exp(-((frac - 0.88f) / 0.05f) * ((frac - 0.88f) / 0.05f)) * (1f - Bump(MathF.Abs(v) / radius) * 0.3f);
				float across = 1f - S01((MathF.Abs(v) / radius - 0.7f) / 0.3f);
				float ends = S01((u + halfLength) / (0.25f * stepLength)) * (1f - S01((u - halfLength + 0.5f * stepLength) / (0.5f * stepLength)));
				float w = across * ends;
				return current + (tread + lip - current) * w;
			}

			/// <summary>A rimstone flight's treads as pools (centre, radius, water level): the pads its pools offer.</summary>
			public List<(Vector2 Centre, float Radius, float Level)> RimstonePools()
			{
				var pools = new List<(Vector2, float, float)>();
				if (Site.Form.Form != BiomeLandformForm.Rimstone)
				{
					return pools;
				}
				int treads = Treads;
				float stepLength = 2f * halfLength / treads;
				float head = !float.IsNaN(Site.Level) ? Site.Level : Site.Ground;
				for (int k = 0; k < treads - 1; k++)
				{
					float u = -halfLength + (k + 0.5f) * stepLength;
					var centre = new Vector2(Site.Centre.x + u * cos, Site.Centre.y + u * sin);
					pools.Add((centre, Mathf.Min(0.4f * stepLength, 0.7f * radius), head - k * Site.Height + 0.8f * Site.Form.Rim));
				}
				return pools;
			}

			// ── Lines (ridges and channels) ───────────────────────────

			private struct LineHit
			{
				public float Distance;
				public float Side;
				public float Along;
				public int Segment;
			}

			private void Nearest(float x, float z, List<LineHit> hits)
			{
				hits.Clear();
				if (buckets == null)
				{
					return;
				}
				long key = ((long)Mathf.FloorToInt(z / bucketMetres) << 32) ^ (uint)Mathf.FloorToInt(x / bucketMetres);
				if (!buckets.TryGetValue(key, out List<int> near))
				{
					return;
				}
				var p = new Vector2(x, z);
				foreach (int i in near)
				{
					BiomeLandformSegment seg = Site.Segments[i];
					Vector2 ab = seg.B - seg.A;
					float len2 = ab.sqrMagnitude;
					float t = len2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - seg.A, ab) / len2) : 0f;
					Vector2 closest = seg.A + ab * t;
					float d = Vector2.Distance(p, closest);
					if (d > lineReach)
					{
						continue;
					}
					float cross = ab.x * (p.y - seg.A.y) - ab.y * (p.x - seg.A.x);
					hits.Add(new LineHit { Distance = d, Side = cross >= 0f ? 1f : -1f, Along = seg.AlongA + t * (seg.AlongB - seg.AlongA), Segment = i });
				}
			}

			[ThreadStatic] private static List<LineHit> scratch;

			private float Taper(in BiomeLandformSegment seg, float along, float half, bool both)
			{
				float scale = Mathf.Max(1f, half);
				float end = S01((seg.LineLength - along) / scale);
				return both && seg.TaperStart ? end * S01(along / scale) : end;
			}

			private float CrossProfile(float d, float side, float half)
			{
				BiomeLandform f = Site.Form;
				float lean = Mathf.Clamp(f.Lean, -0.8f, 0.8f);
				float w = half * (side > 0f ? 1f + lean : 1f - lean);
				float t = d / Mathf.Max(0.05f, w);
				float top = Mathf.Clamp(f.Top, 0f, 0.9f);
				float tt = top > 0f ? Mathf.Max(0f, (t - top) / (1f - top)) : t;
				return Bump(tt);
			}

			private float Ridge(float x, float z, float current)
			{
				BiomeLandform f = Site.Form;
				if (f.Form == BiomeLandformForm.Ring)
				{
					return Ring(x, z, current);
				}
				List<LineHit> hits = scratch ??= new List<LineHit>();
				Nearest(x, z, hits);
				if (hits.Count == 0)
				{
					return current;
				}
				float best = 0f, height = 0f, ditch = 0f, step = 0f;
				if (f.Form == BiomeLandformForm.Graben)
				{
					// A flat floor between two inward-facing scarps, the deepest of the set's troughs (decision 76).
					float top = Mathf.Clamp(f.Top <= 0f ? 0.6f : f.Top, 0.05f, 0.95f);
					float cut = 0f;
					foreach (LineHit hit in hits)
					{
						BiomeLandformSegment seg = Site.Segments[hit.Segment];
						float t = hit.Distance / Mathf.Max(0.05f, seg.Half);
						if (t < 1f)
						{
							cut = Mathf.Max(cut, seg.Depth * (t <= top ? 1f : 1f - S01((t - top) / (1f - top))) * Taper(seg, hit.Along, seg.Half, true));
						}
					}
					return current - cut;
				}
				foreach (LineHit hit in hits)
				{
					BiomeLandformSegment seg = Site.Segments[hit.Segment];
					float taper = Taper(seg, hit.Along, seg.Half, true);
					if (f.Form == BiomeLandformForm.Scarp)
					{
						float s = hit.Side * hit.Distance;
						float back = Mathf.Max(1f, Site.Radius);
						float face = S01((s + seg.Half) / (2f * seg.Half));
						if (f.Steps > 0)
						{
							// Benches down the face (2026-10-26, I2g-f: a plateau's rim stepping down, an ice cap's flight of margin steps).
							face = Stepped(face, f.Steps + 1);
						}
						float raise = face * (1f - S01((s - seg.Half) / back));
						step = Mathf.Max(step, seg.Depth * raise * taper);
						continue;
					}
					float p = CrossProfile(hit.Distance, hit.Side, seg.Half) * taper;
					if (f.Roughness > 0f && p > 0f)
					{
						float wave = Frac(hit.Along / Mathf.Max(2f, 4f * seg.Half) + Unit(Site.Seed));
						p *= 1f + f.Roughness * 0.35f * (2f * wave - 1f);
					}
					best = Mathf.Max(best, Mathf.Clamp01(p));
					float h = seg.Depth * p;
					if (f.Crown > 0f && f.CrownDepth > 0f)
					{
						// A medial trough down the crest (2026-10-25, I2g-d: a double ridge's), its share of the height.
						h -= seg.Depth * f.CrownDepth * Bump(hit.Distance / Mathf.Max(0.05f, f.Crown * seg.Half)) * taper;
					}
					// The largest either way: a negative height is a trench (2026-10-25: a tide crack, a fissure row; Max kept them flat).
					if (MathF.Abs(h) > MathF.Abs(height))
					{
						height = h;
					}
					if (f.Moat > 0f && f.MoatDepth > 0f && hit.Side < 0f)
					{
						float w = seg.Half * (1f - Mathf.Clamp(f.Lean, -0.8f, 0.8f));
						float dd = hit.Distance - w;
						if (dd > 0f && dd < f.Moat)
						{
							ditch = Mathf.Max(ditch, f.MoatDepth * Bump(2f * dd / f.Moat - 1f) * taper);
						}
					}
				}
				if (f.Form == BiomeLandformForm.Scarp)
				{
					return current + step;
				}
				ditch *= 1f - S01(best / 0.2f);
				if (f.Datum != BiomeLandformDatum.Ground && !float.IsNaN(Site.Level))
				{
					return Mathf.Max(current, current + (Site.Level - current) * best) - ditch;
				}
				return current + height - ditch;
			}

			private float Ring(float x, float z, float current)
			{
				BiomeLandform f = Site.Form;
				float dx = x - Site.Centre.x, dz = z - Site.Centre.y;
				float rr = MathF.Sqrt(dx * dx + dz * dz);
				float ring = Site.Radius;
				float half = Mathf.Max(0.5f, Site.HalfWidth);
				float side = rr < ring ? 1f : -1f;
				float p = CrossProfile(MathF.Abs(rr - ring), side, half);
				if (f.Roughness > 0f && p > 0f)
				{
					p *= 1f + f.Roughness * 0.3f * (Noise(dx / half, dz / half, Site.Seed) - 0.5f);
				}
				float ditch = 0f;
				if (f.Moat > 0f && f.MoatDepth > 0f && side < 0f)
				{
					float dd = rr - ring - half * (1f - Mathf.Clamp(f.Lean, -0.8f, 0.8f));
					if (dd > 0f && dd < f.Moat)
					{
						ditch = f.MoatDepth * Bump(2f * dd / f.Moat - 1f);
					}
				}
				if (f.Radials > 0)
				{
					// A web of radial fractures through the ring (2026-10-26, I2g-f: an arachnoid's), cut with its ditch.
					ditch = Mathf.Max(ditch, RingWeb(dx, dz, rr));
				}
				if (f.Datum != BiomeLandformDatum.Ground && !float.IsNaN(Site.Level))
				{
					float target = Mathf.Max(current, current + (Site.Level - current) * Mathf.Clamp01(p));
					if (rr < ring)
					{
						// The lagoon inside, filled to its floor below the crest.
						float inner = S01((ring - rr) / half);
						float floor = Site.Level - Site.Height;
						target = Mathf.Max(target, current + (floor - current) * inner);
					}
					return target - ditch;
				}
				return current + Site.Height * Mathf.Clamp01(p) - ditch;
			}

			private float Channels(float x, float z, float current)
			{
				BiomeLandform f = Site.Form;
				List<LineHit> hits = scratch ??= new List<LineHit>();
				Nearest(x, z, hits);
				if (hits.Count == 0)
				{
					return current;
				}
				if (f.Form == BiomeLandformForm.Treads)
				{
					return current + TreadsHeight(hits, out _, out _);
				}
				float cut = 0f, share = 0f, levee = 0f;
				float top = Mathf.Clamp(f.Top, 0f, 0.95f);
				if (f.Form == BiomeLandformForm.Rille)
				{
					// The vent hollow at its head (decision 76), a bowl deeper than the trench it feeds.
					cut = RilleHead(x, z, out share);
				}
				// 2026-10-26, I2g-f: an inner channel down the floor, a canyon's wall terraces, a rille's roofed stretches.
				bool inner = f.Inner > 0f && (f.Form == BiomeLandformForm.Rille || f.Form == BiomeLandformForm.Canyon);
				bool benched = f.Steps > 0 && f.Form == BiomeLandformForm.Canyon;
				float innerCut = 0f;
				foreach (LineHit hit in hits)
				{
					BiomeLandformSegment seg = Site.Segments[hit.Segment];
					float half = Mathf.Max(0.05f, seg.Half);
					float t = ChannelReach(hit, half);
					float taper = Taper(seg, hit.Along, half, TapersBoth);
					if (f.Roofed > 0f && f.Form == BiomeLandformForm.Rille)
					{
						taper *= 1f - RoofAt(hit.Along, Site.Segments[0].Half);
					}
					if (t < 1f)
					{
						float bed = t <= top ? 1f : 1f - S01((t - top) / (1f - top));
						if (benched)
						{
							bed = Stepped(bed, f.Steps + 1);
						}
						if (f.Roughness > 0f)
						{
							bed *= 1f - f.Roughness * 0.4f * Noise(x / Mathf.Max(1f, half), z / Mathf.Max(1f, half), Site.Seed);
						}
						share = Mathf.Max(share, bed * taper);
						cut = Mathf.Max(cut, seg.Depth * bed * taper);
						if (inner)
						{
							float ti = t / Mathf.Max(0.05f, f.Inner);
							innerCut = Mathf.Max(innerCut, 0.33f * seg.Depth * (1f - S01((ti - 0.5f) / 0.5f)) * taper);
						}
					}
					if (f.Rim > 0f && t > 0.9f && t < 3f)
					{
						levee = Mathf.Max(levee, f.Rim * Bump((t - 1.85f) / 0.95f) * taper);
					}
				}
				cut += innerCut;
				levee *= 1f - S01(share / 0.15f);
				if ((f.Form == BiomeLandformForm.Pass || f.Datum != BiomeLandformDatum.Ground) && !float.IsNaN(Site.Level))
				{
					// A pass, and a channel floored at a level under the sea's surface (decision 68: a rift opened to the sea).
					return current - Mathf.Max(0f, current - Site.Level) * share + levee;
				}
				return current - cut + levee;
			}

			// ── Lobe ──────────────────────────────────────────────────

			private float Lobe(float x, float z, float current, float original, in GroundInfo ground)
			{
				BiomeLandform f = Site.Form;
				if (f.Form == BiomeLandformForm.FlowSteps)
				{
					float w = FieldWeight(x, z, original, ground.Slope);
					if (w <= 0f)
					{
						return current;
					}
					float lambda = Mathf.Max(8f, Site.HalfWidth * 2f);
					float warp = 0.6f * Noise(x / (lambda * 2.3f), z / (lambda * 2.3f), Site.Seed ^ 0x77u);
					float n = Noise(x / lambda + warp, z / lambda - warp, Site.Seed) * Mathf.Max(2, f.Count);
					float frac = Frac(n);
					float profile = frac < 0.82f ? S01(frac / 0.82f) : 1f - S01((frac - 0.82f) / 0.18f);
					return current + w * Site.Height * profile;
				}
				Local(x, z, out float u, out float v);
				switch (f.Form)
				{
					case BiomeLandformForm.Fan:
					{
						float rf = halfLength;
						float r = MathF.Sqrt(u * u + v * v);
						float rho = r / rf;
						if (rho >= 1f)
						{
							return current;
						}
						float phi = MathF.Abs(MathF.Atan2(v, u)) * Mathf.Rad2Deg;
						float spread = f.Flank > 0f ? f.Flank : 50f;
						float g = 1f - S01((phi - (spread - 15f)) / 30f);
						g = 1f + (g - 1f) * S01(rho / 0.15f);
						return current + Site.Height * MathF.Pow(1f - rho, 1.2f) * g;
					}
					case BiomeLandformForm.Tongue:
					{
						float t = (u + halfLength) / (2f * halfLength);
						if (t <= 0f || t >= 1f || MathF.Abs(v) >= radius)
						{
							return current;
						}
						float along = S01(t / 0.35f) * (t < 0.86f ? 1f : 1f - S01((t - 0.86f) / 0.14f));
						if (f.Roughness > 0f)
						{
							along *= 1f + f.Roughness * 0.25f * MathF.Sin(MathF.PI * 2f * u / Mathf.Max(4f, 0.24f * halfLength));
						}
						float body = Site.Height * along * Bump(v / radius);
						if (f.Crown > 0f && f.CrownDepth > 0f)
						{
							// A central channel between its levees (2026-10-25, I2g-d: an ʻaʻā flow's).
							body -= f.CrownDepth * along * Bump(v / Mathf.Max(0.5f, f.Crown * radius));
						}
						return current + body;
					}
					case BiomeLandformForm.Slide:
					{
						float t = (u + halfLength) / (2f * halfLength);
						if (t <= 0f || t >= 1f || MathF.Abs(v) >= radius)
						{
							return current;
						}
						float across = Bump(v / radius);
						float scar = Site.Height * Bump((t - 0.2f) / 0.22f) * S01(t / 0.06f);
						float deposit = 0.55f * Site.Height * Bump((t - 0.72f) / 0.28f);
						return current + across * (deposit - scar);
					}
					default:
						return current;
				}
			}

			// ── Dunes ─────────────────────────────────────────────────

			/// <summary>A dune field's height at a point before its biome's weight (pure): the field's form in the wind's frame.</summary>
			public float DuneHeight(float x, float z)
			{
				float h = DuneShape(x, z);
				return wakes != null && h > 0f ? h * (1f - WakeAt(x, z)) : h;
			}

			private float DuneShape(float x, float z)
			{
				BiomeLandform f = Site.Form;
				Local(x, z, out float u, out float v);
				float lambda = Mathf.Max(10f, Site.HalfWidth * 2f);
				float tanRepose = MathF.Tan(ReposeDegrees * Mathf.Deg2Rad);
				switch (f.Form)
				{
					case BiomeLandformForm.Transverse:
					{
						float h = Mathf.Min(Site.Height, 0.45f * lambda * tanRepose);
						float lee = Mathf.Clamp(Mathf.Max(0.2f, h / (tanRepose * lambda)), 0.05f, 0.5f);
						float phase = u / lambda + 0.3f * (Noise(v / (lambda * 3f), u / (lambda * 4f), Site.Seed) - 0.5f);
						float frac = Frac(phase);
						float p = frac < 1f - lee ? S01(frac / (1f - lee)) : 1f - S01((frac - (1f - lee)) / lee);
						return h * p * (0.7f + 0.3f * Noise(v / (lambda * 0.8f), u / (lambda * 2f), Site.Seed ^ 0x31u));
					}
					case BiomeLandformForm.Barchan:
					{
						float best = 0f;
						int cu = Mathf.FloorToInt(u / lambda), cv = Mathf.FloorToInt(v / lambda);
						for (int j = cv - 1; j <= cv + 1; j++)
						{
							for (int i = cu - 1; i <= cu + 1; i++)
							{
								uint h = Hash(Site.Seed, (uint)i, (uint)j, 0xBA4u);
								if (Unit(h) > 0.65f)
								{
									continue;
								}
								float r = lambda * 0.32f * (0.75f + 0.25f * Unit(Hash(h, 1u)));
								float uu = u - (i + 0.25f + 0.5f * Unit(Hash(h, 2u))) * lambda;
								float vv = v - (j + 0.25f + 0.5f * Unit(Hash(h, 3u))) * lambda;
								float body = Bump(MathF.Sqrt(uu * 0.9f * (uu * 0.9f) + vv * vv) / r);
								float hollow = MathF.Sqrt((uu - 0.45f * r) * (uu - 0.45f * r) / (0.64f * r * r) + vv * vv / (0.9f * r * r));
								float crescent = body * S01((hollow - 0.7f) / 0.4f);
								best = Mathf.Max(best, crescent * Mathf.Min(Site.Height, 0.5f * r));
							}
						}
						return best;
					}
					case BiomeLandformForm.Linear:
					{
						float vv = v + 0.12f * lambda * MathF.Sin(MathF.PI * 2f * u / (5f * lambda) + Unit(Site.Seed) * 6.28f);
						float d = MathF.Abs(Frac(vv / lambda) - 0.5f) * lambda;
						float width = 0.32f * lambda;
						float h = Mathf.Min(Site.Height, 0.5f * width * tanRepose);
						float p = f.Forks != Vector2.zero ? ForkedLinear(u, vv, lambda, width) : Bump(d / width);
						if (f.Roughness > 0f)
						{
							p *= 1f + f.Roughness * 0.3f * MathF.Sin(MathF.PI * 2f * u / (0.7f * lambda));
						}
						return h * p;
					}
					case BiomeLandformForm.StarDune:
					{
						float best = 0f;
						int cu = Mathf.FloorToInt(u / lambda), cv = Mathf.FloorToInt(v / lambda);
						int arms = Mathf.Max(3, f.Count);
						for (int j = cv - 1; j <= cv + 1; j++)
						{
							for (int i = cu - 1; i <= cu + 1; i++)
							{
								uint h = Hash(Site.Seed, (uint)i, (uint)j, 0x57Au);
								float r = lambda * 0.42f;
								float uu = u - (i + 0.3f + 0.4f * Unit(Hash(h, 2u))) * lambda;
								float vv = v - (j + 0.3f + 0.4f * Unit(Hash(h, 3u))) * lambda;
								float rr = MathF.Sqrt(uu * uu + vv * vv);
								if (rr >= r)
								{
									continue;
								}
								float theta = MathF.Atan2(vv, uu) + 0.3f * MathF.Sin(3f * rr / r + Unit(h) * 6.28f);
								float arm = 0.5f + 0.5f * MathF.Cos(arms * theta);
								float q = rr / (r * (0.42f + 0.58f * MathF.Pow(arm, 2.5f)));
								if (q < 1f)
								{
									best = Mathf.Max(best, Mathf.Min(Site.Height, 0.3f * r) * MathF.Pow(1f - q, 1.6f));
								}
							}
						}
						return best;
					}
					case BiomeLandformForm.Parabolic:
					{
						float best = 0f;
						int cu = Mathf.FloorToInt(u / lambda), cv = Mathf.FloorToInt(v / lambda);
						for (int j = cv - 1; j <= cv + 1; j++)
						{
							for (int i = cu - 1; i <= cu + 1; i++)
							{
								uint h = Hash(Site.Seed, (uint)i, (uint)j, 0xBA2u);
								if (Unit(h) > 0.7f)
								{
									continue;
								}
								float r = lambda * 0.4f;
								float uu = u - (i + 0.5f) * lambda;
								float vv = v - (j + 0.3f + 0.4f * Unit(Hash(h, 3u))) * lambda;
								if (MathF.Abs(vv) > 0.75f * r)
								{
									continue;
								}
								float arc = 0.35f * r - 1.2f * vv * vv / r;
								float d = uu - arc;
								float width = 0.18f * r * (d > 0f ? 0.6f : 1f);
								float p = Bump(d / width) * (1f - S01((MathF.Abs(vv) - 0.55f * r) / (0.2f * r)));
								best = Mathf.Max(best, Mathf.Min(Site.Height, 0.5f * width * tanRepose * 2f) * p);
							}
						}
						return best;
					}
					default:
						return 0f;
				}
			}

			// ── Waves ─────────────────────────────────────────────────

			private float WaveHeight(float x, float z, in GroundInfo ground)
			{
				BiomeLandform f = Site.Form;
				float lambda = Mathf.Max(1f, Site.HalfWidth * 2f);
				switch (f.Form)
				{
					case BiomeLandformForm.SedimentWaves:
					{
						float frac = WavePhase(x, z, lambda);
						float p = frac < 0.65f ? S01(frac / 0.65f) : 1f - S01((frac - 0.65f) / 0.35f);
						return Site.Height * (p - 0.5f);
					}
					case BiomeLandformForm.Polygons:
					{
						Polygon(x, z, lambda, out float edge);
						float rim = Bump(edge / (0.09f * lambda));
						return Site.Height * (rim - 0.3f * S01(edge / (0.3f * lambda)));
					}
					case BiomeLandformForm.Rills:
						return -Site.Height * Rill(x, z, lambda);
					case BiomeLandformForm.Fins:
					{
						Local(x, z, out float u, out float v);
						float d = MathF.Abs(Frac(v / lambda + 0.15f * Noise(u / (lambda * 6f), 0f, Site.Seed)) - 0.5f) * lambda;
						float half = Mathf.Max(0.5f, 0.18f * lambda);
						float fin = 1f - S01((d - 0.6f * half) / (0.4f * half));
						float breaks = S01((Noise(u / (lambda * 2.5f), v / lambda, Site.Seed ^ 0xF1u) - 0.3f) / 0.15f);
						return Site.Height * fin * breaks;
					}
					case BiomeLandformForm.Crevasses:
					{
						Local(x, z, out float u, out float v);
						float d = MathF.Abs(Frac(u / lambda + 0.15f * Noise(v / (lambda * 3f), u / (lambda * 5f), Site.Seed)) - 0.5f) * lambda;
						float half = Mathf.Max(0.5f, Mathf.Clamp(Site.Form.Top, 0.02f, 0.5f) * lambda * 0.5f);
						float slot = 1f - S01(d / half);
						float runs = S01((Noise(v / (lambda * 1.5f), u / lambda, Site.Seed ^ 0xC5u) - 0.35f) / 0.15f);
						return -Site.Height * slot * slot * runs;
					}
					case BiomeLandformForm.Cockpits:
						return CockpitHeight(x, z, lambda);
					case BiomeLandformForm.Labyrinth:
						return -Site.Height * LabyrinthValley(x, z, lambda);
					case BiomeLandformForm.Cells:
						return CellHeight(x, z, lambda);
					case BiomeLandformForm.Weave:
						return WeaveHeight(x, z, lambda, out _, out _);
					default:
						return 0f;
				}
			}

			/// <summary>A sediment-wave field's phase at a point, 0 … 1 a wavelength along its axis (0.65 its crest; its lee after it).</summary>
			private float WavePhase(float x, float z, float lambda)
			{
				Local(x, z, out float u, out float v);
				float phase = u / lambda + 0.25f * (Noise(v / (lambda * 4f), u / (lambda * 6f), Site.Seed) - 0.5f);
				return Frac(phase);
			}

			private void Polygon(float x, float z, float lambda, out float edge)
			{
				int cx = Mathf.FloorToInt(x / lambda), cz = Mathf.FloorToInt(z / lambda);
				float d1 = float.MaxValue, d2 = float.MaxValue;
				for (int j = cz - 1; j <= cz + 1; j++)
				{
					for (int i = cx - 1; i <= cx + 1; i++)
					{
						uint h = Hash(Site.Seed, (uint)i, (uint)j, 0x9E1u);
						float px = (i + 0.15f + 0.7f * Unit(h)) * lambda, pz = (j + 0.15f + 0.7f * Unit(Hash(h, 1u))) * lambda;
						float d = MathF.Sqrt((x - px) * (x - px) + (z - pz) * (z - pz));
						if (d < d1)
						{
							d2 = d1;
							d1 = d;
						}
						else if (d < d2)
						{
							d2 = d;
						}
					}
				}
				edge = 0.5f * (d2 - d1);
			}

			/// <summary>
			/// A rill field's groove depth at a point, 0 on a crest … 1 in a groove's bed: grooves down the slope of a lattice cell's
			/// own heading (stored at placement on a coarse lattice, <see cref="BiomeLandformSite.Segments"/>' A = cell centre, B =
			/// its downhill), blended between the four nearest cells so the grooves bend with the ground without a seam.
			/// </summary>
			private float Rill(float x, float z, float lambda)
			{
				float cell = RillCell(lambda);
				float gx = x / cell - 0.5f, gz = z / cell - 0.5f;
				int x0 = Mathf.FloorToInt(gx), z0 = Mathf.FloorToInt(gz);
				float fx = gx - x0, fz = gz - z0;
				float sum = 0f, total = 0f;
				for (int j = 0; j < 2; j++)
				{
					for (int i = 0; i < 2; i++)
					{
						float w = (i == 0 ? 1f - fx : fx) * (j == 0 ? 1f - fz : fz);
						if (w <= 0f)
						{
							continue;
						}
						int ci = x0 + i, cj = z0 + j;
						float a = Unit(Hash(Site.Seed, (uint)ci, (uint)cj, 0x411u)) * MathF.PI * 2f;
						Vector2 down = RillHeading(ci, cj, cell, a);
						float centreX = (ci + 0.5f) * cell, centreZ = (cj + 0.5f) * cell;
						float s = (x - centreX) * -down.y + (z - centreZ) * down.x;
						s += 0.3f * lambda * Noise((x - centreX) * down.x / (lambda * 3f), s / (lambda * 2f), Site.Seed ^ 0x412u);
						sum += w * MathF.Abs(MathF.Cos(MathF.PI * s / lambda));
						total += w;
					}
				}
				return total > 0f ? sum / total : 0f;
			}

			private Vector2 RillHeading(int ci, int cj, float cell, float fallback)
			{
				// The heading the planner laid for this lattice cell (a rill field stores each cell's centre and its way down).
				if (rillHeadings != null && rillHeadings.TryGetValue(((long)cj << 32) ^ (uint)ci, out Vector2 heading) && heading.sqrMagnitude > 1e-6f)
				{
					return heading.normalized;
				}
				return new Vector2(MathF.Cos(fallback), MathF.Sin(fallback));
			}

			/// <summary>The rill lattice's cell size, metres, for a rill spacing of <paramref name="lambda"/>.</summary>
			public static float RillCell(float lambda) => Mathf.Max(lambda * 4f, 8f);

			// ── Masks ─────────────────────────────────────────────────

			/// <summary>
			/// How much of the point (x, z) is the site's <paramref name="part"/>, 0 … 1, with the ground's height and slope there
			/// as the paint reads them (after the carve). What its biome's paint layer covers (BiomeLandform.PaintGround).
			/// </summary>
			public float Mask(BiomeLandformPart part, float x, float z, float ground, float slope)
			{
				BiomeLandform f = Site.Form;
				Vector4 b = Bounds;
				if (x < b.x || x > b.z || z < b.y || z > b.w)
				{
					return 0f;
				}
				switch (FamilyOf(f.Form))
				{
					case BiomeLandformFamily.Mound:
					{
						float q = MoundQ(x, z);
						float top = Mathf.Clamp(f.Top, 0f, 0.95f);
						switch (part)
						{
							case BiomeLandformPart.Top:
								if (f.Form == BiomeLandformForm.Tower)
								{
									return 1f - S01((q - TowerShoulder + 0.02f) / 0.04f);
								}
								return top > 0f ? 1f - S01((q - top) / 0.08f) : 1f - S01((q - 0.25f) / 0.1f);
							case BiomeLandformPart.Flank:
								if (f.Form == BiomeLandformForm.Tower)
								{
									return S01((q - TowerShoulder + 0.02f) / 0.04f) * (1f - S01((q - 0.97f) / 0.03f));
								}
								return S01((q - top) / 0.08f) * (1f - S01((q - 0.95f) / 0.05f));
							case BiomeLandformPart.Lee:
							{
								// The flank turned downwind (its axis is the wind's when aligned to it).
								Local(x, z, out float lu, out float _);
								float flank = f.Form == BiomeLandformForm.Tower ? S01((q - TowerShoulder) / 0.04f) : S01((q - top) / 0.08f);
								return flank * (1f - S01((q - 0.97f) / 0.03f)) * S01(lu / Mathf.Max(0.5f, 0.3f * radius));
							}
							case BiomeLandformPart.Moat:
								return f.Moat > 0f && q > 1f ? Bump(2f * ((q - 1f) * radius) / f.Moat - 1f) * MoatShare(x, z) : 0f;
							case BiomeLandformPart.Rim:
								// Its crown's rim; with no crown, the drum band round its foot (2026-10-25, I2g-d: a tumulus's moulded kerb).
								return f.Crown > 0f ? Bump((q / f.Crown - 1f) / 0.25f) : Bump((q - 0.9f) / 0.1f);
							case BiomeLandformPart.Streaks:
							{
								Local(x, z, out float u, out float v);
								int n = Mathf.Max(1, f.Count);
								float theta = MathF.Atan2(v, u) + 0.15f * Noise(q * 4f, 0f, Site.Seed);
								float streak = MathF.Pow(0.5f + 0.5f * MathF.Cos(n * theta + Unit(Site.Seed) * 6.28f), 12f);
								return streak * S01((q - top) / 0.1f) * (1f - S01((q - 1f) / 0.08f));
							}
							default: return 1f - S01((q - 0.95f) / 0.05f);
						}
					}
					case BiomeLandformFamily.Crater:
					{
						float dx = x - Site.Centre.x, dz = z - Site.Centre.y;
						float r = MathF.Sqrt(dx * dx + dz * dz) / radius;
						if (f.Form == BiomeLandformForm.MultiRing)
						{
							return BasinMask(part, x, z, r);
						}
						if (f.Form == BiomeLandformForm.RingGraben)
						{
							return GrabenMask(part, dx, dz, r);
						}
						if (f.Form == BiomeLandformForm.Rampart && (part == BiomeLandformPart.Ejecta || part == BiomeLandformPart.Crest || part == BiomeLandformPart.Rays))
						{
							return RampartMask(part, dx, dz, r);
						}
						float craterTop = CraterTop();
						switch (part)
						{
							case BiomeLandformPart.Floor: return 1f - S01((r - Mathf.Max(0.05f, craterTop)) / 0.1f);
							case BiomeLandformPart.Rim: return Bump((r - 1f) / 0.18f);
							case BiomeLandformPart.Flank:
							case BiomeLandformPart.Wall: return S01((r - craterTop) / 0.1f) * (1f - S01((r - 0.95f) / 0.08f));
							case BiomeLandformPart.Ejecta: return S01((r - 0.95f) / 0.1f) * (1f - S01((r - 1.6f) / 0.4f));
							case BiomeLandformPart.Rays:
								if (f.Form == BiomeLandformForm.Impact && f.RayReach > 2f)
								{
									// Long rays (2026-10-26, I2g-f: a fresh airless crater's, out to 8 radii), thinning outward.
									float reach = Mathf.Min(f.RayReach, 12f);
									return RayAt(MathF.Atan2(dz, dx)) * S01((r - 0.95f) / 0.1f) * (1f - S01((r - 0.75f * reach) / (0.25f * reach))) * (1f - 0.5f * Mathf.Clamp01(r / reach));
								}
								return RayAt(MathF.Atan2(dz, dx)) * S01((r - 0.95f) / 0.1f) * (1f - S01((r - 1.5f) / 0.5f));
							case BiomeLandformPart.Halo:
								return HaloMask(dx, dz, r);
							default: return 1f - S01((r - 1.05f) / 0.1f);
						}
					}
					case BiomeLandformFamily.Depression:
					{
						if (f.Form == BiomeLandformForm.SpillFill)
						{
							// The filled ground lies dead flat: where the patch's ground is level, the fill shows.
							float w = context.WeightOf(Site.BiomeID, x, z);
							return w * (1f - S01((slope - 0.6f) / 1.4f));
						}
						Local(x, z, out float u, out float v);
						if (f.Form == BiomeLandformForm.Tank)
						{
							return TankMask(part, u, v);
						}
						float q = MathF.Sqrt(u / halfLength * (u / halfLength) + v / radius * (v / radius));
						if (cluster != null)
						{
							float best = 0f;
							foreach (Vector3 c in cluster)
							{
								best = Mathf.Max(best, 1f - S01((MathF.Sqrt((x - c.x) * (x - c.x) + (z - c.y) * (z - c.y)) / c.z - 0.9f) / 0.15f));
							}
							return best;
						}
						switch (part)
						{
							case BiomeLandformPart.Floor: return 1f - S01((q - Mathf.Max(0.1f, f.Top)) / 0.1f);
							case BiomeLandformPart.Rim: return Bump((q - 1.12f) / 0.23f);
							case BiomeLandformPart.Flank: return S01((q - f.Top) / 0.1f) * (1f - S01((q - 1f) / 0.05f));
							default: return 1f - S01((q - 1f) / 0.08f);
						}
					}
					case BiomeLandformFamily.Mesa:
					{
						if (f.Form == BiomeLandformForm.Bench)
						{
							return BenchMask(part, x, z, ground, slope);
						}
						Local(x, z, out float u, out float v);
						if (f.Form == BiomeLandformForm.Rimstone)
						{
							return MathF.Abs(u) < halfLength && MathF.Abs(v) < radius ? 1f - S01((MathF.Abs(v) / radius - 0.75f) / 0.25f) : 0f;
						}
						float q = MathF.Sqrt(u / halfLength * (u / halfLength) + v / radius * (v / radius));
						float top = MesaTop();
						switch (part)
						{
							case BiomeLandformPart.Top: return 1f - S01((q - top + 0.03f) / 0.04f);
							case BiomeLandformPart.Rim: return Bump((q - top) / 0.05f);
							case BiomeLandformPart.Flank: return S01((q - top) / 0.04f) * (1f - S01((q - 0.97f) / 0.03f));
							default: return 1f - S01((q - 0.97f) / 0.03f);
						}
					}
					case BiomeLandformFamily.Ridge:
					case BiomeLandformFamily.Channels:
						return LineMask(part, x, z);
					case BiomeLandformFamily.Lobe:
					{
						if (f.Form == BiomeLandformForm.FlowSteps)
						{
							return context.WeightOf(Site.BiomeID, x, z);
						}
						Local(x, z, out float u, out float v);
						if (f.Form == BiomeLandformForm.Fan)
						{
							float rho = MathF.Sqrt(u * u + v * v) / halfLength;
							float phi = MathF.Abs(MathF.Atan2(v, u)) * Mathf.Rad2Deg;
							float spread = f.Flank > 0f ? f.Flank : 50f;
							return (1f - S01((rho - 0.92f) / 0.08f)) * (1f - S01((phi - spread) / 15f));
						}
						float t = (u + halfLength) / (2f * halfLength);
						float inside = t > 0f && t < 1f ? 1f - S01((MathF.Abs(v) / radius - 0.85f) / 0.15f) : 0f;
						switch (part)
						{
							case BiomeLandformPart.Floor: return f.Form == BiomeLandformForm.Slide ? inside * (1f - S01((t - 0.4f) / 0.05f)) : 0f;
							case BiomeLandformPart.Flank: return inside * S01((t - 0.84f) / 0.04f);
							case BiomeLandformPart.Top: return inside * S01((t - 0.3f) / 0.1f) * (1f - S01((t - 0.84f) / 0.04f));
							default: return inside;
						}
					}
					case BiomeLandformFamily.Dunes:
					{
						float w = FieldWeight(x, z, ground, slope);
						if (w <= 0f)
						{
							return 0f;
						}
						float h = DuneHeight(x, z);
						float rock = 1f - S01((slope - SandRockDegrees) / 5f);
						switch (part)
						{
							case BiomeLandformPart.Sand: return w * S01(h / 0.8f) * rock;
							case BiomeLandformPart.Wake: return w * WakeAt(x, z);
							case BiomeLandformPart.Lee:
							{
								// A slip face: the sand falling away downwind (the field's axis is the wind's).
								float step = Mathf.Max(0.5f, Site.HalfWidth * 0.02f);
								float ahead = DuneHeight(x + cos * step, z + sin * step);
								return w * S01((h - ahead) / (0.25f * step)) * S01(h / 0.5f);
							}
							case BiomeLandformPart.Crest: return w * S01((h / Mathf.Max(0.01f, Site.Height) - 0.75f) / 0.2f);
							case BiomeLandformPart.Floor: return w * (1f - S01(h / 0.5f));
							default: return w;
						}
					}
					case BiomeLandformFamily.Waves:
					{
						float w = FieldWeight(x, z, ground, slope);
						if (w <= 0f)
						{
							return 0f;
						}
						float lambda = Mathf.Max(1f, Site.HalfWidth * 2f);
						switch (f.Form)
						{
							case BiomeLandformForm.Polygons:
							{
								Polygon(x, z, lambda, out float edge);
								return part == BiomeLandformPart.Floor ? w * S01(edge / (0.15f * lambda)) : part == BiomeLandformPart.Whole ? w : w * Bump(edge / (0.12f * lambda));
							}
							case BiomeLandformForm.Rills:
							{
								float groove = Rill(x, z, lambda);
								return part == BiomeLandformPart.Channel || part == BiomeLandformPart.Floor ? w * S01((groove - 0.6f) / 0.3f)
									: part == BiomeLandformPart.Crest ? w * (1f - S01(groove / 0.3f)) : w;
							}
							case BiomeLandformForm.SedimentWaves:
							{
								if (part != BiomeLandformPart.Lee && part != BiomeLandformPart.Crest)
								{
									return w;
								}
								float frac = WavePhase(x, z, lambda);
								return part == BiomeLandformPart.Lee ? w * S01((frac - 0.63f) / 0.04f) * (1f - S01((frac - 0.96f) / 0.04f))
									: w * Bump((frac - 0.65f) / 0.12f);
							}
							case BiomeLandformForm.Cockpits:
							case BiomeLandformForm.Labyrinth:
							case BiomeLandformForm.Cells:
							case BiomeLandformForm.Weave:
								return w * FieldPartMask(part, x, z, lambda);
							default:
								return w;
						}
					}
					default:
						return 0f;
				}
			}

			private float LineMask(BiomeLandformPart part, float x, float z)
			{
				BiomeLandform f = Site.Form;
				if (f.Form == BiomeLandformForm.Ring)
				{
					float dx = x - Site.Centre.x, dz = z - Site.Centre.y;
					float rr = MathF.Sqrt(dx * dx + dz * dz);
					float half = Mathf.Max(0.5f, Site.HalfWidth);
					float d = MathF.Abs(rr - Site.Radius) / half;
					switch (part)
					{
						case BiomeLandformPart.Floor: return 1f - S01((rr - Site.Radius + half) / half);
						case BiomeLandformPart.Crest: return 1f - S01((d - 0.25f) / 0.15f);
						case BiomeLandformPart.Moat:
							return f.Moat > 0f && rr > Site.Radius ? Bump(2f * (rr - Site.Radius - half) / f.Moat - 1f) : 0f;
						default: return 1f - S01((d - 0.9f) / 0.1f);
					}
				}
				List<LineHit> hits = scratch ??= new List<LineHit>();
				Nearest(x, z, hits);
				float best = 0f;
				if (f.Form == BiomeLandformForm.Rille && part == BiomeLandformPart.Head)
				{
					RilleHead(x, z, out float head);
					return head;
				}
				if (f.Form == BiomeLandformForm.Treads)
				{
					TreadsHeight(hits, out float floorShare, out float crest);
					return part == BiomeLandformPart.Crest ? crest : part == BiomeLandformPart.Floor || part == BiomeLandformPart.Whole ? floorShare : 0f;
				}
				if (f.Form == BiomeLandformForm.Canyon && part == BiomeLandformPart.Head)
				{
					return CanyonHead(hits);
				}
				if (part == BiomeLandformPart.Wall || part == BiomeLandformPart.Scarp)
				{
					// A trough's walls are its flanks (decision 76).
					part = BiomeLandformPart.Flank;
				}
				foreach (LineHit hit in hits)
				{
					BiomeLandformSegment seg = Site.Segments[hit.Segment];
					float half = Mathf.Max(0.05f, seg.Half);
					float t = FamilyOf(f.Form) == BiomeLandformFamily.Channels ? ChannelReach(hit, half) : hit.Distance / half;
					if (f.Form == BiomeLandformForm.Graben && part == BiomeLandformPart.Rim)
					{
						// A graben's shoulders, just outside its scarps' tops.
						best = Mathf.Max(best, Bump((t - 1.1f) / 0.2f) * Taper(seg, hit.Along, half, true));
						continue;
					}
					float m;
					if (f.Form == BiomeLandformForm.Scarp && part == BiomeLandformPart.Top)
					{
						// A scarp's raised back (2026-10-25, I2g-b: a raised reef's terrace above its cliff).
						float sd = hit.Side * hit.Distance;
						best = Mathf.Max(best, S01((sd - 0.5f * half) / half) * (1f - S01((sd - half) / Mathf.Max(1f, Site.Radius))) * Taper(seg, hit.Along, half, true));
						continue;
					}
					switch (part)
					{
						case BiomeLandformPart.Channel:
						case BiomeLandformPart.Floor:
						case BiomeLandformPart.Crest:
							m = 1f - S01((t - Mathf.Max(0.2f, f.Top)) / 0.15f);
							break;
						case BiomeLandformPart.Flank:
							m = S01((t - Mathf.Max(0.2f, f.Top)) / 0.15f) * (1f - S01((t - 1f) / 0.12f));
							break;
						case BiomeLandformPart.Rim:
							m = f.Rim > 0f ? Bump((t - 1.85f) / 0.95f) : 0f;
							break;
						case BiomeLandformPart.Moat:
						{
							float dd = hit.Distance - half;
							m = f.Moat > 0f && hit.Side < 0f && dd > 0f ? Bump(2f * dd / f.Moat - 1f) : 0f;
							break;
						}
						default:
							m = 1f - S01((t - 0.95f) / 0.1f);
							break;
					}
					best = Mathf.Max(best, m * Taper(seg, hit.Along, half, FamilyOf(f.Form) == BiomeLandformFamily.Ridge || f.Form == BiomeLandformForm.Spiral
						|| f.Form == BiomeLandformForm.Canyon || f.OnHost));
				}
				return best;
			}
		}

		// ── The carve ─────────────────────────────────────────────────

		/// <summary>A height grid: row-major from the south-west, <c>Metres[z * Width + x]</c> at (East0 + x·Spacing, North0 + z·Spacing).</summary>
		public struct Grid
		{
			public float[] Metres;
			public int Width;
			public int Depth;
			public float Spacing;
			public float East0;
			public float North0;

			public Grid(float[] metres, int width, int depth, float spacing, float east0, float north0)
			{
				Metres = metres;
				Width = width;
				Depth = depth;
				Spacing = spacing;
				East0 = east0;
				North0 = north0;
			}
		}

		/// <summary>What a carve changed: every sample's index and its height before the first landform touched it.</summary>
		public sealed class Carving
		{
			public int[] Index = Array.Empty<int>();
			public float[] Original = Array.Empty<float>();
			public int Count => Index.Length;
		}

		/// <summary>
		/// Carves <paramref name="sites"/> into <paramref name="grid"/> in order and returns what it changed (pure but for the grid).
		/// <paramref name="keep"/> (world x, z → 0 … 1) holds the ground where it says 1 (rivers, lakes and their banks); null holds
		/// none. Restoring the result (<see cref="Restore"/>) puts back exactly the grid it was given.
		/// </summary>
		public static Carving Carve(Grid grid, IReadOnlyList<BiomeLandformSite> sites, Context context, Func<float, float, float> keep = null)
		{
			var carving = new Carving();
			if (sites == null || sites.Count == 0 || grid.Metres == null)
			{
				return carving;
			}
			float[] metres = grid.Metres;
			float[] original = (float[])metres.Clone();
			context ??= new Context();
			foreach (BiomeLandformSite site in sites)
			{
				var shape = new Shape(site, context);
				Vector4 b = shape.Bounds;
				int x0 = Mathf.Clamp(Mathf.FloorToInt((b.x - grid.East0) / grid.Spacing), 0, grid.Width - 1);
				int x1 = Mathf.Clamp(Mathf.CeilToInt((b.z - grid.East0) / grid.Spacing), 0, grid.Width - 1);
				int z0 = Mathf.Clamp(Mathf.FloorToInt((b.y - grid.North0) / grid.Spacing), 0, grid.Depth - 1);
				int z1 = Mathf.Clamp(Mathf.CeilToInt((b.w - grid.North0) / grid.Spacing), 0, grid.Depth - 1);
				if (x1 < x0 || z1 < z0)
				{
					continue;
				}
				if (site.Form.Form == BiomeLandformForm.SpillFill)
				{
					SpillFill(grid, shape, context, keep, x0, x1, z0, z1);
					continue;
				}
				Grid g = grid;
				Parallel.For(z0, z1 + 1, z =>
				{
					float north = g.North0 + z * g.Spacing;
					for (int x = x0; x <= x1; x++)
					{
						int i = z * g.Width + x;
						float east = g.East0 + x * g.Spacing;
						float current = metres[i];
						GroundInfo info = InfoAt(original, g, x, z);
						float next = shape.Apply(east, north, current, original[i], info);
						if (float.IsNaN(next) || float.IsInfinity(next))
						{
							continue;
						}
						if (keep != null)
						{
							float k = Mathf.Clamp01(keep(east, north));
							next = current + (next - current) * (1f - k);
						}
						metres[i] = next;
					}
				});
			}
			var index = new List<int>();
			var before = new List<float>();
			for (int i = 0; i < metres.Length; i++)
			{
				if (BitConverter.SingleToInt32Bits(metres[i]) != BitConverter.SingleToInt32Bits(original[i]))
				{
					index.Add(i);
					before.Add(original[i]);
				}
			}
			carving.Index = index.ToArray();
			carving.Original = before.ToArray();
			return carving;
		}

		/// <summary>Puts back every sample a carve changed, bit for bit (pure but for the grid).</summary>
		public static void Restore(float[] metres, Carving carving)
		{
			if (metres == null || carving == null)
			{
				return;
			}
			for (int k = 0; k < carving.Index.Length; k++)
			{
				int i = carving.Index[k];
				if (i >= 0 && i < metres.Length)
				{
					metres[i] = carving.Original[k];
				}
			}
		}

		/// <summary>The slope (degrees) and the way down at a sample of a grid, by central differences.</summary>
		public static GroundInfo InfoAt(float[] metres, in Grid grid, int x, int z)
		{
			int xa = Math.Max(0, x - 1), xb = Math.Min(grid.Width - 1, x + 1);
			int za = Math.Max(0, z - 1), zb = Math.Min(grid.Depth - 1, z + 1);
			float gx = (metres[z * grid.Width + xb] - metres[z * grid.Width + xa]) / Mathf.Max(1e-4f, (xb - xa) * grid.Spacing);
			float gz = (metres[zb * grid.Width + x] - metres[za * grid.Width + x]) / Mathf.Max(1e-4f, (zb - za) * grid.Spacing);
			float m = MathF.Sqrt(gx * gx + gz * gz);
			return new GroundInfo
			{
				Slope = MathF.Atan(m) * Mathf.Rad2Deg,
				Downhill = m > 1e-6f ? new Vector2(-gx / m, -gz / m) : Vector2.zero,
			};
		}

		/// <summary>
		/// Fills every closed low of a spill-fill site's biome flat to its spill level: a priority flood on a coarse lattice (about
		/// 4 m) over the site's box, whose outlets are its edge and every node outside the biome; the fine samples are raised to the
		/// lattice's filled level wherever it filled, plus the site's few centimetres of swell.
		/// </summary>
		private static void SpillFill(Grid grid, Shape shape, Context context, Func<float, float, float> keep, int x0, int x1, int z0, int z1)
		{
			int step = Mathf.Max(1, Mathf.RoundToInt(4f / Mathf.Max(0.01f, grid.Spacing)));
			int cw = (x1 - x0) / step + 2, cd = (z1 - z0) / step + 2;
			var level = new float[cw * cd];
			var filled = new float[cw * cd];
			var inside = new bool[cw * cd];
			var done = new bool[cw * cd];
			int id = shape.Site.BiomeID;
			for (int j = 0; j < cd; j++)
			{
				for (int i = 0; i < cw; i++)
				{
					int gx = Math.Min(grid.Width - 1, x0 + i * step), gz = Math.Min(grid.Depth - 1, z0 + j * step);
					int k = j * cw + i;
					level[k] = grid.Metres[gz * grid.Width + gx];
					float east = grid.East0 + gx * grid.Spacing, north = grid.North0 + gz * grid.Spacing;
					inside[k] = i > 0 && j > 0 && i < cw - 1 && j < cd - 1 && context.WeightOf(id, east, north) >= 0.5f;
				}
			}
			var heap = new MinHeap(cw * cd);
			for (int k = 0; k < level.Length; k++)
			{
				if (!inside[k])
				{
					filled[k] = level[k];
					done[k] = true;
					heap.Push(k, level[k]);
				}
			}
			while (heap.Count > 0)
			{
				heap.Pop(out int k, out float h);
				int i = k % cw, j = k / cw;
				for (int n = 0; n < 4; n++)
				{
					int ni = i + (n == 0 ? 1 : n == 1 ? -1 : 0), nj = j + (n == 2 ? 1 : n == 3 ? -1 : 0);
					if (ni < 0 || nj < 0 || ni >= cw || nj >= cd)
					{
						continue;
					}
					int nk = nj * cw + ni;
					if (done[nk])
					{
						continue;
					}
					done[nk] = true;
					filled[nk] = Mathf.Max(level[nk], h);
					heap.Push(nk, filled[nk]);
				}
			}
			if (shape.Site.Form.Top > 0f)
			{
				// Filled only part way (2026-10-26, I2g-f: a Cayley plain's fill to three tenths of its crater's rim): each closed
				// low's flat level that share of the way from its floor up to its spill.
				PartFill(level, filled, cw, cd, Mathf.Clamp01(shape.Site.Form.Top));
			}
			float[] metres = grid.Metres;
			Grid g = grid;
			Parallel.For(z0, z1 + 1, z =>
			{
				float north = g.North0 + z * g.Spacing;
				float cz = (z - z0) / (float)step;
				int j0 = Math.Min(cd - 2, (int)cz);
				float fz = Mathf.Clamp01(cz - j0);
				for (int x = x0; x <= x1; x++)
				{
					float cx = (x - x0) / (float)step;
					int i0 = Math.Min(cw - 2, (int)cx);
					float fx = Mathf.Clamp01(cx - i0);
					int a = j0 * cw + i0, b = a + 1, c = a + cw, d = c + 1;
					float depthMax = Mathf.Max(Mathf.Max(filled[a] - level[a], filled[b] - level[b]), Mathf.Max(filled[c] - level[c], filled[d] - level[d]));
					if (depthMax <= 0.01f)
					{
						continue;
					}
					float fill = Mathf.Lerp(Mathf.Lerp(filled[a], filled[b], fx), Mathf.Lerp(filled[c], filled[d], fx), fz);
					int idx = z * g.Width + x;
					float east = g.East0 + x * g.Spacing;
					float current = metres[idx];
					float w = context.WeightOf(id, east, north);
					float next = current + Mathf.Max(0f, fill + shape.Swell(east, north) - current) * w;
					if (keep != null)
					{
						next = current + (next - current) * (1f - Mathf.Clamp01(keep(east, north)));
					}
					metres[idx] = next;
				}
			});
		}

		/// <summary>A small binary min-heap of (index, priority), ties broken by index so the order is fixed.</summary>
		private sealed class MinHeap
		{
			private int[] keys;
			private float[] priorities;
			public int Count;

			public MinHeap(int capacity)
			{
				keys = new int[Math.Max(4, capacity)];
				priorities = new float[keys.Length];
			}

			private bool Less(int a, int b) => priorities[a] < priorities[b] || priorities[a] == priorities[b] && keys[a] < keys[b];

			private void Swap(int a, int b)
			{
				(keys[a], keys[b]) = (keys[b], keys[a]);
				(priorities[a], priorities[b]) = (priorities[b], priorities[a]);
			}

			public void Push(int key, float priority)
			{
				if (Count == keys.Length)
				{
					Array.Resize(ref keys, keys.Length * 2);
					Array.Resize(ref priorities, keys.Length);
				}
				int i = Count++;
				keys[i] = key;
				priorities[i] = priority;
				while (i > 0)
				{
					int parent = (i - 1) / 2;
					if (!Less(i, parent))
					{
						break;
					}
					Swap(i, parent);
					i = parent;
				}
			}

			public void Pop(out int key, out float priority)
			{
				key = keys[0];
				priority = priorities[0];
				Count--;
				keys[0] = keys[Count];
				priorities[0] = priorities[Count];
				int i = 0;
				while (true)
				{
					int l = 2 * i + 1, r = l + 1, m = i;
					if (l < Count && Less(l, m))
					{
						m = l;
					}
					if (r < Count && Less(r, m))
					{
						m = r;
					}
					if (m == i)
					{
						break;
					}
					Swap(i, m);
					i = m;
				}
			}
		}
	}
}
