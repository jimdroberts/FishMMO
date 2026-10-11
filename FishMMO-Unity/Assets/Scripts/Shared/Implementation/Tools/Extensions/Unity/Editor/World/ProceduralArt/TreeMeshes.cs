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
		/// <summary>
		/// A narrow column held from near the ground (Italian cypress, Lombardy poplar): a fastigiate tree, whose
		/// branches grow up nearly alongside the stem instead of out from it, so its sprays leave the trunk a few
		/// degrees off vertical in close whorls, the column widest a third of the way up and closing to a blunt tip.
		/// </summary>
		Columnar,
		/// <summary>
		/// A stout stem that forks and forks again into crooked limbs, each ending in a spiky rosette of strap leaves
		/// over a skirt of older, browning ones (Joshua tree, dragon tree, screw pine).
		/// </summary>
		Rosette,
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

		// ── Added by the vegetation expansion (2026-10-10) ──
		// Every field below is zero on every species made before them, and zero means "as the form always grew",
		// so no tree made before them changes: each is read only when a species sets it.

		/// <summary>
		/// How far a palm's stem leans, as a share of the stem's height; 0 = the form's own (a coconut's 0.15). A date
		/// palm stands near straight (0.02): the coconut's lean is the sea wind and the light over a beach.
		/// </summary>
		public float Lean;
		/// <summary>
		/// How a palm holds its fronds: the middle of the range each frond rises by before it droops (±0.3 round it),
		/// in the frond's own lengths; 0 = the coconut's 0.3–0.8. A stiff date palm frond near level (0.95), a tree
		/// fern's shuttlecock (0.7), a banana's steep paddles (1.5), a nipa's erect fronds from the mud (2.8).
		/// </summary>
		/// <remarks>
		/// A palm that sets it is measured to its frond tips, as every other species is to its crown's top: its stem
		/// stops at <see cref="CrownBase"/> and the fronds rise above it. The coconut, made before, stands its stem to
		/// the full height and keeps doing so.
		/// </remarks>
		public float FrondLift;
		/// <summary>Gnarling, 0..1: a twisted, partly hollow trunk and a share of bare limbs (olive, bristlecone).</summary>
		public float Gnarl;
		/// <summary>A bottle trunk: the most the stem swells over its plain taper (baobab 0.6 = ×1.6 at a third of the height).</summary>
		public float TrunkSwell;
		/// <summary>Plank buttresses round the foot (kapok 5–7, bald cypress flutes); 0 = none.</summary>
		public int ButtressCount;
		/// <summary>How far up the trunk the buttresses reach, as a share of the height.</summary>
		public float ButtressHeight;
		/// <summary>How far out from the trunk the buttresses reach at the ground, metres.</summary>
		public float ButtressReach;
		/// <summary>Arching prop roots from the stem to a ring on the ground (mangrove 8–16); 0 = none.</summary>
		public int PropRootCount;
		/// <summary>Where the highest prop root leaves the stem, as a share of the height.</summary>
		public float PropRootTop;
		/// <summary>The radius of the ring the prop roots land on, metres (<see cref="ProceduralArtCatalogue.CrownRadius"/> is at least this).</summary>
		public float PropRootRing;
		/// <summary>Weeping: hanging leaf curtains from the limb ends, as a share of the height they fall (0.3–0.5).</summary>
		public float Weeping;
		/// <summary>Hanging strands (Spanish moss, Usnea) from the limbs, as a share of the limbs carrying them; 0 = none.</summary>
		public float Hanging;
		/// <summary>The hanging strands' colour.</summary>
		public Color HangingColour;
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
				case TreeForm.Columnar: builder.Columnar(); break;
				case TreeForm.Rosette: builder.Rosette(); break;
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

			/// <summary>A colour shaded as <see cref="ShadedLeaf"/> shades the leaf colour, by its place in the crown.</summary>
			private Color32 ShadedTone(Color tone, Vector3 at, Vector3 crown, float alpha = 1f)
			{
				float crownRadius = Mathf.Max(0.5f, sp.CrownWidth * sp.Height);
				Vector3 offset = at - crown;
				float outside = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(new Vector2(offset.x, offset.z).magnitude / crownRadius));
				float top = Mathf.Clamp01(offset.y / Mathf.Max(0.5f, sp.Height * (1f - sp.CrownBase) * 0.5f) * 0.5f + 0.5f);
				return PlantParts.C32(tone * (Mathf.Lerp(0.5f, 1f, outside) * Mathf.Lerp(0.8f, 1.05f, top)), alpha);
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

			/// <param name="tone">A colour the spray takes in place of the species' leaf colour (a rosette's browning skirt), shaded by
			/// its place in the crown the same way; null for the leaf colour.</param>
			private void Spray(Vector3 root, Vector3 direction, float length, float width, Vector3 crown, FoliageCell cell, float bend = 0.6f, Color? tone = null)
			{
				// A card on its part: a frond or a bough is kept or dropped whole by the branch fullness; a leaf
				// card on a limb by the leaf fullness too, by its own rank.
				bool leaf = partKind != PlantPart.Frond;
				mesh.SetPart(partPivot, partHash, partRank, leaf ? PlantPart.Leaf : PlantPart.Frond);
				int card = cardSerial++;
				width *= leafScale;
				mesh.SetCard(Hash(partId * 4099 + card, 11), leaf ? 0.02f + 0.98f * Hash(partId * 4099 + card, 13) : 0f);
				float pick = rng.NextFloat();
				Color32 shaded = tone.HasValue ? ShadedTone(tone.Value, root + direction * length * 0.5f, crown) : ShadedLeaf(pick, root + direction * length * 0.5f, crown);
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
				if (sp.Gnarl > 0f)
				{
					// A bristlecone's strip-bark: living bands twisting round a stem mostly dead, from the ground to
					// half its height (the straight trunk under them is the dead wood they wrap).
					GnarledStrands(Vector3.zero, new Vector3(0f, h * 0.5f, 0f), sp.TrunkRadius * 0.55f, sp.TrunkRadius * 0.5f);
				}
				Buttresses(y => Vector3.up * y);
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
						if (BareShare > 0f && Hash(1000 + w * 64 + a, 23) < BareShare)
						{
							// A dead bough: a bare crooked stick with a few twigs (half a bristlecone's crown is these).
							BareBough(root, dir * reach * 0.85f, crown);
							continue;
						}
						// Wide boughs: at real sizes a spray half as wide as it was long read as a bare stick.
						Spray(root, dir, reach, Mathf.Max(0.6f, reach * 0.8f), crown, sp.LeafCell, 0.5f);
						if (sp.Hanging > 0f && Hash(1000 + w * 64 + a, 41) < sp.Hanging)
						{
							// Spanish moss from the bough's outer half, where it hangs clear of the crown below.
							Strands(root + dir * reach * rng.Range(0.5f, 0.95f), crown);
						}
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
				if (sp.TrunkSwell > 0f)
				{
					SwollenTrunk(top);
				}
				else if (sp.Gnarl > 0f)
				{
					// An old olive's trunk: twisted lobes parting and rejoining round a hollow, no single stem.
					GnarledStrands(Vector3.zero, top, sp.TrunkRadius, sp.TrunkRadius * 0.7f);
				}
				else
				{
					Limb(Vector3.zero, new Vector3(0f, split * 0.5f, 0f), top, sp.TrunkRadius, sp.TrunkRadius * 0.7f, 10, lod == 0 ? 5 : 3, false);
				}
				// The trunk's centre at a height: the curve above runs t = y / split, its lean growing as t².
				Vector3 TrunkAt(float y)
				{
					float t = Mathf.Clamp01(y / Mathf.Max(0.01f, split));
					return new Vector3(lean.x * t * t, y, lean.z * t * t);
				}
				Buttresses(TrunkAt);
				PropRoots(TrunkAt);
				var crown = new Vector3(lean.x, (split + h) * 0.55f, lean.z);
				float crownRadius = sp.CrownWidth * h;
				(int limbs, int coreLimbs) = Spared(Mathf.Max(3, sp.Branches));
				for (int l = 0; l < limbs; l++)
				{
					Reseed(100 + l);
					float yaw = Mathf.Deg2Rad * (l * 360f / limbs + rng.Range(-20f, 20f));
					var outward = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw));
					float rise = rng.Range(0.6f, 1.1f);
					// Limbs leave the trunk over its upper third, not all from one point — a bottle trunk's from its
					// shoulders only, or they would break out of its swollen sides halfway up, and a buttressed
					// emergent's too: a kapok's bole stands bare to its crown.
					bool shoulders = sp.TrunkSwell > 0f || sp.ButtressCount > 0;
					Vector3 start = top + Vector3.down * rng.Range(0f, split * (shoulders ? 0.08f : 0.35f));
					Vector3 end = crown + outward * crownRadius * rng.Range(0.55f, 0.9f) + Vector3.up * (h - crown.y) * rise * 0.6f;
					// A gnarled tree's dead limbs (half a bristlecone's), drawn as a dead tree's.
					bool leafy = leaves && !(BareShare > 0f && Hash(100 + l, 23) < BareShare);
					if (!leafy)
					{
						// Dead limbs reach and twist instead of filling a crown.
						end += new Vector3(rng.Range(-1f, 1f), rng.Range(-0.5f, 0.8f), rng.Range(-1f, 1f)) * crownRadius * 0.25f;
					}
					// The limb, its forks, its twigs and their leaves: one part, turned about where it leaves the trunk.
					BeginPart(100 + l, start, Rank(l, limbs, coreLimbs, 100), PlantPart.Limb);
					float r0 = sp.TrunkRadius * 0.55f, r1 = sp.TrunkRadius * 0.12f;
					float limbWander = leafy ? 0.08f : 0.13f;
					if (sp.Gnarl > 0f)
					{
						limbWander += sp.Gnarl * 0.08f; // contorted, as an old olive's limbs are
					}
					Crooked(start, end, r0, r1, 6, lod == 0 ? 7 : 4, limbWander, 0.1f, true);
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
						Crooked(at, forkEnd, forkRadius, forkRadius * 0.25f, 4, lod == 0 ? 4 : 2, leafy ? 0.1f : 0.15f, 0.08f, true);
						if (leafy)
						{
							Cluster(forkEnd, crown, Spared(lod == 0 ? 7 : 3).total);
							if (sp.Weeping > 0f)
							{
								Curtains(forkEnd, outward, crown);
							}
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
						if (lod == 0 || !leafy)
						{
							Crooked(at, twigEnd, sp.TrunkRadius * 0.15f, sp.TrunkRadius * 0.05f, 4, 3, 0.12f, 0.06f, true);
						}
						if (leafy)
						{
							Cluster(twigEnd, crown, Spared(lod == 0 ? 5 : 3).total);
						}
						else if (lod == 0)
						{
							Spray(twigEnd, (twigEnd - at).normalized, sp.LeafSize, sp.LeafSize * 0.8f, crown, FoliageCell.Twigs, 0.2f);
						}
					}
					if (leafy)
					{
						Reseed(20000 + l);
						Cluster(end, crown, Spared(lod == 0 ? 9 : 4).total);
						if (sp.Weeping > 0f)
						{
							Curtains(end, outward, crown);
						}
					}
					if (leafy && sp.Hanging > 0f && Hash(100 + l, 41) < sp.Hanging)
					{
						Reseed(40000 + l);
						Strands(OnPath(limbPath, rng.Range(0.35f, 0.85f)), crown);
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
				// A palm that sets how its fronds rise stands them above its stem (TreeSpecies.FrondLift); the coconut's
				// stem is its height.
				float stem = sp.FrondLift > 0f ? h * Mathf.Clamp(sp.CrownBase, 0.02f, 1f) : h;
				float lean = sp.Lean > 0f ? sp.Lean : 0.15f;
				Vector3 top = new Vector3(0f, stem, 0f) + leanDir * stem * lean;
				Vector3 mid = new Vector3(0f, stem * 0.55f, 0f) + leanDir * stem * (sp.Lean > 0f ? sp.Lean * (0.02f / 0.15f) : 0.02f);
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
					float rise = sp.FrondLift > 0f ? rng.Range(sp.FrondLift - 0.3f, sp.FrondLift + 0.3f) : rng.Range(0.3f, 0.8f);
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
					// The species' own leaf: a coconut's or a date palm's pinnate frond, a tree fern's, a banana's paddle.
					PlantParts.Strip(mesh, LeafSubmesh, spine, widths, Vector3.Cross(Vector3.up, outward), sp.LeafCell, frondColours, frondWind,
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

			// ── Forms and features added by the vegetation expansion (2026-10-10) ──────────────────
			// Each runs only for a species that asks for it (a field set, or one of the two new forms), so every tree
			// made before is drawn exactly as it was: none of these draws from a stream an older species reads.

			/// <summary>
			/// The share of a gnarled tree's limbs or boughs that are dead (<see cref="TreeSpecies.Gnarl"/> above a half):
			/// an old olive keeps nearly all its limbs in leaf (0.1 at 0.6), a five-thousand-year bristlecone carries half
			/// its crown dead (0.45 at 0.95).
			/// </summary>
			private float BareShare => Mathf.Max(0f, sp.Gnarl - 0.5f);

			/// <summary>
			/// A gnarled trunk: two to four lobes twisting up round the axis from <paramref name="foot"/> to
			/// <paramref name="top"/>, each its own tube, standing apart where they wander out and merging where they
			/// close, so the trunk reads as fluted, split and partly hollow — an old olive's, or a bristlecone's living
			/// strip-bark round its dead wood — rather than one smooth stem. Fixed parts, so the collider has them.
			/// </summary>
			/// <param name="r0">The bundle's radius at the foot (the lobes are sized to fill about it).</param>
			/// <param name="r1">At the top.</param>
			private void GnarledStrands(Vector3 foot, Vector3 top, float r0, float r1)
			{
				int strands = 2 + Mathf.RoundToInt(Mathf.Clamp01(sp.Gnarl) * 2f);
				float lobe = 1.05f / Mathf.Sqrt(strands);
				float twist = Mathf.Clamp01(sp.Gnarl) * Mathf.PI * 0.9f;
				int segments = lod == 0 ? 8 : 5;
				for (int k = 0; k < strands; k++)
				{
					Reseed(9000 + k);
					BeginPart(0, foot, 0f, PlantPart.Fixed);
					float start = (k + rng.Range(-0.2f, 0.2f)) * Mathf.PI * 2f / strands;
					float phase = rng.NextFloat() * Mathf.PI * 2f;
					float waves = rng.Range(1.5f, 3f);
					path.Clear(); radii.Clear(); colours.Clear(); wind.Clear();
					for (int s = 0; s <= segments; s++)
					{
						float t = (float)s / segments;
						Vector3 axis = Vector3.Lerp(foot, top, t);
						float bundle = Mathf.Lerp(r0, r1, t);
						float angle = start + twist * t;
						// Out from the axis by about half the bundle, swelling and closing along the trunk; flared at the foot.
						float flare = t < 0.15f ? (1f - t / 0.15f) * (1f - t / 0.15f) * 0.45f : 0f;
						float offset = bundle * (0.5f * (1f - 0.45f * t) * (1f + 0.35f * Mathf.Sin(waves * Mathf.PI * t + phase)) + flare);
						Vector3 p = axis + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * offset;
						path.Add(p);
						radii.Add(bundle * lobe * Mathf.Lerp(1f, 0.8f, t));
						colours.Add(Bark);
						wind.Add(Sway(p, 0f));
					}
					int firstRing = mesh.VertexCount;
					PlantParts.Tube(mesh, BarkSubmesh, path, radii, Sides(7), 1f, colours, wind);
					mesh.MarkTrunkRings(firstRing, path);
				}
			}

			/// <summary>
			/// A bottle trunk (baobab): the plain taper of a broadleaf's trunk swollen by up to <see cref="TrunkSwell"/> of
			/// itself about a third of the tree's height, a little flared at the foot and drawn in to its shoulders, where
			/// the limbs leave it.
			/// </summary>
			private void SwollenTrunk(Vector3 top)
			{
				float h = sp.Height;
				float split = Mathf.Max(0.01f, top.y);
				int segments = lod == 0 ? 10 : 6;
				path.Clear(); radii.Clear(); colours.Clear(); wind.Clear();
				Vector3 b = new Vector3(0f, split * 0.5f, 0f);
				for (int s = 0; s <= segments; s++)
				{
					float t = (float)s / segments;
					Vector3 p = Curve(Vector3.zero, b, top, t);
					float height = p.y / Mathf.Max(0.1f, h);
					float taper = Mathf.Lerp(1f, 0.5f, Mathf.Pow(t, 1.4f));
					float bulge = (height - 0.35f) / 0.22f;
					float flare = 1f + 0.15f * Mathf.Pow(1f - t, 6f);
					path.Add(p);
					radii.Add(sp.TrunkRadius * taper * flare * (1f + sp.TrunkSwell * Mathf.Exp(-bulge * bulge)));
					colours.Add(Bark);
					wind.Add(Sway(p, 0f));
				}
				int firstRing = mesh.VertexCount;
				PlantParts.Tube(mesh, BarkSubmesh, path, radii, Sides(12), 1f, colours, wind);
				mesh.MarkTrunkRings(firstRing, path);
			}

			/// <summary>
			/// Plank buttresses round the foot (<see cref="TreeSpecies.ButtressCount"/>): thin fins standing out from the
			/// trunk, each reaching <see cref="TreeSpecies.ButtressReach"/> along the ground and up the trunk to
			/// <see cref="TreeSpecies.ButtressHeight"/> of the height, their top edge sagging between in a concave curve
			/// and wandering a little sideways, as a kapok's do. Few and tall they are a rainforest emergent's planks;
			/// many and low, a bald cypress's fluted, swollen base. Fixed parts, so the trunk collider has them.
			/// </summary>
			/// <param name="trunkAt">The trunk's centre at a height (a broadleaf's leans).</param>
			private void Buttresses(System.Func<float, Vector3> trunkAt)
			{
				int count = sp.ButtressCount;
				if (count <= 0 || sp.ButtressReach <= 0f || sp.ButtressHeight <= 0f)
				{
					return;
				}
				float r = sp.TrunkRadius;
				int rings = lod == 0 ? 7 : 4;
				var section = new Vector3[6];
				for (int i = 0; i < count; i++)
				{
					Reseed(8000 + i);
					BeginPart(0, Vector3.zero, 0f, PlantPart.Fixed);
					float yaw = (i + rng.Range(-0.25f, 0.25f)) * Mathf.PI * 2f / count;
					var outward = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw));
					Vector3 side = Vector3.Cross(Vector3.up, outward);
					float reach = sp.ButtressReach * rng.Range(0.7f, 1.1f);
					float tall = sp.Height * sp.ButtressHeight * rng.Range(0.75f, 1.1f);
					float thick = Mathf.Clamp(r * 0.28f, 0.1f, 0.45f);
					float phase = rng.NextFloat() * Mathf.PI * 2f;
					int first = mesh.VertexCount;
					for (int k = 0; k <= rings; k++)
					{
						float s = (float)k / rings;
						// From inside the trunk (half its radius) out to the tip on the ground.
						float x = Mathf.Lerp(r * 0.5f, r + reach, s);
						float topY = tall * (1f - s) * (1f - s) + 0.04f;
						float w = k == rings ? 0.01f : thick * Mathf.Lerp(1f, 0.35f, s);
						Vector3 centre = trunkAt(topY * 0.5f);
						centre.y = 0f;
						centre += outward * x + side * (Mathf.Sin(phase + s * 3f) * reach * 0.07f * s);
						const float below = -0.35f;
						section[0] = centre - side * w + Vector3.up * below;
						section[1] = centre - side * w + Vector3.up * Mathf.Max(below, topY - w * 1.2f);
						section[2] = centre - side * w * 0.35f + Vector3.up * topY;
						section[3] = centre + side * w * 0.35f + Vector3.up * topY;
						section[4] = centre + side * w + Vector3.up * Mathf.Max(below, topY - w * 1.2f);
						section[5] = centre + side * w + Vector3.up * below;
						for (int j = 0; j < 6; j++)
						{
							// Bark in metres: along the fin and up it, the fibres standing as the trunk's do.
							Vector3 p = section[j];
							mesh.AddVertex(p, Vector3.zero, new Vector2(x + (j < 3 ? 0f : thick * 2f), p.y), Bark, Sway(p, 0f));
						}
					}
					for (int k = 0; k < rings; k++)
					{
						for (int j = 0; j < 5; j++)
						{
							int a = first + k * 6 + j, bb = a + 1, c = a + 7, d = a + 6;
							// Out from the fin's middle plane at its foot: sideways on its flanks, up across its edge.
							Vector3 mid = (mesh.Positions[a] + mesh.Positions[c]) * 0.5f;
							Vector3 core = (mesh.Positions[first + k * 6] + mesh.Positions[first + k * 6 + 5] + mesh.Positions[first + (k + 1) * 6] + mesh.Positions[first + (k + 1) * 6 + 5]) * 0.25f;
							mesh.AddQuad(BarkSubmesh, a, bb, c, d, mid - core);
						}
					}
				}
			}

			/// <summary>
			/// Prop roots (<see cref="TreeSpecies.PropRootCount"/>): a red mangrove's stilts, arching out and down from
			/// the stem between a twentieth of the height and <see cref="TreeSpecies.PropRootTop"/> of it to a ring
			/// <see cref="TreeSpecies.PropRootRing"/> across on the mud; every other one forks halfway into a second
			/// root. Fixed parts: a mangrove's root tangle is not walked through.
			/// </summary>
			private void PropRoots(System.Func<float, Vector3> trunkAt)
			{
				int count = sp.PropRootCount;
				if (count <= 0 || sp.PropRootRing <= 0f)
				{
					return;
				}
				float h = sp.Height;
				float topShare = Mathf.Max(0.06f, sp.PropRootTop);
				float radius = Mathf.Clamp(sp.TrunkRadius * 0.35f, 0.05f, 0.14f);
				for (int i = 0; i < count; i++)
				{
					Reseed(7000 + i);
					BeginPart(0, Vector3.zero, 0f, PlantPart.Fixed);
					float share = Mathf.Lerp(0.05f, topShare, (i + rng.NextFloat()) / count);
					float yaw = i * 2.39996f + rng.Range(-0.35f, 0.35f); // the golden angle: no two land on one bearing
					var outward = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw));
					float ring = sp.PropRootRing * rng.Range(0.55f, 1f) * Mathf.Lerp(0.8f, 1.1f, share / topShare);
					float y0 = h * share;
					Vector3 a = trunkAt(y0) + outward * sp.TrunkRadius * 0.6f;
					Vector3 c = new Vector3(a.x, 0f, a.z) + outward * ring + Vector3.down * 0.25f;
					// Out first and over, then down steeply into the mud: the control point stands above the root's foot.
					Vector3 b = a + outward * ring * 0.6f + Vector3.up * (y0 * 0.3f + 0.3f);
					Limb(a, b, c, radius, radius * 0.75f, 5, lod == 0 ? 6 : 4, false);
					if ((i & 1) == 0)
					{
						Vector3 at = Curve(a, b, c, 0.5f);
						Vector3 aside = Vector3.Cross(Vector3.up, outward) * (rng.NextFloat() < 0.5f ? -1f : 1f);
						Vector3 to = new Vector3(at.x, 0f, at.z) + (outward + aside * 0.7f).normalized * ring * rng.Range(0.4f, 0.6f) + Vector3.down * 0.25f;
						Limb(at, at + (to - at) * 0.4f + Vector3.up * 0.25f, to, radius * 0.7f, radius * 0.55f, 4, lod == 0 ? 4 : 3, false);
					}
				}
			}

			/// <summary>A dead bough on a gnarled conifer: a bare crooked stick to <paramref name="reach"/> with a spray of twigs at its end.</summary>
			private void BareBough(Vector3 root, Vector3 reach, Vector3 crown)
			{
				Vector3 end = root + reach + Vector3.up * reach.magnitude * rng.Range(-0.1f, 0.3f);
				Crooked(root, end, sp.TrunkRadius * 0.12f, sp.TrunkRadius * 0.03f, 4, lod == 0 ? 4 : 2, 0.15f, 0.06f, true);
				if (lod == 0)
				{
					Spray(end - reach.normalized * 0.3f, (end - root).normalized, Mathf.Max(0.6f, sp.LeafSize), Mathf.Max(0.5f, sp.LeafSize * 0.8f), crown, FoliageCell.Twigs, 0.2f);
				}
			}

			/// <summary>
			/// Hanging strands (<see cref="TreeSpecies.Hanging"/>): Spanish moss or old man's beard from a limb, two
			/// crossed strips of the <see cref="FoliageCell.Strand"/> cell in <see cref="TreeSpecies.HangingColour"/>.
			/// Not tintable (vertex alpha 0): an epiphyte does not turn with the tree in autumn or drop with its leaves,
			/// so a bald cypress stands bare in winter still hung with its moss. Swing loose in the wind.
			/// </summary>
			private void Strands(Vector3 at, Vector3 crown)
			{
				// Festoons a metre to three long on a grown tree (Spanish moss reaches six).
				float length = Mathf.Clamp(sp.Height * 0.08f, 0.8f, 3.2f) * rng.Range(0.6f, 1.1f);
				length = Mathf.Min(length, at.y - 0.4f);
				if (length < 0.3f)
				{
					return;
				}
				float width = Mathf.Max(0.3f, length * 0.35f) * leafScale;
				int segments = lod == 0 ? 3 : 2;
				bool leaf = partKind != PlantPart.Frond;
				mesh.SetPart(partPivot, partHash, partRank, leaf ? PlantPart.Leaf : PlantPart.Frond);
				Color32 colour = ShadedTone(sp.HangingColour * rng.Range(0.85f, 1.1f), at, crown, 0f);
				float drift = rng.Range(-0.15f, 0.15f);
				float yaw = rng.NextFloat() * Mathf.PI * 2f;
				var side = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw));
				var spine = new List<Vector3>(segments + 1);
				var widths = new List<float>(segments + 1);
				var tones = new List<Color32>(segments + 1);
				var sway = new List<Vector2>(segments + 1);
				for (int s = 0; s <= segments; s++)
				{
					float t = (float)s / segments;
					Vector3 p = at + Vector3.down * length * t + side * (drift * length * t * t);
					spine.Add(p);
					widths.Add(width * Mathf.Lerp(1f, 0.6f, t));
					tones.Add(colour);
					sway.Add(Sway(at, 0.5f + 0.5f * t));
				}
				for (int k = 0; k < 2; k++)
				{
					int card = cardSerial++;
					mesh.SetCard(Hash(partId * 4099 + card, 11), leaf ? 0.02f + 0.98f * Hash(partId * 4099 + card, 13) : 0f);
					Vector3 across = k == 0 ? side : Vector3.Cross(Vector3.up, side);
					PlantParts.Strip(mesh, LeafSubmesh, spine, widths, across, FoliageCell.Strand, tones, sway, (at - crown).normalized);
				}
			}

			/// <summary>
			/// A weeping tree's curtains (<see cref="TreeSpecies.Weeping"/>): from a limb's leafy end, two crossed strips
			/// of the <see cref="FoliageCell.Strand"/> cell — pendulous shoots hung with narrow leaves — falling about
			/// <see cref="TreeSpecies.Weeping"/> of the tree's height, out a little from the crown first and then straight
			/// down, stopping short of the ground. Leaf-coloured and tintable: a willow's curtains yellow and fall with its
			/// leaves. They flutter more toward their tips.
			/// </summary>
			private void Curtains(Vector3 at, Vector3 outward, Vector3 crown)
			{
				float length = sp.Weeping * sp.Height * rng.Range(0.6f, 1f);
				length = Mathf.Min(length, at.y - 0.4f);
				if (length < 0.5f)
				{
					return;
				}
				float width = sp.LeafSize * 0.8f * leafScale;
				int segments = lod == 0 ? 4 : 3;
				mesh.SetPart(partPivot, partHash, partRank, PlantPart.Leaf);
				float pick = rng.NextFloat();
				Vector3 flare = (outward + new Vector3(rng.Range(-0.5f, 0.5f), 0f, rng.Range(-0.5f, 0.5f))).normalized;
				var spine = new List<Vector3>(segments + 1);
				var widths = new List<float>(segments + 1);
				var tones = new List<Color32>(segments + 1);
				var sway = new List<Vector2>(segments + 1);
				for (int s = 0; s <= segments; s++)
				{
					float t = (float)s / segments;
					Vector3 p = at + flare * (length * 0.2f * Mathf.Sin(t * Mathf.PI * 0.5f)) + Vector3.down * length * t;
					spine.Add(p);
					widths.Add(width * Mathf.Lerp(1f, 0.7f, t));
					tones.Add(ShadedLeaf(pick, p, crown));
					sway.Add(Sway(at, 0.3f + 0.7f * t) + new Vector2(0.15f * t * Mathf.Min(sp.Height / 10f, 2.4f), 0f));
				}
				for (int k = 0; k < 2; k++)
				{
					int card = cardSerial++;
					mesh.SetCard(Hash(partId * 4099 + card, 11), 0.02f + 0.98f * Hash(partId * 4099 + card, 13));
					Vector3 across = k == 0 ? Vector3.Cross(Vector3.up, flare) : flare;
					PlantParts.Strip(mesh, LeafSubmesh, spine, widths, across, FoliageCell.Strand, tones, sway, (spine[segments / 2] - crown).normalized);
				}
			}

			/// <summary>
			/// A columnar tree (<see cref="TreeForm.Columnar"/>): a straight stem to near the top, and close whorls of
			/// sprays leaving it twelve to twenty-two degrees off vertical, each as long as it must be to reach the
			/// column's radius at its height — so the sprays overlap up the column like a fastigiate tree's shoots. The
			/// column is widest a third of the way up, narrower at its foot, and closes to a blunt tip.
			/// </summary>
			public void Columnar()
			{
				float h = sp.Height;
				BeginPart(0, Vector3.zero, 0f, PlantPart.Fixed);
				Limb(Vector3.zero, new Vector3(0f, h * 0.5f, 0f), new Vector3(0f, h * 0.97f, 0f), sp.TrunkRadius, sp.TrunkRadius * 0.1f, 8, lod == 0 ? 8 : 4, true);
				Buttresses(y => Vector3.up * y);
				int whorls = Mathf.Max(8, sp.Branches);
				float baseY = h * sp.CrownBase;
				var crown = new Vector3(0f, (baseY + h) * 0.5f, 0f);
				for (int w = 0; w < whorls; w++)
				{
					float t = (w + 0.5f) / whorls;
					Reseed(3000 + Mathf.RoundToInt(t * 997f));
					float y = Mathf.Lerp(baseY, h * 0.93f, t);
					float profile = t < 0.3f
						? Mathf.Lerp(0.6f, 1f, Mathf.SmoothStep(0f, 1f, t / 0.3f))
						: 1f - 0.85f * Mathf.Pow((t - 0.3f) / 0.7f, 2f);
					float radius = sp.CrownWidth * h * profile * rng.Range(0.9f, 1.1f) + 0.1f;
					(int arms, int coreArms) = Spared(lod == 0 ? 6 : 4);
					float twist = rng.NextFloat() * 360f;
					for (int a = 0; a < arms; a++)
					{
						float yaw = Mathf.Deg2Rad * (twist + a * 360f / arms + rng.Range(-15f, 15f));
						var outward = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw));
						float tilt = Mathf.Deg2Rad * rng.Range(12f, 22f);
						Vector3 dir = outward * Mathf.Sin(tilt) + Vector3.up * Mathf.Cos(tilt);
						// Long enough to reach the column's edge, never past the tree's top.
						float length = Mathf.Min(radius / Mathf.Sin(tilt), Mathf.Max(0.6f, (h - y) / Mathf.Cos(tilt) + 0.3f));
						var root = new Vector3(0f, y, 0f) + outward * sp.TrunkRadius * 0.6f;
						BeginPart(3000 + w * 64 + a, root, Rank(a, arms, coreArms, 3000 + w), PlantPart.Frond);
						Spray(root, dir, length, Mathf.Max(0.45f, radius * 0.95f), crown, sp.LeafCell, 0.55f);
					}
				}
				// The blunt tip.
				BeginPart(2999, new Vector3(0f, h * 0.88f, 0f), 0f, PlantPart.Frond);
				Spray(new Vector3(0f, h * 0.88f, 0f), Vector3.up, h * 0.12f, Mathf.Max(0.4f, sp.CrownWidth * h * 0.6f), crown, sp.LeafCell, 0.3f);
			}

			/// <summary>
			/// A rosette tree (<see cref="TreeForm.Rosette"/>): a stout, slightly crooked stem to
			/// <see cref="TreeSpecies.CrownBase"/> of the height, then <see cref="TreeSpecies.Branches"/> limbs that each
			/// fork twice more — every fork two ways, now and then three, each branch seven tenths of the last — aimed so
			/// the tips reach the crown's edge and top, and every tip a rosette (<see cref="Tuft"/>). A Joshua tree's
			/// angular, upturned branching; with more limbs and a wider crown, a dragon tree's dense umbrella.
			/// </summary>
			public void Rosette()
			{
				float h = sp.Height;
				Reseed(0);
				float stem = h * Mathf.Clamp(sp.CrownBase, 0.1f, 0.9f);
				Vector3 lean = new Vector3(rng.Range(-0.05f, 0.05f), 0f, rng.Range(-0.05f, 0.05f)) * stem;
				Vector3 top = new Vector3(0f, stem, 0f) + lean;
				BeginPart(0, Vector3.zero, 0f, PlantPart.Fixed);
				Crooked(Vector3.zero, top, sp.TrunkRadius, sp.TrunkRadius * 0.75f, 9, lod == 0 ? 6 : 3, 0.04f, 0f, false);
				Vector3 TrunkAt(float y) => Vector3.Lerp(Vector3.zero, top, y / Mathf.Max(0.01f, stem));
				Buttresses(TrunkAt);
				PropRoots(TrunkAt);
				var crown = new Vector3(lean.x, (stem + h) * 0.5f, lean.z);
				float crownRadius = sp.CrownWidth * h;
				(int limbs, int coreLimbs) = Spared(Mathf.Max(2, sp.Branches));
				for (int l = 0; l < limbs; l++)
				{
					Reseed(100 + l);
					float yaw = Mathf.Deg2Rad * (l * 360f / limbs + rng.Range(-25f, 25f));
					var outward = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw));
					// Where this limb's tips should end up: out at the crown's edge, up near the top.
					Vector3 goal = outward * crownRadius * rng.Range(0.6f, 0.95f) + Vector3.up * (h - stem - sp.LeafSize * 0.4f) * rng.Range(0.75f, 1f);
					float reach = goal.magnitude / (1f + 0.7f + 0.49f);
					BeginPart(100 + l, top, Rank(l, limbs, coreLimbs, 100), PlantPart.Limb);
					Fork(top, goal.normalized, reach, sp.TrunkRadius * 0.62f, 3, 100 + l, crown);
				}
			}

			/// <summary>One branch of a rosette tree and, below <paramref name="level"/> 1, the forks it splits into.</summary>
			private void Fork(Vector3 from, Vector3 dir, float length, float radius, int level, int id, Vector3 crown)
			{
				Reseed(50000 + id);
				Vector3 end = from + dir * length;
				Crooked(from, end, radius, radius * 0.72f, lod == 0 ? 6 : 4, lod == 0 ? 4 : 2, 0.06f, 0.04f, false);
				if (level <= 1)
				{
					Tuft(end, dir, crown);
					return;
				}
				int forks = Hash(id, 31) < 0.25f ? 3 : 2;
				float spin = rng.NextFloat() * 360f;
				Vector3 across = PlantParts.Perpendicular(dir);
				for (int k = 0; k < forks; k++)
				{
					// Splayed off the parent, turned round it, and bent up: the branches climb toward the light.
					Vector3 splay = Quaternion.AngleAxis(spin + k * 360f / forks + rng.Range(-20f, 20f), dir) * Quaternion.AngleAxis(rng.Range(22f, 40f), across) * dir;
					Vector3 child = (splay + Vector3.up * 0.25f).normalized;
					Fork(end, child, length * rng.Range(0.62f, 0.78f), radius * 0.7f, level - 1, id * 4 + k + 1, crown);
				}
			}

			/// <summary>
			/// A rosette at a branch tip: fans of strap leaves (the species' <see cref="TreeSpecies.LeafCell"/>, a fan
			/// springing from a point a little up its card) splayed up and round the tip into a spiky ball, and under it a
			/// fan hanging down in a browner tone — the dead leaves a yucca keeps as a skirt below its living ones.
			/// </summary>
			private void Tuft(Vector3 at, Vector3 dir, Vector3 crown)
			{
				float size = sp.LeafSize;
				int fans = lod == 0 ? 4 : 3;
				Vector3 axis = (dir + Vector3.up * 0.8f).normalized;
				Vector3 across = PlantParts.Perpendicular(axis);
				float spin = rng.NextFloat() * 360f;
				for (int f = 0; f < fans; f++)
				{
					Vector3 d = f == 0 ? axis
						: Quaternion.AngleAxis(spin + f * 360f / (fans - 1), axis) * Quaternion.AngleAxis(rng.Range(45f, 70f), across) * axis;
					// The cell's fan springs from 0.28 up the card: set back so it springs from the tip.
					Spray(at - d * size * 0.28f, d, size, size, crown, sp.LeafCell, 0.4f);
				}
				var horizontal = new Vector3(dir.x, 0f, dir.z);
				Vector3 droop = (Vector3.down + horizontal.normalized * 0.45f).normalized;
				Color dead = Color.Lerp(sp.LeafA, new Color(0.6f, 0.53f, 0.38f, 1f), 0.6f);
				Spray(at - droop * size * 0.2f, droop, size * 0.85f, size * 0.85f, crown, sp.LeafCell, 0.3f, dead);
			}
		}
	}
}
#endif
