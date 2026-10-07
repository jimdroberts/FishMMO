#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>The growth form a tree is built with.</summary>
	public enum TreeForm
	{
		/// <summary>One straight stem, whorls of drooping needle sprays narrowing to a spire.</summary>
		Conifer,
		/// <summary>A stem that splits into curving limbs with leaf clusters round a rounded crown.</summary>
		Broadleaf,
		/// <summary>The broadleaf skeleton, leafless, with a few bare twigs.</summary>
		Dead,
		/// <summary>A leaning ringed stem with a crown of arching fronds.</summary>
		Palm,
		/// <summary>A ribbed column with upturned arms.</summary>
		Cactus,
		/// <summary>Several stems spreading from the ground into a flat umbrella crown.</summary>
		Umbrella,
		/// <summary>A clump of tall segmented culms with leaf sprays up their upper half.</summary>
		Bamboo,
		/// <summary>
		/// A pine: a tall bare stem under a rounded, flattening crown of near-level boughs turning up at their
		/// tips — not the spruce's spire (<see cref="Conifer"/>), which every pine was drawn as.
		/// </summary>
		Pine,
	}

	/// <summary>One tree species' recipe.</summary>
	public struct TreeSpecies
	{
		public string Name;
		public TreeForm Form;
		public float Height;
		public float TrunkRadius;
		/// <summary>Crown radius as a fraction of the height.</summary>
		public float CrownWidth;
		/// <summary>Where the crown starts, as a fraction of the height.</summary>
		public float CrownBase;
		public int Branches;
		/// <summary>The bark family (<see cref="Bark"/>) its trunk material uses.</summary>
		public string BarkFamily;
		/// <summary>Leaf colours, picked between per card.</summary>
		public Color LeafA, LeafB;
		public FoliageCell LeafCell;
		/// <summary>Leaf card size, metres.</summary>
		public float LeafSize;
		/// <summary>Turns in autumn and drops its leaves in winter.</summary>
		public bool Deciduous;
	}

	/// <summary>
	/// Trees as two sub-meshes — 0 bark, 1 leaves — at a level of detail; the billboard last LOD is
	/// made separately (<see cref="BillboardImpostor"/>).
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Levels share a skeleton.</b> Every level is grown from the same seeded skeleton with fewer
	/// sides on each tube and fewer, larger leaf cards, so the crown keeps its shape as it steps
	/// down instead of turning into a different tree.
	/// </para>
	/// <para>
	/// <b>Wind data.</b> TEXCOORD1.x is how far a vertex sways with the whole tree:
	/// <c>(height fraction)² × height / 10</c>, so a twelve-metre tree's crown swings further than
	/// a three-metre bush's, and the trunk's foot not at all; y is leaf flutter, 1 on leaves and 0
	/// on wood. Bark and leaves use the same sway, so leaves stay on their branches.
	/// </para>
	/// <para>
	/// <b>Parts, and spares.</b> Every limb, bough, frond and culm is a part (<see cref="MeshBuilder.SetPart"/>):
	/// where it attaches, its own hash, and a keep rank; every leaf card hangs on its part with a rank of its
	/// own. Each form grows half again as many limbs and leaf cards as its species asks for — about seven in
	/// ten of the asked-for number at rank 0, always drawn, and the rest ranked above it — and the vegetation
	/// shader draws only as many as each plant's own fullness keeps, then turns, tilts and stretches each kept
	/// part about its foot (VegVary). So one mesh makes a sparse tree and a full one, holding its limbs its own
	/// way, and no two neighbours match. The levels draw their parts from the same indices, so a tree keeps
	/// the same limbs across the cross-fade.
	/// </para>
	/// </remarks>
	public static class TreeMeshes
	{
		public const int BarkSubmesh = 0;
		public const int LeafSubmesh = 1;
		/// <summary>How many limbs and leaf cards are grown, against how many the species asks for.</summary>
		public const float Spare = 1.5f;
		/// <summary>How many of the asked-for number are always drawn (rank 0).</summary>
		public const float Core = 0.7f;

		/// <summary>The share of the full tree's leaf area a reduced level keeps (<see cref="BuildReduced"/>).</summary>
		public const float LodFoliageShare = 0.8f;

		/// <summary>The most a reduced level's leaf cards are widened to keep that share.</summary>
		public const float MaxLodLeafScale = 4f;

		/// <summary>
		/// Level 0 is the full tree; level 1 has fewer limbs, sides and cards. <paramref name="leafScale"/> widens every
		/// leaf card (its length, and so the crown's reach, unchanged); <see cref="BuildReduced"/> picks it.
		/// </summary>
		public static MeshBuilder Build(in TreeSpecies species, int lod, int seed, float leafScale = 1f)
		{
			var mesh = new MeshBuilder(2);
			TreeSpecies sp = species;
			var builder = new Grower(mesh, in sp, Mathf.Clamp(lod, 0, 1), ProceduralNoise.SeedFor(sp.Name, seed), Mathf.Max(0.01f, leafScale));
			switch (sp.Form)
			{
				case TreeForm.Conifer: builder.Conifer(false); break;
				case TreeForm.Pine: builder.Conifer(true); break;
				case TreeForm.Broadleaf: builder.Broadleaf(true); break;
				case TreeForm.Dead: builder.Broadleaf(false); break;
				case TreeForm.Palm: builder.Palm(); break;
				case TreeForm.Cactus: builder.Cactus(); break;
				case TreeForm.Umbrella: builder.Umbrella(); break;
				case TreeForm.Bamboo: builder.Bamboo(); break;
			}
			mesh.RecalculateNormals(true, onlyMissing: true);
			mesh.RecalculateTangents();
			return mesh;
		}

		/// <summary>
		/// A reduced level whose foliage still covers what the full tree's does: fewer cards, each widened until their
		/// area is <see cref="LodFoliageShare"/> of <paramref name="full"/>'s (at most <see cref="MaxLodLeafScale"/>).
		/// The level used to keep a tenth of a conifer's needle area at the same card size (pine 146 leaf triangles
		/// against 1 396) and about a seventh of a broadleaf's: from fifty metres on a pine stood as a bare trunk with a
		/// few tufts (Jim, 2026-10-07). Area is linear in the width scale, so one correction lands it.
		/// </summary>
		public static MeshBuilder BuildReduced(in TreeSpecies species, int lod, int seed, MeshBuilder full)
		{
			float target = LeafArea(full) * LodFoliageShare;
			float scale = 1f;
			MeshBuilder mesh = Build(in species, lod, seed, scale);
			for (int i = 0; i < 3 && target > 0f; i++)
			{
				float area = LeafArea(mesh);
				if (area <= 0f || Mathf.Abs(area - target) <= target * 0.03f)
				{
					break;
				}
				float next = Mathf.Clamp(scale * target / area, 1f, MaxLodLeafScale);
				if (Mathf.Abs(next - scale) < 1e-3f)
				{
					break;
				}
				scale = next;
				mesh = Build(in species, lod, seed, scale);
			}
			return mesh;
		}

		/// <summary>The summed area of a tree mesh's leaf cards, m² (0 when it has no leaf submesh).</summary>
		public static float LeafArea(MeshBuilder mesh)
		{
			if (mesh == null || mesh.Submeshes.Count <= LeafSubmesh)
			{
				return 0f;
			}
			List<int> indices = mesh.Submeshes[LeafSubmesh];
			float area = 0f;
			for (int i = 0; i + 2 < indices.Count; i += 3)
			{
				Vector3 a = mesh.Positions[indices[i]], b = mesh.Positions[indices[i + 1]], c = mesh.Positions[indices[i + 2]];
				area += 0.5f * Vector3.Cross(b - a, c - a).magnitude;
			}
			return area;
		}

		private sealed class Grower
		{
			private readonly MeshBuilder mesh;
			private readonly TreeSpecies sp;
			private readonly int lod;
			private readonly int seed;
			/// <summary>How much wider every leaf card is grown (a reduced level's, BuildReduced).</summary>
			private readonly float leafScale;
			private DeterministicRNG rng;
			private readonly List<Vector3> path = new List<Vector3>();
			private readonly List<float> radii = new List<float>();
			private readonly List<Color32> colours = new List<Color32>();
			private readonly List<Vector2> wind = new List<Vector2>();
			// The part the next geometry belongs to (BeginPart), and how many cards it has hung so far.
			private Vector3 partPivot;
			private float partHash;
			private float partRank;
			private PlantPart partKind;
			private int partId;
			private int cardSerial;

			public Grower(MeshBuilder mesh, in TreeSpecies species, int lod, int seed, float leafScale)
			{
				this.mesh = mesh;
				sp = species;
				this.lod = lod;
				this.seed = seed;
				this.leafScale = leafScale;
				rng = new DeterministicRNG(seed);
			}

			/// <summary>
			/// A fresh stream for one part of the skeleton (a limb, a twig, a frond). Each part draws
			/// from its own, so a level that hangs fewer cards on one limb does not shift where the
			/// next limb grows: the levels share the skeleton and differ only in how finely it is drawn.
			/// </summary>
			private void Reseed(int part) => rng = new DeterministicRNG(seed ^ (int)ProceduralNoise.Mix((uint)part * 0x9e3779b1u + 1u));

			/// <summary>A hash of this tree's seed and a number, 0..1: drawn without touching the skeleton's streams.</summary>
			private float Hash(int n, int salt) => (ProceduralNoise.Mix((uint)seed ^ ((uint)n * 0x9e3779b1u) ^ ((uint)salt * 0x85ebca6bu)) >> 8) * (1f / 16777216f);

			/// <summary>
			/// The keep rank of the <paramref name="index"/>th of <paramref name="total"/> parts, <paramref name="core"/>
			/// of them always drawn: the core spread evenly through the indices (so a sparse tree is not bare on one
			/// side), the spares ranked by their own hash above 0.
			/// </summary>
			private float Rank(int index, int total, int core, int salt)
			{
				bool always = core >= total || (int)((long)(index + 1) * core / total) > (int)((long)index * core / total);
				return always ? 0f : 0.02f + 0.98f * Hash(index, salt);
			}

			/// <summary>The part everything grown next belongs to, until the next.</summary>
			private void BeginPart(int id, Vector3 pivot, float rank, PlantPart kind)
			{
				partId = id;
				partPivot = pivot;
				partHash = Hash(id, 7);
				partRank = rank;
				partKind = kind;
				cardSerial = 0;
				mesh.SetPart(pivot, partHash, rank, kind);
			}

			/// <summary>Counts of a spared set: how many to grow, and how many of them always to draw.</summary>
			private static (int total, int core) Spared(int asked) =>
				(Mathf.Max(asked, Mathf.RoundToInt(asked * Spare)), Mathf.Max(1, Mathf.RoundToInt(asked * Core)));

			private Vector2 Sway(Vector3 p, float flutter)
			{
				float f = Mathf.Clamp01(p.y / Mathf.Max(0.1f, sp.Height));
				// The crown of a taller tree swings further, but not without end: at real sizes (a 36 m emergent)
				// height over ten gave 3.6 against the 2 the wind was tuned on, and the crowns thrashed. Held to 2.4.
				return new Vector2(f * f * Mathf.Min(sp.Height / 10f, 2.4f), flutter);
			}

			private int Sides(int full) => lod == 0 ? full : Mathf.Max(3, full / 2);

			private Color32 Bark => new Color32(255, 255, 255, 0);

			private Color32 Leaf(float pick) => PlantParts.C32(Color.Lerp(sp.LeafA, sp.LeafB, pick), 1f);

			/// <summary>
			/// A leaf colour shaded by where the card hangs in its crown: darker the deeper toward the middle, and a
			/// little darker toward the crown's underside — the light a crown takes from the sky reaches its outside
			/// and its top. Every card was the same brightness, so a crown read as a stack of flat cut-outs, the
			/// inner ones as bright as the outer; graded, it reads as one mass of foliage. Baked into the vertex
			/// colour, so it costs nothing to draw.
			/// </summary>
			private Color32 ShadedLeaf(float pick, Vector3 at, Vector3 crown)
			{
				float crownRadius = Mathf.Max(0.5f, sp.CrownWidth * sp.Height);
				Vector3 offset = at - crown;
				float depth = Mathf.Clamp01(new Vector2(offset.x, offset.z).magnitude / crownRadius);
				float outside = Mathf.SmoothStep(0f, 1f, depth);
				float top = Mathf.Clamp01(offset.y / Mathf.Max(0.5f, sp.Height * (1f - sp.CrownBase) * 0.5f) * 0.5f + 0.5f);
				float shade = Mathf.Lerp(0.5f, 1f, outside) * Mathf.Lerp(0.8f, 1.05f, top);
				return PlantParts.C32(Color.Lerp(sp.LeafA, sp.LeafB, pick) * shade, 1f);
			}

			/// <summary>A tube along a quadratic curve from a to c bending through b.</summary>
			private void Limb(Vector3 a, Vector3 b, Vector3 c, float r0, float r1, int sides, int segments, bool point)
			{
				mesh.SetPart(partPivot, partHash, partRank, partKind == PlantPart.Fixed ? PlantPart.Fixed : PlantPart.Limb);
				path.Clear(); radii.Clear(); colours.Clear(); wind.Clear();
				for (int s = 0; s <= segments; s++)
				{
					float t = (float)s / segments;
					Vector3 p = (1 - t) * (1 - t) * a + 2 * (1 - t) * t * b + t * t * c;
					path.Add(p);
					radii.Add(point && s == segments ? 0f : Mathf.Lerp(r0, r1, t));
					colours.Add(Bark);
					wind.Add(Sway(p, 0f));
				}
				int firstRing = mesh.VertexCount;
				PlantParts.Tube(mesh, BarkSubmesh, path, radii, Sides(sides), 1f, colours, wind);
				if (partKind == PlantPart.Fixed)
				{
					// The trunk: each tree thickens or thins it about its own centre line (VegVary).
					mesh.MarkTrunkRings(firstRing, path);
				}
			}

			private Vector3 Curve(Vector3 a, Vector3 b, Vector3 c, float t) => (1 - t) * (1 - t) * a + 2 * (1 - t) * t * b + t * t * c;

			// The centre line of the last crooked limb drawn, to hang its forks and twigs on.
			private readonly List<Vector3> lastPath = new List<Vector3>();

			/// <summary>
			/// A limb from a to c that wanders: an arc lifted at its middle by <paramref name="lift"/> of its length,
			/// bent off it by two waves of its own phase and pitch across two directions, <paramref name="wander"/> of
			/// its length at most and nothing at either end, so it leaves its parent and reaches its tip exactly. A
			/// single smooth curve from one control point was every limb the same arc — straight sticks, alike on
			/// every tree. Leaves its centre line in <see cref="lastPath"/>.
			/// </summary>
			private void Crooked(Vector3 a, Vector3 c, float r0, float r1, int sides, int segments, float wander, float lift, bool point)
			{
				mesh.SetPart(partPivot, partHash, partRank, partKind == PlantPart.Fixed ? PlantPart.Fixed : PlantPart.Limb);
				path.Clear(); radii.Clear(); colours.Clear(); wind.Clear(); lastPath.Clear();
				Vector3 axis = c - a;
				float length = Mathf.Max(0.01f, axis.magnitude);
				Vector3 along = axis / length;
				Vector3 side = PlantParts.Perpendicular(along).normalized;
				Vector3 other = Vector3.Cross(along, side).normalized;
				float phaseA = rng.NextFloat() * Mathf.PI * 2f, phaseB = rng.NextFloat() * Mathf.PI * 2f;
				float waves = rng.Range(1.2f, 2.6f);
				for (int s = 0; s <= segments; s++)
				{
					float t = (float)s / segments;
					float envelope = Mathf.Sin(Mathf.PI * t);
					Vector3 p = Vector3.Lerp(a, c, t) + Vector3.up * (lift * length * 4f * t * (1f - t))
						+ (side * Mathf.Sin(waves * Mathf.PI * t + phaseA) + other * Mathf.Sin((waves + 0.7f) * Mathf.PI * t + phaseB)) * (wander * length * envelope);
					path.Add(p);
					lastPath.Add(p);
					radii.Add(point && s == segments ? 0f : Mathf.Lerp(r0, r1, t));
					colours.Add(Bark);
					wind.Add(Sway(p, 0f));
				}
				int firstRing = mesh.VertexCount;
				PlantParts.Tube(mesh, BarkSubmesh, path, radii, Sides(sides), 1f, colours, wind);
				if (partKind == PlantPart.Fixed)
				{
					// The trunk: each tree thickens or thins it about its own centre line (VegVary).
					mesh.MarkTrunkRings(firstRing, path);
				}
			}

			/// <summary>A point a share of the way along a centre line.</summary>
			private static Vector3 OnPath(List<Vector3> points, float t)
			{
				float f = Mathf.Clamp01(t) * (points.Count - 1);
				int i = Mathf.Min((int)f, points.Count - 2);
				return Vector3.Lerp(points[i], points[i + 1], f - i);
			}

			private void Spray(Vector3 root, Vector3 direction, float length, float width, Vector3 crown, FoliageCell cell, float bend = 0.6f)
			{
				// A card on its part: a frond or a bough is kept or dropped whole by the branch fullness; a leaf
				// card on a limb by the leaf fullness too, by its own rank.
				bool leaf = partKind != PlantPart.Frond;
				mesh.SetPart(partPivot, partHash, partRank, leaf ? PlantPart.Leaf : PlantPart.Frond);
				int card = cardSerial++;
				width *= leafScale;
				mesh.SetCard(Hash(partId * 4099 + card, 11), leaf ? 0.02f + 0.98f * Hash(partId * 4099 + card, 13) : 0f);
				float pick = rng.NextFloat();
				Color32 shaded = ShadedLeaf(pick, root + direction * length * 0.5f, crown);
				PlantParts.SprayCard(mesh, LeafSubmesh, root, direction, length, width, rng.Range(-30f, 30f), cell, shaded,
					Sway(root, 0.3f), Sway(root + direction * length, 1f), crown, bend);
				// A second card across the first gives the spray depth from every side. On every level: alone, a card
				// seen edge-on is a line, and a reduced crown of single cards came and went as it turned.
				PlantParts.SprayCard(mesh, LeafSubmesh, root, direction, length, width, rng.Range(60f, 120f), cell, shaded,
					Sway(root, 0.3f), Sway(root + direction * length, 1f), crown, bend);
			}

			/// <param name="rounded">A pine's crown (<see cref="TreeForm.Pine"/>): widest a third of the way down and
			/// rounding over a flattish top, its boughs near level and turning up; otherwise a spruce's cone of drooping boughs.</param>
			public void Conifer(bool rounded)
			{
				float h = sp.Height;
				BeginPart(0, Vector3.zero, 0f, PlantPart.Fixed);
				Limb(Vector3.zero, new Vector3(0f, h * 0.5f, 0f), new Vector3(0f, h, 0f), sp.TrunkRadius, sp.TrunkRadius * 0.08f, 8, lod == 0 ? 8 : 4, true);
				// Every level has every whorl: halved, the reduced crown showed bare trunk between its tiers.
				int whorls = Mathf.Max(6, sp.Branches);
				float baseY = h * sp.CrownBase;
				var crown = new Vector3(0f, (baseY + h) * 0.5f, 0f);
				for (int w = 0; w < whorls; w++)
				{
					float t = (w + 0.5f) / whorls;
					Reseed(1000 + Mathf.RoundToInt(t * 997f));
					float y = Mathf.Lerp(baseY, h * 0.97f, t);
					// A spruce narrows straight to its spire; a pine's crown swells and rounds over.
					float profile = rounded ? Mathf.Pow(Mathf.Sin(Mathf.PI * Mathf.Lerp(0.2f, 0.92f, t)), 0.6f) : 1f - t;
					float reach = sp.CrownWidth * h * profile * rng.Range(0.85f, 1.1f) + 0.3f;
					(int arms, int coreArms) = Spared(lod == 0 ? 8 : 5);
					float twist = rng.NextFloat() * 360f;
					for (int a = 0; a < arms; a++)
					{
						float yaw = Mathf.Deg2Rad * (twist + a * 360f / arms + rng.Range(-15f, 15f));
						var outward = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw));
						// A spruce's lower boughs droop and its top ones reach up; a pine's stand near level and lift.
						var dir = rounded
							? (outward + Vector3.up * Mathf.Lerp(0.05f, 0.5f, t)).normalized
							: (outward + Vector3.down * Mathf.Lerp(0.45f, -0.35f, t)).normalized;
						var root = new Vector3(0f, y, 0f) + outward * sp.TrunkRadius * (1f - t);
						// Each bough a part of its own, kept or dropped whole, turned about where it leaves the trunk.
						BeginPart(1000 + w * 64 + a, root, Rank(a, arms, coreArms, 1000 + w), PlantPart.Frond);
						// Wide boughs: at real sizes a spray half as wide as it was long read as a bare stick.
						Spray(root, dir, reach, Mathf.Max(0.6f, reach * 0.8f), crown, sp.LeafCell, 0.5f);
						if (lod == 0 && t < 0.6f && reach > 1.2f)
						{
							// The longer lower boughs carry a second, shorter spray turned off the first, so a crown
							// has depth between its whorls rather than a stack of flat plates.
							Vector3 midBough = root + dir * reach * rng.Range(0.35f, 0.55f);
							Vector3 offDir = (Quaternion.AngleAxis(rng.Range(-40f, 40f), Vector3.up) * dir + Vector3.down * 0.15f).normalized;
							Spray(midBough, offDir, reach * 0.55f, Mathf.Max(0.5f, reach * 0.5f), crown, sp.LeafCell, 0.5f);
						}
					}
				}
				// A leader at the top.
				BeginPart(999, new Vector3(0f, h * 0.9f, 0f), 0f, PlantPart.Frond);
				Spray(new Vector3(0f, h * (rounded ? 0.94f : 0.9f), 0f), Vector3.up, h * (rounded ? 0.05f : 0.12f), h * (rounded ? 0.05f : 0.06f), crown, sp.LeafCell, 0.3f);
			}

			public void Broadleaf(bool leaves)
			{
				float h = sp.Height;
				float split = h * sp.CrownBase;
				Reseed(0);
				Vector3 lean = new Vector3(rng.Range(-0.05f, 0.05f), 0f, rng.Range(-0.05f, 0.05f)) * h;
				Vector3 top = new Vector3(0f, split, 0f) + lean;
				BeginPart(0, Vector3.zero, 0f, PlantPart.Fixed);
				Limb(Vector3.zero, new Vector3(0f, split * 0.5f, 0f), top, sp.TrunkRadius, sp.TrunkRadius * 0.7f, 10, lod == 0 ? 5 : 3, false);
				var crown = new Vector3(lean.x, (split + h) * 0.55f, lean.z);
				float crownRadius = sp.CrownWidth * h;
				(int limbs, int coreLimbs) = Spared(Mathf.Max(3, sp.Branches));
				for (int l = 0; l < limbs; l++)
				{
					Reseed(100 + l);
					float yaw = Mathf.Deg2Rad * (l * 360f / limbs + rng.Range(-20f, 20f));
					var outward = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw));
					float rise = rng.Range(0.6f, 1.1f);
					// Limbs leave the trunk over its upper third, not all from one point.
					Vector3 start = top + Vector3.down * rng.Range(0f, split * 0.35f);
					Vector3 end = crown + outward * crownRadius * rng.Range(0.55f, 0.9f) + Vector3.up * (h - crown.y) * rise * 0.6f;
					if (!leaves)
					{
						// Dead limbs reach and twist instead of filling a crown.
						end += new Vector3(rng.Range(-1f, 1f), rng.Range(-0.5f, 0.8f), rng.Range(-1f, 1f)) * crownRadius * 0.25f;
					}
					// The limb, its forks, its twigs and their leaves: one part, turned about where it leaves the trunk.
					BeginPart(100 + l, start, Rank(l, limbs, coreLimbs, 100), PlantPart.Limb);
					float r0 = sp.TrunkRadius * 0.55f, r1 = sp.TrunkRadius * 0.12f;
					Crooked(start, end, r0, r1, 6, lod == 0 ? 7 : 4, leaves ? 0.08f : 0.13f, 0.1f, true);
					var limbPath = new List<Vector3>(lastPath);
					float limbLength = Vector3.Distance(start, end);

					// Forks: two or three crooked sub-limbs off the limb's middle, each turned its own way and ending
					// in leaves — where a crown gets its depth and its ragged outline.
					int forks = lod == 0 ? 2 + (rng.NextFloat() < 0.5f ? 1 : 0) : 1;
					for (int f = 0; f < forks; f++)
					{
						Reseed(30000 + l * 16 + f);
						float t = rng.Range(0.3f, 0.75f);
						Vector3 at = OnPath(limbPath, t);
						Vector3 heading = (OnPath(limbPath, Mathf.Min(1f, t + 0.05f)) - at).normalized;
						Vector3 bend = Quaternion.AngleAxis(rng.Range(-180f, 180f), heading) * Quaternion.AngleAxis(rng.Range(25f, 55f), PlantParts.Perpendicular(heading).normalized) * heading;
						Vector3 forkEnd = at + (bend + Vector3.up * 0.25f).normalized * limbLength * rng.Range(0.35f, 0.6f);
						float forkRadius = Mathf.Lerp(r0, r1, t) * 0.6f;
						Crooked(at, forkEnd, forkRadius, forkRadius * 0.25f, 4, lod == 0 ? 4 : 2, leaves ? 0.1f : 0.15f, 0.08f, true);
						if (leaves)
						{
							Cluster(forkEnd, crown, Spared(lod == 0 ? 7 : 3).total);
						}
						else if (lod == 0)
						{
							Spray(forkEnd, (forkEnd - at).normalized, sp.LeafSize, sp.LeafSize * 0.8f, crown, FoliageCell.Twigs, 0.2f);
						}
					}

					int twigs = lod == 0 ? 3 : 2;
					for (int k = 0; k < twigs; k++)
					{
						Reseed(10000 + l * 16 + k);
						float t = rng.Range(0.45f, 0.9f);
						Vector3 at = OnPath(limbPath, t);
						Vector3 side = Quaternion.AngleAxis(rng.Range(-70f, 70f), Vector3.up) * outward;
						Vector3 twigEnd = at + (side + Vector3.up * rng.Range(0.1f, 0.7f)).normalized * crownRadius * rng.Range(0.3f, 0.5f);
						if (lod == 0 || !leaves)
						{
							Crooked(at, twigEnd, sp.TrunkRadius * 0.15f, sp.TrunkRadius * 0.05f, 4, 3, 0.12f, 0.06f, true);
						}
						if (leaves)
						{
							Cluster(twigEnd, crown, Spared(lod == 0 ? 5 : 3).total);
						}
						else if (lod == 0)
						{
							Spray(twigEnd, (twigEnd - at).normalized, sp.LeafSize, sp.LeafSize * 0.8f, crown, FoliageCell.Twigs, 0.2f);
						}
					}
					if (leaves)
					{
						Reseed(20000 + l);
						Cluster(end, crown, Spared(lod == 0 ? 9 : 4).total);
					}
				}
			}

			private void Cluster(Vector3 at, Vector3 crown, int cards)
			{
				for (int c = 0; c < cards; c++)
				{
					Vector3 outward = (at - crown).normalized;
					Vector3 dir = (outward + Random3() * 0.9f + Vector3.up * 0.3f).normalized;
					float size = sp.LeafSize * rng.Range(0.8f, 1.2f) * (lod == 0 ? 1f : 1.4f);
					Vector3 root = at - dir * size * 0.35f + Random3() * size * 0.2f;
					Spray(root, dir, size, size, crown, sp.LeafCell, 0.75f);
				}
			}

			private Vector3 Random3() => new Vector3(rng.Range(-1f, 1f), rng.Range(-1f, 1f), rng.Range(-1f, 1f));

			public void Palm()
			{
				float h = sp.Height;
				Reseed(0);
				var leanDir = new Vector3(rng.Range(-1f, 1f), 0f, rng.Range(-1f, 1f)).normalized;
				if (leanDir.sqrMagnitude < 1e-6f)
				{
					leanDir = Vector3.forward;
				}
				Vector3 top = new Vector3(0f, h, 0f) + leanDir * h * 0.15f;
				Vector3 mid = new Vector3(0f, h * 0.55f, 0f) + leanDir * h * 0.02f;
				BeginPart(0, Vector3.zero, 0f, PlantPart.Fixed);
				Limb(Vector3.zero, mid, top, sp.TrunkRadius, sp.TrunkRadius * 0.75f, 8, lod == 0 ? 10 : 5, false);
				(int fronds, int coreFronds) = Spared(lod == 0 ? Mathf.Max(8, sp.Branches) : Mathf.Max(6, sp.Branches - 3));
				var spine = new List<Vector3>();
				var widths = new List<float>();
				var frondColours = new List<Color32>();
				var frondWind = new List<Vector2>();
				int segments = lod == 0 ? 5 : 3;
				for (int f = 0; f < fronds; f++)
				{
					Reseed(100 + f);
					BeginPart(100 + f, top, Rank(f, fronds, coreFronds, 100), PlantPart.Frond);
					mesh.SetCard(Hash(100 + f, 11), 0f);
					float yaw = Mathf.Deg2Rad * (f * 360f / fronds + rng.Range(-12f, 12f));
					var outward = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw));
					float length = sp.CrownWidth * h * rng.Range(0.85f, 1.15f);
					float rise = rng.Range(0.3f, 0.8f);
					Color32 colour = Leaf(rng.NextFloat());
					spine.Clear(); widths.Clear(); frondColours.Clear(); frondWind.Clear();
					for (int s = 0; s <= segments; s++)
					{
						float t = (float)s / segments;
						Vector3 p = top + outward * length * t + Vector3.up * length * (rise * t - 0.9f * t * t);
						spine.Add(p);
						widths.Add(sp.LeafSize * (s == segments ? 0f : 1f));
						frondColours.Add(colour);
						frondWind.Add(Sway(p, t));
					}
					PlantParts.Strip(mesh, LeafSubmesh, spine, widths, Vector3.Cross(Vector3.up, outward), FoliageCell.PalmFrond, frondColours, frondWind,
						(Vector3.up + outward * 0.4f).normalized);
				}
			}

			public void Cactus()
			{
				float h = sp.Height;
				int sides = lod == 0 ? 12 : 8;
				BeginPart(0, Vector3.zero, 0f, PlantPart.Fixed);
				ColumnCactus(Vector3.zero, Vector3.up * h * 0.5f, Vector3.up * h, sp.TrunkRadius, sides, lod == 0 ? 8 : 4);
				int asked = Mathf.Max(0, sp.Branches);
				int arms = asked > 0 ? asked + 1 : 0;
				int coreArms = Mathf.Max(0, asked - 1);
				for (int a = 0; a < arms; a++)
				{
					Reseed(100 + a);
					float yaw = Mathf.Deg2Rad * (a * 360f / Mathf.Max(1, arms) + rng.Range(-30f, 30f));
					var outward = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw));
					float y = h * rng.Range(0.3f, 0.55f);
					Vector3 start = new Vector3(0f, y, 0f);
					float reach = sp.TrunkRadius * rng.Range(2.2f, 3f);
					Vector3 elbow = start + outward * reach;
					Vector3 end = elbow + Vector3.up * h * rng.Range(0.25f, 0.4f);
					BeginPart(100 + a, start, Rank(a, arms, coreArms, 100), PlantPart.Limb);
					ColumnCactus(start, elbow + outward * 0.1f, end, sp.TrunkRadius * 0.65f, sides, lod == 0 ? 6 : 3);
				}
			}

			private void ColumnCactus(Vector3 a, Vector3 b, Vector3 c, float radius, int sides, int segments)
			{
				mesh.SetPart(partPivot, partHash, partRank, partKind == PlantPart.Fixed ? PlantPart.Fixed : PlantPart.Limb);
				path.Clear(); radii.Clear(); colours.Clear(); wind.Clear();
				for (int s = 0; s <= segments; s++)
				{
					float t = (float)s / segments;
					Vector3 p = Curve(a, b, c, t);
					path.Add(p);
					// A rounded crown: the last fifth closes over.
					float close = t > 0.8f ? Mathf.Sqrt(Mathf.Clamp01(1f - (t - 0.8f) / 0.2f)) : 1f;
					radii.Add(s == segments ? 0f : radius * Mathf.Max(0.25f, close));
					colours.Add(Bark);
					wind.Add(Vector2.zero);
				}
				PlantParts.Tube(mesh, BarkSubmesh, path, radii, sides, 0.5f, colours, wind, ribs: sides, ribDepth: 0.1f, analyticNormals: false);
			}

			public void Umbrella()
			{
				float h = sp.Height;
				int stems = Mathf.Max(2, sp.Branches);
				float crownRadius = sp.CrownWidth * h;
				var crown = new Vector3(0f, h * 0.85f, 0f);
				for (int s = 0; s < stems; s++)
				{
					Reseed(100 + s);
					float yaw = Mathf.Deg2Rad * (s * 360f / stems + rng.Range(-25f, 25f));
					var outward = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw));
					Vector3 end = new Vector3(0f, h * rng.Range(0.8f, 0.9f), 0f) + outward * crownRadius * rng.Range(0.4f, 0.6f);
					Vector3 mid = new Vector3(0f, h * 0.45f, 0f) + outward * crownRadius * 0.15f;
					// The stems hold the crown up: always drawn, and only nudged at their feet.
					BeginPart(100 + s, outward * sp.TrunkRadius * 0.3f, 0f, PlantPart.Limb);
					Limb(outward * sp.TrunkRadius * 0.3f, mid, end, sp.TrunkRadius * 0.6f, sp.TrunkRadius * 0.2f, 6, lod == 0 ? 4 : 2, true);
				}
				// The crown: a flat layer of leaf cards lying almost level, each kept by the leaf fullness.
				int cards = Spared(lod == 0 ? 26 : 10).total;
				Reseed(5000);
				BeginPart(5000, crown, 0f, PlantPart.Fixed);
				for (int c = 0; c < cards; c++)
				{
					mesh.SetPart(partPivot, partHash, 0f, PlantPart.Leaf);
					mesh.SetCard(Hash(5000 * 4099 + c, 11), 0.02f + 0.98f * Hash(5000 * 4099 + c, 13));
					float a = rng.NextFloat() * Mathf.PI * 2f;
					float r = Mathf.Sqrt(rng.NextFloat()) * crownRadius;
					var at = new Vector3(Mathf.Cos(a) * r, h * rng.Range(0.82f, 0.95f) - r * r / (crownRadius * crownRadius) * h * 0.08f, Mathf.Sin(a) * r);
					var dir = (new Vector3(Mathf.Cos(a), 0.15f, Mathf.Sin(a)) + Random3() * 0.3f).normalized;
					float size = sp.LeafSize * (lod == 0 ? 1f : 1.6f);
					PlantParts.SprayCard(mesh, LeafSubmesh, at - dir * size * 0.5f, dir, size, size * leafScale, rng.Range(70f, 110f), sp.LeafCell, ShadedLeaf(rng.NextFloat(), at, crown),
						Sway(at, 0.4f), Sway(at, 1f), crown + Vector3.down * h * 0.3f, 0.8f);
				}
			}

			public void Bamboo()
			{
				float h = sp.Height;
				(int culms, int coreCulms) = Spared(lod == 0 ? Mathf.Max(5, sp.Branches) : Mathf.Max(3, sp.Branches / 2));
				var crown = new Vector3(0f, h * 0.7f, 0f);
				for (int c = 0; c < culms; c++)
				{
					Reseed(100 + c);
					float a = rng.NextFloat() * Mathf.PI * 2f;
					var foot = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * rng.Range(0f, sp.CrownWidth * h * 0.25f);
					// A culm and its sprays: one part, from its foot.
					BeginPart(100 + c, foot, Rank(c, culms, coreCulms, 100), PlantPart.Limb);
					float height = h * rng.Range(0.75f, 1.05f);
					var lean = new Vector3(foot.x, 0f, foot.z).normalized * height * rng.Range(0.05f, 0.15f);
					Vector3 top = foot + Vector3.up * height + lean;
					Limb(foot, foot + Vector3.up * height * 0.5f + lean * 0.2f, top, sp.TrunkRadius, sp.TrunkRadius * 0.7f, 6, lod == 0 ? 6 : 3, false);
					int sprays = Spared(lod == 0 ? 7 : 3).total;
					for (int s = 0; s < sprays; s++)
					{
						float t = Mathf.Lerp(0.45f, 1f, (s + 0.5f) / sprays);
						Vector3 at = Curve(foot, foot + Vector3.up * height * 0.5f + lean * 0.2f, top, t);
						float yaw = rng.NextFloat() * Mathf.PI * 2f;
						var dir = (new Vector3(Mathf.Cos(yaw), rng.Range(0.1f, 0.6f), Mathf.Sin(yaw))).normalized;
						Spray(at, dir, sp.LeafSize, sp.LeafSize, crown, sp.LeafCell, 0.6f);
					}
				}
			}
		}
	}
}
#endif
