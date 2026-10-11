#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>How a shrub grows: the shape of its crown and the way its stems reach it.</summary>
	public enum BushHabit
	{
		/// <summary>A dense dome wider than tall, leaves down to the ground (laurel, box, rhododendron).</summary>
		Mound,
		/// <summary>Stems fanning up and out from a narrow foot into a goblet, the base bare (hazel, willow, creosote).</summary>
		Vase,
		/// <summary>A tall narrow column or flame with a pointed top (common juniper).</summary>
		Upright,
		/// <summary>Low and wide with a flat top, stems sprawling out near the ground (mountain pine).</summary>
		Spreading,
		/// <summary>Long canes rising to an arch and bowing back to the ground at the edge (bramble).</summary>
		Arching,
		/// <summary>A tight rounded cushion of short stiff shoots (gorse).</summary>
		Cushion,
		/// <summary>
		/// Straight unbranched canes splaying 10–25° from vertical out of one foot, small leaves all along them and a
		/// flower spike at each tip, airy (ocotillo). The canes are drawn straight to the species' height, inside its
		/// width; the leaves hang on the canes, not on a crown shell.
		/// </summary>
		Whip,
	}

	/// <summary>One shrub species' recipe, at its mature size: one unit is one metre.</summary>
	public struct BushSpecies
	{
		public string Name;
		public BushHabit Habit;
		/// <summary>Height of a mature plant, metres.</summary>
		public float Height;
		/// <summary>Crown diameter, metres.</summary>
		public float Width;
		/// <summary>Stems (or canes) rising from the base.</summary>
		public int Stems;
		/// <summary>A stem's radius at its foot, metres.</summary>
		public float StemRadius;
		/// <summary>Side branches on each stem.</summary>
		public int Branches;
		/// <summary>Bark colour of the stems.</summary>
		public Color Bark;
		public FoliageCell LeafCell;
		/// <summary>Leaf spray card size, metres: the cell's leaves come out at their real size.</summary>
		public float LeafSize;
		public Color LeafA, LeafB;
		/// <summary>How deep the leaves reach into the crown, as a share of its radius: the inside is shaded and bare.</summary>
		public float Shell;
		/// <summary>Leaf card area per square metre of crown surface (about 4 is opaque, 2.5 airy).</summary>
		public float Fullness;
		/// <summary>The share of the height at the foot with no leaves (the bare stems of a vase shrub).</summary>
		public float BareBase;
		/// <summary>How lobed and lopsided the crown is, as a share of its radius.</summary>
		public float Irregularity;
		/// <summary>Flower colours, picked between per flower; null for none.</summary>
		public Color[] Flowers;
		/// <summary>Flowers per leaf spray.</summary>
		public float FlowerShare;
		/// <summary>Flower (or truss) diameter, metres.</summary>
		public float FlowerSize;
		/// <summary>Turns in autumn and drops its leaves in winter.</summary>
		public bool Deciduous;
		/// <summary>
		/// An open, see-through shrub that hides nobody, as the real one does not (ocotillo's bare canes, a thornbush's
		/// sparse twigs): exempt from the cover a player standing in a bush is owed (BushTests). Its cover is still the
		/// same for every player, which is the fairness rule; it is only low.
		/// </summary>
		public bool Open;
		/// <summary>
		/// Each leaf spray is a palmate fan (palmetto): pointed segments radiating from the spray's root in its plane,
		/// drooping at their tips, instead of a pair of crossed leaf cards. The atlas has no fan cell (it is full), so the
		/// segments are blade-cell strips.
		/// </summary>
		public bool FanLeaves;
	}

	/// <summary>
	/// Shrubs, built the way they grow: as one sub-mesh for one material (the detail channel's rule), at three
	/// levels of detail that share one skeleton, with no collider (players walk into them and hide).
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>A crown, then the stems that fill it.</b> Each species has a crown envelope from its habit: a dome, a goblet,
	/// a flame, a flat-topped sprawl or an arch of canes, made lopsided and lobed by a few broad bulges. A shrub is
	/// multi-stemmed from the ground (basitonic): its stems rise from a small root crown, bend outward and end inside
	/// the crown's outer layer, each with a few side branches reaching for the parts of the crown round it. That is
	/// what separates a bush from a small tree, which has one trunk and a crown lifted on it.
	/// </para>
	/// <para>
	/// <b>Leaves on the outside.</b> A dense shrub's inside is shaded, so its leaves live in an outer shell
	/// (<see cref="BushSpecies.Shell"/> deep) over bare inner wood. Leaf sprays are spread evenly over that shell
	/// (a Fibonacci lattice over the crown's directions, each spray at its own depth, more of them near the
	/// surface), and as many as the shell's area times <see cref="BushSpecies.Fullness"/>: about four times the
	/// crown's surface in card area keeps a laurel or a juniper opaque from any side, so a player standing in one
	/// cannot be seen through it. Each spray belongs to the stem nearest it, so the plant variation (VegVary)
	/// swings it with its stem; it is shaded darker the deeper it sits and the lower it hangs.
	/// </para>
	/// <para>
	/// <b>Levels.</b> Level 0 is the whole plant. Level 1 keeps every stem (fewer sides, no twigs) and a third of the
	/// sprays, each grown until the leaf area is <see cref="ReducedFoliageShare"/> of the full plant's. Level 2 has no
	/// wood and about a tenth of the sprays, moved out toward the surface and grown to keep <see cref="FarFoliageShare"/>
	/// of the area, so the bush keeps its silhouette and its cover at any distance. Levels pick their sprays by a
	/// stride through the lattice, which keeps them spread over the crown.
	/// </para>
	/// <para>
	/// <b>Vertex layout</b> is the vegetation shader's (FishVegetationPasses.hlsl): wood on the atlas's solid cell
	/// in its bark colour with alpha 0 (no seasonal tint), leaves with alpha 1, flowers with alpha 0.3; TEXCOORD1 the
	/// wind (sway grows with height, flutter on leaves); parts on TEXCOORD2/3 (<see cref="MeshBuilder.SetPart"/>).
	/// </para>
	/// </remarks>
	public static class BushMeshes
	{
		/// <summary>The levels of detail every shrub is built at.</summary>
		public const int Levels = 3;

		/// <summary>The share of the full plant's leaf area level 1 keeps.</summary>
		public const float ReducedFoliageShare = 1.3f;

		/// <summary>The share of the full plant's leaf area level 2 keeps.</summary>
		public const float FarFoliageShare = 1.5f;

		/// <summary>Every how-many-th spray level 1 and level 2 keep.</summary>
		public const int ReducedStride = 2, FarStride = 4;

		/// <summary>Leaf sprays grown beyond the asked-for number, as spares the plant variation may drop.</summary>
		public const float Spare = 1.15f;

		/// <summary>Vertex colour alpha on flowers: under 0.5, so they take no seasonal tint and no contact skirt.</summary>
		public const float FlowerAlpha = 0.3f;

		/// <summary>The shrub at a level of detail (0 full, 1 reduced, 2 far), one sub-mesh.</summary>
		public static MeshBuilder Build(in BushSpecies species, int lod, int seed)
		{
			var grower = new Grower(in species, ProceduralNoise.SeedFor(species.Name, seed));
			grower.Grow();
			var mesh = new MeshBuilder(1);
			grower.Emit(mesh, Mathf.Clamp(lod, 0, Levels - 1));
			mesh.RecalculateNormals(true, onlyMissing: true);
			mesh.RecalculateTangents();
			return mesh;
		}

		/// <summary>The summed area of a shrub mesh's leaf cards (vertex alpha 1), m².</summary>
		public static float LeafArea(MeshBuilder mesh)
		{
			float area = 0f;
			List<int> indices = mesh.Submeshes[0];
			for (int i = 0; i + 2 < indices.Count; i += 3)
			{
				if (mesh.Colors[indices[i]].a < 250)
				{
					continue;
				}
				Vector3 a = mesh.Positions[indices[i]], b = mesh.Positions[indices[i + 1]], c = mesh.Positions[indices[i + 2]];
				area += 0.5f * Vector3.Cross(b - a, c - a).magnitude;
			}
			return area;
		}

		/// <summary>The crown's reach from the plant's axis at scale 1, metres.</summary>
		public static float CrownRadius(in BushSpecies species) => species.Width * 0.5f;

		// ── The crown ─────────────────────────────────────────────────

		/// <summary>A broad bulge (or hollow) of the crown toward a direction.</summary>
		private struct Lump
		{
			public Vector3 Direction;
			public float Amount;
			/// <summary>Cosine of the angle at which it starts.</summary>
			public float Start;
		}

		/// <summary>
		/// The crown: a super-ellipsoid about <see cref="Centre"/> with its own top and bottom half-heights and
		/// exponents, a foot that narrows (a goblet), and lumps; star-shaped about its centre, so every direction
		/// meets its surface once.
		/// </summary>
		private sealed class Envelope
		{
			public Vector3 Centre;
			public float A, Top, Bottom, PowerTop, PowerBottom;
			/// <summary>The crown's width at the ground against its full width (1 none: a dome; under 1 a goblet).</summary>
			public float FootWidth;
			/// <summary>Leaves only above this height.</summary>
			public float Floor;
			public Lump[] Lumps;
			public float Reach;

			private float ScaleToward(Vector3 direction)
			{
				float s = 1f;
				foreach (Lump l in Lumps)
				{
					float c = Vector3.Dot(direction, l.Direction);
					if (c > l.Start)
					{
						float t = (c - l.Start) / (1f - l.Start);
						s += l.Amount * t * t * (3f - 2f * t);
					}
				}
				return Mathf.Max(0.45f, s);
			}

			private float WidthAt(float y)
			{
				if (FootWidth >= 1f || y >= Centre.y)
				{
					return 1f;
				}
				float t = Mathf.Clamp01(y / Mathf.Max(0.01f, Centre.y));
				return Mathf.Lerp(FootWidth, 1f, t * t * (3f - 2f * t));
			}

			/// <summary>Under 1 inside the crown, 1 on its surface.</summary>
			public float Field(Vector3 p)
			{
				Vector3 o = p - Centre;
				float m = o.magnitude;
				float s = m > 1e-5f ? ScaleToward(o / m) : 1f;
				float h = new Vector2(o.x, o.z).magnitude / (A * WidthAt(p.y) * s);
				float v = Mathf.Abs(o.y) / ((o.y >= 0f ? Top : Bottom) * s);
				return h * h + Mathf.Pow(v, o.y >= 0f ? PowerTop : PowerBottom);
			}

			/// <summary>Distance from the centre to the surface along a unit direction.</summary>
			public float Radius(Vector3 direction)
			{
				float lo = 0f, hi = Reach;
				for (int i = 0; i < 26; i++)
				{
					float mid = 0.5f * (lo + hi);
					if (Field(Centre + direction * mid) < 1f)
					{
						lo = mid;
					}
					else
					{
						hi = mid;
					}
				}
				return 0.5f * (lo + hi);
			}

			public Vector3 Surface(Vector3 direction) => Centre + direction * Radius(direction);

			/// <summary>The outward normal of the surface near a point.</summary>
			public Vector3 Normal(Vector3 p)
			{
				const float e = 0.01f;
				var g = new Vector3(
					Field(p + Vector3.right * e) - Field(p - Vector3.right * e),
					Field(p + Vector3.up * e) - Field(p - Vector3.up * e),
					Field(p + Vector3.forward * e) - Field(p - Vector3.forward * e));
				return g.sqrMagnitude > 1e-12f ? g.normalized : (p - Centre).normalized;
			}
		}

		// ── The plant ─────────────────────────────────────────────────

		/// <summary>A woody axis: a stem, a branch, a cane or a twig, as a centre line with radii.</summary>
		private sealed class Wood
		{
			public int Part;
			public readonly List<Vector3> Path = new List<Vector3>();
			public readonly List<float> Radii = new List<float>();
			/// <summary>Twigs are drawn at level 0 only.</summary>
			public bool Twig;
		}

		/// <summary>A stem and everything on it: one part for the plant variation, turned about its foot.</summary>
		private struct Part
		{
			public Vector3 Pivot;
			public float Hash;
			/// <summary>A small shift of the leaf colour shared by the whole stem.</summary>
			public float Tint;
		}

		/// <summary>A leaf spray or a flower, with everything about it decided once for every level.</summary>
		private struct Spray
		{
			public Vector3 Position;
			/// <summary>Direction the spray points (or a flower faces).</summary>
			public Vector3 Direction;
			/// <summary>The crown's outward direction from the spray's spot, for re-placing it on reduced levels.</summary>
			public Vector3 Outward;
			/// <summary>The crown surface's normal at the spray: its main card faces about this way.</summary>
			public Vector3 Normal;
			public float Size;
			public float Roll;
			public Color32 Colour;
			public int Part;
			public float CardHash, CardRank;
			public bool Flower;
		}

		private sealed class Grower
		{
			private readonly BushSpecies sp;
			private readonly int seed;
			private DeterministicRNG rng;
			private Envelope crown;
			private readonly List<Part> parts = new List<Part>();
			private readonly List<Wood> wood = new List<Wood>();
			private readonly List<Spray> sprays = new List<Spray>();
			// Where leaves may hang: points along every stem and branch, with the part each belongs to.
			private readonly List<Vector3> holds = new List<Vector3>();
			private readonly List<int> holdParts = new List<int>();
			private float footRadius;

			public Grower(in BushSpecies species, int seed)
			{
				sp = species;
				this.seed = seed;
				rng = new DeterministicRNG(seed);
			}

			/// <summary>A fresh stream for one element, so one element's draws never shift the next.</summary>
			private void Reseed(int element) => rng = new DeterministicRNG(seed ^ (int)ProceduralNoise.Mix((uint)element * 0x9e3779b1u + 1u));

			/// <summary>A hash of this plant's seed and a number, 0..1, without touching the streams.</summary>
			private float Hash(int n, int salt) => (ProceduralNoise.Mix((uint)seed ^ ((uint)n * 0x9e3779b1u) ^ ((uint)salt * 0x85ebca6bu)) >> 8) * (1f / 16777216f);

			private Vector3 Random3() => new Vector3(rng.Range(-1f, 1f), rng.Range(-1f, 1f), rng.Range(-1f, 1f));

			public void Grow()
			{
				Reseed(0);
				crown = Shape();
				Stems();
				Foliage();
			}

			// ── Crown ─────────────────────────────────────────────────

			private Envelope Shape()
			{
				// The leaf cards reach past the crown's surface by up to about two thirds of their size: the crown is inset by
				// that, so the plant comes out at the species' height and width.
				float overhang = 0.6f * Mathf.Max(0f, sp.LeafSize);
				float h = Mathf.Max(0.1f, sp.Height - overhang);
				var e = new Envelope { A = Mathf.Max(0.1f, sp.Width * 0.5f - overhang), FootWidth = 1f };
				switch (sp.Habit)
				{
					case BushHabit.Vase:
						e.Centre = new Vector3(0f, 0.62f * h, 0f);
						e.Top = 0.38f * h; e.Bottom = 0.62f * h; e.PowerTop = 2f; e.PowerBottom = 1.6f; e.FootWidth = 0.22f;
						footRadius = 0.12f * e.A;
						break;
					case BushHabit.Upright:
						e.Centre = new Vector3(0f, 0.45f * h, 0f);
						e.Top = 0.55f * h; e.Bottom = 0.5f * h; e.PowerTop = 1.5f; e.PowerBottom = 3f; e.FootWidth = 0.85f;
						footRadius = 0.15f * e.A;
						break;
					case BushHabit.Spreading:
						e.Centre = new Vector3(0f, 0.2f * h, 0f);
						e.Top = 0.8f * h; e.Bottom = 0.35f * h; e.PowerTop = 3.2f; e.PowerBottom = 2f;
						footRadius = 0.25f * e.A;
						break;
					case BushHabit.Arching:
						e.Centre = new Vector3(0f, 0.3f * h, 0f);
						e.Top = 0.7f * h; e.Bottom = 0.4f * h; e.PowerTop = 2f; e.PowerBottom = 2.2f;
						footRadius = 0.3f * e.A;
						break;
					case BushHabit.Cushion:
						e.Centre = new Vector3(0f, 0.42f * h, 0f);
						e.Top = 0.58f * h; e.Bottom = 0.55f * h; e.PowerTop = 2.6f; e.PowerBottom = 3f;
						footRadius = 0.2f * e.A;
						break;
					case BushHabit.Whip:
						// An inverted cone: every cane from one narrow foot. Only the reduced levels' spray choice reads it
						// (the canes are drawn straight, and their leaves hang on them).
						e.Centre = new Vector3(0f, 0.55f * h, 0f);
						e.Top = 0.45f * h; e.Bottom = 0.55f * h; e.PowerTop = 1.6f; e.PowerBottom = 1.1f; e.FootWidth = 0.1f;
						footRadius = 0.05f * e.A;
						break;
					default:
						e.Centre = new Vector3(0f, 0.45f * h, 0f);
						e.Top = 0.55f * h; e.Bottom = 0.6f * h; e.PowerTop = 2.2f; e.PowerBottom = 2.4f;
						footRadius = 0.18f * e.A;
						break;
				}
				// Grown toward the light on one side: the crown's heart a little off the root.
				float lean = rng.NextFloat() * Mathf.PI * 2f;
				e.Centre += new Vector3(Mathf.Cos(lean), 0f, Mathf.Sin(lean)) * (e.A * rng.Range(0.02f, 0.08f));
				e.Floor = Mathf.Max(0.02f, sp.BareBase * h);

				// A few broad bulges and hollows round the sides and top, so no two sides of a bush match.
				var lumps = new List<Lump>();
				int count = 5 + rng.Next(3);
				for (int i = 0; i < count; i++)
				{
					float yaw = rng.NextFloat() * Mathf.PI * 2f;
					float rise = rng.Range(-0.2f, 0.85f);
					var d = new Vector3(Mathf.Cos(yaw) * Mathf.Sqrt(1f - rise * rise), rise, Mathf.Sin(yaw) * Mathf.Sqrt(1f - rise * rise));
					lumps.Add(new Lump { Direction = d.normalized, Amount = sp.Irregularity * rng.Range(-0.7f, 1f), Start = rng.Range(0.35f, 0.75f) });
				}
				if (sp.Habit == BushHabit.Vase)
				{
					// The goblet's crown is open in the middle: its stems lean out and leave a shallow hollow on top.
					lumps.Add(new Lump { Direction = Vector3.up, Amount = -0.12f, Start = 0.8f });
				}
				e.Lumps = lumps.ToArray();
				e.Reach = Mathf.Max(e.A, Mathf.Max(e.Top, e.Bottom)) * (2f + sp.Irregularity);

				// The bulges push the crown past the habit's extent (a bramble came out a third taller than the species):
				// measured, and the top half and the width scaled back to the species' size.
				float top = 0f, side = 0f;
				const int probes = 512;
				for (int i = 0; i < probes; i++)
				{
					Vector3 p = e.Surface(Lattice(i, probes));
					top = Mathf.Max(top, p.y);
					side = Mathf.Max(side, new Vector2(p.x - e.Centre.x, p.z - e.Centre.z).magnitude);
				}
				if (top > e.Centre.y + 0.01f)
				{
					e.Top *= (h - e.Centre.y) / (top - e.Centre.y);
				}
				if (side > 0.01f)
				{
					e.A *= Mathf.Max(0.1f, sp.Width * 0.5f - overhang) / side;
				}
				return e;
			}

			/// <summary>A direction from the crown's centre, by its turn about the axis and its elevation (radians).</summary>
			private static Vector3 Direction(float yaw, float elevation) =>
				new Vector3(Mathf.Cos(yaw) * Mathf.Cos(elevation), Mathf.Sin(elevation), Mathf.Sin(yaw) * Mathf.Cos(elevation));

			/// <summary>The elevations (degrees) a stem of this habit reaches its crown at.</summary>
			private Vector2 StemElevations()
			{
				switch (sp.Habit)
				{
					case BushHabit.Vase: return new Vector2(15f, 70f);
					case BushHabit.Upright: return new Vector2(30f, 85f);
					case BushHabit.Spreading: return new Vector2(-10f, 40f);
					case BushHabit.Cushion: return new Vector2(-10f, 80f);
					case BushHabit.Whip: return new Vector2(65f, 80f);
					default: return new Vector2(-15f, 75f);
				}
			}

			/// <summary>How far a stem rises before it turns out, as a share of its length.</summary>
			private float StemRise()
			{
				switch (sp.Habit)
				{
					case BushHabit.Vase: return 0.45f;
					case BushHabit.Upright: return 0.5f;
					case BushHabit.Spreading: return 0.12f;
					default: return 0.3f;
				}
			}

			// ── Wood ──────────────────────────────────────────────────

			private void Stems()
			{
				int stems = Mathf.Max(1, sp.Stems);
				Vector2 elevations = StemElevations();
				for (int i = 0; i < stems; i++)
				{
					Reseed(100 + i);
					float yaw = (i + rng.Range(-0.35f, 0.35f)) * Mathf.PI * 2f / stems;
					var outward = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw));
					// Rooted a few centimetres down, so neither the sink nor a slope shows a cut end.
					Vector3 foot = outward * (footRadius * Mathf.Sqrt(rng.Range(0.1f, 1f))) + Vector3.down * 0.05f;
					int part = parts.Count;
					parts.Add(new Part { Pivot = new Vector3(foot.x, 0f, foot.z), Hash = Hash(part, 7), Tint = rng.Range(-1f, 1f) });

					var stem = new Wood { Part = part };
					float r0 = sp.StemRadius * rng.Range(0.75f, 1.1f);
					if (sp.Habit == BushHabit.Arching)
					{
						Cane(stem, foot, outward, r0);
					}
					else if (sp.Habit == BushHabit.Whip)
					{
						Whip(stem, foot, yaw, r0);
					}
					else
					{
						Vector3 d = Direction(yaw + rng.Range(-0.25f, 0.25f), Mathf.Deg2Rad * rng.Range(elevations.x, elevations.y));
						Vector3 target = crown.Centre + d * (crown.Radius(d) * rng.Range(0.7f, 0.9f));
						target.y = Mathf.Max(target.y, crown.Floor + 0.05f);
						Rising(stem, foot, target, r0, 0.25f);
					}
					wood.Add(stem);
					Hold(stem, 0.25f);
					Branches(stem, i);
				}
			}

			/// <summary>
			/// A stem from its foot to a point in the crown: up first (a shoot from the base rises before it bends out
			/// to the light), then out, arriving pointed away from the crown's heart, wandering a little on the way.
			/// </summary>
			private void Rising(Wood w, Vector3 foot, Vector3 target, float r0, float tipShare)
			{
				float length = Vector3.Distance(foot, target);
				Vector3 away = (target - crown.Centre).sqrMagnitude > 1e-6f ? (target - crown.Centre).normalized : Vector3.up;
				Vector3 flatOut = new Vector3(target.x - foot.x, 0f, target.z - foot.z);
				flatOut = flatOut.sqrMagnitude > 1e-6f ? flatOut.normalized : Vector3.forward;
				Vector3 p1 = foot + Vector3.up * (length * StemRise()) + flatOut * (length * 0.08f);
				Vector3 p2 = target - away * (length * 0.3f);
				Curve(w, foot, p1, p2, target, r0, r0 * tipShare, 0.05f);
			}

			/// <summary>A bramble cane: up to an arch two-thirds of the way out, bowing back down toward the ground at the crown's edge.</summary>
			private void Cane(Wood w, Vector3 foot, Vector3 outward, float r0)
			{
				float h = sp.Height, a = crown.A;
				float apexY = h * rng.Range(0.7f, 0.95f), apexOut = a * rng.Range(0.3f, 0.55f);
				float tipOut = a * rng.Range(0.85f, 1.05f), tipY = h * rng.Range(0.05f, 0.25f);
				Vector3 side = Vector3.Cross(Vector3.up, outward) * rng.Range(-0.25f, 0.25f);
				Vector3 p1 = foot + Vector3.up * (apexY * 1.1f) + outward * (apexOut * 0.3f);
				Vector3 p2 = (outward + side) * ((apexOut + tipOut) * 0.6f) + Vector3.up * (apexY * 1.05f);
				Vector3 p3 = (outward + side * 1.5f) * tipOut + Vector3.up * tipY;
				Curve(w, foot, p1, p2, p3, r0, r0 * 0.5f, 0.04f);
			}

			/// <summary>
			/// An ocotillo cane: straight from the foot, splayed 10–25° from vertical (no shallower than keeps its tip inside
			/// the species' width), three-quarters to all of the species' height, tapering to a third of its foot.
			/// </summary>
			private void Whip(Wood w, Vector3 foot, float yaw, float r0)
			{
				float overhang = 0.6f * Mathf.Max(0f, sp.LeafSize);
				float h = Mathf.Max(0.1f, sp.Height - overhang);
				// The widest canes reach the species' half-width (their leaves stand out a little past it; the shortened canes fall short).
				float reach = Mathf.Max(0.05f, sp.Width * 0.5f);
				Vector2 elevations = StemElevations();
				float lowest = Mathf.Max(elevations.x, Mathf.Atan2(h, reach) * Mathf.Rad2Deg);
				// Most canes splay wide, a few stand near upright in the middle.
				float elevation = Mathf.Deg2Rad * Mathf.Lerp(lowest, Mathf.Max(lowest, elevations.y), Mathf.Pow(rng.NextFloat(), 1.6f));
				Vector3 dir = Direction(yaw + rng.Range(-0.2f, 0.2f), elevation);
				float length = h / Mathf.Sin(elevation) * rng.Range(0.78f, 1f);
				Vector3 bow = Vector3.Cross(dir, Vector3.Cross(Vector3.up, dir)).normalized * (length * rng.Range(-0.02f, 0.02f));
				Curve(w, foot, foot + dir * (length * 0.33f) + bow, foot + dir * (length * 0.66f) + bow, foot + dir * length, r0, r0 * 0.35f, 0.012f);
			}

			/// <summary>A cubic from p0 to p3 with a gentle wander across it, in segments of about a third of a metre.</summary>
			private void Curve(Wood w, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float r0, float r1, float wander)
			{
				float length = Vector3.Distance(p0, p1) + Vector3.Distance(p1, p2) + Vector3.Distance(p2, p3);
				int segments = Mathf.Clamp(Mathf.RoundToInt(length / 0.33f), 3, 8);
				Vector3 along = (p3 - p0).sqrMagnitude > 1e-6f ? (p3 - p0).normalized : Vector3.up;
				Vector3 sideA = PlantParts.Perpendicular(along), sideB = Vector3.Cross(along, sideA).normalized;
				float phaseA = rng.NextFloat() * Mathf.PI * 2f, phaseB = rng.NextFloat() * Mathf.PI * 2f;
				for (int s = 0; s <= segments; s++)
				{
					float t = (float)s / segments, u = 1f - t;
					Vector3 p = u * u * u * p0 + 3f * u * u * t * p1 + 3f * u * t * t * p2 + t * t * t * p3;
					float envelope = Mathf.Sin(Mathf.PI * t) * wander * length;
					p += (sideA * Mathf.Sin(2.3f * Mathf.PI * t + phaseA) + sideB * Mathf.Sin(3.1f * Mathf.PI * t + phaseB)) * envelope;
					w.Path.Add(p);
					w.Radii.Add(Mathf.Lerp(r0, r1, t));
				}
			}

			/// <summary>Records where leaves may hang along a piece of wood, from a share of the way along it.</summary>
			private void Hold(Wood w, float from)
			{
				float total = 0f;
				for (int i = 1; i < w.Path.Count; i++)
				{
					total += Vector3.Distance(w.Path[i - 1], w.Path[i]);
				}
				int count = Mathf.Max(2, Mathf.CeilToInt(total / 0.2f));
				for (int k = 0; k <= count; k++)
				{
					float t = Mathf.Lerp(from, 1f, (float)k / count);
					holds.Add(OnPath(w.Path, t));
					holdParts.Add(w.Part);
				}
			}

			private static Vector3 OnPath(List<Vector3> points, float t)
			{
				float f = Mathf.Clamp01(t) * (points.Count - 1);
				int i = Mathf.Min((int)f, points.Count - 2);
				return Vector3.Lerp(points[i], points[i + 1], f - i);
			}

			private static float RadiusOn(Wood w, float t)
			{
				float f = Mathf.Clamp01(t) * (w.Radii.Count - 1);
				int i = Mathf.Min((int)f, w.Radii.Count - 2);
				return Mathf.Lerp(w.Radii[i], w.Radii[i + 1], f - i);
			}

			/// <summary>
			/// Side branches up a stem, each reaching for a part of the crown beside the stem's own, so the stems
			/// together fill the crown; on a cane, short laterals off the arch. Half carry a twig at level 0.
			/// </summary>
			private void Branches(Wood stem, int index)
			{
				int count = Mathf.Max(0, sp.Branches);
				Vector3 tip = stem.Path[stem.Path.Count - 1];
				Vector3 away = (tip - crown.Centre).sqrMagnitude > 1e-6f ? (tip - crown.Centre).normalized : Vector3.up;
				Vector2 elevations = StemElevations();
				for (int b = 0; b < count; b++)
				{
					Reseed(10000 + index * 64 + b);
					float t = Mathf.Lerp(0.3f, 0.9f, (b + rng.Range(0.1f, 0.9f)) / Mathf.Max(1, count));
					Vector3 at = OnPath(stem.Path, t);
					Vector3 target;
					if (sp.Habit == BushHabit.Arching)
					{
						Vector3 flat = new Vector3(at.x, 0f, at.z);
						flat = flat.sqrMagnitude > 1e-6f ? flat.normalized : Vector3.forward;
						Vector3 d = (flat + Vector3.Cross(Vector3.up, flat) * rng.Range(-0.8f, 0.8f) + Vector3.up * rng.Range(-0.1f, 0.4f)).normalized;
						target = at + d * (sp.Height * rng.Range(0.2f, 0.45f));
						target = Inside(target, 0.95f);
					}
					else
					{
						Vector3 axis = Vector3.Cross(away, Random3());
						axis = axis.sqrMagnitude > 1e-6f ? axis.normalized : PlantParts.Perpendicular(away);
						Vector3 d = Quaternion.AngleAxis(rng.Range(25f, 60f), axis) * away;
						float elevation = Mathf.Clamp(Mathf.Asin(Mathf.Clamp(d.y, -1f, 1f)) * Mathf.Rad2Deg, elevations.x - 10f, elevations.y + 10f);
						d = Direction(Mathf.Atan2(d.z, d.x), elevation * Mathf.Deg2Rad);
						target = crown.Centre + d * (crown.Radius(d) * rng.Range(0.75f, 0.95f));
						target.y = Mathf.Max(target.y, crown.Floor + 0.05f);
					}
					float length = Vector3.Distance(at, target);
					if (length < 0.15f)
					{
						continue;
					}
					var branch = new Wood { Part = stem.Part };
					float r = RadiusOn(stem, t) * 0.55f;
					Vector3 mid = Vector3.Lerp(at, target, 0.5f) + Vector3.up * (length * rng.Range(0.05f, 0.15f));
					Vector3 p1 = Vector3.Lerp(at, mid, 0.66f), p2 = Vector3.Lerp(target, mid, 0.66f);
					Curve(branch, at, p1, p2, target, r, r * 0.3f, 0.06f);
					wood.Add(branch);
					Hold(branch, 0.2f);
					if (rng.NextFloat() < 0.5f)
					{
						Twig(branch);
					}
				}
			}

			/// <summary>A short, thin twig off the outer part of a branch, toward the crown's surface.</summary>
			private void Twig(Wood branch)
			{
				float t = rng.Range(0.55f, 0.9f);
				Vector3 at = OnPath(branch.Path, t);
				Vector3 away = (at - crown.Centre).sqrMagnitude > 1e-6f ? (at - crown.Centre).normalized : Vector3.up;
				Vector3 d = (away + Random3() * 0.6f).normalized;
				var twig = new Wood { Part = branch.Part, Twig = true };
				twig.Path.Add(at);
				twig.Path.Add(Inside(at + d * rng.Range(0.15f, 0.35f), 1f));
				twig.Radii.Add(Mathf.Max(0.004f, RadiusOn(branch, t) * 0.5f));
				twig.Radii.Add(0f);
				if (Vector3.Distance(twig.Path[0], twig.Path[1]) > 0.05f)
				{
					wood.Add(twig);
				}
			}

			/// <summary>A point pulled back inside the crown to a share of its radius, if it lies beyond it.</summary>
			private Vector3 Inside(Vector3 p, float share)
			{
				Vector3 o = p - crown.Centre;
				float m = o.magnitude;
				if (m < 1e-5f)
				{
					return p;
				}
				float limit = crown.Radius(o / m) * share;
				p = m > limit ? crown.Centre + o / m * limit : p;
				p.y = Mathf.Max(p.y, 0.02f);
				return p;
			}

			// ── Leaves ────────────────────────────────────────────────

			/// <summary>The i-th of n directions spread evenly over the sphere (a Fibonacci lattice).</summary>
			private static Vector3 Lattice(int i, int n)
			{
				float y = 1f - 2f * (i + 0.5f) / n;
				float r = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
				float phi = i * 2.39996323f;
				return new Vector3(Mathf.Cos(phi) * r, y, Mathf.Sin(phi) * r);
			}

			/// <summary>The crown's surface area above its floor (m²), and the share of directions that reach it there.</summary>
			private (float area, float share) Surface()
			{
				const int probes = 1536;
				double area = 0.0;
				int above = 0;
				float solid = 4f * Mathf.PI / probes;
				for (int i = 0; i < probes; i++)
				{
					Vector3 d = Lattice(i, probes);
					float r = crown.Radius(d);
					Vector3 p = crown.Centre + d * r;
					if (p.y < crown.Floor)
					{
						continue;
					}
					above++;
					float facing = Mathf.Max(0.25f, Vector3.Dot(d, crown.Normal(p)));
					area += r * r * solid / facing;
				}
				return ((float)area, above / (float)probes);
			}

			private void Foliage()
			{
				if (sp.Habit == BushHabit.Whip)
				{
					CaneFoliage();
					return;
				}
				(float area, float share) = Surface();
				float leaf = Mathf.Max(0.05f, sp.LeafSize);
				int asked = Mathf.Clamp(Mathf.RoundToInt(sp.Fullness * area / (2f * leaf * leaf)), 24, 1200);
				int total = Mathf.Max(asked, Mathf.RoundToInt(asked * Spare));
				int flowers = sp.Flowers != null && sp.Flowers.Length > 0 ? Mathf.RoundToInt(asked * Mathf.Max(0f, sp.FlowerShare)) : 0;
				// Directions are tried until the asked-for number land above the floor: below it is bare.
				int tries = Mathf.CeilToInt((total + flowers) / Mathf.Max(0.05f, share) * 1.05f) + 8;
				int leaves = 0, blooms = 0;
				// The whole lattice, always: it runs from the top down, and stopping at the count left the foot bare.
				for (int i = 0; i < tries; i++)
				{
					Reseed(50000 + i);
					// Every so many lattice points is a flower, so flowers are spread over the crown like the leaves.
					bool flower = flowers > 0 && blooms < flowers && (leaves >= total || (long)(i + 1) * flowers / (total + flowers) > (long)i * flowers / (total + flowers));
					Vector3 d = (Lattice(i, tries) + Random3() * (0.6f / Mathf.Sqrt(tries))).normalized;
					float r = crown.Radius(d);
					// Leaves through the shell, most near the surface; flowers on the surface.
					float depth = flower ? rng.Range(0.95f, 1.02f) : 1f - Mathf.Clamp01(sp.Shell) * Mathf.Pow(rng.NextFloat(), 1.6f);
					Vector3 p = crown.Centre + d * (r * depth);
					if (p.y < crown.Floor)
					{
						continue;
					}
					Vector3 n = crown.Normal(crown.Centre + d * r);
					int part = NearestPart(p);
					float height01 = Mathf.Clamp01((p.y - crown.Floor) / Mathf.Max(0.05f, sp.Height - crown.Floor));
					if (flower)
					{
						Color petal = sp.Flowers[rng.Next(sp.Flowers.Length)];
						sprays.Add(new Spray
						{
							Position = p,
							Direction = n,
							Outward = d,
							Normal = n,
							Size = Mathf.Max(0.02f, sp.FlowerSize) * rng.Range(0.8f, 1.2f),
							Roll = rng.Range(0f, 360f),
							Colour = PlantParts.C32(petal * rng.Range(0.9f, 1.05f), FlowerAlpha),
							Part = part,
							CardHash = Hash(i, 13),
							Flower = true,
						});
						blooms++;
						continue;
					}
					// Sprays point out of the crown, upward a little (leaves turn to the light).
					/* Laid along the crown's surface, tipped out of it a little, like shingles: its main card then faces out
					 * of the crown. A spray pointing straight out showed both its crossed cards edge-on to anyone facing that
					 * side, and the reduced levels, with few sprays, had a hole wherever the crown faced the camera. */
					float up = sp.Habit == BushHabit.Upright ? 1.2f : sp.Habit == BushHabit.Arching ? 0.1f : 0.45f;
					Vector3 along = Vector3.ProjectOnPlane(Random3() + Vector3.up * up, n);
					along = along.sqrMagnitude > 1e-6f ? along.normalized : PlantParts.Perpendicular(n);
					float tilt = Mathf.Deg2Rad * rng.Range(15f, 40f);
					Vector3 dir = (along * Mathf.Cos(tilt) + n * Mathf.Sin(tilt)).normalized;
					if (p.y < leaf && dir.y < 0f)
					{
						// Near the ground a spray grows up off it, not down into it.
						dir.y = -dir.y;
					}
					// Darker the deeper the spray sits and the lower it hangs: the crown shades its own inside and underside.
					float inside = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1f - Mathf.Clamp01(sp.Shell), 1f, depth));
					float shade = Mathf.Lerp(0.5f, 1f, inside) * Mathf.Lerp(0.78f, 1.05f, height01);
					Color colour = Color.Lerp(sp.LeafA, sp.LeafB, rng.NextFloat()) * (shade * (1f + 0.05f * parts[part].Tint));
					colour.a = 1f;
					// The asked-for number always drawn (spread evenly through the lattice), the spares ranked above them.
					bool always = (long)(leaves + 1) * asked / total > (long)leaves * asked / total;
					sprays.Add(new Spray
					{
						Position = p,
						Direction = dir,
						Outward = d,
						Size = leaf * rng.Range(0.8f, 1.2f),
						Roll = rng.Range(-40f, 40f),
						Colour = PlantParts.C32(colour, 1f),
						Part = part,
						CardHash = Hash(i, 11),
						CardRank = always ? 0f : 0.02f + 0.98f * Hash(i, 12),
					});
					leaves++;
				}
			}

			/// <summary>
			/// A whip shrub's leaves: small clusters spaced all along every cane above its bare foot (as many as the cane's
			/// length times <see cref="BushSpecies.Fullness"/> over the leaf size, plus spares), each pointing out and up from
			/// the cane, and a flower spike at the tips of as many canes as the flower share asks for.
			/// </summary>
			private void CaneFoliage()
			{
				float leaf = Mathf.Max(0.05f, sp.LeafSize);
				int index = 0;
				int wantFlowers = 0;
				for (int c = 0; c < wood.Count; c++)
				{
					Wood cane = wood[c];
					if (cane.Twig)
					{
						continue;
					}
					float length = 0f;
					for (int i = 1; i < cane.Path.Count; i++)
					{
						length += Vector3.Distance(cane.Path[i - 1], cane.Path[i]);
					}
					float from = Mathf.Clamp01(sp.BareBase + 0.02f);
					int asked = Mathf.Max(2, Mathf.RoundToInt(length * (1f - from) * Mathf.Max(0.1f, sp.Fullness) / leaf));
					int total = Mathf.Max(asked, Mathf.RoundToInt(asked * Spare));
					wantFlowers += sp.Flowers != null && sp.Flowers.Length > 0 ? Mathf.RoundToInt(asked * Mathf.Max(0f, sp.FlowerShare)) : 0;
					Vector3 axis = (cane.Path[cane.Path.Count - 1] - cane.Path[0]).normalized;
					for (int k = 0; k < total; k++, index++)
					{
						Reseed(70000 + index);
						float t = Mathf.Lerp(from, 0.98f, (k + rng.Range(0.1f, 0.9f)) / total);
						Vector3 at = OnPath(cane.Path, t);
						Vector3 radial = Vector3.ProjectOnPlane(Random3(), axis);
						radial = radial.sqrMagnitude > 1e-6f ? radial.normalized : PlantParts.Perpendicular(axis);
						Vector3 dir = (axis * rng.Range(0.3f, 0.7f) + radial).normalized;
						float height01 = Mathf.Clamp01(at.y / Mathf.Max(0.05f, sp.Height));
						Color colour = Color.Lerp(sp.LeafA, sp.LeafB, rng.NextFloat()) * (Mathf.Lerp(0.8f, 1.05f, height01) * (1f + 0.05f * parts[cane.Part].Tint));
						colour.a = 1f;
						bool always = (long)(k + 1) * asked / total > (long)k * asked / total;
						sprays.Add(new Spray
						{
							Position = at + radial * RadiusOn(cane, t),
							Direction = dir,
							Outward = (at - crown.Centre).sqrMagnitude > 1e-6f ? (at - crown.Centre).normalized : Vector3.up,
							Normal = radial,
							Size = leaf * rng.Range(0.8f, 1.2f),
							Roll = rng.Range(-40f, 40f),
							Colour = PlantParts.C32(colour, 1f),
							Part = cane.Part,
							CardHash = Hash(index, 11),
							CardRank = always ? 0f : 0.02f + 0.98f * Hash(index, 12),
						});
					}
				}
				// Flower spikes at the cane tips, round the canes in turn.
				for (int flowers = 0; flowers < wantFlowers && wood.Count > 0; flowers++)
				{
					Wood cane = wood[flowers % wood.Count];
					Reseed(90000 + flowers);
					Vector3 tip = cane.Path[cane.Path.Count - 1];
					Vector3 axis = (tip - cane.Path[cane.Path.Count - 2]).normalized;
					Vector3 facing = new Vector3(tip.x, 0f, tip.z);
					facing = facing.sqrMagnitude > 1e-6f ? facing.normalized : Vector3.forward;
					float size = Mathf.Max(0.02f, sp.FlowerSize) * rng.Range(0.8f, 1.2f);
					// A second round of flowers on the same canes sits a little lower on them.
					float lower = 0.3f + 0.8f * (flowers / wood.Count);
					sprays.Add(new Spray
					{
						Position = tip - axis * (size * lower),
						Direction = facing,
						Outward = (tip - crown.Centre).normalized,
						Normal = facing,
						Size = size,
						Roll = rng.Range(-15f, 15f),
						Colour = PlantParts.C32(sp.Flowers[rng.Next(sp.Flowers.Length)] * rng.Range(0.9f, 1.05f), FlowerAlpha),
						Part = cane.Part,
						CardHash = Hash(flowers, 13),
						Flower = true,
					});
				}
			}

			private int NearestPart(Vector3 p)
			{
				int best = 0;
				float bestDistance = float.MaxValue;
				for (int i = 0; i < holds.Count; i++)
				{
					float d = (holds[i] - p).sqrMagnitude;
					if (d < bestDistance)
					{
						bestDistance = d;
						best = holdParts[i];
					}
				}
				return parts.Count > 0 ? best : 0;
			}

			// ── Geometry ──────────────────────────────────────────────

			private Vector2 Sway(Vector3 p, float flutter)
			{
				float f = Mathf.Clamp01(p.y / Mathf.Max(0.1f, sp.Height));
				return new Vector2(f * f * Mathf.Clamp(sp.Height / 10f, 0.2f, 0.5f), flutter);
			}

			private void BeginPart(MeshBuilder mesh, int part, PlantPart kind)
			{
				Part p = parts.Count > 0 ? parts[Mathf.Clamp(part, 0, parts.Count - 1)] : default;
				mesh.SetPart(p.Pivot, p.Hash, 0f, kind);
			}

			public void Emit(MeshBuilder mesh, int lod)
			{
				// A whip shrub keeps its canes at the far level too: they are the plant, and its leaves would float without them.
				if (lod < 2 || sp.Habit == BushHabit.Whip)
				{
					EmitWood(mesh, lod);
				}
				EmitSprays(mesh, lod);
			}

			private void EmitWood(MeshBuilder mesh, int lod)
			{
				var colours = new List<Color32>();
				var winds = new List<Vector2>();
				var path = new List<Vector3>();
				var radii = new List<float>();
				Vector2 Solid(float u, float v) => FoliageAtlas.CellUV(FoliageCell.Solid, u, Mathf.Repeat(v, 1f));
				foreach (Wood w in wood)
				{
					if (w.Twig && lod > 0)
					{
						continue;
					}
					path.Clear(); radii.Clear(); colours.Clear(); winds.Clear();
					// At the far level (whip canes only) each cane is one straight piece, foot to tip.
					int step = lod == 0 || w.Path.Count <= 3 ? 1 : lod == 1 ? 2 : w.Path.Count - 1;
					for (int i = 0; i < w.Path.Count; i += step)
					{
						path.Add(w.Path[i]);
						radii.Add(w.Radii[i]);
					}
					if ((w.Path.Count - 1) % step != 0)
					{
						path.Add(w.Path[w.Path.Count - 1]);
						radii.Add(w.Radii[w.Radii.Count - 1]);
					}
					for (int i = 0; i < path.Count; i++)
					{
						float t = (float)i / Mathf.Max(1, path.Count - 1);
						// Older and darker at the foot.
						colours.Add(PlantParts.C32(sp.Bark * Mathf.Lerp(0.72f, 1f, t), 0f));
						winds.Add(Sway(path[i], 0f));
					}
					int sides = lod == 0 ? (radii[0] >= 0.025f ? 5 : w.Twig ? 3 : 4) : 3;
					BeginPart(mesh, w.Part, PlantPart.Limb);
					PlantParts.Tube(mesh, 0, path, radii, sides, 1f, colours, winds, uvMap: Solid);
				}
			}

			/// <summary>One leaf card spanning <paramref name="side"/> and <paramref name="along"/>, lit bent toward the outside of the crown.</summary>
			private void Shingle(MeshBuilder mesh, Vector3 centre, Vector3 side, Vector3 along, float size, Color32 colour, Vector2 windRoot, Vector2 windTip, Vector3 outward, float bend)
			{
				Vector3 face = Vector3.Cross(side, along).normalized;
				Vector3 n = Vector3.Slerp(Vector3.Dot(face, outward) >= 0f ? face : -face, outward, bend);
				PlantParts.Card(mesh, 0, centre, side * size, along * size, sp.LeafCell, colour, windRoot, windTip, n);
			}

			/// <summary>Segments a palmate fan leaf is cut into.</summary>
			private const int FanSegments = 7;
			
			/// <summary>Segments a fan keeps at the far level, each widened so the fan keeps its area.</summary>
			private const int FarFanSegments = 5;

			/// <summary>
			/// A palmate fan leaf (palmetto) from <paramref name="root"/>: <see cref="FanSegments"/> pointed segments spread
			/// over 150° about <paramref name="axis"/> in the plane it makes with <paramref name="side"/>, folded up a little
			/// along their middles and drooping at their tips; lit bent toward the outside of the crown.
			/// </summary>
			private void Fan(MeshBuilder mesh, Vector3 root, Vector3 axis, Vector3 side, Vector3 outward, float length, Color32 colour, Vector2 windRoot, Vector2 windTip, int segments)
			{
				Vector3 face = Vector3.Cross(axis, side).normalized;
				if (Vector3.Dot(face, outward) < 0f)
				{
					face = -face;
				}
				var spine = new List<Vector3>(3);
				var widths = new List<float>(3);
				var colours = new List<Color32> { colour, colour, colour };
				var winds = new List<Vector2> { windRoot, (windRoot + windTip) * 0.5f, windTip };
				Vector3 light = Vector3.Slerp(face, outward, 0.7f);
				float widen = (float)FanSegments / segments;
				for (int k = 0; k < segments; k++)
				{
					float a = Mathf.Deg2Rad * Mathf.Lerp(-75f, 75f, (float)k / (segments - 1));
					Vector3 dir = (axis * Mathf.Cos(a) + side * Mathf.Sin(a)).normalized;
					float droop = 0.12f + 0.18f * Mathf.Abs(Mathf.Sin(a));
					spine.Clear(); widths.Clear();
					spine.Add(root);
					spine.Add(root + dir * (length * 0.55f) + face * (length * 0.04f));
					spine.Add(root + dir * length - face * (length * droop));
					widths.Add(length * 0.1f * widen); widths.Add(length * 0.13f * widen); widths.Add(0f);
					PlantParts.Strip(mesh, 0, spine, widths, Vector3.Cross(face, dir), FoliageCell.Blade, colours, winds, light);
				}
			}

			/// <summary>
			/// The sprays a level keeps, about one in <paramref name="stride"/> of the leaves and of the flowers: for each
			/// point of a coarser lattice of its own, the spray nearest it in direction. Keeping every n-th spray of the
			/// fine lattice did not spread them: the golden-angle steps add up to spiral arms, and the far level had a
			/// hole wherever the crown faced the camera.
			/// </summary>
			private bool[] Keep(int stride)
			{
				var keep = new bool[sprays.Count];
				if (stride <= 1)
				{
					for (int i = 0; i < keep.Length; i++)
					{
						keep[i] = true;
					}
					return keep;
				}
				foreach (bool flowers in new[] { false, true })
				{
					int count = 0;
					foreach (Spray s in sprays)
					{
						count += s.Flower == flowers ? 1 : 0;
					}
					int wanted = count == 0 ? 0 : Mathf.Max(1, Mathf.CeilToInt(count / (float)stride));
					for (int j = 0; j < wanted; j++)
					{
						Vector3 d = Lattice(j, wanted);
						int best = -1;
						float bestDot = float.MinValue;
						for (int i = 0; i < sprays.Count; i++)
						{
							if (keep[i] || sprays[i].Flower != flowers)
							{
								continue;
							}
							float dot = Vector3.Dot(sprays[i].Outward, d);
							if (dot > bestDot)
							{
								bestDot = dot;
								best = i;
							}
						}
						if (best >= 0)
						{
							keep[best] = true;
						}
					}
				}
				return keep;
			}

			private void EmitSprays(MeshBuilder mesh, int lod)
			{
				int stride = lod == 0 ? 1 : lod == 1 ? ReducedStride : FarStride;
				bool[] keep = Keep(stride);
				float scale = 1f, flowerScale = 1f;
				if (lod > 0)
				{
					// Fewer, larger sprays carrying the same share of the full plant's leaf area.
					double all = 0.0, kept = 0.0;
					for (int i = 0; i < sprays.Count; i++)
					{
						Spray s = sprays[i];
						if (s.Flower)
						{
							continue;
						}
						all += s.Size * s.Size;
						if (keep[i])
						{
							kept += s.Size * s.Size;
						}
					}
					float target = lod == 1 ? ReducedFoliageShare : FarFoliageShare;
					float cap = lod == 1 ? 2f : 2.8f;
					scale = kept > 0.0 ? Mathf.Clamp(Mathf.Sqrt((float)(target * all / kept)), 1f, cap) : 1f;
					flowerScale = lod == 1 ? 1.4f : 2f;
				}
				float shell = Mathf.Clamp01(sp.Shell);
				for (int i = 0; i < sprays.Count; i++)
				{
					if (!keep[i])
					{
						continue;
					}
					Spray s = sprays[i];
					Vector3 at = s.Position;
					if (lod > 0 && sp.Habit != BushHabit.Whip)
					{
						// Larger sprays sit nearer the surface, so they do not reach out past it from deep inside.
						float r = crown.Radius(s.Outward);
						float depth = Vector3.Dot(at - crown.Centre, s.Outward) / Mathf.Max(0.01f, r);
						at = crown.Centre + s.Outward * (r * Mathf.Max(depth, 1f - shell * (lod == 1 ? 0.5f : 0.3f)));
						at.y = Mathf.Max(at.y, crown.Floor);
					}
					BeginPart(mesh, s.Part, PlantPart.Leaf);
					mesh.SetCard(s.CardHash, s.CardRank);
					if (s.Flower)
					{
						float size = s.Size * flowerScale;
						Quaternion roll = Quaternion.AngleAxis(s.Roll, s.Direction);
						Vector3 right = roll * PlantParts.Perpendicular(s.Direction);
						Vector3 up = Vector3.Cross(s.Direction, right);
						var wind = Sway(at, 0.6f);
						PlantParts.Card(mesh, 0, at + s.Direction * 0.01f, right * size, up * size, FoliageCell.Flower, s.Colour, wind, wind, s.Direction);
						continue;
					}
					float length = s.Size * scale;
					Vector3 root = at - s.Direction * (length * 0.35f);
					// Bent toward the outside of the crown: lit as one soft mass, not as a heap of flat cards.
					float bend = lod == 2 ? 0.9f : 0.75f;
					Vector3 side = Vector3.Cross(s.Direction, s.Normal);
					side = side.sqrMagnitude > 1e-6f ? side.normalized : PlantParts.Perpendicular(s.Direction);
					// Turned a little about its own axis, so the shingles do not all lie at one angle.
					side = Quaternion.AngleAxis(s.Roll * 0.5f, s.Direction) * side;
					Vector3 centre = root + s.Direction * (length * 0.5f);
					Vector3 outward = (centre - crown.Centre).sqrMagnitude > 1e-6f ? (centre - crown.Centre).normalized : Vector3.up;
					Vector2 windRoot = Sway(root, 0.3f), windTip = Sway(root + s.Direction * length, 1f);
					if (sp.FanLeaves)
					{
						Fan(mesh, at - s.Direction * (length * 0.2f), s.Direction, side, outward, length, s.Colour, windRoot, windTip, lod == 2 ? FarFanSegments : FanSegments);
						continue;
					}
					Shingle(mesh, centre, side, s.Direction, length, s.Colour, windRoot, windTip, outward, bend);
					// A second card across the first, standing out of the crown: alone, a card seen edge-on is a line.
					Shingle(mesh, centre, Quaternion.AngleAxis(90f, s.Direction) * side, s.Direction, length, s.Colour, windRoot, windTip, outward, bend);
				}
			}
		}
	}
}
#endif
