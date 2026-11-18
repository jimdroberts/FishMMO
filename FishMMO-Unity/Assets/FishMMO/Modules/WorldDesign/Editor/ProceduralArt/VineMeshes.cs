#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>The leaf a vine species wears (its cells in the <see cref="VineAtlas"/>).</summary>
	public enum VineLeaf
	{
		/// <summary>Common ivy's lobed leaf (<see cref="VineCell.IvySpray"/>, <see cref="VineCell.IvyMass"/>).</summary>
		Ivy,
		/// <summary>A liana's broad heart (<see cref="VineCell.HeartSpray"/>, <see cref="VineCell.HeartMass"/>).</summary>
		Heart,
		/// <summary>Virginia creeper's five leaflets (<see cref="VineCell.PalmateSpray"/>, <see cref="VineCell.PalmateMass"/>).</summary>
		Palmate,
	}

	/// <summary>What shape of growth a vine piece is, and so how it is placed.</summary>
	public enum VineForm
	{
		/// <summary>
		/// A row of strands hung from an edge (a cave's lintel, a ledge, a wall top): x along the edge, centred on the origin;
		/// y up, the origin at the edge's top; +z out from the face it hangs down. Over the edge the growth lies back across
		/// the top (−z) for a little, so it reads as draped over, not glued under.
		/// </summary>
		Curtain,
		/// <summary>Ivy spread flat over a vertical face: the face is the z = 0 plane, leaves toward +z; x across, centred; y up from the origin at its foot.</summary>
		Mat,
		/// <summary>Runners spilling over the ground from the origin, fanning out toward +z: the foot of a wall, a curtain or a cave's jamb.</summary>
		Spill,
		/// <summary>Climbing a tree's trunk: built on the trunk of one tree species and placed with that tree's own transform.</summary>
		Climber,
	}

	/// <summary>One vine species' recipe. One unit is one metre.</summary>
	public struct VineSpecies
	{
		public string Name;
		public VineLeaf Leaf;
		/// <summary>The leaf colour, picked between per card (sRGB, as authored).</summary>
		public Color LeafA, LeafB;
		/// <summary>The stems' colour.</summary>
		public Color Stem;
		/// <summary>A leaf shoot card's length, metres: the atlas's leaves come out at their real size.</summary>
		public float LeafScale;
		/// <summary>A leaf mass card's side, metres.</summary>
		public float MassScale;
		/// <summary>Turns in autumn and drops its leaves in winter (the vegetation shader's season).</summary>
		public bool Deciduous;
		/// <summary>The autumn colour, as a multiplier on the leaf colour (the material's <c>_AutumnColor</c>).</summary>
		public Color Autumn;
		/// <summary>A liana: thick woody stems hung in loops, and aerial roots, rather than a fine curtain of shoots.</summary>
		public bool Woody;
		/// <summary>Strands across a metre of a hanging curtain.</summary>
		public float StrandsPerMetre;
	}

	/// <summary>One size of one form, at which every species is built (a prefab a variant).</summary>
	public struct VinePiece
	{
		public VineForm Form;
		/// <summary>The size's name, part of the prefab's name (never renamed once used).</summary>
		public string Size;
		/// <summary>Across, metres (a curtain's edge, a mat's width; a spill's reach).</summary>
		public float Width;
		/// <summary>Down a curtain, up a mat, metres.</summary>
		public float Length;
		public int Variants;
	}

	/// <summary>
	/// Vines and ivy (2026-10-12: "Cave mouths, Ruins and old walls, Cliffs and outcrops, Trees, any other relevant
	/// places"): hanging curtains, wall mats, ground spill and trunk climbers, each as one sub-mesh for one vegetation
	/// material, at three levels of detail grown from one set of strands and leaves.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Stems, then leaves on them.</b> Every form is grown as a vine grows: stems first (strands falling from an edge,
	/// shoots climbing a face or a trunk and branching, runners spreading over the ground), then leaf cards hung on them —
	/// shoots (a trailing stem of leaves, <see cref="VineCell.IvySpray"/>) along hanging strands and at the edges of a mat,
	/// masses (<see cref="VineCell.IvyMass"/>) laid over a face, a trunk or the ground where the growth is thick. Leaves
	/// stand a few centimetres off what they cling to, tilted out as real ivy holds its leaves to the light.
	/// </para>
	/// <para>
	/// <b>Levels.</b> Level 0 is the whole vine. Level 1 keeps every other leaf card, grown to carry
	/// <see cref="ReducedFoliageShare"/> of the full leaf area, and only the woody stems; level 2 every sixth card, grown to
	/// <see cref="FarFoliageShare"/>, and no stems: a curtain stays a curtain from far off, not a few loose cards (the
	/// shrubs' rule, BushMeshes). Cards are kept by a stride through the order they were grown in, which is strand by
	/// strand, so every strand keeps its share.
	/// </para>
	/// <para>
	/// <b>Wind.</b> TEXCOORD1.x, how far a vertex sways with the plant, is how far it hangs below its edge for a curtain
	/// (the tip of a long strand swings, the lintel holds still), nothing for a mat or a spill (they are held to what they
	/// grow on; their leaves only flutter), and for a climber exactly what the tree's own bark has at that height
	/// (TreeMeshes: (height share)² × height / 10, held to 2.4), so ivy on a trunk moves with the trunk.
	/// </para>
	/// <para>
	/// <b>A climber follows its tree.</b> Its vertices are marked as the trunk's rings are (fixed parts carrying the trunk's
	/// centre line, <see cref="MeshBuilder.MarkTrunkRings"/>) and its material carries the trees' variation figures, so the
	/// vegetation shader thickens, stretches, twists and leans it exactly as it does the trunk it is placed on (both are
	/// keyed by the same root position): the ivy stays on the bark on every tree.
	/// </para>
	/// <para>
	/// <b>Vertex layout</b> is the vegetation shader's: stems on the atlas's <see cref="VineCell.Stem"/> cell in their colour
	/// with alpha 0 (no seasonal tint), leaves alpha 1; the parts on TEXCOORD2/3 so missing channels never read as a trunk.
	/// </para>
	/// </remarks>
	public static class VineMeshes
	{
		public const int Levels = 3;

		/// <summary>The share of the full vine's leaf area level 1 keeps, and level 2.</summary>
		public const float ReducedFoliageShare = 1.3f, FarFoliageShare = 1.5f;

		/// <summary>
		/// Leaf cards kept by level: every card, every other, then every sixth of a curtain's (its strands carry many small
		/// shoots) but every third of anything laid over a face, a trunk or the ground, whose masses would otherwise grow past
		/// the outline of what they cover.
		/// </summary>
		public static int StrideFor(VineForm form, int lod) => lod <= 0 ? 1 : lod == 1 ? 2 : form == VineForm.Curtain ? 6 : 3;

		/// <summary>How far a hanging strand's sway weight grows per metre it hangs below its edge.</summary>
		public const float SwayPerMetre = 0.4f;

		/// <summary>The vine at a level of detail (0 full, 1 reduced, 2 far), one sub-mesh. Not a <see cref="VineForm.Climber"/> (<see cref="BuildClimber"/>).</summary>
		public static MeshBuilder Build(in VineSpecies species, in VinePiece piece, int variant, int lod, int seed)
		{
			var g = new Grower(species, ProceduralNoise.SeedFor($"vine/{species.Name}/{piece.Form}{piece.Size}/{variant}", seed));
			switch (piece.Form)
			{
				case VineForm.Curtain:
					if (species.Woody) g.Liana(piece.Width, piece.Length);
					else g.Curtain(piece.Width, piece.Length);
					break;
				case VineForm.Mat:
					g.Mat(piece.Width, piece.Length);
					break;
				case VineForm.Spill:
					g.Spill(piece.Width);
					break;
				default:
					throw new ArgumentException($"{piece.Form} is built from a tree (BuildClimber)");
			}
			return g.Emit(Mathf.Clamp(lod, 0, Levels - 1), piece.Form, null);
		}

		/// <summary>
		/// The vine climbing one tree's trunk (<paramref name="trunk"/>, read from that tree's level 0), to
		/// <paramref name="reach"/> of the way up its clear trunk, one sub-mesh; <paramref name="treeHeight"/> is the tree
		/// species' height, for the bark's own sway.
		/// </summary>
		public static MeshBuilder BuildClimber(in VineSpecies species, TrunkProfile trunk, float treeHeight, float reach, float maxMetres, string host, int variant, int lod, int seed)
		{
			var g = new Grower(species, ProceduralNoise.SeedFor($"vine/{species.Name}/Climber/{host}/{variant}", seed));
			g.Climb(trunk, treeHeight, reach, maxMetres);
			return g.Emit(Mathf.Clamp(lod, 0, Levels - 1), VineForm.Climber, trunk);
		}

		/// <summary>The summed area of a vine mesh's leaf cards (vertex alpha 1), m².</summary>
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

		/// <summary>The cells a species' shoots and masses are cut from.</summary>
		public static VineCell SprayCell(VineLeaf leaf) => leaf == VineLeaf.Heart ? VineCell.HeartSpray : leaf == VineLeaf.Palmate ? VineCell.PalmateSpray : VineCell.IvySpray;

		public static VineCell MassCell(VineLeaf leaf) => leaf == VineLeaf.Heart ? VineCell.HeartMass : leaf == VineLeaf.Palmate ? VineCell.PalmateMass : VineCell.IvyMass;

		// ── A tree's trunk ────────────────────────────────────────

		/// <summary>
		/// A tree's main trunk as its level-0 mesh draws it: the centre and radius of each ring up its centre line, read from
		/// the bark's trunk rings (fixed parts marked with their centre, <see cref="MeshBuilder.MarkTrunkRings"/>).
		/// </summary>
		public sealed class TrunkProfile
		{
			public readonly List<float> Heights = new List<float>();
			public readonly List<Vector3> Centres = new List<Vector3>();
			public readonly List<float> Radii = new List<float>();

			/// <summary>The highest ring of the trunk, metres above its foot.</summary>
			public float Top => Heights.Count > 0 ? Heights[Heights.Count - 1] : 0f;

			public bool Valid => Heights.Count >= 2 && Top > 1.5f;

			/// <summary>The trunk's centre line as a path, foot to top (what the climber's vertices are marked round).</summary>
			public List<Vector3> Path => new List<Vector3>(Centres);

			public Vector3 CentreAt(float y) => Interpolate(y, out int i, out float t) ? Vector3.Lerp(Centres[i], Centres[i + 1], t) : Centres[i];

			public float RadiusAt(float y) => Interpolate(y, out int i, out float t) ? Mathf.Lerp(Radii[i], Radii[i + 1], t) : Radii[i];

			private bool Interpolate(float y, out int i, out float t)
			{
				t = 0f;
				if (y <= Heights[0])
				{
					i = 0;
					return false;
				}
				for (i = 0; i + 1 < Heights.Count; i++)
				{
					if (y <= Heights[i + 1])
					{
						t = (y - Heights[i]) / Mathf.Max(1e-4f, Heights[i + 1] - Heights[i]);
						return true;
					}
				}
				i = Heights.Count - 1;
				return false;
			}

			/// <summary>
			/// Reads the trunk from a tree's level 0: every bark vertex marked as a trunk ring belongs to the ring centred on its
			/// pivot; the main trunk is the chain of rings from the lowest one nearest the axis, each next ring the next one up
			/// whose centre is within reach of the last (a buttress or a second culm is left out).
			/// </summary>
			public static TrunkProfile From(MeshBuilder tree)
			{
				var rings = new Dictionary<(int, int, int), (Vector3 centre, float radius, int count)>();
				for (int i = 0; i < tree.VertexCount; i++)
				{
					Vector4 data = tree.PartData[i];
					if (data.z > 0.5f || data.w < 0.5f || tree.Colors[i].a > 8)
					{
						continue;
					}
					Vector4 pivot = tree.PartPivots[i];
					var centre = new Vector3(pivot.x, pivot.y, pivot.z);
					var key = (Mathf.RoundToInt(centre.x * 200f), Mathf.RoundToInt(centre.y * 200f), Mathf.RoundToInt(centre.z * 200f));
					float r = Vector3.Distance(tree.Positions[i], centre);
					rings[key] = rings.TryGetValue(key, out var ring) ? (centre, ring.radius + r, ring.count + 1) : (centre, r, 1);
				}
				var list = new List<(Vector3 centre, float radius)>();
				foreach (var ring in rings.Values)
				{
					if (ring.count >= 3)
					{
						list.Add((ring.centre, ring.radius / ring.count));
					}
				}
				list.Sort((a, b) => a.centre.y.CompareTo(b.centre.y));
				var profile = new TrunkProfile();
				if (list.Count == 0)
				{
					return profile;
				}
				// The foot: low and on the axis.
				int start = 0;
				float best = float.MaxValue;
				for (int i = 0; i < list.Count; i++)
				{
					float score = list[i].centre.y + 2f * new Vector2(list[i].centre.x, list[i].centre.z).magnitude;
					if (score < best)
					{
						best = score;
						start = i;
					}
				}
				Vector3 at = list[start].centre;
				profile.Add(list[start].centre, list[start].radius);
				for (int i = start + 1; i < list.Count; i++)
				{
					Vector3 c = list[i].centre;
					float dy = c.y - at.y;
					if (dy < 0.05f)
					{
						continue;
					}
					// Rings are a segment apart (a tall emergent's are many metres): only a real gap ends the trunk.
					if (dy > 15f)
					{
						break;
					}
					if (new Vector2(c.x - at.x, c.z - at.z).magnitude > 0.6f * dy + 0.4f)
					{
						continue;
					}
					profile.Add(c, list[i].radius);
					at = c;
				}
				return profile;
			}

			private void Add(Vector3 centre, float radius)
			{
				Heights.Add(centre.y);
				Centres.Add(centre);
				Radii.Add(radius);
			}
		}

		// ── The grower ────────────────────────────────────────────

		/// <summary>A leaf card: a shoot from its stalk (<see cref="Centred"/> false) or a mass about its middle.</summary>
		private struct Card
		{
			public Vector3 Root;
			/// <summary>Unit: from the stalk to the tip (a shoot), or the card's up (a mass).</summary>
			public Vector3 Along;
			/// <summary>Unit, across the card.</summary>
			public Vector3 Across;
			/// <summary>The normal written for lighting: bent out from what the vine grows on, so it lights as one mass.</summary>
			public Vector3 Light;
			public float Length, Width;
			public bool Centred;
			public VineCell Cell;
			public Color32 Colour;
			public Vector2 WindRoot, WindTip;
			public float Hash;
			/// <summary>The bare-strand cell (<see cref="VineCell.Roots"/>) is drawn hanging from its top: its v runs the other way.</summary>
			public bool RootsUpsideDown;
		}

		/// <summary>A stem: a tube for a woody stem, a flat ribbon facing <see cref="Facing"/> for a fine one.</summary>
		private sealed class Stem
		{
			public readonly List<Vector3> Path = new List<Vector3>();
			public readonly List<float> Radii = new List<float>();
			public readonly List<Vector2> Wind = new List<Vector2>();
			/// <summary>Zero: a tube. Otherwise the ribbon faces this way (out of the wall, up off the ground).</summary>
			public Vector3 Facing;
			/// <summary>The deepest level the stem is drawn at (0 only the full vine, 1 the reduced one too).</summary>
			public int Level;
		}

		private sealed class Grower
		{
			private readonly VineSpecies sp;
			private readonly DeterministicRNG rng;
			private readonly List<Card> cards = new List<Card>();
			private readonly List<Stem> stems = new List<Stem>();
			private readonly VineCell spray, mass;

			public Grower(VineSpecies species, int seed)
			{
				sp = species;
				rng = new DeterministicRNG(seed);
				spray = SprayCell(species.Leaf);
				mass = MassCell(species.Leaf);
			}

			private Color32 LeafColour(float shade = 1f) => PlantParts.C32(Color.Lerp(sp.LeafA, sp.LeafB, rng.NextFloat()) * (shade * rng.Range(0.88f, 1.08f)), 1f);

			private Color32 StemColour => PlantParts.C32(sp.Stem, 0f);

			/// <summary>A smooth wander over 0..1 (three sines of their own phase), 0..1.</summary>
			private Func<float, float> Wave()
			{
				float p0 = rng.Range(0f, 6.283f), p1 = rng.Range(0f, 6.283f), p2 = rng.Range(0f, 6.283f);
				float f0 = rng.Range(2f, 3.5f), f1 = rng.Range(5f, 8f), f2 = rng.Range(11f, 16f);
				return t => 0.5f + 0.25f * Mathf.Sin(t * f0 + p0) + 0.15f * Mathf.Sin(t * f1 + p1) + 0.1f * Mathf.Sin(t * f2 + p2);
			}

			private void Shoot(Vector3 root, Vector3 dir, Vector3 face, float length, Vector2 windRoot, Vector2 windTip, float shade = 1f)
			{
				dir = dir.normalized;
				Vector3 across = Vector3.Cross(face, dir);
				if (across.sqrMagnitude < 1e-6f)
				{
					across = PlantParts.Perpendicular(dir);
				}
				across = Quaternion.AngleAxis(rng.Range(-25f, 25f), dir) * across.normalized;
				Vector3 normal = Vector3.Cross(dir, across).normalized;
				if (Vector3.Dot(normal, face) < 0f)
				{
					normal = -normal;
				}
				cards.Add(new Card
				{
					Root = root, Along = dir, Across = across, Light = Vector3.Slerp(normal, face, 0.55f).normalized,
					Length = length, Width = length * 0.78f, Centred = false, Cell = spray, Colour = LeafColour(shade),
					WindRoot = windRoot, WindTip = windTip, Hash = rng.NextFloat(),
				});
			}

			private void Mass(Vector3 centre, Vector3 up, Vector3 face, float size, Vector2 wind, float shade = 1f)
			{
				up = up.normalized;
				Vector3 across = Vector3.Cross(face, up);
				if (across.sqrMagnitude < 1e-6f)
				{
					across = PlantParts.Perpendicular(up);
				}
				across.Normalize();
				Vector3 normal = Vector3.Cross(up, across).normalized;
				if (Vector3.Dot(normal, face) < 0f)
				{
					normal = -normal;
				}
				cards.Add(new Card
				{
					Root = centre, Along = up, Across = across, Light = Vector3.Slerp(normal, face, 0.6f).normalized,
					Length = size, Width = size, Centred = true, Cell = mass, Colour = LeafColour(shade),
					WindRoot = wind, WindTip = wind, Hash = rng.NextFloat(),
				});
			}

			// ── A hanging curtain of fine strands (ivy, creeper) ──

			public void Curtain(float width, float length)
			{
				int strands = Mathf.Max(3, Mathf.RoundToInt(width * sp.StrandsPerMetre));
				Func<float, float> hem = Wave();
				float back = rng.Range(0.3f, 0.5f);
				var outward = Vector3.forward;
				for (int i = 0; i < strands; i++)
				{
					float u = (i + 0.5f) / strands;
					float x = (u - 0.5f) * width + rng.Range(-0.3f, 0.3f) * width / strands;
					// A ragged hem: longest in the middle, shorter at the ends, each strand its own.
					float edge = Mathf.Abs(u - 0.5f) * 2f;
					float hang = length * Mathf.Clamp(0.35f + 0.65f * hem(u) * (1f - 0.45f * edge * edge), 0.25f, 1f) * rng.Range(0.85f, 1.08f);
					Strand(x, hang, back * rng.Range(0.6f, 1f), outward);
				}
				// Over the edge: masses lying back across the top, and a row over the lip itself.
				float massSize = sp.MassScale;
				int across = Mathf.Max(2, Mathf.CeilToInt(width / (massSize * 0.6f)));
				for (int i = 0; i < across; i++)
				{
					float x = ((i + 0.5f) / across - 0.5f) * width + rng.Range(-0.1f, 0.1f);
					var top = new Vector3(x, 0.05f + rng.Range(0f, 0.04f), -back * rng.Range(0.35f, 0.75f));
					Mass(top, new Vector3(0f, 0.45f, -1f), Vector3.up + Vector3.forward * 0.3f, massSize * rng.Range(0.85f, 1.1f), Vector2.zero);
					var lip = new Vector3(x + rng.Range(-0.08f, 0.08f), -massSize * 0.3f, 0.08f + rng.Range(0f, 0.04f));
					Mass(lip, Vector3.up + Vector3.forward * 0.25f, outward + Vector3.up * 0.3f, massSize * rng.Range(0.8f, 1f), new Vector2(SwayPerMetre * massSize * 0.3f, 0.4f));
				}
			}

			/// <summary>One strand: from back over the top, over the lip and down, with shoots alternating along it.</summary>
			private void Strand(float x, float hang, float back, Vector3 outward)
			{
				var stem = new Stem { Facing = outward, Level = 0 };
				const float step = 0.3f;
				float drift = rng.Range(-0.06f, 0.06f), bow = rng.Range(0.02f, 0.08f), phase = rng.Range(0f, 6.283f);
				stem.Path.Add(new Vector3(x, 0.03f, -back));
				stem.Path.Add(new Vector3(x, 0.035f, -0.08f));
				stem.Path.Add(new Vector3(x, -0.04f, 0.05f));
				int steps = Mathf.Max(1, Mathf.CeilToInt(hang / step));
				for (int s = 1; s <= steps; s++)
				{
					float d = hang * s / steps;
					stem.Path.Add(new Vector3(x + drift * d + 0.03f * Mathf.Sin(phase + d * 2.3f), -d, 0.06f + bow * Mathf.Sin(Mathf.PI * d / hang)));
				}
				foreach (Vector3 p in stem.Path)
				{
					stem.Radii.Add(0.006f);
					stem.Wind.Add(new Vector2(SwayPerMetre * Mathf.Max(0f, -p.y), 0f));
				}
				stems.Add(stem);
				// Shoots down the strand, alternating, hanging down and out and a little to either side.
				float spacing = sp.LeafScale * 0.5f;
				float side = rng.NextFloat() < 0.5f ? -1f : 1f;
				for (float d = rng.Range(0f, spacing); d < hang; d += spacing * rng.Range(0.8f, 1.2f))
				{
					Vector3 root = OnPath(stem.Path, 2, d / hang);
					side = -side;
					float swing = rng.Range(25f, 60f) * Mathf.Deg2Rad;
					Vector3 dir = Vector3.down * Mathf.Cos(swing) + Vector3.right * (side * Mathf.Sin(swing)) + outward * rng.Range(0.2f, 0.6f);
					float length = sp.LeafScale * rng.Range(0.85f, 1.15f) * (1f - 0.25f * d / hang);
					float tipDepth = -root.y + dir.normalized.y * -length;
					Shoot(root, dir, outward, length, new Vector2(SwayPerMetre * -root.y, 0.3f), new Vector2(SwayPerMetre * tipDepth, 1f), 1f - 0.12f * d / hang);
				}
				// The strand's growing end: one shoot straight on down.
				Vector3 end = stem.Path[stem.Path.Count - 1];
				Shoot(end + Vector3.up * 0.05f, Vector3.down + outward * 0.2f, outward, sp.LeafScale * 0.8f,
					new Vector2(SwayPerMetre * -end.y, 0.4f), new Vector2(SwayPerMetre * (-end.y + sp.LeafScale), 1f));
			}

			// ── A liana's curtain: woody loops, aerial roots, big leaves ──

			public void Liana(float width, float length)
			{
				var outward = Vector3.forward;
				int ropes = Mathf.Max(3, Mathf.RoundToInt(width * sp.StrandsPerMetre));
				var xs = new float[ropes];
				for (int i = 0; i < ropes; i++)
				{
					xs[i] = ((i + 0.5f) / ropes - 0.5f) * width + rng.Range(-0.15f, 0.15f);
				}
				for (int i = 0; i < ropes; i++)
				{
					// The ropes are the liana's look from any distance: kept at every level.
					var stem = new Stem { Facing = Vector3.zero, Level = 2 };
					float radius = rng.Range(0.014f, 0.035f);
					bool loop = i + 1 < ropes && rng.NextFloat() < 0.45f;
					if (loop)
					{
						// Hung between two holds: a catenary-like dip.
						float x0 = xs[i], x1 = xs[i + 1] + rng.Range(0.2f, 0.6f);
						float dip = length * rng.Range(0.3f, 0.65f);
						const int n = 12;
						for (int s = 0; s <= n; s++)
						{
							float t = s / (float)n;
							float y = -dip * (1f - Mathf.Pow(2f * t - 1f, 2f)) * (1f + 0.1f * Mathf.Sin(t * 9f));
							stem.Path.Add(new Vector3(Mathf.Lerp(x0, x1, t), y + 0.02f, 0.05f + 0.12f * Mathf.Sin(Mathf.PI * t)));
						}
						i++;
					}
					else
					{
						float hang = length * rng.Range(0.55f, 1f);
						float drift = rng.Range(-0.1f, 0.1f), phase = rng.Range(0f, 6.283f);
						stem.Path.Add(new Vector3(xs[i], 0.03f, -0.25f));
						stem.Path.Add(new Vector3(xs[i], -0.02f, 0.06f));
						const int n = 10;
						for (int s = 1; s <= n; s++)
						{
							float d = hang * s / n;
							stem.Path.Add(new Vector3(xs[i] + drift * d + 0.06f * Mathf.Sin(phase + d * 1.7f), -d, 0.08f + 0.05f * Mathf.Sin(d)));
						}
					}
					for (int s = 0; s < stem.Path.Count; s++)
					{
						float t = s / (float)(stem.Path.Count - 1);
						stem.Radii.Add(radius * Mathf.Lerp(1f, 0.6f, t));
						stem.Wind.Add(new Vector2(SwayPerMetre * 0.6f * Mathf.Max(0f, -stem.Path[s].y), 0f));
					}
					stems.Add(stem);
					// Big leaves on the upper part of each rope.
					for (float t = rng.Range(0.02f, 0.1f); t < 0.75f; t += rng.Range(0.09f, 0.16f))
					{
						Vector3 root = OnPath(stem.Path, 0, t);
						float side = rng.NextFloat() < 0.5f ? -1f : 1f;
						Vector3 dir = Vector3.down * rng.Range(0.3f, 0.9f) + Vector3.right * side * rng.Range(0.4f, 0.9f) + outward * rng.Range(0.3f, 0.7f);
						float leaf = sp.LeafScale * rng.Range(0.85f, 1.2f);
						Shoot(root, dir, outward, leaf, new Vector2(SwayPerMetre * 0.6f * -root.y, 0.3f), new Vector2(SwayPerMetre * 0.6f * (-root.y + leaf * 0.5f), 1f));
					}
				}
				// Aerial roots: long bare strands hanging from the edge between the ropes.
				int roots = Mathf.Max(2, Mathf.RoundToInt(width * 1.3f));
				for (int i = 0; i < roots; i++)
				{
					float x = ((i + rng.Range(0.2f, 0.8f)) / roots - 0.5f) * width;
					float hang = length * rng.Range(0.5f, 1.05f);
					var root = new Vector3(x, 0f, 0.1f + rng.Range(0f, 0.08f));
					cards.Add(new Card
					{
						Root = root, Along = Vector3.down, Across = Vector3.right, Light = (outward + Vector3.up * 0.2f).normalized,
						Length = hang, Width = rng.Range(0.3f, 0.45f), Centred = false, Cell = VineCell.Roots, Colour = PlantParts.C32(sp.Stem * 1.15f, 0f),
						WindRoot = Vector2.zero, WindTip = new Vector2(SwayPerMetre * hang, 0.5f), Hash = rng.NextFloat(), RootsUpsideDown = true,
					});
				}
				// The top: a mat of leaves lying over the edge.
				int across = Mathf.Max(2, Mathf.CeilToInt(width / (sp.MassScale * 0.7f)));
				for (int i = 0; i < across; i++)
				{
					float x = ((i + 0.5f) / across - 0.5f) * width + rng.Range(-0.12f, 0.12f);
					Mass(new Vector3(x, 0.1f, -rng.Range(0.05f, 0.3f)), new Vector3(0f, 0.55f, -1f), Vector3.up + outward * 0.4f, sp.MassScale * rng.Range(0.7f, 0.95f), Vector2.zero);
					Mass(new Vector3(x + rng.Range(-0.1f, 0.1f), -sp.MassScale * 0.35f, 0.12f), Vector3.up + outward * 0.2f, outward + Vector3.up * 0.2f, sp.MassScale * rng.Range(0.8f, 1f),
						new Vector2(SwayPerMetre * 0.2f, 0.4f));
				}
			}

			// ── A mat over a vertical face ──

			public void Mat(float width, float height)
			{
				var face = Vector3.forward;
				Func<float, float> top = Wave();
				float narrow = rng.Range(0.2f, 0.4f);
				bool Inside(float x, float y)
				{
					float h = height * (0.6f + 0.4f * top(x / width + 0.5f));
					float half = width * 0.5f * (1f - narrow * Mathf.Pow(Mathf.Clamp01(y / Mathf.Max(0.1f, h)), 2f));
					return y >= 0f && y <= h && Mathf.Abs(x) <= half;
				}
				// Stems: a few climbing from the foot, branching sideways and up as they go.
				int climbers = Mathf.Max(2, Mathf.RoundToInt(width / 1.1f) + 1);
				for (int k = 0; k < climbers; k++)
				{
					float x = ((k + 0.5f) / climbers - 0.5f) * width * 0.8f + rng.Range(-0.15f, 0.15f);
					Climb2D(new Vector2(x, -0.05f), new Vector2(rng.Range(-0.3f, 0.3f), 1f), height * rng.Range(0.75f, 1f), 0.016f, 2, Inside, face);
				}
				// Leaf masses over everything inside the outline, on a jittered grid, in two layers out from the face.
				float size = sp.MassScale;
				float spacing = size * 0.55f;
				int nx = Mathf.CeilToInt(width / spacing) + 1, ny = Mathf.CeilToInt(height / spacing) + 1;
				for (int j = 0; j < ny; j++)
				{
					for (int i = 0; i < nx; i++)
					{
						float x = (i - (nx - 1) * 0.5f) * spacing + rng.Range(-0.35f, 0.35f) * spacing;
						float y = (j + 0.25f) * spacing + rng.Range(-0.35f, 0.35f) * spacing;
						float s = size * rng.Range(0.8f, 1.15f);
						if (!Inside(x, y))
						{
							continue;
						}
						// Leaves stand out from the face and tilt out at their tops; lower ones in the shade of the rest.
						float off = rng.Range(0.03f, 0.1f);
						Vector3 up = Quaternion.AngleAxis(rng.Range(-180f, 180f), face) * Vector3.up;
						up = (up + face * rng.Range(0.15f, 0.4f)).normalized;
						Mass(new Vector3(x, Mathf.Max(s * 0.3f, y), off), up, face, s, new Vector2(0f, 0.35f), Mathf.Lerp(0.85f, 1.05f, Mathf.Clamp01(y / height)));
					}
				}
				// Shoots at the edge, reaching out of the outline (up from the top, sideways off the flanks).
				int around = Mathf.Max(6, Mathf.RoundToInt((width + 2f * height) / (sp.LeafScale * 0.6f)));
				for (int i = 0; i < around; i++)
				{
					float t = (i + rng.NextFloat()) / around;
					// Round the outline: up the left side, across the top, down the right side.
					Vector2 p, outwardIn;
					if (t < 0.3f)
					{
						float y = t / 0.3f * height;
						p = new Vector2(-width * 0.5f, y);
						outwardIn = new Vector2(-1f, 0.3f);
					}
					else if (t < 0.7f)
					{
						float x = ((t - 0.3f) / 0.4f - 0.5f) * width;
						p = new Vector2(x, height);
						outwardIn = new Vector2(rng.Range(-0.4f, 0.4f), 1f);
					}
					else
					{
						float y = (1f - (t - 0.7f) / 0.3f) * height;
						p = new Vector2(width * 0.5f, y);
						outwardIn = new Vector2(1f, 0.3f);
					}
					// Pulled in until it is on the growth.
					Vector2 inward = -outwardIn.normalized;
					for (int k = 0; k < 20 && !Inside(p.x, p.y); k++)
					{
						p += inward * 0.08f;
					}
					if (!Inside(p.x, p.y))
					{
						continue;
					}
					var root = new Vector3(p.x, p.y, rng.Range(0.03f, 0.08f));
					Vector3 dir = new Vector3(outwardIn.x, outwardIn.y, 0f).normalized + face * rng.Range(0.1f, 0.35f);
					Shoot(root, dir, face, sp.LeafScale * rng.Range(0.8f, 1.15f), new Vector2(0f, 0.2f), new Vector2(0.03f, 0.8f));
				}
			}

			/// <summary>
			/// A climbing shoot over a face (the z = 0 plane) from <paramref name="from"/>: up in steps, wandering, putting out
			/// side shoots that are themselves climbed to <paramref name="depth"/> generations, kept inside the outline.
			/// </summary>
			private void Climb2D(Vector2 from, Vector2 heading, float reach, float radius, int depth, Func<float, float, bool> inside, Vector3 face)
			{
				var stem = new Stem { Facing = face, Level = 0 };
				const float step = 0.25f;
				Vector2 p = from, dir = heading.normalized;
				var branches = new List<(Vector2 at, Vector2 dir, float reach)>();
				for (float done = 0f; done < reach; done += step)
				{
					stem.Path.Add(new Vector3(p.x, p.y, 0.012f));
					Vector2 next = p + dir * step;
					if (!inside(next.x, Mathf.Max(0f, next.y)) && done > step)
					{
						break;
					}
					p = next;
					// Wander, and lean back toward up (ivy climbs).
					float turn = rng.Range(-0.35f, 0.35f);
					dir = new Vector2(dir.x * Mathf.Cos(turn) - dir.y * Mathf.Sin(turn), dir.x * Mathf.Sin(turn) + dir.y * Mathf.Cos(turn));
					dir = Vector2.Lerp(dir, Vector2.up, 0.25f).normalized;
					if (depth > 0 && rng.NextFloat() < 0.35f)
					{
						float side = rng.NextFloat() < 0.5f ? -1f : 1f;
						branches.Add((p, new Vector2(side, rng.Range(0.3f, 1.2f)), reach * rng.Range(0.3f, 0.55f)));
					}
				}
				if (stem.Path.Count >= 2)
				{
					for (int s = 0; s < stem.Path.Count; s++)
					{
						stem.Radii.Add(radius * Mathf.Lerp(1f, 0.5f, s / (float)(stem.Path.Count - 1)));
						stem.Wind.Add(Vector2.zero);
					}
					stems.Add(stem);
				}
				foreach ((Vector2 at, Vector2 d, float r) in branches)
				{
					Climb2D(at, d, r, radius * 0.6f, depth - 1, inside, face);
				}
			}

			// ── Spill over the ground ──

			public void Spill(float reach)
			{
				var up = Vector3.up;
				int runners = Mathf.Max(4, Mathf.RoundToInt(reach * 2.6f));
				for (int r = 0; r < runners; r++)
				{
					float yaw = Mathf.Lerp(-75f, 75f, (r + rng.Range(0.15f, 0.85f)) / runners);
					float length = reach * rng.Range(0.55f, 1f);
					var stem = new Stem { Facing = up, Level = 0 };
					Vector3 heading = Quaternion.AngleAxis(yaw, up) * Vector3.forward;
					Vector3 p = new Vector3(rng.Range(-0.15f, 0.15f), 0.02f, rng.Range(-0.05f, 0.1f));
					const float step = 0.3f;
					for (float d = 0f; d <= length; d += step)
					{
						stem.Path.Add(p);
						heading = Quaternion.AngleAxis(rng.Range(-18f, 18f), up) * heading;
						p += heading * step;
						p.y = 0.02f;
					}
					if (stem.Path.Count < 2)
					{
						continue;
					}
					foreach (Vector3 _ in stem.Path)
					{
						stem.Radii.Add(0.007f);
						stem.Wind.Add(Vector2.zero);
					}
					stems.Add(stem);
					// Shoots along the runner, lying out to either side, lifted a little off the ground.
					float spacing = sp.LeafScale * 0.55f;
					float side = 1f;
					float total = PathLength(stem.Path);
					for (float d = rng.Range(0f, spacing); d < total; d += spacing * rng.Range(0.8f, 1.2f))
					{
						Vector3 root = OnPath(stem.Path, 0, d / total) + up * 0.015f;
						Vector3 tangent = (OnPath(stem.Path, 0, Mathf.Min(1f, (d + 0.05f) / total)) - OnPath(stem.Path, 0, Mathf.Max(0f, (d - 0.05f) / total))).normalized;
						side = -side;
						Vector3 dir = Quaternion.AngleAxis(side * rng.Range(30f, 65f), up) * tangent + up * rng.Range(0.15f, 0.4f);
						Shoot(root, dir, up, sp.LeafScale * rng.Range(0.8f, 1.1f) * (1f - 0.2f * d / total), new Vector2(0f, 0.3f), new Vector2(0f, 0.8f));
					}
				}
				// Thick at the root: masses lying on the ground about the origin, thinning out.
				int masses = Mathf.Max(3, Mathf.RoundToInt(reach * reach * 2.2f));
				for (int i = 0; i < masses; i++)
				{
					float r = reach * 0.55f * Mathf.Sqrt(rng.NextFloat());
					float yaw = rng.Range(-80f, 80f) * Mathf.Deg2Rad;
					var centre = new Vector3(Mathf.Sin(yaw) * r, rng.Range(0.04f, 0.09f), Mathf.Cos(yaw) * r * 0.9f + 0.1f);
					Vector3 cardUp = Quaternion.AngleAxis(rng.Range(-180f, 180f), up) * Vector3.forward;
					cardUp = (cardUp + up * rng.Range(0.05f, 0.3f)).normalized;
					Mass(centre, cardUp, up, sp.MassScale * rng.Range(0.75f, 1.05f), new Vector2(0f, 0.3f), 0.95f);
				}
			}

			// ── Up a trunk ──

			private TrunkProfile trunk;
			private float treeHeight;

			/// <summary>The bark's own sway at a height (TreeMeshes' Sway), so the climber moves with the trunk.</summary>
			private Vector2 BarkSway(float y, float flutter)
			{
				float f = Mathf.Clamp01(y / Mathf.Max(0.1f, treeHeight));
				return new Vector2(f * f * Mathf.Min(treeHeight / 10f, 2.4f), flutter);
			}

			private Vector3 OnTrunk(float y, float angle, float off)
			{
				Vector3 c = trunk.CentreAt(y);
				float r = trunk.RadiusAt(y) + off;
				return new Vector3(c.x + Mathf.Cos(angle) * r, y, c.z + Mathf.Sin(angle) * r);
			}

			public void Climb(TrunkProfile profile, float height, float reach, float maxMetres)
			{
				trunk = profile;
				treeHeight = height;
				if (!profile.Valid)
				{
					return;
				}
				// Up a share of the clear trunk, but no higher than a vine climbs: a 40 m emergent is not ivied to its crown.
				float top = Mathf.Clamp(Mathf.Min(profile.Top * reach, maxMetres), 1.5f, profile.Top - 0.3f);
				float girth = profile.RadiusAt(0.5f);
				int count = Mathf.Clamp(Mathf.RoundToInt(2f * Mathf.PI * girth / 0.35f), 3, 7);
				var heads = new List<(float angle, float top)>();
				float twistSign = rng.NextFloat() < 0.5f ? -1f : 1f;
				for (int k = 0; k < count; k++)
				{
					float angle = (k + rng.Range(0.1f, 0.9f)) / count * Mathf.PI * 2f;
					float stemTop = top * rng.Range(0.55f, 1f);
					float twist = twistSign * rng.Range(0.04f, 0.22f);
					var stem = new Stem { Facing = Vector3.zero, Level = sp.Woody ? 1 : 0 };
					float radius = sp.Woody ? rng.Range(0.02f, 0.04f) : rng.Range(0.01f, 0.02f);
					float step = sp.Woody ? 0.6f : 0.4f;
					float a = angle;
					for (float y = -0.05f; y <= stemTop; y += step)
					{
						stem.Path.Add(OnTrunk(Mathf.Max(0f, y), a, radius));
						a += (twist + rng.Range(-0.08f, 0.08f)) * step / Mathf.Max(0.15f, profile.RadiusAt(Mathf.Max(0f, y)));
					}
					if (stem.Path.Count < 2)
					{
						continue;
					}
					for (int s = 0; s < stem.Path.Count; s++)
					{
						stem.Radii.Add(radius * Mathf.Lerp(1f, 0.45f, s / (float)(stem.Path.Count - 1)));
						stem.Wind.Add(BarkSway(stem.Path[s].y, 0f));
					}
					stems.Add(stem);
					heads.Add((angle, stemTop));
				}
				// The cover: leaf masses laid round the trunk where the stems run, thick low down, thinning up to a ragged top.
				float size = sp.MassScale * 0.85f;
				Func<float, float> rag = Wave();
				for (float y = size * 0.3f; y < top + size * 0.3f; y += size * 0.58f)
				{
					float r = profile.RadiusAt(Mathf.Min(y, profile.Top));
					int round = Mathf.Max(3, Mathf.CeilToInt(2f * Mathf.PI * (r + 0.06f) / (size * 0.68f)));
					for (int i = 0; i < round; i++)
					{
						float angle = (i + rng.Range(-0.3f, 0.3f)) / round * Mathf.PI * 2f;
						float cover = 0f;
						foreach ((float stemAngle, float stemTop) in heads)
						{
							float arc = Mathf.Abs(Mathf.DeltaAngle(angle * Mathf.Rad2Deg, stemAngle * Mathf.Rad2Deg)) * Mathf.Deg2Rad * r;
							float high = Mathf.Clamp01((stemTop * (0.85f + 0.3f * rag(angle / 6.283f)) - y) / 0.8f);
							cover = Mathf.Max(cover, high * Mathf.Clamp01(1f - arc / (0.35f + 0.8f * r)));
						}
						if (cover < 0.25f + 0.4f * rng.NextFloat())
						{
							continue;
						}
						float off = rng.Range(0.03f, 0.08f);
						Vector3 at = OnTrunk(y + rng.Range(-0.1f, 0.1f), angle, off);
						Vector3 radial = (at - profile.CentreAt(at.y));
						radial.y = 0f;
						radial = radial.sqrMagnitude > 1e-6f ? radial.normalized : Vector3.forward;
						Vector3 up = (Quaternion.AngleAxis(rng.Range(-40f, 40f), radial) * Vector3.up + radial * rng.Range(0.1f, 0.35f)).normalized;
						Mass(at, up, radial, size * rng.Range(0.8f, 1.1f), BarkSway(at.y, 0.4f), Mathf.Lerp(0.85f, 1.05f, y / Mathf.Max(1f, top)));
					}
				}
				// Shoots standing out at the top of the cover: ivy's flowering, bushy arms where it has climbed into the light.
				foreach ((float angle, float stemTop) in heads)
				{
					int shoots = rng.Range(2, 5);
					for (int s = 0; s < shoots; s++)
					{
						float y = stemTop * rng.Range(0.7f, 1f);
						float a = angle + rng.Range(-0.4f, 0.4f);
						Vector3 at = OnTrunk(y, a, 0.04f);
						Vector3 radial = at - profile.CentreAt(y);
						radial.y = 0f;
						radial = radial.sqrMagnitude > 1e-6f ? radial.normalized : Vector3.forward;
						Vector3 dir = radial * rng.Range(0.5f, 1f) + Vector3.up * rng.Range(0.2f, 0.8f);
						float length = sp.LeafScale * rng.Range(0.8f, 1.1f);
						Shoot(at, dir, radial, length, BarkSway(y, 0.3f), BarkSway(y + length * 0.5f, 1f));
					}
				}
			}

			// ── Out to a mesh ──

			public MeshBuilder Emit(int lod, VineForm form, TrunkProfile climbing)
			{
				var mesh = new MeshBuilder(1);
				mesh.SetPart(Vector3.zero, 0f, 0f, PlantPart.Fixed);
				foreach (Stem stem in stems)
				{
					if (stem.Level < lod || stem.Path.Count < 2)
					{
						continue;
					}
					EmitStem(mesh, stem, lod);
				}
				int stride = StrideFor(form, lod);
				float share = lod == 0 ? 1f : (lod == 1 ? ReducedFoliageShare : FarFoliageShare) * stride;
				for (int i = 0; i < cards.Count; i += stride)
				{
					Card card = cards[i];
					// A mass grows evenly; a shoot more across than along, so a hanging strand does not reach further down. Round a
					// trunk a mass grows up the trunk rather than across it: a flat card wider than the trunk would stand off it.
					float lengthScale = card.Centred ? Mathf.Sqrt(share) : Mathf.Pow(share, 0.35f);
					if (card.Centred && form == VineForm.Climber)
					{
						lengthScale = share / Mathf.Min(Mathf.Sqrt(share), 1.3f);
					}
					float widthScale = share / lengthScale;
					if (card.Cell == VineCell.Roots)
					{
						lengthScale = 1f;
						widthScale = Mathf.Min(share, 3f);
					}
					EmitCard(mesh, in card, lengthScale, widthScale);
				}
				if (climbing != null && climbing.Valid)
				{
					// Every vertex follows the trunk's own girth (VegVary): marked round its centre line.
					mesh.MarkTrunkRings(0, climbing.Path);
				}
				mesh.RecalculateNormals(true, onlyMissing: true);
				mesh.RecalculateTangents();
				return mesh;
			}

			private void EmitStem(MeshBuilder mesh, Stem stem, int lod)
			{
				float total = Mathf.Max(0.01f, PathLength(stem.Path));
				var colours = new List<Color32>(stem.Path.Count);
				for (int i = 0; i < stem.Path.Count; i++)
				{
					colours.Add(StemColour);
				}
				if (stem.Facing == Vector3.zero)
				{
					int sides = lod == 0 ? 4 : 3;
					if (lod < 2)
					{
						PlantParts.Tube(mesh, 0, stem.Path, stem.Radii, sides, 1f, colours, stem.Wind,
							uvMap: (u, along) => VineAtlas.CellUV(VineCell.Stem, u, along / total));
						return;
					}
					// Far off, every other joint: the rope's line, not its sag in detail.
					var path = new List<Vector3>();
					var radii = new List<float>();
					var wind = new List<Vector2>();
					var tone = new List<Color32>();
					for (int i = 0; i < stem.Path.Count; i++)
					{
						if (i % 2 == 0 || i == stem.Path.Count - 1)
						{
							path.Add(stem.Path[i]);
							radii.Add(stem.Radii[i] * 1.3f);
							wind.Add(stem.Wind[i]);
							tone.Add(colours[i]);
						}
					}
					PlantParts.Tube(mesh, 0, path, radii, 3, 1f, tone, wind, uvMap: (u, along) => VineAtlas.CellUV(VineCell.Stem, u, along / total));
					return;
				}
				// A ribbon facing out of what it lies on: two triangles a step, seen as a line of stem in the gaps.
				int previousL = -1, previousR = -1;
				float along = 0f;
				for (int i = 0; i < stem.Path.Count; i++)
				{
					if (i > 0)
					{
						along += Vector3.Distance(stem.Path[i], stem.Path[i - 1]);
					}
					Vector3 tangent = (stem.Path[Mathf.Min(i + 1, stem.Path.Count - 1)] - stem.Path[Mathf.Max(0, i - 1)]).normalized;
					Vector3 side = Vector3.Cross(stem.Facing, tangent);
					if (side.sqrMagnitude < 1e-6f)
					{
						side = PlantParts.Perpendicular(tangent);
					}
					side = side.normalized * Mathf.Max(0.004f, stem.Radii[i] * 1.6f);
					float v = along / total;
					int l = mesh.AddVertex(stem.Path[i] - side, stem.Facing, VineAtlas.CellUV(VineCell.Stem, 0f, v), colours[i], stem.Wind[i]);
					int r = mesh.AddVertex(stem.Path[i] + side, stem.Facing, VineAtlas.CellUV(VineCell.Stem, 1f, v), colours[i], stem.Wind[i]);
					if (previousL >= 0)
					{
						mesh.AddQuad(0, previousL, previousR, r, l, stem.Facing);
					}
					previousL = l;
					previousR = r;
				}
			}

			private static void EmitCard(MeshBuilder mesh, in Card card, float lengthScale, float widthScale)
			{
				Vector3 along = card.Along * (card.Length * lengthScale);
				Vector3 across = card.Across * (card.Width * widthScale);
				Vector3 foot = card.Centred ? card.Root - along * 0.5f : card.Root;
				mesh.SetCard(card.Hash, 0f);
				// The roots' cell hangs from its top (v = 1): drawn upside down, so its strands start at the card's root.
				float v0 = card.RootsUpsideDown ? 1f : 0f, v1 = card.RootsUpsideDown ? 0f : 1f;
				int a = mesh.AddVertex(foot - across * 0.5f, card.Light, VineAtlas.CellUV(card.Cell, 0f, v0), card.Colour, card.WindRoot);
				int b = mesh.AddVertex(foot + across * 0.5f, card.Light, VineAtlas.CellUV(card.Cell, 1f, v0), card.Colour, card.WindRoot);
				int c = mesh.AddVertex(foot + across * 0.5f + along, card.Light, VineAtlas.CellUV(card.Cell, 1f, v1), card.Colour, card.WindTip);
				int d = mesh.AddVertex(foot - across * 0.5f + along, card.Light, VineAtlas.CellUV(card.Cell, 0f, v1), card.Colour, card.WindTip);
				mesh.AddQuad(0, a, b, c, d, card.Light);
			}
		}

		// ── Paths ─────────────────────────────────────────────────

		private static float PathLength(List<Vector3> path)
		{
			float total = 0f;
			for (int i = 1; i < path.Count; i++)
			{
				total += Vector3.Distance(path[i], path[i - 1]);
			}
			return total;
		}

		/// <summary>The point <paramref name="t"/> of the way along a path by length, from its point <paramref name="from"/> on.</summary>
		private static Vector3 OnPath(List<Vector3> path, int from, float t)
		{
			from = Mathf.Clamp(from, 0, path.Count - 2);
			float total = 0f;
			for (int i = from + 1; i < path.Count; i++)
			{
				total += Vector3.Distance(path[i], path[i - 1]);
			}
			float want = Mathf.Clamp01(t) * total;
			for (int i = from + 1; i < path.Count; i++)
			{
				float d = Vector3.Distance(path[i], path[i - 1]);
				if (want <= d || i == path.Count - 1)
				{
					return Vector3.Lerp(path[i - 1], path[i], d > 1e-6f ? Mathf.Clamp01(want / d) : 0f);
				}
				want -= d;
			}
			return path[path.Count - 1];
		}
	}
}
#endif
