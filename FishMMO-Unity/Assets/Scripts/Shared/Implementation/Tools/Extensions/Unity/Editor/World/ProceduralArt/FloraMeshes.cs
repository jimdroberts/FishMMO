#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// The forms within a detail kind (<see cref="DetailPlant.Variant"/>) that the land flora builds. 0 is always the
	/// kind's first form: for the legacy kinds (grass, reeds, fern, shrub, dry shrub) that is the legacy builder, so
	/// every detail made before variants comes out exactly as it did.
	/// </summary>
	public static class FloraVariant
	{
		// Grass: 0 plain blades.
		/// <summary>Cottongrass (<i>Eriophorum</i>): a sedge tuft with white cotton heads on bare stems.</summary>
		public const int GrassCotton = 1;
		/// <summary>Feather grass (<i>Stipa</i>): a fine tuft with long silky awns arching from the stems.</summary>
		public const int GrassPlume = 2;
		/// <summary>Rush (<i>Juncus</i>): stiff dark cylindrical stems with a small brown tuft partway up.</summary>
		public const int GrassRush = 3;
		/// <summary>Wheat: golden stems each with an ear.</summary>
		public const int GrassEar = 4;

		// Reeds: 0 plain straps (the cattail's leaves).
		/// <summary>Common reed (<i>Phragmites</i>): tall culms with strap leaves and a purple-brown plume.</summary>
		public const int ReedsPlume = 1;

		// Fern: 0 the arching crown of fronds.
		/// <summary>Bracken (<i>Pteridium</i>): fronds on tall stalks, their triangular blades held near level.</summary>
		public const int FernBracken = 1;
		/// <summary>Hart's-tongue (<i>Asplenium scolopendrium</i>): undivided glossy strap fronds.</summary>
		public const int FernHartstongue = 2;

		// Shrub: 0 the small leafy dome.
		/// <summary>Nettle and dock: upright leafy stems over a few broad basal leaves.</summary>
		public const int ShrubHerb = 1;
		/// <summary>Samphire (<i>Salicornia</i>): jointed succulent stems, green reddening to the tips.</summary>
		public const int ShrubSucculent = 2;
		/// <summary>Horsetail (<i>Equisetum</i>): jointed stems with whorls of fine branches.</summary>
		public const int ShrubHorsetail = 3;

		// DryShrub: 0 the dead twiggy dome.
		/// <summary>Tumbleweed (<i>Salsola</i>): a tan ball of twigs resting on the ground.</summary>
		public const int DryShrubBall = 1;

		// Rosette.
		public const int RosetteAgave = 0, RosetteYucca = 1, RosetteBromeliad = 2;
		/// <summary>Tower of jewels (<i>Echium wildpretii</i>): a silver rosette under a tall red flower spike.</summary>
		public const int RosetteEchium = 3;
		/// <summary>Pitcher plant (<i>Sarracenia</i>): a clump of upright hooded trumpets.</summary>
		public const int RosettePitcher = 4;

		// ColumnCluster.
		public const int OrganPipe = 0, Euphorbia = 1;

		// Cushion.
		public const int CushionMossCampion = 0, CushionThrift = 1, CushionSpiny = 2, CushionSphagnum = 3, CushionMoss = 4;

		// Lichen.
		public const int LichenClump = 0, LichenLava = 1;

		// Creeper.
		public const int CreeperIvy = 0, CreeperBeachVine = 1;

		// RootKnobs.
		public const int Pneumatophores = 0, CypressKnees = 1;

		// Mushroom.
		public const int MushroomForest = 0, MushroomCluster = 1, MushroomSwamp = 2, MushroomCave = 3;
	}

	/// <summary>
	/// The land flora's detail meshes added by the vegetation expansion (2026-10-10): broad herbs, pad and cholla
	/// cacti, column clusters, rosettes, cushions, lichens, creepers, root knobs, mushrooms and the newer litter —
	/// the <see cref="DetailKind"/> members from <see cref="DetailKind.BroadHerb"/> to <see cref="DetailKind.DebrisBamboo"/> —
	/// and the newer forms of the legacy kinds (<see cref="FloraVariant"/>: headed grasses, rushes, the reed's plume,
	/// bracken and hart's-tongue, ruderal herbs, samphire, horsetail, tumbleweed).
	/// Built like every other detail (<see cref="VegetationMeshes"/>): one sub-mesh, vertex colours for the hue, the
	/// foliage atlas's cells for the outline, and the sway and flutter weights the vegetation shader reads.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <see cref="VegetationMeshes.Build"/> falls through to here after <see cref="SeaFloorMeshes.Build"/> for every kind
	/// it does not build itself, and calls <see cref="Grass"/>, <see cref="Reeds"/>, <see cref="Fern"/>, <see cref="Herb"/>
	/// and <see cref="DryShrub"/> for a legacy kind's non-zero variant. The legacy kinds' builders and part helpers are
	/// internal to the assembly (<c>VegetationMeshes.Blades</c>, <c>Coral</c>, <c>Part</c>, <c>Spared</c> …), and solids
	/// turn on <see cref="PlantParts.Lathe"/>, so nothing here needs another file edited. A kind's material traits
	/// (shadows, sink, bark, sway) are declared in ProceduralArtCatalogue.Flora.cs.
	/// </para>
	/// <para>
	/// <b>Budgets.</b> A grass or reed clump is at most 100 triangles (the blade renderer's fallback is drawn by the
	/// million), every other detail at most 400. Builders whose part count the recipe sets stop adding parts before a
	/// part would cross the budget (<see cref="Fits"/>), so a recipe asking for too many simply gets fewer.
	/// </para>
	/// <para>
	/// <b>Colours.</b> Vertex alpha is the seasonal-tint mask as everywhere: 1 on leaves and blades, 0 on dead wood,
	/// fungi, lichen and cactus skin, <see cref="HeadAlpha"/> on flower and seed heads — which is also how the blade
	/// renderer tells a grass mesh's head colours from its blades' (GrassTerrain.HeadVertexAlpha, 0.5).
	/// </para>
	/// </remarks>
	public static class FloraMeshes
	{
		/// <summary>Vertex alpha on flowers and seed heads: under the blade renderer's 0.5, so it reads them as head colours.</summary>
		public const float HeadAlpha = 0.3f;

		/// <summary>The triangle budget of a grass or reed clump.</summary>
		public const int GrassBudget = 100;

		/// <summary>The triangle budget of every other detail (a little under the 400 the tests allow).</summary>
		public const int PlantBudget = 396;

		/// <summary>Builds a land-flora kind into <paramref name="mesh"/>; false for a kind that is not one.</summary>
		/// <remarks>Must not touch <paramref name="rng"/> before it knows the kind is its own.</remarks>
		public static bool Build(MeshBuilder mesh, in DetailPlant p, DeterministicRNG rng)
		{
			switch (p.Kind)
			{
				case DetailKind.BroadHerb: BroadHerb(mesh, in p, rng); return true;
				case DetailKind.PadCactus: PadCactus(mesh, in p, rng); return true;
				case DetailKind.Cholla: Cholla(mesh, in p, rng); return true;
				case DetailKind.ColumnCluster: ColumnCluster(mesh, in p, rng); return true;
				case DetailKind.Rosette: Rosette(mesh, in p, rng); return true;
				case DetailKind.Cushion: Cushion(mesh, in p, rng); return true;
				case DetailKind.Lichen: Lichen(mesh, in p, rng); return true;
				case DetailKind.Creeper: Creeper(mesh, in p, rng); return true;
				case DetailKind.RootKnobs: RootKnobs(mesh, in p, rng); return true;
				case DetailKind.Mushroom: Mushrooms(mesh, in p, rng); return true;
				case DetailKind.DebrisNeedle: DebrisNeedle(mesh, in p, rng); return true;
				case DetailKind.DebrisPalm: DebrisPalm(mesh, in p, rng); return true;
				case DetailKind.DebrisWrack: DebrisWrack(mesh, in p, rng); return true;
				case DetailKind.DebrisBranch: DebrisBranch(mesh, in p, rng); return true;
				case DetailKind.DebrisBamboo: DebrisBamboo(mesh, in p, rng); return true;
				default:
					return false;
			}
		}

		// ── Shared pieces ─────────────────────────────────────────────

		private static Vector2 SolidUV(float u, float v) => FoliageAtlas.CellUV(FoliageCell.Solid, Mathf.Clamp01(u), Mathf.Repeat(v, 1f));

		/// <summary>A point in a disc on the ground.</summary>
		private static Vector3 Disc(DeterministicRNG rng, float radius)
		{
			float angle = rng.NextFloat() * Mathf.PI * 2f;
			float r = Mathf.Sqrt(rng.NextFloat()) * radius;
			return new Vector3(Mathf.Cos(angle) * r, 0f, Mathf.Sin(angle) * r);
		}

		private static Vector3 Flat(float yaw) => new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw));

		/// <summary>A direction by its turn about the up axis and its elevation (radians).</summary>
		private static Vector3 Direction(float yaw, float elevation) =>
			new Vector3(Mathf.Cos(yaw) * Mathf.Cos(elevation), Mathf.Sin(elevation), Mathf.Sin(yaw) * Mathf.Cos(elevation));

		private static Color Pick(in DetailPlant p, DeterministicRNG rng) => VegetationMeshes.Pick(in p, rng);

		/// <summary>The plant's i-th accent colour, or <paramref name="fallback"/> when it has none.</summary>
		private static Color Accent(in DetailPlant p, int i, Color fallback) =>
			p.Accents != null && p.Accents.Length > 0 ? p.Accents[((i % p.Accents.Length) + p.Accents.Length) % p.Accents.Length] : fallback;

		/// <summary>A hash of an index and a salt, 0..1, without touching the plant's stream.</summary>
		private static float Hash(int n, int salt) => VegetationMeshes.Hash(n, salt);

		/// <summary>Everything from here on holds still: no part of its own for the plant variation to drop or turn.</summary>
		private static void Fixed(MeshBuilder mesh) => mesh.SetPart(Vector3.zero, 0f, 0f, PlantPart.Fixed);

		/// <summary>True when <paramref name="cost"/> more triangles stay within <paramref name="budget"/>.</summary>
		private static bool Fits(MeshBuilder mesh, int cost, int budget = PlantBudget) => mesh.TriangleCount + cost <= budget;

		/// <summary>Triangles a lathe of <paramref name="points"/> profile points round <paramref name="sides"/> makes.</summary>
		private static int LatheCost(int points, int sides) => (points - 1) * sides * 2;

		/// <summary>Triangles a tube of <paramref name="points"/> rings makes, closed with a point when <paramref name="tip"/>.</summary>
		private static int TubeCost(int points, int sides, bool tip) => tip ? (points - 2) * sides * 2 + sides : (points - 1) * sides * 2;

		/// <summary>Triangles a strip of <paramref name="segments"/> makes, pointed or square-ended.</summary>
		private static int StripCost(int segments, bool pointed) => pointed ? (segments - 1) * 2 + 1 : segments * 2;

		/// <summary>
		/// A thin stem (a flat strip on the blade cell) from <paramref name="root"/> to <paramref name="top"/>, bowed
		/// sideways by <paramref name="bow"/> at its middle; darker at the foot, swaying more toward the top.
		/// </summary>
		private static void Stem(MeshBuilder mesh, Vector3 root, Vector3 top, Vector3 bow, float width0, float width1, Color colour, int segments, bool pointed, float flutter = 0.1f)
		{
			var spine = new List<Vector3>();
			var widths = new List<float>();
			var colours = new List<Color32>();
			var wind = new List<Vector2>();
			float height = Mathf.Max(0.01f, top.y - root.y);
			Vector3 mid = (root + top) * 0.5f + bow;
			for (int s = 0; s <= segments; s++)
			{
				float t = (float)s / segments;
				spine.Add((1f - t) * (1f - t) * root + 2f * (1f - t) * t * mid + t * t * top);
				widths.Add(pointed && s == segments ? 0f : Mathf.Lerp(width0, width1, t));
				colours.Add(PlantParts.C32(colour * Mathf.Lerp(0.6f, 1f, t), 1f));
				wind.Add(new Vector2(Mathf.Pow(t, 1.5f) * height, flutter * t));
			}
			Vector3 side = PlantParts.Perpendicular(top - root);
			PlantParts.Strip(mesh, 0, spine, widths, side, FoliageCell.Blade, colours, wind, Vector3.up);
		}

		/// <summary>Two cards crossed along <paramref name="axis"/>: a head that reads from every side.</summary>
		private static void CrossedHead(MeshBuilder mesh, Vector3 centre, Vector3 axis, float width, float length, FoliageCell cell, Color32 colour, Vector2 wind, float yaw)
		{
			Vector3 a = axis.sqrMagnitude > 1e-8f ? axis.normalized : Vector3.up;
			Vector3 s = Quaternion.AngleAxis(yaw, a) * PlantParts.Perpendicular(a);
			Vector3 t = Vector3.Cross(a, s).normalized;
			PlantParts.Card(mesh, 0, centre, s * width, a * length, cell, colour, wind, wind, Vector3.up);
			PlantParts.Card(mesh, 0, centre, t * width, a * length, cell, colour, wind, wind, Vector3.up);
		}

		/// <summary>A strip along an explicit spine on an atlas cell, coloured by a function of the share along it.</summary>
		private static void Ribbon(MeshBuilder mesh, IList<Vector3> spine, IList<float> widths, Vector3 side, FoliageCell cell, Func<float, Color32> colour, float sway, float flutter, Vector3 light)
		{
			var colours = new List<Color32>(spine.Count);
			var wind = new List<Vector2>(spine.Count);
			for (int i = 0; i < spine.Count; i++)
			{
				float t = spine.Count > 1 ? (float)i / (spine.Count - 1) : 0f;
				colours.Add(colour(t));
				wind.Add(new Vector2(sway * t * t, flutter * t));
			}
			PlantParts.Strip(mesh, 0, spine, widths, side, cell, colours, wind, light);
		}

		/// <summary>
		/// A thick succulent leaf (agave, yucca, bromeliad): a midrib with two halves whose edges are raised into a
		/// channel open to the sky, so even one sheet reads as a fleshy, keeled leaf from the side and from above; it
		/// tapers to a point by <paramref name="profile"/> and bends down under its weight by <paramref name="droop"/>.
		/// On the atlas's solid cell, coloured by the share along it.
		/// </summary>
		private static void KeeledLeaf(MeshBuilder mesh, Vector3 root, Vector3 dir, float length, float width, float channel, float droop,
			int segments, Func<float, float> profile, Func<float, Color32> colour, float sway)
		{
			dir = dir.normalized;
			Vector3 side = Vector3.Cross(Vector3.up, dir);
			side = side.sqrMagnitude > 1e-6f ? side.normalized : Vector3.right;
			Vector3 face = Vector3.Cross(dir, side).normalized;
			int prevM = -1, prevL = -1, prevR = -1;
			for (int i = 0; i <= segments; i++)
			{
				float t = (float)i / segments;
				Vector3 m = root + dir * (length * t) + Vector3.down * (droop * length * t * t);
				float w = width * profile(t);
				Color32 c = colour(t);
				var wind = new Vector2(sway * length * t * t, 0.05f * t);
				if ((i == segments || w <= 1e-4f) && prevM >= 0)
				{
					int tip = mesh.AddVertex(m, Vector3.zero, SolidUV(0.5f, t), c, wind);
					mesh.AddTriangle(0, prevM, prevL, tip, face);
					mesh.AddTriangle(0, prevM, tip, prevR, face);
					return;
				}
				Vector3 lift = face * (channel * w * 0.5f);
				int vm = mesh.AddVertex(m, Vector3.zero, SolidUV(0.5f, t), c, wind);
				int vl = mesh.AddVertex(m - side * (w * 0.5f) + lift, Vector3.zero, SolidUV(0f, t), c, wind);
				int vr = mesh.AddVertex(m + side * (w * 0.5f) + lift, Vector3.zero, SolidUV(1f, t), c, wind);
				if (prevM >= 0)
				{
					mesh.AddQuad(0, prevM, prevL, vl, vm, face);
					mesh.AddQuad(0, prevM, vm, vr, prevR, face);
				}
				prevM = vm; prevL = vl; prevR = vr;
			}
		}

		/// <summary>
		/// A lathed solid placed in the world: <paramref name="profile"/> turned about the local up axis, then rotated and
		/// moved (cones, husks, fruits, pads). Normals are left for the mesh's recalculation.
		/// </summary>
		private static void Solid(MeshBuilder mesh, IList<Vector2> profile, int sides, Matrix4x4 place, Func<float, float, Color32> colour,
			Func<float, float, float> lump = null, Func<float, float, Vector2> uv = null)
		{
			int first = mesh.VertexCount;
			PlantParts.Lathe(mesh, Vector3.zero, profile, sides, lump, colour, null, 0f, 0, uv);
			mesh.Transform(place, first);
		}

		/// <summary>An ellipsoid's profile, pole to pole: <paramref name="bands"/> bands, radius by length.</summary>
		private static Vector2[] EllipsoidProfile(float radius, float length, int bands, float bluntBottom = 0f)
		{
			var profile = new Vector2[bands + 1];
			for (int i = 0; i <= bands; i++)
			{
				float a = Mathf.PI * i / bands;
				profile[i] = new Vector2(Mathf.Max(radius * bluntBottom * (i == 0 ? 1f : 0f), radius * Mathf.Sin(a)), -Mathf.Cos(a) * length * 0.5f);
			}
			return profile;
		}

		// ── Grasses, sedges, rushes, reeds (legacy kinds' variants) ───

		/// <summary>A grass kind's variant: headed grasses and rushes; plain blades for 0.</summary>
		internal static void Grass(MeshBuilder mesh, in DetailPlant p, DeterministicRNG rng)
		{
			switch (p.Variant)
			{
				case FloraVariant.GrassCotton: HeadedGrass(mesh, in p, rng, GrassHeadForm.Cotton, 0.6f); break;
				case FloraVariant.GrassPlume: HeadedGrass(mesh, in p, rng, GrassHeadForm.Plume, 0.65f); break;
				case FloraVariant.GrassEar: HeadedGrass(mesh, in p, rng, GrassHeadForm.Ear, 0.6f); break;
				case FloraVariant.GrassRush: Rush(mesh, in p, rng); break;
				default: VegetationMeshes.Blades(mesh, in p, rng, FoliageCell.Blade); break;
			}
		}

		private enum GrassHeadForm { Cotton, Plume, Ear }

		/// <summary>
		/// A low tuft of leaves with flowering stems standing out of it, each carrying its head: cottongrass's white
		/// tufts (two crossed flower cards, a fluffy ball from any side), a feather grass's silky awn arching over on the
		/// strand cell, or a wheat ear (crossed pointed cards along the stem). The heads take the plant's first accent.
		/// </summary>
		private static void HeadedGrass(MeshBuilder mesh, in DetailPlant p, DeterministicRNG rng, GrassHeadForm head, float leafShare)
		{
			DetailPlant leaves = p;
			leaves.Height = p.Height * leafShare;
			leaves.Segments = 2;
			leaves.Variant = 0;
			VegetationMeshes.Blades(mesh, in leaves, rng, FoliageCell.Blade);

			int asked = Mathf.Max(2, Mathf.RoundToInt(p.Count * 0.6f));
			(int stems, int core) = VegetationMeshes.Spared(asked, VegetationMeshes.BladeSpare);
			Color headColour = Accent(in p, 0, Color.white);
			int headCost = head == GrassHeadForm.Plume ? StripCost(3, true) : 4;
			for (int s = 0; s < stems; s++)
			{
				if (!Fits(mesh, StripCost(2, false) + headCost, GrassBudget))
				{
					break;
				}
				Vector3 root = Disc(rng, p.Radius * 0.7f);
				VegetationMeshes.Part(mesh, s, stems, core, root, PlantPart.Frond, 820);
				float height = p.Height * rng.Range(0.8f, 1.15f);
				float yaw = rng.NextFloat() * Mathf.PI * 2f;
				Vector3 outward = Flat(yaw);
				Vector3 top = root + Vector3.up * height + outward * (height * p.Lean * rng.Range(0.1f, 0.4f));
				Color stem = Pick(in p, rng);
				Stem(mesh, root, top, outward * (height * 0.04f), 0.006f, 0.004f, stem, 2, false);
				Color32 hc = PlantParts.C32(headColour * rng.Range(0.92f, 1.04f), HeadAlpha);
				var headWind = new Vector2(height, 0.5f);
				switch (head)
				{
					case GrassHeadForm.Cotton:
					{
						float size = rng.Range(0.035f, 0.055f);
						CrossedHead(mesh, top + Vector3.up * (size * 0.3f), Vector3.up, size, size, FoliageCell.Flower, hc, headWind, yaw * Mathf.Rad2Deg);
						break;
					}
					case GrassHeadForm.Plume:
					{
						// The awns: a silky plume a third of the stem's length, rising from the tip and arching over.
						float length = height * rng.Range(0.3f, 0.42f);
						var spine = new List<Vector3>
						{
							top,
							top + outward * (length * 0.12f) + Vector3.up * (length * 0.25f),
							top + outward * (length * 0.5f) + Vector3.up * (length * 0.18f),
							top + outward * (length * 0.85f) - Vector3.up * (length * 0.12f),
						};
						var widths = new List<float> { 0.02f, 0.024f, 0.014f, 0f };
						Ribbon(mesh, spine, widths, Vector3.Cross(Vector3.up, outward), FoliageCell.Strand, t => hc, height, 0.6f, Vector3.up);
						break;
					}
					default:
					{
						float length = rng.Range(0.08f, 0.11f);
						Vector3 axis = (top - root).normalized;
						CrossedHead(mesh, top + axis * (length * 0.45f), axis, 0.022f, length, FoliageCell.Blade, hc, headWind, yaw * Mathf.Rad2Deg);
						break;
					}
				}
			}
		}

		/// <summary>
		/// A rush tussock: stiff, near-upright, pointed dark stems (a rush's "leaves" are its round green stems), every
		/// third with the loose brown flower tuft that breaks out of its side two-thirds of the way up.
		/// </summary>
		private static void Rush(MeshBuilder mesh, in DetailPlant p, DeterministicRNG rng)
		{
			(int stems, int core) = VegetationMeshes.Spared(p.Count, VegetationMeshes.BladeSpare);
			Color tuft = Accent(in p, 0, new Color(0.45f, 0.32f, 0.18f));
			for (int b = 0; b < stems; b++)
			{
				bool tufted = b % 3 == 0;
				if (!Fits(mesh, StripCost(2, true) + (tufted ? 2 : 0), GrassBudget))
				{
					break;
				}
				Vector3 root = Disc(rng, p.Radius);
				VegetationMeshes.Part(mesh, b, stems, core, root, PlantPart.Leaf, 840);
				float height = p.Height * rng.Range(0.6f, 1.1f);
				float yaw = rng.NextFloat() * Mathf.PI * 2f;
				Vector3 outward = root.sqrMagnitude > 1e-6f ? Vector3.Slerp(Flat(yaw), root.normalized, 0.6f) : Flat(yaw);
				Vector3 top = root + Vector3.up * height + outward * (height * p.Lean * rng.Range(0.3f, 1f));
				Color colour = Pick(in p, rng);
				Stem(mesh, root, top, outward * (height * 0.03f), p.Width, p.Width * 0.85f, colour, 2, true, 0.15f);
				if (tufted)
				{
					float t = rng.Range(0.62f, 0.8f);
					Vector3 at = Vector3.Lerp(root, top, t) + Vector3.Cross(Vector3.up, outward).normalized * 0.015f;
					float size = rng.Range(0.04f, 0.06f);
					Vector3 across = Quaternion.AngleAxis(rng.Range(0f, 180f), Vector3.up) * Vector3.right;
					var wind = new Vector2(height * t, 0.3f);
					PlantParts.Card(mesh, 0, at, across * size, (Vector3.up + outward * 0.4f).normalized * (size * 1.2f), FoliageCell.Twigs,
						PlantParts.C32(tuft * rng.Range(0.9f, 1.05f), HeadAlpha), wind, wind, Vector3.up);
				}
			}
		}

		/// <summary>A reeds kind's variant: the common reed's plumed culms; plain straps for 0.</summary>
		internal static void Reeds(MeshBuilder mesh, in DetailPlant p, DeterministicRNG rng)
		{
			if (p.Variant != FloraVariant.ReedsPlume)
			{
				VegetationMeshes.Blades(mesh, in p, rng, FoliageCell.Strap);
				return;
			}
			// Phragmites: a stiff culm, a strap leaf held out from a node and drooping at its tip, and the dense
			// purple-brown panicle nodding from the top (strand cell, the head colour).
			(int stems, int core) = VegetationMeshes.Spared(p.Count, VegetationMeshes.BladeSpare);
			Color plume = Accent(in p, 0, new Color(0.42f, 0.3f, 0.3f));
			int cost = StripCost(2, false) + StripCost(2, true) * 2;
			for (int s = 0; s < stems; s++)
			{
				if (!Fits(mesh, cost, GrassBudget))
				{
					break;
				}
				Vector3 root = Disc(rng, p.Radius);
				VegetationMeshes.Part(mesh, s, stems, core, root, PlantPart.Leaf, 860);
				float height = p.Height * rng.Range(0.75f, 1.1f);
				float yaw = rng.NextFloat() * Mathf.PI * 2f;
				Vector3 outward = Flat(yaw);
				Vector3 top = root + Vector3.up * height + outward * (height * p.Lean * rng.Range(0.2f, 1f));
				Color colour = Pick(in p, rng);
				Stem(mesh, root, top, outward * (height * 0.02f), 0.014f, 0.009f, colour, 2, false, 0.05f);

				float node = rng.Range(0.35f, 0.6f);
				Vector3 at = Vector3.Lerp(root, top, node);
				Vector3 leafDir = Quaternion.AngleAxis(rng.Range(-100f, 100f), Vector3.up) * outward;
				float leaf = height * rng.Range(0.24f, 0.32f);
				var spine = new List<Vector3> { at, at + leafDir * (leaf * 0.5f) + Vector3.up * (leaf * 0.18f), at + leafDir * leaf - Vector3.up * (leaf * 0.08f) };
				var widths = new List<float> { p.Width, p.Width * 0.8f, 0f };
				Color32 lc = PlantParts.C32(colour, 1f);
				Ribbon(mesh, spine, widths, Vector3.Cross(Vector3.up, leafDir), FoliageCell.Strap, t => lc, height * node + leaf * 0.5f, 0.35f, Vector3.up);

				float length = rng.Range(0.25f, 0.38f);
				var panicle = new List<Vector3> { top, top + Vector3.up * (length * 0.45f) + outward * (length * 0.12f), top + outward * (length * 0.45f) + Vector3.up * (length * 0.55f) };
				var panicleWidths = new List<float> { 0.04f, 0.06f, 0f };
				Color32 pc = PlantParts.C32(plume * rng.Range(0.9f, 1.08f), HeadAlpha);
				Ribbon(mesh, panicle, panicleWidths, Vector3.Cross(Vector3.up, outward), FoliageCell.Strand, t => pc, height, 0.5f, Vector3.up);
			}
		}

		// ── Ferns (the fern kind's variants) ──────────────────────────

		/// <summary>A fern kind's variant: bracken or hart's-tongue; the legacy crown of fronds for 0.</summary>
		internal static void Fern(MeshBuilder mesh, in DetailPlant p, DeterministicRNG rng)
		{
			switch (p.Variant)
			{
				case FloraVariant.FernBracken: Bracken(mesh, in p, rng); break;
				case FloraVariant.FernHartstongue: Hartstongue(mesh, in p, rng); break;
				default: VegetationMeshes.Fern(mesh, in p, rng); break;
			}
		}

		/// <summary>
		/// Bracken: each frond rises separately from the creeping rhizome on a tall bare stalk (about half its height),
		/// then holds its broadly triangular, finely divided blade out near level, the tip drooping a little — a canopy
		/// of flat green tables a metre up, not a fountain like the male fern's crown.
		/// </summary>
		private static void Bracken(MeshBuilder mesh, in DetailPlant p, DeterministicRNG rng)
		{
			int segments = Mathf.Max(3, p.Segments);
			(int fronds, int core) = VegetationMeshes.Spared(p.Count);
			for (int f = 0; f < fronds; f++)
			{
				if (!Fits(mesh, StripCost(2, false) + StripCost(segments, true)))
				{
					break;
				}
				float yaw = (f + rng.Range(-0.35f, 0.35f)) * Mathf.PI * 2f / fronds;
				Vector3 dir = Flat(yaw);
				Vector3 root = dir * (p.Radius * rng.Range(0.2f, 1f));
				VegetationMeshes.Part(mesh, f, fronds, core, root, PlantPart.Frond, 560);
				float stalk = p.Height * rng.Range(0.55f, 0.72f);
				Vector3 stalkTop = root + Vector3.up * stalk + dir * (stalk * rng.Range(0.08f, 0.2f));
				Color colour = Pick(in p, rng);
				Stem(mesh, root, stalkTop, dir * (stalk * 0.03f), 0.014f, 0.01f, colour * 0.85f, 2, false, 0.05f);

				float length = p.Height * rng.Range(0.5f, 0.66f);
				float rise = rng.Range(0.05f, 0.2f);
				var spine = new List<Vector3>();
				var widths = new List<float>();
				for (int s = 0; s <= segments; s++)
				{
					float t = (float)s / segments;
					spine.Add(stalkTop + dir * (length * t) + Vector3.up * (length * (rise * t - 0.32f * t * t)));
					// A triangle: widest at the base pinnae, to a point.
					widths.Add(s == segments ? 0f : p.Width * (1f - t) * (s == 0 ? 0.85f : 1f));
				}
				Color32 frond = PlantParts.C32(colour, 1f);
				Ribbon(mesh, spine, widths, Vector3.Cross(Vector3.up, dir), FoliageCell.Frond, t => PlantParts.C32(colour * Mathf.Lerp(0.85f, 1.05f, t), 1f),
					stalk + length * 0.4f, 0.4f, (Vector3.up * 0.85f + dir * 0.15f).normalized);
			}
		}

		/// <summary>
		/// Hart's-tongue: a shuttlecock of undivided, glossy strap fronds from one crown, each narrow at its stalk,
		/// widest past the middle and tapering to a point, arching out over the rock it grows in.
		/// </summary>
		private static void Hartstongue(MeshBuilder mesh, in DetailPlant p, DeterministicRNG rng)
		{
			int segments = Mathf.Max(4, p.Segments);
			(int fronds, int core) = VegetationMeshes.Spared(p.Count);
			for (int f = 0; f < fronds; f++)
			{
				if (!Fits(mesh, StripCost(segments, true)))
				{
					break;
				}
				float yaw = (f + rng.Range(-0.3f, 0.3f)) * Mathf.PI * 2f / fronds;
				Vector3 dir = Flat(yaw);
				Vector3 root = dir * p.Radius;
				VegetationMeshes.Part(mesh, f, fronds, core, root, PlantPart.Frond, 580);
				float length = p.Height * rng.Range(0.85f, 1.25f);
				float elevation = Mathf.Deg2Rad * rng.Range(35f, 68f);
				float droop = rng.Range(0.35f, 0.6f);
				Color colour = Pick(in p, rng);
				var spine = new List<Vector3>();
				var widths = new List<float>();
				for (int s = 0; s <= segments; s++)
				{
					float t = (float)s / segments;
					spine.Add(root + dir * (length * t * Mathf.Cos(elevation)) + Vector3.up * (length * (Mathf.Sin(elevation) * t - droop * t * t)));
					float shape = s == segments ? 0f : Mathf.Sin(Mathf.PI * Mathf.Pow(t, 0.8f)) * 0.85f + 0.15f;
					widths.Add(s == 0 ? p.Width * 0.2f : p.Width * shape);
				}
				Ribbon(mesh, spine, widths, Vector3.Cross(Vector3.up, dir), FoliageCell.Strap, t => PlantParts.C32(colour * Mathf.Lerp(0.75f, 1.05f, t), 1f),
					length * 0.5f, 0.3f, (Vector3.up * 0.7f + dir * 0.3f).normalized);
			}
		}

		// ── Herbs (the shrub and dry shrub kinds' variants) ───────────

		/// <summary>A shrub kind's variant: ruderal herbs, samphire or horsetail; the legacy leafy dome for 0.</summary>
		internal static void Herb(MeshBuilder mesh, in DetailPlant p, DeterministicRNG rng)
		{
			switch (p.Variant)
			{
				case FloraVariant.ShrubHerb: Ruderal(mesh, in p, rng); break;
				case FloraVariant.ShrubSucculent: Samphire(mesh, in p, rng); break;
				case FloraVariant.ShrubHorsetail: Horsetail(mesh, in p, rng); break;
				default: VegetationMeshes.Shrub(mesh, in p, rng, FoliageCell.SmallLeaves, 1f); break;
			}
		}

		/// <summary>
		/// Nettle and dock, the weeds of middens and wall feet: upright stems with leaves in opposite pairs, each pair
		/// turned a quarter from the last (decussate, as a nettle's are) and smaller toward the top, over a few long
		/// broad dock leaves lying near the ground.
		/// </summary>
		private static void Ruderal(MeshBuilder mesh, in DetailPlant p, DeterministicRNG rng)
		{
			var crown = new Vector3(0f, p.Height * 0.4f, 0f);
			(int stems, int core) = VegetationMeshes.Spared(p.Count);
			const int nodes = 3;
			for (int s = 0; s < stems; s++)
			{
				if (!Fits(mesh, StripCost(2, false) + nodes * 4))
				{
					break;
				}
				Vector3 root = Disc(rng, p.Radius * 0.6f);
				VegetationMeshes.Part(mesh, s, stems, core, root, PlantPart.Frond, 740);
				float height = p.Height * rng.Range(0.7f, 1.1f);
				float yaw = rng.NextFloat() * Mathf.PI * 2f;
				Vector3 top = root + Vector3.up * height + Flat(yaw) * (height * rng.Range(0.03f, 0.12f));
				Color colour = Pick(in p, rng);
				Stem(mesh, root, top, Vector3.zero, 0.012f, 0.007f, colour * 0.8f, 2, false, 0.05f);
				float turn = rng.NextFloat() * 180f;
				for (int n = 0; n < nodes; n++)
				{
					float t = 0.3f + 0.25f * n;
					Vector3 at = Vector3.Lerp(root, top, t);
					float length = p.Width * (1.1f - 0.45f * t) * rng.Range(0.85f, 1.15f);
					for (int side = 0; side < 2; side++)
					{
						Vector3 outward = Quaternion.AngleAxis(turn + n * 90f + side * 180f, Vector3.up) * Vector3.right;
						Vector3 dir = (outward * Mathf.Cos(Mathf.Deg2Rad * 15f) + Vector3.down * Mathf.Sin(Mathf.Deg2Rad * 15f)).normalized;
						PlantParts.SprayCard(mesh, 0, at, dir, length, length * 0.7f, rng.Range(-25f, 25f), FoliageCell.BroadLeaves,
							PlantParts.C32(colour * rng.Range(0.9f, 1.05f), 1f), new Vector2(height * t * 0.5f, 0.1f), new Vector2(height * t, 0.5f), crown, 0.6f);
					}
				}
			}
			// The basal leaves: a dock's long blades on the ground under the stems.
			int basal = Mathf.Max(2, p.Count / 2);
			for (int k = 0; k < basal; k++)
			{
				if (!Fits(mesh, 2))
				{
					break;
				}
				VegetationMeshes.Part(mesh, stems + k, stems + basal, stems + basal, Vector3.zero, PlantPart.Leaf, 745);
				float yaw = (k + rng.Range(-0.3f, 0.3f)) * Mathf.PI * 2f / basal;
				Vector3 dir = Direction(yaw, Mathf.Deg2Rad * rng.Range(8f, 22f));
				float length = p.Width * rng.Range(1.6f, 2.2f);
				PlantParts.SprayCard(mesh, 0, Vector3.up * 0.02f, dir, length, length * 0.45f, rng.Range(-15f, 15f), FoliageCell.BroadLeaves,
					PlantParts.C32(Pick(in p, rng) * 0.9f, 1f), new Vector2(0f, 0.05f), new Vector2(0.05f, 0.3f), crown, 0.5f);
			}
		}

		/// <summary>
		/// Samphire (glasswort): finger-thick jointed succulent stems branching from the base, constricted at every joint,
		/// green where young and reddening toward the tips as the salt marsh does in late summer. Solid tubes on the
		/// atlas's solid cell.
		/// </summary>
		private static void Samphire(MeshBuilder mesh, in DetailPlant p, DeterministicRNG rng)
		{
			var path = new List<Vector3>();
			var radii = new List<float>();
			var colours = new List<Color32>();
			var wind = new List<Vector2>();
			Color red = Accent(in p, 0, new Color(0.66f, 0.25f, 0.22f));
			(int stems, int core) = VegetationMeshes.Spared(p.Count, 1.15f);
			float plantHeight = Mathf.Max(0.01f, p.Height);
			Color green = p.ColourA, young = p.ColourB;
			void Joint(Vector3 a, Vector3 b, float radius, float redness, float height)
			{
				path.Clear(); radii.Clear(); colours.Clear(); wind.Clear();
				// Swollen segments pinched at the joint between them, and a blunt tip.
				Vector3 mid = Vector3.Lerp(a, b, 0.5f);
				Vector3 along = (b - a).normalized;
				path.Add(a); path.Add(mid); path.Add(b); path.Add(b + along * Mathf.Max(0.012f, Vector3.Distance(a, b) * 0.12f));
				radii.Add(radius); radii.Add(radius * 0.78f); radii.Add(radius); radii.Add(0f);
				for (int i = 0; i < path.Count; i++)
				{
					float t = (float)i / (path.Count - 1);
					float y = Mathf.Clamp01(path[i].y / plantHeight);
					colours.Add(PlantParts.C32(Color.Lerp(green, young, t) * Mathf.Lerp(0.75f, 1f, y) * (1f - redness * y) + red * (redness * y), 1f));
					wind.Add(new Vector2(y * y * height, 0f));
				}
				PlantParts.Tube(mesh, 0, path, radii, 4, 1f, colours, wind, uvMap: SolidUV);
			}
			for (int s = 0; s < stems; s++)
			{
				if (!Fits(mesh, TubeCost(4, 4, true) * 2))
				{
					break;
				}
				Vector3 root = Disc(rng, p.Radius) + Vector3.down * 0.01f;
				VegetationMeshes.Part(mesh, s, stems, core, root, PlantPart.Limb, 720);
				float height = p.Height * rng.Range(0.6f, 1.1f);
				float yaw = rng.NextFloat() * Mathf.PI * 2f;
				Vector3 dir = Direction(yaw, Mathf.Deg2Rad * rng.Range(60f, 85f));
				Vector3 top = root + dir * height;
				float redness = rng.Range(0.25f, 0.9f);
				float radius = p.Width * rng.Range(0.8f, 1.15f);
				Joint(root, top, radius, redness, height);
				// One side shoot from low on the stem, rising more steeply.
				Vector3 at = Vector3.Lerp(root, top, rng.Range(0.3f, 0.5f));
				Vector3 branch = Direction(yaw + rng.Range(0.8f, 2.2f) * (rng.NextFloat() < 0.5f ? -1f : 1f), Mathf.Deg2Rad * rng.Range(55f, 80f));
				Joint(at, at + branch * (height * rng.Range(0.45f, 0.7f)), radius * 0.8f, redness, height);
			}
		}

		/// <summary>
		/// Horsetail: a colony of slender jointed green stems, each ringed at its nodes with a whorl of fine branches
		/// standing out like a bottle-brush, the whorls smaller toward the top.
		/// </summary>
		private static void Horsetail(MeshBuilder mesh, in DetailPlant p, DeterministicRNG rng)
		{
			(int stems, int core) = VegetationMeshes.Spared(p.Count, 1.2f);
			const int whorls = 3;
			for (int s = 0; s < stems; s++)
			{
				if (!Fits(mesh, StripCost(3, true) + whorls * 2))
				{
					break;
				}
				Vector3 root = Disc(rng, p.Radius);
				VegetationMeshes.Part(mesh, s, stems, core, root, PlantPart.Frond, 760);
				float height = p.Height * rng.Range(0.7f, 1.1f);
				Vector3 top = root + Vector3.up * height + Flat(rng.NextFloat() * Mathf.PI * 2f) * (height * rng.Range(0.02f, 0.12f));
				Color colour = Pick(in p, rng);
				Stem(mesh, root, top, Vector3.zero, 0.008f, 0.005f, colour, 3, true, 0.1f);
				for (int w = 0; w < whorls; w++)
				{
					float t = 0.32f + 0.2f * w;
					Vector3 at = Vector3.Lerp(root, top, t);
					float size = p.Width * (1.15f - t) * rng.Range(0.85f, 1.15f);
					Quaternion turn = Quaternion.AngleAxis(rng.NextFloat() * 360f, Vector3.up);
					// Tipped up a little: the branches angle upward from the node.
					Vector3 right = turn * Vector3.right * size;
					Vector3 across = (turn * Vector3.forward * Mathf.Cos(0.35f) + Vector3.up * Mathf.Sin(0.35f)) * size;
					var wind = new Vector2(height * t, 0.3f);
					PlantParts.Card(mesh, 0, at, right, across, FoliageCell.NeedleSpray, PlantParts.C32(colour * 1.05f, 1f), wind, wind, Vector3.up);
				}
			}
		}

		/// <summary>A dry shrub kind's variant: the tumbleweed's ball; the legacy dead dome for 0.</summary>
		internal static void DryShrub(MeshBuilder mesh, in DetailPlant p, DeterministicRNG rng)
		{
			if (p.Variant != FloraVariant.DryShrubBall)
			{
				VegetationMeshes.Shrub(mesh, in p, rng, FoliageCell.Twigs, 0f);
				return;
			}
			// Russian thistle once it has died and dried: a near-round ball of stiff interlaced twigs, tan to straw,
			// resting on the ground on its underside (sunk a few centimetres, so it touches rather than balances).
			float radius = p.Height * 0.5f;
			var centre = new Vector3(0f, radius * 0.92f, 0f);
			(int sprays, int core) = VegetationMeshes.Spared(p.Count);
			for (int k = 0; k < sprays; k++)
			{
				if (!Fits(mesh, 4))
				{
					break;
				}
				float y = 1f - 2f * (k + 0.5f) / sprays;
				float ring = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
				float phi = k * 2.39996323f;
				var d = new Vector3(Mathf.Cos(phi) * ring, y, Mathf.Sin(phi) * ring);
				Vector3 root = centre + d * (radius * rng.Range(0.2f, 0.5f));
				VegetationMeshes.Part(mesh, k, sprays, core, root, PlantPart.Leaf, 780);
				Vector3 dir = (d + new Vector3(rng.Range(-0.4f, 0.4f), rng.Range(-0.4f, 0.4f), rng.Range(-0.4f, 0.4f))).normalized;
				float length = radius * rng.Range(0.5f, 0.7f);
				float width = p.Width * rng.Range(0.8f, 1.2f);
				Color32 colour = PlantParts.C32(Pick(in p, rng), 0f);
				float roll = rng.Range(0f, 180f);
				var windRoot = new Vector2(0.02f, 0.05f);
				var windTip = new Vector2(0.05f, 0.2f);
				PlantParts.SprayCard(mesh, 0, root, dir, length, width, roll, FoliageCell.Twigs, colour, windRoot, windTip, centre, 0.5f);
				PlantParts.SprayCard(mesh, 0, root, dir, length, width, roll + 90f, FoliageCell.Twigs, colour, windRoot, windTip, centre, 0.5f);
			}
		}

		// ── Broad herbs ───────────────────────────────────────────────

		/// <summary>
		/// Monstera / elephant-ear (<i>Colocasia</i>, <i>Alocasia</i>): a crown of long petioles rising from one root,
		/// each carrying a great paddle leaf that hangs tip-down and outward (elephant-ear) or is held out near level
		/// (monstera), bent along its midrib under its own weight. The petioles are the frond parts the plant variation
		/// drops and swings.
		/// </summary>
		private static void BroadHerb(MeshBuilder mesh, in DetailPlant p, DeterministicRNG rng)
		{
			(int leaves, int core) = VegetationMeshes.Spared(p.Count);
			int segments = Mathf.Clamp(p.Segments, 2, 4);
			for (int k = 0; k < leaves; k++)
			{
				if (!Fits(mesh, StripCost(2, false) + StripCost(segments, false)))
				{
					break;
				}
				float yaw = (k + rng.Range(-0.35f, 0.35f)) * Mathf.PI * 2f / leaves;
				Vector3 dir = Flat(yaw);
				Vector3 root = dir * (p.Radius * rng.Range(0.1f, 0.4f));
				VegetationMeshes.Part(mesh, k, leaves, core, root, PlantPart.Frond, 900);
				float petiole = p.Height * rng.Range(0.5f, 0.85f);
				float elevation = Mathf.Deg2Rad * rng.Range(50f, 78f);
				Vector3 end = root + Direction(yaw, elevation) * petiole;
				Color colour = Pick(in p, rng);
				Stem(mesh, root, end, Vector3.up * (petiole * 0.08f), 0.022f, 0.014f, colour * 0.9f, 2, false, 0.05f);

				// The blade: from the petiole's tip outward and down, curving over along its midrib.
				float length = p.Width * rng.Range(0.8f, 1.2f);
				float tilt = Mathf.Deg2Rad * rng.Range(-55f, -10f);
				Vector3 along = Direction(yaw + rng.Range(-0.25f, 0.25f), tilt);
				var spine = new List<Vector3>();
				var widths = new List<float>();
				for (int s = 0; s <= segments; s++)
				{
					float t = (float)s / segments;
					spine.Add(end + along * (length * t) + Vector3.down * (length * 0.18f * t * t));
					widths.Add(length * 0.8f);
				}
				Vector3 side = Vector3.Cross(Vector3.up, along);
				Ribbon(mesh, spine, widths, side, FoliageCell.Paddle, t => PlantParts.C32(colour * Mathf.Lerp(0.85f, 1.05f, t), 1f),
					petiole + length * 0.3f, 0.35f, (Vector3.up * 0.8f + dir * 0.2f).normalized);
			}
		}

		// ── Cacti ─────────────────────────────────────────────────────
		// Cacti wear the cactus bark (DetailKindTraits.BarkFamily): their UVs are bark metres, their vertex colour a tint
		// on it (white = the bark as drawn), alpha 0 (no seasonal tint), and they hold still (no wind weights).

		/// <summary>Bark UVs for a lathed cactus solid: round once per <paramref name="round"/> tiles, along in bark metres.</summary>
		private static Func<float, float, Vector2> CactusUV(float round, float length) => (u, s) => new Vector2(u * round, s * length / 0.5f);

		/// <summary>
		/// Prickly pear (<i>Opuntia</i>): flat oval pads, each budding one or two more from its upper rim, turned a little
		/// in its own plane and out of it, so the clump climbs as a zig-zag of paddles about a metre high; a few
		/// magenta fruits (tunas) on the top pads' rims.
		/// </summary>
		private static void PadCactus(MeshBuilder mesh, in DetailPlant p, DeterministicRNG rng)
		{
			Fixed(mesh);
			const int sides = 7;
			float padLength = Mathf.Max(0.1f, p.Height * 0.28f);
			float padWidth = Mathf.Max(0.06f, p.Width);
			// Lens profile along the pad: narrow at its foot, widest just above the middle, rounded top.
			Vector2[] profile = { new Vector2(0.18f, 0f), new Vector2(0.78f, 0.16f), new Vector2(1f, 0.52f), new Vector2(0.72f, 0.88f), new Vector2(0.06f, 1f) };
			int padCost = LatheCost(profile.Length, sides);
			var tops = new List<(Vector3 at, Quaternion turn, float length, float width)>();
			var queue = new Queue<(Vector3 at, float yaw, float tilt, float roll, float length, int generation)>();
			int bases = Mathf.Max(1, p.Count);
			for (int b = 0; b < bases; b++)
			{
				queue.Enqueue((Disc(rng, p.Radius * 0.4f) + Vector3.down * 0.05f, rng.NextFloat() * 360f, rng.Range(-18f, 18f), rng.Range(-10f, 10f), padLength * rng.Range(0.95f, 1.1f), 0));
			}
			while (queue.Count > 0 && Fits(mesh, padCost))
			{
				var pad = queue.Dequeue();
				float width = padWidth * (pad.length / padLength) * rng.Range(0.9f, 1.05f);
				Quaternion turn = Quaternion.AngleAxis(pad.yaw, Vector3.up) * Quaternion.AngleAxis(pad.roll, Vector3.right) * Quaternion.AngleAxis(pad.tilt, Vector3.forward);
				var scaled = new Vector2[profile.Length];
				for (int i = 0; i < profile.Length; i++)
				{
					scaled[i] = new Vector2(profile[i].x * width * 0.5f, profile[i].y * pad.length);
				}
				// Flattened across the pad's face to about a tenth of its width.
				Matrix4x4 place = Matrix4x4.TRS(pad.at, turn, new Vector3(1f, 1f, 0.2f));
				float shade = rng.Range(0.88f, 1.04f);
				Color32 skin = PlantParts.C32(new Color(shade, shade * rng.Range(0.98f, 1.03f), shade * 0.94f), 0f);
				Solid(mesh, scaled, sides, place, (s, u) => skin, null, CactusUV(2f, pad.length));
				tops.Add((pad.at, turn, pad.length, width));
				if (pad.generation >= 3)
				{
					continue;
				}
				int children = pad.generation == 0 ? 2 : rng.NextFloat() < 0.6f ? 1 : 2;
				for (int k = 0; k < children; k++)
				{
					float across = children == 1 ? rng.Range(-0.2f, 0.2f) : (k == 0 ? -0.32f : 0.32f);
					Vector3 at = pad.at + turn * new Vector3(across * width, pad.length * 0.86f, 0f);
					float lean = (children == 1 ? rng.Range(-25f, 25f) : (k == 0 ? 1f : -1f) * rng.Range(18f, 42f));
					queue.Enqueue((at, pad.yaw + rng.Range(-25f, 25f), pad.tilt + lean, pad.roll + rng.Range(-15f, 15f), pad.length * rng.Range(0.82f, 1f), pad.generation + 1));
				}
			}
			// Fruits on the rims of the highest pads.
			tops.Sort((a, b) => (b.at + b.turn * Vector3.up * b.length).y.CompareTo((a.at + a.turn * Vector3.up * a.length).y));
			Color fruit = Accent(in p, 0, new Color(0.62f, 0.15f, 0.3f));
			Vector2[] fruitProfile = EllipsoidProfile(0.022f, 0.06f, 2);
			for (int i = 0; i < tops.Count && i < 4; i++)
			{
				if (!Fits(mesh, LatheCost(fruitProfile.Length, 5)))
				{
					break;
				}
				var pad = tops[i];
				float across = rng.Range(-0.38f, 0.38f);
				float up = 0.98f - across * across * 0.9f;
				Vector3 at = pad.at + pad.turn * new Vector3(across * pad.width, pad.length * up, 0f);
				Color32 colour = PlantParts.C32(fruit * rng.Range(0.85f, 1.05f), 0f);
				Solid(mesh, fruitProfile, 5, Matrix4x4.TRS(at + pad.turn * Vector3.up * 0.02f, pad.turn, Vector3.one), (s, u) => colour, null, CactusUV(1f, 0.06f));
			}
		}

		/// <summary>
		/// Cholla (<i>Cylindropuntia</i>, teddy-bear and jumping cholla): a short dark trunk of old dead joints, then
		/// branching chains of cylindrical joints set at angles, densely spined so the living tips glow straw-gold.
		/// </summary>
		private static void Cholla(MeshBuilder mesh, in DetailPlant p, DeterministicRNG rng)
		{
			Fixed(mesh);
			var path = new List<Vector3>();
			var radii = new List<float>();
			var colours = new List<Color32>();
			var wind = new List<Vector2>();
			Color gold = Accent(in p, 0, new Color(0.95f, 0.9f, 0.68f));
			Color dark = Accent(in p, 1, new Color(0.45f, 0.4f, 0.34f));
			void Joint(Vector3 a, Vector3 dir, float length, float radius, int ringSides, float age, bool terminal)
			{
				path.Clear(); radii.Clear(); colours.Clear(); wind.Clear();
				Vector3 bend = PlantParts.Perpendicular(dir) * (length * rng.Range(-0.06f, 0.06f));
				Vector3 b = a + dir * length;
				path.Add(a - dir * (radius * 0.4f));
				path.Add(Vector3.Lerp(a, b, 0.5f) + bend);
				path.Add(b);
				radii.Add(radius * 0.92f); radii.Add(radius * 1.05f); radii.Add(radius * 0.95f);
				if (terminal)
				{
					path.Add(b + dir * (radius * 0.7f));
					radii.Add(0f);
				}
				Color c = Color.Lerp(gold, dark, age);
				for (int i = 0; i < path.Count; i++)
				{
					colours.Add(PlantParts.C32(c, 0f));
					wind.Add(Vector2.zero);
				}
				PlantParts.Tube(mesh, 0, path, radii, ringSides, 0.4f, colours, wind);
			}
			float height = p.Height;
			float trunk = height * rng.Range(0.25f, 0.35f);
			float trunkRadius = p.Width * 1.4f;
			Vector3 trunkTop = new Vector3(rng.Range(-0.03f, 0.03f), trunk, rng.Range(-0.03f, 0.03f));
			Joint(Vector3.down * 0.06f, (trunkTop - Vector3.down * 0.06f).normalized, trunk + 0.06f, trunkRadius, 6, 1f, false);
			var tips = new Queue<(Vector3 at, Vector3 dir, int generation)>();
			int arms = Mathf.Max(2, p.Count);
			float yaw0 = rng.NextFloat() * Mathf.PI * 2f;
			for (int a = 0; a < arms; a++)
			{
				tips.Enqueue((trunkTop, Direction(yaw0 + (a + rng.Range(-0.3f, 0.3f)) * Mathf.PI * 2f / arms, Mathf.Deg2Rad * rng.Range(25f, 60f)), 1));
			}
			const int sides = 5;
			while (tips.Count > 0)
			{
				var tip = tips.Dequeue();
				bool terminal = tip.generation >= 3 || (tip.generation == 2 && rng.NextFloat() < 0.35f);
				if (!Fits(mesh, TubeCost(terminal ? 4 : 3, sides, terminal)))
				{
					break;
				}
				float length = height * rng.Range(0.13f, 0.19f);
				float radius = p.Width * rng.Range(0.9f, 1.1f) * (1.1f - 0.08f * tip.generation);
				Joint(tip.at, tip.dir, length, radius, sides, Mathf.Clamp01(0.45f - 0.2f * tip.generation), terminal);
				if (terminal)
				{
					continue;
				}
				Vector3 end = tip.at + tip.dir * length;
				int children = tip.generation == 1 ? 2 : 1 + (rng.NextFloat() < 0.4f ? 1 : 0);
				float yaw = Mathf.Atan2(tip.dir.z, tip.dir.x);
				for (int k = 0; k < children; k++)
				{
					float spread = children == 1 ? rng.Range(-0.6f, 0.6f) : (k == 0 ? -1f : 1f) * rng.Range(0.4f, 1.1f);
					tips.Enqueue((end, Direction(yaw + spread, Mathf.Deg2Rad * rng.Range(10f, 70f)), tip.generation + 1));
				}
			}
		}

		/// <summary>
		/// Column clusters: an organ pipe (<i>Stenocereus thurberi</i>) — many columns rising from one base, curving out
		/// and then straight up, round-topped — or a candelabra euphorbia (<i>E. ingens</i>) — a short thick trunk
		/// whose four-winged arms swing out and turn up into a candelabrum.
		/// </summary>
		private static void ColumnCluster(MeshBuilder mesh, in DetailPlant p, DeterministicRNG rng)
		{
			Fixed(mesh);
			var path = new List<Vector3>();
			var radii = new List<float>();
			var colours = new List<Color32>();
			var wind = new List<Vector2>();
			void Column(IList<Vector3> points, IList<float> r, int ringSides, int ribs, float ribDepth, Color tint)
			{
				path.Clear(); radii.Clear(); colours.Clear(); wind.Clear();
				for (int i = 0; i < points.Count; i++)
				{
					path.Add(points[i]);
					radii.Add(r[i]);
					colours.Add(PlantParts.C32(tint, 0f));
					wind.Add(Vector2.zero);
				}
				PlantParts.Tube(mesh, 0, path, radii, ringSides, 0.5f, colours, wind, ribs: ribs, ribDepth: ribDepth, analyticNormals: ribs == 0);
			}
			Color tintA = Accent(in p, 0, Color.white), tintB = Accent(in p, 1, new Color(0.9f, 0.95f, 0.88f));
			if (p.Variant == FloraVariant.Euphorbia)
			{
				const int sides = 8, ribs = 4;
				float trunk = p.Height * rng.Range(0.22f, 0.32f);
				float trunkRadius = p.Width * 1.6f;
				Column(new[] { Vector3.down * 0.06f, Vector3.up * (trunk * 0.5f), Vector3.up * trunk, Vector3.up * (trunk + trunkRadius * 0.6f) },
					new[] { trunkRadius * 1.1f, trunkRadius, trunkRadius * 0.92f, 0f }, sides, ribs, 0.3f, Color.Lerp(tintA, tintB, 0.5f) * 0.85f);
				int arms = Mathf.Max(3, p.Count);
				float yaw0 = rng.NextFloat() * Mathf.PI * 2f;
				for (int a = 0; a < arms; a++)
				{
					if (!Fits(mesh, TubeCost(5, sides, true)))
					{
						break;
					}
					float yaw = yaw0 + (a + rng.Range(-0.3f, 0.3f)) * Mathf.PI * 2f / arms;
					Vector3 outward = Flat(yaw);
					Vector3 start = Vector3.up * (trunk * rng.Range(0.82f, 0.98f)) + outward * (trunkRadius * 0.5f);
					float reach = p.Height * rng.Range(0.1f, 0.2f);
					float top = p.Height * rng.Range(0.75f, 1f);
					float r = p.Width * rng.Range(0.85f, 1.05f);
					Column(new[]
						{
							start,
							start + outward * (reach * 0.7f) + Vector3.up * (reach * 0.1f),
							start + outward * reach + Vector3.up * (reach * 0.6f),
							new Vector3(start.x + outward.x * reach * 1.05f, top - r * 1.1f, start.z + outward.z * reach * 1.05f),
							new Vector3(start.x + outward.x * reach * 1.06f, top, start.z + outward.z * reach * 1.06f),
							},
							// Blunt, rounded tips, as the real arms' are.
							new[] { r, r, r * 0.97f, r * 0.85f, 0f }, sides, ribs, 0.35f, Color.Lerp(tintA, tintB, rng.NextFloat()));
				}
				return;
			}
			{
				const int sides = 7;
				int columns = Mathf.Max(3, p.Count);
				float yaw0 = rng.NextFloat() * Mathf.PI * 2f;
				for (int c = 0; c < columns; c++)
				{
					if (!Fits(mesh, TubeCost(5, sides, true)))
					{
						break;
					}
					float yaw = yaw0 + (c + rng.Range(-0.35f, 0.35f)) * Mathf.PI * 2f / columns;
					Vector3 outward = Flat(yaw);
					Vector3 foot = outward * (p.Radius * rng.Range(0.15f, 0.5f)) + Vector3.down * 0.06f;
					float height = p.Height * rng.Range(0.55f, 1f);
					float r = p.Width * rng.Range(0.85f, 1.1f);
					float splay = p.Radius * rng.Range(0.3f, 0.9f);
					Vector3 bend = foot + outward * splay;
					Column(new[]
						{
							foot,
							foot + outward * (splay * 0.6f) + Vector3.up * (height * 0.12f),
							new Vector3(bend.x, height * 0.4f, bend.z),
							new Vector3(bend.x, height - r * 0.9f, bend.z),
							new Vector3(bend.x, height, bend.z),
						},
						new[] { r * 0.85f, r, r, r * 0.8f, 0f }, sides, 0, 0f, Color.Lerp(tintA, tintB, rng.NextFloat()));
				}
			}
		}

		// ── Rosettes ──────────────────────────────────────────────────

		/// <summary>
		/// Rosettes of thick tapered leaves: agave, yucca, bromeliad, and the two later forms, echium (tower of jewels)
		/// and the pitcher plant. Leaves spiral out by the golden angle, the outer ones longest and lowest.
		/// </summary>
		private static void Rosette(MeshBuilder mesh, in DetailPlant p, DeterministicRNG rng)
		{
			switch (p.Variant)
			{
				case FloraVariant.RosettePitcher: PitcherPlant(mesh, in p, rng); return;
				case FloraVariant.RosetteYucca: RosetteLeaves(mesh, in p, rng, 18f, 82f, 0.02f, 0.25f, true); Yucca(mesh, in p, rng); return;
				case FloraVariant.RosetteBromeliad: RosetteLeaves(mesh, in p, rng, 22f, 76f, 0.16f, 0.5f, false); return;
				case FloraVariant.RosetteEchium: RosetteLeaves(mesh, in p, rng, 14f, 60f, 0.1f, 0.3f, false); Echium(mesh, in p, rng); return;
				default: RosetteLeaves(mesh, in p, rng, 12f, 78f, 0.06f, 0.45f, true); return;
			}
		}

		/// <summary>
		/// The rosette's leaves: <see cref="DetailPlant.Count"/> of them, by the golden angle from outer to inner,
		/// rising from <paramref name="outerElevation"/> to <paramref name="innerElevation"/>; channelled (agave's
		/// thick blue-grey leaves, yucca's daggers), drooping by <paramref name="droop"/> (a bromeliad's recurved straps).
		/// A spined leaf ends in a dark terminal spine. The inner leaves take the plant's first accent when it has one
		/// (a bromeliad's red heart).
		/// </summary>
		private static void RosetteLeaves(MeshBuilder mesh, in DetailPlant p, DeterministicRNG rng, float outerElevation, float innerElevation, float droop, float channel, bool spined)
		{
			int segments = Mathf.Clamp(p.Segments, 2, 4);
			int cost = (segments - 1) * 4 + 2;
			(int leaves, int core) = VegetationMeshes.Spared(p.Count, 1.1f);
			float yaw0 = rng.NextFloat() * Mathf.PI * 2f;
			bool heart = p.Accents != null && p.Accents.Length > 0 && p.Variant == FloraVariant.RosetteBromeliad;
			Color spine = new Color(0.25f, 0.2f, 0.15f);
			Color agaveA = p.ColourA, agaveB = p.ColourB;
			for (int k = 0; k < leaves; k++)
			{
				if (!Fits(mesh, cost))
				{
					break;
				}
				float inner = leaves > 1 ? (float)k / (leaves - 1) : 0f;
				float yaw = yaw0 + k * 2.39996323f;
				float elevation = Mathf.Deg2Rad * Mathf.Lerp(outerElevation, innerElevation, inner) * rng.Range(0.9f, 1.1f);
				Vector3 dir = Direction(yaw, elevation);
				Vector3 root = Flat(yaw) * (p.Radius * (1f - inner) * 0.6f) + Vector3.down * 0.03f;
				VegetationMeshes.Part(mesh, k, leaves, core, root, PlantPart.Leaf, 940);
				float length = p.Height * Mathf.Lerp(1.1f, 0.65f, inner) * rng.Range(0.85f, 1.1f) / Mathf.Max(0.35f, Mathf.Sin(Mathf.Lerp(outerElevation, innerElevation, 0.5f) * Mathf.Deg2Rad));
				length = Mathf.Min(length, p.Height * 1.6f);
				float width = p.Width * Mathf.Lerp(1f, 0.75f, inner) * rng.Range(0.85f, 1.1f);
				Color colour = Color.Lerp(agaveA, agaveB, rng.NextFloat());
				if (heart && inner > 0.62f)
				{
					colour = Color.Lerp(colour, Accent(in p, k, colour), Mathf.InverseLerp(0.62f, 0.9f, inner));
				}
				float alpha = heart && inner > 0.62f ? 0.3f : 1f;
				Color c0 = colour * 0.8f, c1 = colour;
				KeeledLeaf(mesh, root, dir, length, width, channel, droop, segments,
					t => t < 0.12f ? 0.85f : Mathf.Pow(1f - t, 0.75f) * 1.05f,
					t => PlantParts.C32(spined && t > 0.99f ? spine : Color.Lerp(c0, c1, Mathf.Clamp01(t * 1.5f)), alpha),
					spined ? 0.05f : 0.2f);
			}
		}

		/// <summary>
		/// A yucca's flower stalk: a bare spike rising well above the leaves, hung along its upper part with cream
		/// bell flowers (the plant's first accent; head colours).
		/// </summary>
		private static void Yucca(MeshBuilder mesh, in DetailPlant p, DeterministicRNG rng)
		{
			if (!Fits(mesh, StripCost(3, false) + 8))
			{
				return;
			}
			VegetationMeshes.Part(mesh, 999, 1000, 1000, Vector3.zero, PlantPart.Frond, 950);
			float height = p.Height * rng.Range(2.2f, 2.8f);
			Vector3 top = new Vector3(rng.Range(-0.05f, 0.05f), height, rng.Range(-0.05f, 0.05f));
			Stem(mesh, Vector3.down * 0.02f, top, Vector3.zero, 0.03f, 0.012f, Color.Lerp(p.ColourA, new Color(0.55f, 0.5f, 0.35f), 0.5f), 3, false, 0.02f);
			Color bell = Accent(in p, 0, new Color(0.94f, 0.92f, 0.8f));
			for (int k = 0; k < 12 && Fits(mesh, 4); k++)
			{
				float t = Mathf.Lerp(0.58f, 0.98f, (k + rng.Range(0.1f, 0.9f)) / 12f);
				Vector3 at = Vector3.Lerp(Vector3.zero, top, t) + Flat(k * 2.4f) * (0.06f * (1.1f - t));
				float size = rng.Range(0.05f, 0.075f);
				CrossedHead(mesh, at, Vector3.up, size, size, FoliageCell.Flower, PlantParts.C32(bell * rng.Range(0.93f, 1.03f), HeadAlpha),
					new Vector2(height * t * 0.3f, 0.3f), rng.Range(0f, 90f));
			}
		}

		/// <summary>
		/// Tower of jewels: over its silver rosette, a dense tapering spike two metres tall, packed with small red
		/// flowers — a lathed cone mottled red and crimson (head colours), with flower cards set into its surface.
		/// </summary>
		private static void Echium(MeshBuilder mesh, in DetailPlant p, DeterministicRNG rng)
		{
			Fixed(mesh);
			float height = p.Height * rng.Range(4.5f, 5.5f);
			float radius = p.Radius * 0.5f;
			Color red = Accent(in p, 0, new Color(0.75f, 0.2f, 0.28f));
			Color deep = Accent(in p, 1, new Color(0.6f, 0.12f, 0.2f));
			Vector2[] profile =
			{
				new Vector2(radius * 0.7f, p.Height * 0.4f), new Vector2(radius, height * 0.25f), new Vector2(radius * 0.85f, height * 0.55f),
				new Vector2(radius * 0.45f, height * 0.82f), new Vector2(radius * 0.05f, height),
			};
			const int sides = 9;
			if (Fits(mesh, LatheCost(profile.Length, sides)))
			{
				int phase = rng.Next(1000);
				PlantParts.Lathe(mesh, Vector3.zero, profile, sides, (s, u) => 1f + 0.14f * Mathf.Sin(u * Mathf.PI * 6f + s * 17f) * Mathf.Sin(s * 23f + u * 9f),
					(s, u) => PlantParts.C32(Color.Lerp(red, deep, Hash(Mathf.RoundToInt(u * sides) * 31 + Mathf.RoundToInt(s * 8f), phase)) * Mathf.Lerp(0.8f, 1.05f, s), HeadAlpha),
					s => new Vector2(s * s * height * 0.15f, 0f));
			}
			// Flowers all over the spike, so it reads as a column of small blooms rather than a smooth cone.
			const int blooms = 44;
			for (int k = 0; k < blooms && Fits(mesh, 2); k++)
			{
				float t = Mathf.Lerp(0.15f, 0.95f, (k + 0.5f) / blooms);
				float r = radius * (t < 0.55f ? Mathf.Lerp(1f, 0.85f, t / 0.55f) : Mathf.Lerp(0.85f, 0.05f, (t - 0.55f) / 0.45f));
				float yaw = k * 2.39996323f;
				Vector3 outward = Flat(yaw);
				Vector3 at = Vector3.up * (height * t) + outward * (r * 1.02f);
				float size = rng.Range(0.07f, 0.1f);
				PlantParts.Card(mesh, 0, at, Vector3.Cross(Vector3.up, outward) * size, (Vector3.up + outward * 0.5f).normalized * size, FoliageCell.Flower,
					PlantParts.C32(Color.Lerp(red, Color.white, rng.Range(0.1f, 0.35f)), HeadAlpha), new Vector2(height * t * 0.15f, 0.2f), new Vector2(height * t * 0.15f, 0.3f), outward);
			}
		}

		/// <summary>
		/// Pitcher plants: a clump of upright hollow trumpets, narrow at the foot and flaring to an open mouth under a
		/// small hood, green below and flushed red with veins toward the top (the plant's first accent).
		/// </summary>
		private static void PitcherPlant(MeshBuilder mesh, in DetailPlant p, DeterministicRNG rng)
		{
			const int sides = 6;
			Color red = Accent(in p, 0, new Color(0.62f, 0.2f, 0.18f));
			(int pitchers, int core) = VegetationMeshes.Spared(p.Count, 1.15f);
			for (int k = 0; k < pitchers; k++)
			{
				if (!Fits(mesh, LatheCost(4, sides) + 2))
				{
					break;
				}
				Vector3 root = Disc(rng, p.Radius) + Vector3.down * 0.02f;
				VegetationMeshes.Part(mesh, k, pitchers, core, root, PlantPart.Leaf, 960);
				float height = p.Height * rng.Range(0.55f, 1.05f);
				float mouth = p.Width * rng.Range(0.8f, 1.15f);
				Vector2[] profile = { new Vector2(mouth * 0.2f, 0f), new Vector2(mouth * 0.35f, height * 0.3f), new Vector2(mouth * 0.65f, height * 0.75f), new Vector2(mouth, height) };
				Color green = Pick(in p, rng);
				float flush = rng.Range(0.4f, 1f);
				int salt = rng.Next(1000);
				int first = mesh.VertexCount;
				PlantParts.Lathe(mesh, Vector3.zero, profile, sides, null,
					(s, u) => PlantParts.C32(Color.Lerp(green, red, flush * Mathf.SmoothStep(0.3f, 1f, s) * (0.75f + 0.25f * Hash(Mathf.RoundToInt(u * sides * 2f), salt))), 1f),
					s => new Vector2(s * s * height, 0.05f * s));
				// Leaning out from the clump, a little.
				Vector3 lean = new Vector3(root.x, 0f, root.z);
				Quaternion tilt = lean.sqrMagnitude > 1e-6f ? Quaternion.AngleAxis(rng.Range(4f, 14f), Vector3.Cross(Vector3.up, lean.normalized)) : Quaternion.identity;
				mesh.Transform(Matrix4x4.TRS(root, tilt, Vector3.one), first);
				// The hood: a rounded flap leaning over the mouth from its back.
				float yaw = rng.NextFloat() * Mathf.PI * 2f;
				Vector3 back = tilt * Flat(yaw);
				Vector3 lip = root + tilt * (Vector3.up * height) - back * (mouth * 0.85f);
				Vector3 hoodUp = (tilt * Vector3.up * 0.6f + back * 0.8f).normalized;
				PlantParts.Card(mesh, 0, lip + hoodUp * (mouth * 0.45f), Vector3.Cross(hoodUp, back).normalized * (mouth * 1.3f), hoodUp * (mouth * 0.9f), FoliageCell.Solid,
					PlantParts.C32(Color.Lerp(green, red, flush), 1f), new Vector2(height, 0.1f), new Vector2(height, 0.2f), back);
			}
		}

		// ── Cushions ──────────────────────────────────────────────────

		/// <summary>
		/// Cushion plants: a low dome (a super-ellipse lathe, lumpy by its form), its surface shingled with small leaf
		/// cards so it reads as a mass of tight shoots rather than a smooth shell, and for the flowering ones flower
		/// cards set into it. Moss campion's bright green bun starred with pink; thrift's grassy hummock under pink
		/// pom-poms on bare stalks; the spiny grey-green hedgehog cushions of dry mountains; a sphagnum hummock in red,
		/// green and ochre; and a moss cushion on stone.
		/// </summary>
		private static void Cushion(MeshBuilder mesh, in DetailPlant p, DeterministicRNG rng)
		{
			int form = p.Variant;
			float radius = p.Radius, height = p.Height;
			float lumpiness = form == FloraVariant.CushionMoss ? 0.16f : form == FloraVariant.CushionSphagnum ? 0.1f : 0.06f;
			float flatness = form == FloraVariant.CushionSpiny ? 2.2f : 3f;
			float phaseA = rng.NextFloat() * Mathf.PI * 2f, phaseB = rng.NextFloat() * Mathf.PI * 2f;
			int lobes = 2 + rng.Next(3);
			float Lump(float u, float el) => 1f + lumpiness * (Mathf.Sin(lobes * u * Mathf.PI * 2f + phaseA) * 0.7f + Mathf.Sin((lobes + 2) * u * Mathf.PI * 2f + phaseB + el * 3f) * 0.3f);
			// The dome's surface by elevation (0 the rim, π/2 the crown) and turn (0..1): a super-ellipse, lumped.
			Vector3 Surface(float el, float u)
			{
				float c = Mathf.Cos(el), s = Mathf.Sin(el);
				float r = radius * Mathf.Pow(Mathf.Max(0f, c), 2f / flatness) * Lump(u, el);
				float y = height * Mathf.Pow(Mathf.Max(0f, s), 2f / flatness);
				float turn = u * Mathf.PI * 2f;
				return new Vector3(Mathf.Cos(turn) * r, y, Mathf.Sin(turn) * r);
			}
			Vector3 Normal(float el, float u)
			{
				Vector3 du = Surface(el, u + 0.01f) - Surface(el, u - 0.01f);
				Vector3 de = Surface(Mathf.Min(1.5707f, el + 0.02f), u) - Surface(Mathf.Max(0f, el - 0.02f), u);
				Vector3 n = Vector3.Cross(du, de);
				if (n.y < 0f) n = -n;
				return n.sqrMagnitude > 1e-12f ? n.normalized : Vector3.up;
			}

			// The dome.
			Fixed(mesh);
			const int sides = 12;
			float[] elevations = { -0.12f, 0.35f, 0.8f, 1.2f, 1.5708f };
			var profile = new Vector2[elevations.Length];
			for (int i = 0; i < elevations.Length; i++)
			{
				float el = Mathf.Max(0f, elevations[i]);
				// (The crown's cosine is a hair under zero in floats: clamped, or the power is NaN.)
				profile[i] = new Vector2(radius * Mathf.Pow(Mathf.Max(0f, Mathf.Cos(el)), 2f / flatness), elevations[i] < 0f ? -height * 0.15f : height * Mathf.Pow(Mathf.Max(0f, Mathf.Sin(el)), 2f / flatness));
			}
			int salt = rng.Next(1 << 20);
			Color a = p.ColourA, b = p.ColourB;
			Color third = Accent(in p, 2, b);
			bool sphagnum = form == FloraVariant.CushionSphagnum;
			Color DomeColour(float s, float u)
			{
				float h = Hash(Mathf.RoundToInt(u * sides) * 7 + Mathf.RoundToInt(s * 4f), salt);
				Color c = Color.Lerp(a, b, h);
				if (sphagnum)
				{
					// Patches, not a blend (a blend of crimson and green is orange): crimson mostly on the hummock's sunny
					// crown, green in drifts down its sides, a few ochre heads.
					float patch = 0.5f + 0.5f * Mathf.Sin(u * Mathf.PI * 2f * lobes + phaseB + s * 4f) * Mathf.Cos(u * Mathf.PI * 2f * (lobes + 1) + phaseA);
					c = patch + 0.45f * s > 0.75f ? Color.Lerp(a, a * 0.85f, h) : Color.Lerp(b, b * 0.85f, h);
					if (h > 0.88f)
					{
						c = third;
					}
				}
				return c * Mathf.Lerp(0.55f, 1f, Mathf.Clamp01(s * 1.6f));
			}
			if (Fits(mesh, LatheCost(profile.Length, sides)))
			{
				// The dome is the cushion's shaded inside between its shoots: darker than the shoots over it.
				PlantParts.Lathe(mesh, Vector3.zero, profile, sides, (s, u) => Lump(u, s * 1.57f), (s, u) => PlantParts.C32(DomeColour(s, u) * 0.7f, 1f));
			}

			// The shingles: tight shoots over the dome, tangent to it and tipped out a little.
			// Needle shoots on the spiny cushions and thrift's grassy hummock, small leaves on the rest.
			FoliageCell cell = form == FloraVariant.CushionSpiny || form == FloraVariant.CushionThrift ? FoliageCell.BushNeedle : FoliageCell.BushSmall;
			float shingle = p.Width > 0f ? p.Width : radius * 0.3f;
			int count = Mathf.Max(0, p.Count);
			(int shingles, int core) = VegetationMeshes.Spared(Mathf.Max(1, count), 1.15f);
			for (int k = 0; k < shingles && count > 0; k++)
			{
				if (!Fits(mesh, 2))
				{
					break;
				}
				// A Fibonacci spread over the upper dome.
				float y = (k + 0.5f) / shingles;
				float el = Mathf.Asin(Mathf.Clamp01(1f - y * 0.92f));
				float u = Mathf.Repeat(k * 0.381966f, 1f);
				Vector3 at = Surface(el, u);
				Vector3 n = Normal(el, u);
				VegetationMeshes.Part(mesh, k, shingles, core, at, PlantPart.Leaf, 980);
				Vector3 tangent = Vector3.Cross(n, Quaternion.AngleAxis(rng.Range(0f, 360f), n) * PlantParts.Perpendicular(n)).normalized;
				Vector3 along = (tangent * Mathf.Cos(0.45f) + n * Mathf.Sin(0.45f)).normalized;
				Vector3 across = Vector3.Cross(n, along).normalized;
				float size = shingle * rng.Range(0.8f, 1.2f);
				float height01 = Mathf.Clamp01(at.y / Mathf.Max(0.01f, height));
				Color colour = DomeColour(height01, u) * rng.Range(1f, 1.15f);
				PlantParts.Card(mesh, 0, at + n * (size * 0.12f), across * size, along * size, cell, PlantParts.C32(colour, 1f),
					new Vector2(0f, 0.05f), new Vector2(0.01f, 0.1f), n);
			}

			if (form == FloraVariant.CushionThrift)
			{
				Thrift(mesh, in p, rng, Surface);
				return;
			}
			// Flowers in the cushion's surface (moss campion's pink stars, a few on a spiny cushion).
			int flowers = p.Accents == null || p.Accents.Length == 0 || sphagnum || form == FloraVariant.CushionMoss ? 0
				: form == FloraVariant.CushionSpiny ? Mathf.Max(3, count / 8) : Mathf.Max(6, count * 3 / 4);
			float bloom = form == FloraVariant.CushionSpiny ? shingle * 0.3f : shingle * 0.55f;
			for (int k = 0; k < flowers; k++)
			{
				if (!Fits(mesh, 2))
				{
					break;
				}
				float y = (k + 0.5f) / flowers;
				float el = Mathf.Asin(Mathf.Clamp01(1f - y * 0.8f));
				float u = Mathf.Repeat(k * 0.618034f + 0.13f, 1f);
				Vector3 n = Normal(el, u);
				Vector3 at = Surface(el, u) + n * 0.012f;
				VegetationMeshes.Part(mesh, shingles + k, shingles + flowers, shingles + flowers, at, PlantPart.Leaf, 985);
				Vector3 right = PlantParts.Perpendicular(n);
				Vector3 up = Vector3.Cross(n, right).normalized;
				float size = bloom * rng.Range(0.8f, 1.2f);
				PlantParts.Card(mesh, 0, at, right * size, up * size, FoliageCell.Flower, PlantParts.C32(Accent(in p, k, Color.white), HeadAlpha),
					new Vector2(0f, 0.1f), new Vector2(0f, 0.1f), n);
			}
		}

		/// <summary>Thrift: a grassy hummock of fine leaves, and pink pom-pom heads on bare stalks standing over it.</summary>
		private static void Thrift(MeshBuilder mesh, in DetailPlant p, DeterministicRNG rng, Func<float, float, Vector3> surface)
		{
			DetailPlant leaves = p;
			leaves.Kind = DetailKind.Grass;
			leaves.Count = Mathf.Max(6, p.Count / 2);
			leaves.Height = p.Height * 1.1f;
			leaves.Width = 0.008f;
			leaves.Radius = p.Radius * 0.8f;
			leaves.Segments = 2;
			leaves.Lean = 0.7f;
			VegetationMeshes.Blades(mesh, in leaves, rng, FoliageCell.Blade);
			int heads = Mathf.Max(3, p.Count / 4);
			(int stalks, int core) = VegetationMeshes.Spared(heads, 1.2f);
			for (int k = 0; k < stalks; k++)
			{
				if (!Fits(mesh, StripCost(2, false) + 4))
				{
					break;
				}
				float u = Mathf.Repeat(k * 0.618034f, 1f);
				Vector3 root = surface(rng.Range(0.6f, 1.4f), u) * 0.8f;
				VegetationMeshes.Part(mesh, k, stalks, core, root, PlantPart.Frond, 990);
				float height = Mathf.Max(p.Height * 1.5f, 0.2f) * rng.Range(0.8f, 1.15f);
				Vector3 top = root + Vector3.up * height + new Vector3(root.x, 0f, root.z) * 0.4f;
				Stem(mesh, root, top, Vector3.zero, 0.004f, 0.003f, Color.Lerp(p.ColourA, new Color(0.5f, 0.55f, 0.35f), 0.4f), 2, false, 0.2f);
				float size = rng.Range(0.03f, 0.045f);
				CrossedHead(mesh, top + Vector3.up * (size * 0.3f), Vector3.up, size, size, FoliageCell.Flower,
					PlantParts.C32(Accent(in p, k, new Color(0.88f, 0.6f, 0.75f)) * rng.Range(0.93f, 1.05f), HeadAlpha), new Vector2(height, 0.5f), rng.Range(0f, 90f));
			}
		}

		// ── Lichens ───────────────────────────────────────────────────

		/// <summary>
		/// Fruticose lichens: reindeer lichen (<i>Cladonia</i>), pale grey-green cushions of fine branching, and the
		/// lava lichen (<i>Stereocaulon</i>) that is the first life on a cooled flow, grey-white and lower. Each is a
		/// small mat of lumps shingled with fine branching, with a clump of the branching coral's geometry
		/// (<see cref="VegetationMeshes.Coral"/>) standing out of it — the coral alone read as a few bare twigs, not a
		/// cushion. Not aquatic (its own kind), coloured by the plant's own colours, rigid (no wind weights).
		/// </summary>
		private static void Lichen(MeshBuilder mesh, in DetailPlant p, DeterministicRNG rng)
		{
			// A mat of low lumps (lathed domes in the plant's pale colours), shingled with fine branching cards so they
			// read as the dense tangle of a lichen cushion, and a clump of the branching coral's geometry rising out of the
			// largest so the tips stand proud from the side.
			Fixed(mesh);
			bool lava = p.Variant == FloraVariant.LichenLava;
			Color pale = p.ColourA, light = p.ColourB, shade = Accent(in p, 0, Color.Lerp(p.ColourA, p.ColourB, 0.5f) * 0.85f);
			const int lumps = 3, sides = 8;
			var centres = new Vector3[lumps];
			var radii = new float[lumps];
			var heights = new float[lumps];
			for (int k = 0; k < lumps; k++)
			{
				if (!Fits(mesh, LatheCost(4, sides)))
				{
					break;
				}
				centres[k] = k == 0 ? Vector3.zero : Disc(rng, p.Radius * 0.75f);
				radii[k] = p.Radius * (k == 0 ? 0.5f : rng.Range(0.3f, 0.45f));
				float h = p.Height * (k == 0 ? 0.75f : rng.Range(0.45f, 0.7f)) * (lava ? 0.8f : 1f);
				heights[k] = h;
				float r = radii[k];
				Vector2[] dome = { new Vector2(r, -0.01f), new Vector2(r * 0.9f, h * 0.55f), new Vector2(r * 0.55f, h * 0.92f), new Vector2(r * 0.02f, h) };
				int salt = rng.Next(1000);
				Solid(mesh, dome, sides, Matrix4x4.TRS(centres[k], Quaternion.identity, Vector3.one),
					(s, u) => PlantParts.C32(Color.Lerp(shade, pale, Hash(Mathf.RoundToInt(u * sides) + Mathf.RoundToInt(s * 3f) * 11, salt)) * Mathf.Lerp(0.45f, 0.7f, s), 0f),
					(s, u) => 1f + 0.12f * Mathf.Sin(u * Mathf.PI * 2f * 3f + salt));
			}
			// The branching shingles, over the lumps' tops and sides.
			int cards = Mathf.Max(12, p.Count * 20);
			for (int k = 0; k < cards && Fits(mesh, 2 + 120); k++)
			{
				int lump = k % lumps;
				float el = Mathf.Lerp(0.25f, 1.45f, Hash(k, 31));
				float yaw = Hash(k, 37) * Mathf.PI * 2f;
				Vector3 n = Direction(yaw, el);
				float r = radii[lump];
				// On the lump's surface (its dome is near an ellipsoid of its radius and height).
				var at = centres[lump] + new Vector3(n.x * r * 0.95f, Mathf.Max(0f, n.y) * heights[lump] * 0.95f, n.z * r * 0.95f);
				float size = r * rng.Range(0.75f, 1.05f);
				Vector3 right = PlantParts.Perpendicular(n) * size;
				Vector3 up = Vector3.Cross(n, right.normalized) * size;
				// The needle-mass cell: a dense tangle of fine tips, as a lichen cushion's branchlets read from a step away.
				PlantParts.Card(mesh, 0, at + n * 0.004f, right, up, FoliageCell.BushNeedle, PlantParts.C32(Color.Lerp(pale, light, rng.NextFloat()), 0f), Vector2.zero, Vector2.zero, n);
			}
			// The proud tips: one clump of fine branches.
			if (Fits(mesh, 120))
			{
				DetailPlant clump = p;
				clump.Kind = DetailKind.Coral;
				clump.Count = 2;
				clump.Height = p.Height * 0.8f;
				clump.Accents = new[] { pale, light };
				int first = mesh.VertexCount;
				VegetationMeshes.Coral(mesh, in clump, rng);
				mesh.Transform(Matrix4x4.TRS(centres[0] + Vector3.up * (p.Height * 0.2f), Quaternion.AngleAxis(rng.Range(0f, 360f), Vector3.up), Vector3.one), first);
			}
		}

		// ── Creepers ──────────────────────────────────────────────────

		/// <summary>
		/// Ground creepers. Ivy: a carpet of dark glossy leaves lying nearly flat over stone and litter, on a few
		/// trailing stems. Beach morning-glory (<i>Ipomoea pes-caprae</i>): long reddish runners radiating over the
		/// sand, with round leaves held up on short stalks along them and pink-purple trumpet flowers.
		/// </summary>
		private static void Creeper(MeshBuilder mesh, in DetailPlant p, DeterministicRNG rng)
		{
			if (p.Variant == FloraVariant.CreeperBeachVine)
			{
				BeachVine(mesh, in p, rng);
				return;
			}
			var spine = new List<Vector3>();
			var widths = new List<float>();
			Color stemColour = Accent(in p, 0, new Color(0.35f, 0.32f, 0.2f));
			int runners = 4;
			for (int r = 0; r < runners && Fits(mesh, StripCost(3, true)); r++)
			{
				VegetationMeshes.Part(mesh, r, runners, runners, Vector3.zero, PlantPart.Fixed, 1000);
				float yaw = (r + rng.Range(-0.3f, 0.3f)) * Mathf.PI * 2f / runners;
				spine.Clear(); widths.Clear();
				float length = p.Radius * rng.Range(0.7f, 1f);
				for (int s = 0; s <= 3; s++)
				{
					float t = s / 3f;
					spine.Add(Flat(yaw + Mathf.Sin(t * 3f + r) * 0.3f) * (length * t) + Vector3.up * 0.008f);
					widths.Add(s == 3 ? 0f : 0.008f);
				}
				Color32 sc = PlantParts.C32(stemColour, 0f);
				Ribbon(mesh, spine, widths, Vector3.Cross(Vector3.up, Flat(yaw)), FoliageCell.Blade, t => sc, 0f, 0f, Vector3.up);
			}
			(int leaves, int core) = VegetationMeshes.Spared(p.Count, 1.15f);
			for (int k = 0; k < leaves; k++)
			{
				if (!Fits(mesh, 2))
				{
					break;
				}
				Vector3 at = Disc(rng, p.Radius);
				float edge = at.magnitude / Mathf.Max(0.01f, p.Radius);
				at.y = rng.Range(0.006f, p.Height) * (1f - 0.5f * edge);
				VegetationMeshes.Part(mesh, k, leaves, core, at, PlantPart.Leaf, 1010);
				Quaternion turn = Quaternion.AngleAxis(rng.Range(0f, 360f), Vector3.up);
				float tilt = Mathf.Deg2Rad * rng.Range(8f, 30f);
				float size = p.Width * rng.Range(0.8f, 1.2f);
				Vector3 right = turn * Vector3.right * size;
				Vector3 up = (turn * Vector3.forward * Mathf.Cos(tilt) + Vector3.up * Mathf.Sin(tilt)) * size;
				PlantParts.Card(mesh, 0, at, right, up, FoliageCell.BroadLeaves, PlantParts.C32(Pick(in p, rng), 1f), new Vector2(0f, 0.05f), new Vector2(0.01f, 0.15f), Vector3.up);
			}
		}

		private static void BeachVine(MeshBuilder mesh, in DetailPlant p, DeterministicRNG rng)
		{
			var spine = new List<Vector3>();
			var widths = new List<float>();
			Color runnerColour = Accent(in p, 1, new Color(0.45f, 0.32f, 0.22f));
			Color flower = Accent(in p, 0, new Color(0.75f, 0.3f, 0.6f));
			int runners = Mathf.Max(3, p.Count / 6);
			for (int r = 0; r < runners; r++)
			{
				if (!Fits(mesh, StripCost(4, true) + 6 * 2 + 4))
				{
					break;
				}
				VegetationMeshes.Part(mesh, r, runners, Mathf.Max(1, runners * 2 / 3), Vector3.zero, PlantPart.Frond, 1020);
				float yaw = (r + rng.Range(-0.35f, 0.35f)) * Mathf.PI * 2f / runners;
				float length = p.Radius * rng.Range(0.6f, 1f);
				float wander = rng.Range(-0.5f, 0.5f);
				spine.Clear(); widths.Clear();
				for (int s = 0; s <= 4; s++)
				{
					float t = s / 4f;
					spine.Add(Flat(yaw + wander * t * t) * (length * t) + Vector3.up * 0.01f);
					widths.Add(s == 4 ? 0f : 0.009f);
				}
				Color32 rc = PlantParts.C32(runnerColour, 0f);
				Ribbon(mesh, spine, widths, Vector3.Cross(Vector3.up, Flat(yaw)), FoliageCell.Blade, t => rc, 0f, 0f, Vector3.up);
				int nodes = Mathf.Clamp(Mathf.RoundToInt(length / 0.16f), 2, 6);
				for (int n = 0; n < nodes; n++)
				{
					float t = (n + 0.6f) / (nodes + 0.3f);
					float f = t * 4f;
					int i = Mathf.Min(3, (int)f);
					Vector3 at = Vector3.Lerp(spine[i], spine[i + 1], f - i);
					Vector3 dir = (Flat(yaw + wander * t * t + (n % 2 == 0 ? 1.2f : -1.2f)) + Vector3.up * rng.Range(0.6f, 1.4f)).normalized;
					float size = p.Width * rng.Range(0.8f, 1.15f);
					PlantParts.SprayCard(mesh, 0, at, dir, size, size, rng.Range(-30f, 30f), FoliageCell.BroadLeaves, PlantParts.C32(Pick(in p, rng), 1f),
						new Vector2(0f, 0.05f), new Vector2(0.02f, 0.3f), at + Vector3.down, 0.6f);
					if (n % 3 == 1 && Fits(mesh, 4))
					{
						float bloom = rng.Range(0.05f, 0.07f);
						CrossedHead(mesh, at + Vector3.up * rng.Range(0.05f, 0.09f), Vector3.up, bloom, bloom, FoliageCell.Flower,
							PlantParts.C32(flower * rng.Range(0.92f, 1.05f), HeadAlpha), new Vector2(0.03f, 0.3f), rng.Range(0f, 90f));
					}
				}
			}
		}

		// ── Root knobs ────────────────────────────────────────────────

		/// <summary>
		/// Root knobs rising from tidal or swamp mud, on the bark the kind wears (DetailKindTraits.BarkFamily).
		/// Pneumatophores: a black mangrove's breathing roots, a dense bed of pencil-thin spikes 10–30 cm tall, mud-stained
		/// at the foot. Cypress knees: a bald cypress's woody cones, a few to a patch, broad-footed, knobbly and
		/// round-topped, up to a metre tall.
		/// </summary>
		private static void RootKnobs(MeshBuilder mesh, in DetailPlant p, DeterministicRNG rng)
		{
			Fixed(mesh);
			if (p.Variant == FloraVariant.CypressKnees)
			{
				const int sides = 7;
				int knees = Mathf.Max(1, p.Count);
				for (int k = 0; k < knees; k++)
				{
					if (!Fits(mesh, LatheCost(6, sides)))
					{
						break;
					}
					Vector3 at = (k == 0 ? Vector3.zero : Disc(rng, p.Radius));
					float height = p.Height * rng.Range(0.3f, 1f);
					float foot = height * rng.Range(0.18f, 0.28f);
					Vector2[] profile =
					{
						new Vector2(foot * 1.3f, -0.08f), new Vector2(foot, height * 0.1f), new Vector2(foot * 0.72f, height * 0.42f),
						new Vector2(foot * 0.48f, height * 0.72f), new Vector2(foot * 0.28f, height * 0.92f), new Vector2(foot * 0.04f, height),
					};
					float phase = rng.NextFloat() * Mathf.PI * 2f;
					int flutes = 2 + rng.Next(3);
					Color colourA = p.ColourA, colourB = p.ColourB;
					float round = Mathf.Max(1f, Mathf.Round(2f * Mathf.PI * foot / 0.5f));
					Solid(mesh, profile, sides, Matrix4x4.TRS(at, Quaternion.AngleAxis(rng.Range(-8f, 8f), Vector3.forward), Vector3.one),
						(s, u) => PlantParts.C32(Color.Lerp(colourA, colourB, s), 0f),
						(s, u) => 1f + 0.18f * (1f - s) * Mathf.Sin(flutes * u * Mathf.PI * 2f + phase),
						(u, s) => new Vector2(u * round, s * height / 0.5f));
				}
				return;
			}
			var path = new List<Vector3>();
			var radii = new List<float>();
			var colours = new List<Color32>();
			var wind = new List<Vector2>();
			int spikes = Mathf.Max(1, p.Count);
			for (int k = 0; k < spikes; k++)
			{
				if (!Fits(mesh, TubeCost(3, 4, true)))
				{
					break;
				}
				Vector3 root = Disc(rng, p.Radius) + Vector3.down * 0.05f;
				float height = p.Height * rng.Range(0.35f, 1f) + 0.05f;
				Vector3 lean = new Vector3(rng.Range(-1f, 1f), 0f, rng.Range(-1f, 1f)) * (height * 0.08f);
				path.Clear(); radii.Clear(); colours.Clear(); wind.Clear();
				path.Add(root); path.Add(root + Vector3.up * (height * 0.6f) + lean * 0.6f); path.Add(root + Vector3.up * height + lean);
				float r = p.Width * rng.Range(0.8f, 1.2f);
				radii.Add(r); radii.Add(r * 0.75f); radii.Add(0f);
				Color mud = p.ColourA, grey = p.ColourB;
				colours.Add(PlantParts.C32(mud, 0f)); colours.Add(PlantParts.C32(Color.Lerp(mud, grey, 0.8f), 0f)); colours.Add(PlantParts.C32(grey, 0f));
				wind.Add(Vector2.zero); wind.Add(Vector2.zero); wind.Add(Vector2.zero);
				PlantParts.Tube(mesh, 0, path, radii, 4, 0.3f, colours, wind);
			}
		}

		// ── Mushrooms ─────────────────────────────────────────────────

		/// <summary>The shape of a mushroom's cap.</summary>
		private enum CapShape { Convex, Bell, Cone }

		/// <summary>
		/// One mushroom at the origin, then turned by <paramref name="tilt"/> about its foot and moved to
		/// <paramref name="at"/>: a lathed stem (swollen at the foot for a bolete) and a lathed cap whose profile runs
		/// from the gills under it out to the rim and over the top, so the underside faces down. Warts (a fly agaric's
		/// white flecks) are small solid cards on the cap.
		/// </summary>
		private static void Mushroom(MeshBuilder mesh, Vector3 at, Quaternion tilt, float capRadius, float capHeight, float stemHeight, float stemRadius,
			CapShape shape, Color cap, Color gills, Color stem, int sides, bool swollen, int warts, Color wart, DeterministicRNG rng)
		{
			int first = mesh.VertexCount;
			Vector2[] stemProfile = swollen
				? new[] { new Vector2(stemRadius * 1.5f, -0.01f), new Vector2(stemRadius * 1.7f, stemHeight * 0.3f), new Vector2(stemRadius, stemHeight) }
				: new[] { new Vector2(stemRadius * 1.2f, -0.01f), new Vector2(stemRadius, stemHeight) };
			PlantParts.Lathe(mesh, Vector3.zero, stemProfile, Mathf.Max(4, sides - 3), null,
				(s, u) => PlantParts.C32(stem * Mathf.Lerp(0.75f, 1f, s), 0f));
			Vector2[] capProfile;
			float y0 = stemHeight;
			switch (shape)
			{
				case CapShape.Bell:
					// A shaggy ink cap: a tall bell hanging round the stem, its rim low.
					capProfile = new[]
					{
						new Vector2(stemRadius * 1.1f, y0 - capHeight * 0.75f), new Vector2(capRadius * 0.95f, y0 - capHeight * 0.8f),
						new Vector2(capRadius, y0 - capHeight * 0.35f), new Vector2(capRadius * 0.75f, y0 + capHeight * 0.12f), new Vector2(0f, y0 + capHeight * 0.22f),
					};
					break;
				case CapShape.Cone:
					capProfile = new[] { new Vector2(stemRadius * 1.1f, y0 - capHeight * 0.1f), new Vector2(capRadius, y0 - capHeight * 0.15f), new Vector2(0f, y0 + capHeight * 0.85f) };
					break;
				default:
					capProfile = new[]
					{
						new Vector2(stemRadius * 1.1f, y0 - capHeight * 0.05f), new Vector2(capRadius * 0.95f, y0 - capHeight * 0.2f),
						new Vector2(capRadius, y0 + capHeight * 0.1f), new Vector2(capRadius * 0.65f, y0 + capHeight * 0.75f), new Vector2(0f, y0 + capHeight),
					};
					break;
			}
			// The first segment is the underside (gills); a bell's rim blackens as it deliquesces.
			float under = 1f / (capProfile.Length - 1);
			bool inky = shape == CapShape.Bell;
			PlantParts.Lathe(mesh, Vector3.zero, capProfile, sides, null, (s, u) =>
			{
				if (s <= under * 0.5f) return PlantParts.C32(gills, 0f);
				if (inky && s <= under * 1.5f) return PlantParts.C32(Color.Lerp(gills, cap, 0.3f), 0f);
				return PlantParts.C32(cap * Mathf.Lerp(1.05f, 0.85f, s), 0f);
			});
			for (int w = 0; w < warts; w++)
			{
				float yaw = rng.NextFloat() * Mathf.PI * 2f;
				float reach = rng.Range(0.2f, 0.75f);
				// On a convex cap: height from the profile's top arc.
				Vector3 n = Direction(yaw, Mathf.Lerp(1.5f, 0.6f, reach));
				Vector3 p = new Vector3(Mathf.Cos(yaw) * capRadius * reach * 0.95f, y0 + capHeight * Mathf.Lerp(0.98f, 0.45f, reach * reach) + 0.002f, Mathf.Sin(yaw) * capRadius * reach * 0.95f);
				float size = capRadius * rng.Range(0.12f, 0.2f);
				Vector3 right = PlantParts.Perpendicular(n) * size;
				Vector3 up = Vector3.Cross(n, right.normalized) * size;
				PlantParts.Card(mesh, 0, p, right, up, FoliageCell.Solid, PlantParts.C32(wart, 0f), Vector2.zero, Vector2.zero, n);
			}
			mesh.Transform(Matrix4x4.TRS(at, tilt, Vector3.one), first);
		}

		/// <summary>
		/// Mushroom groups. Forest: fly agarics (scarlet caps flecked white on white stems) among penny-bun boletes
		/// (brown caps on swollen pale stems). Cluster: a dense tuft of honey fungus springing from one base, slender
		/// stems curving out. Swamp: shaggy ink caps, tall white bells blackening at the rim, beside a rotting stick
		/// shelved with bracket fungi. Cave: pale long-stemmed bonnets in the dark.
		/// </summary>
		private static void Mushrooms(MeshBuilder mesh, in DetailPlant p, DeterministicRNG rng)
		{
			Fixed(mesh);
			Color capA = p.ColourA, capB = p.ColourB;
			Color gills = Accent(in p, 0, new Color(0.9f, 0.86f, 0.76f));
			Color stem = Accent(in p, 1, new Color(0.92f, 0.9f, 0.84f));
			int count = Mathf.Max(1, p.Count);
			switch (p.Variant)
			{
				case FloraVariant.MushroomCluster:
				{
					Vector3 base0 = Disc(rng, p.Radius * 0.3f);
					for (int k = 0; k < count && Fits(mesh, LatheCost(4, 6) + LatheCost(2, 4)); k++)
					{
						float capR = p.Width * rng.Range(0.7f, 1.2f);
						float stemH = p.Height * rng.Range(0.55f, 1.05f);
						Vector3 at = base0 + Disc(rng, 0.04f) + Vector3.down * 0.01f;
						Vector3 lean = at - base0 + Flat(rng.NextFloat() * Mathf.PI * 2f) * 0.02f;
						Quaternion tilt = Quaternion.AngleAxis(rng.Range(8f, 30f), Vector3.Cross(Vector3.up, lean.sqrMagnitude > 1e-8f ? lean.normalized : Vector3.right));
						Mushroom(mesh, at, tilt, capR, capR * rng.Range(0.45f, 0.65f), stemH, capR * 0.18f, CapShape.Convex,
							Color.Lerp(capA, capB, rng.NextFloat()), gills, stem, 6, false, 0, Color.white, rng);
					}
					break;
				}
				case FloraVariant.MushroomSwamp:
				{
					// The rotting stick and its brackets first, so the ink caps fill what is left of the budget.
					SwampStick(mesh, in p, rng);
					for (int k = 0; k < count && Fits(mesh, LatheCost(5, 7) + LatheCost(2, 4)); k++)
					{
						float capR = p.Width * rng.Range(0.8f, 1.15f);
						Vector3 at = Disc(rng, p.Radius) + Vector3.down * 0.01f;
						Mushroom(mesh, at, Quaternion.AngleAxis(rng.Range(-6f, 6f), Vector3.forward), capR, p.Height * rng.Range(0.5f, 0.7f), p.Height * rng.Range(0.6f, 0.9f), capR * 0.25f,
							CapShape.Bell, Color.Lerp(capA, capB, rng.NextFloat()), Accent(in p, 2, new Color(0.18f, 0.16f, 0.16f)), stem, 7, false, 0, Color.white, rng);
					}
					break;
				}
				case FloraVariant.MushroomCave:
				{
					for (int k = 0; k < count && Fits(mesh, LatheCost(3, 6) + LatheCost(2, 4)); k++)
					{
						float capR = p.Width * rng.Range(0.6f, 1.2f);
						Vector3 at = Disc(rng, p.Radius) + Vector3.down * 0.01f;
						Mushroom(mesh, at, Quaternion.AngleAxis(rng.Range(-15f, 15f), Flat(rng.NextFloat() * Mathf.PI * 2f)), capR, capR * rng.Range(0.7f, 1f),
							p.Height * rng.Range(0.5f, 1.1f), capR * 0.12f, CapShape.Cone, Color.Lerp(capA, capB, rng.NextFloat()), gills, stem, 6, false, 0, Color.white, rng);
					}
					break;
				}
				default:
				{
					// Fly agarics: the first accent-free colour pair is theirs (scarlet), the boletes take the third and fourth accents.
					Color bolete = Accent(in p, 2, new Color(0.48f, 0.3f, 0.16f));
					Color boleteStem = Accent(in p, 3, new Color(0.85f, 0.8f, 0.68f));
					int agarics = Mathf.Max(1, count / 2);
					for (int k = 0; k < count; k++)
					{
						bool agaric = k < agarics;
						int cost = agaric ? LatheCost(5, 8) + LatheCost(2, 5) + 8 : LatheCost(5, 7) + LatheCost(3, 5);
						if (!Fits(mesh, cost))
						{
							break;
						}
						Vector3 at = Disc(rng, p.Radius) + Vector3.down * 0.01f;
						Quaternion tilt = Quaternion.AngleAxis(rng.Range(-8f, 8f), Flat(rng.NextFloat() * Mathf.PI * 2f));
						if (agaric)
						{
							float capR = p.Width * rng.Range(0.9f, 1.25f);
							Mushroom(mesh, at, tilt, capR, capR * rng.Range(0.4f, 0.55f), p.Height * rng.Range(0.8f, 1.1f), capR * 0.17f, CapShape.Convex,
								Color.Lerp(capA, capB, rng.NextFloat()), gills, stem, 8, false, 4, new Color(0.96f, 0.95f, 0.9f), rng);
						}
						else
						{
							float capR = p.Width * rng.Range(0.75f, 1.1f);
							Mushroom(mesh, at, tilt, capR, capR * rng.Range(0.55f, 0.7f), p.Height * rng.Range(0.4f, 0.6f), capR * 0.3f, CapShape.Convex,
								bolete * rng.Range(0.85f, 1.1f), new Color(0.82f, 0.78f, 0.45f), boleteStem, 7, true, 0, Color.white, rng);
						}
					}
					break;
				}
			}
		}

		/// <summary>A short mossy rotting stick lying on the mud with bracket fungi shelved along its side (turkey tail, zoned brown and cream).</summary>
		private static void SwampStick(MeshBuilder mesh, in DetailPlant p, DeterministicRNG rng)
		{
			const int sides = 5;
			float length = p.Radius * rng.Range(1.2f, 1.6f);
			float radius = rng.Range(0.035f, 0.05f);
			float yaw = rng.NextFloat() * 180f;
			Quaternion turn = Quaternion.AngleAxis(yaw, Vector3.up);
			Vector3 a = turn * new Vector3(-length * 0.5f, radius * 0.6f, 0f), c = turn * new Vector3(length * 0.5f, radius * 0.55f, 0f);
			var path = new List<Vector3> { a, Vector3.Lerp(a, c, 0.5f) + Vector3.up * 0.01f, c };
			var radii = new List<float> { radius, radius * 0.95f, radius * 0.85f };
			Color wood = new Color(0.36f, 0.3f, 0.22f), moss = new Color(0.3f, 0.42f, 0.16f);
			var colours = new List<Color32> { PlantParts.C32(Color.Lerp(wood, moss, 0.5f), 0f), PlantParts.C32(Color.Lerp(wood, moss, 0.3f), 0f), PlantParts.C32(wood, 0f) };
			var wind = new List<Vector2> { Vector2.zero, Vector2.zero, Vector2.zero };
			PlantParts.Tube(mesh, 0, path, radii, sides, 1f, colours, wind, uvMap: SolidUV);
			// Its broken ends, closed with flat discs of pale rotten wood.
			Color rot = new Color(0.62f, 0.52f, 0.38f);
			for (int end = 0; end < 2; end++)
			{
				Vector3 at = end == 0 ? a : c;
				Vector3 outward = (end == 0 ? a - c : c - a).normalized;
				float r = end == 0 ? radius : radius * 0.85f;
				Solid(mesh, new[] { new Vector2(r * 1.02f, 0f), new Vector2(r * 0.01f, 0.004f) }, sides,
					Matrix4x4.TRS(at, Quaternion.FromToRotation(Vector3.up, outward), Vector3.one), (s, u) => PlantParts.C32(rot, 0f));
			}
			// Brackets: flattened discs half sunk into the stick's side, stacked along it.
			Color zoneA = Accent(in p, 3, new Color(0.5f, 0.42f, 0.32f)), zoneB = new Color(0.86f, 0.82f, 0.7f);
			Vector3 side = turn * Vector3.forward;
			int brackets = 3;
			for (int k = 0; k < brackets; k++)
			{
				if (!Fits(mesh, LatheCost(4, 7)))
				{
					break;
				}
				float t = Mathf.Lerp(0.2f, 0.8f, (k + rng.Range(0.2f, 0.8f)) / brackets);
				float r = rng.Range(0.035f, 0.055f);
				Vector3 at = Vector3.Lerp(a, c, t) + side * (radius * 0.6f) + Vector3.up * rng.Range(-0.01f, 0.02f);
				Vector2[] shelf = { new Vector2(r * 0.2f, 0f), new Vector2(r, 0.004f), new Vector2(r * 0.8f, 0.012f), new Vector2(0f, 0.016f) };
				Solid(mesh, shelf, 7, Matrix4x4.TRS(at, turn, new Vector3(1f, 1f, 0.75f)),
					(s, u) => PlantParts.C32(s < 0.34f ? zoneB : Color.Lerp(zoneB, zoneA, Mathf.PingPong(s * 4f, 1f)), 0f));
			}
		}

		// ── Litter ────────────────────────────────────────────────────

		/// <summary>A card lying on the ground: flat but for a small tilt, at a height.</summary>
		private static void LyingCard(MeshBuilder mesh, Vector3 at, float yaw, float tilt, float width, float length, FoliageCell cell, Color32 colour)
		{
			Quaternion turn = Quaternion.AngleAxis(yaw, Vector3.up);
			Vector3 right = turn * Vector3.right * width;
			Vector3 up = (turn * Vector3.forward * Mathf.Cos(tilt) + Vector3.up * Mathf.Sin(tilt)) * length;
			PlantParts.Card(mesh, 0, at, right, up, cell, colour, Vector2.zero, Vector2.zero, Vector3.up);
		}

		/// <summary>
		/// Conifer litter: a mat of fallen rust-brown needles, a few bare twigs, and cones lying on their sides (scaled:
		/// alternate rings of the cone darker and lighter).
		/// </summary>
		private static void DebrisNeedle(MeshBuilder mesh, in DetailPlant p, DeterministicRNG rng)
		{
			Fixed(mesh);
			int mats = Mathf.Max(1, p.Count);
			for (int k = 0; k < mats && Fits(mesh, 2); k++)
			{
				Vector3 at = Disc(rng, p.Radius);
				at.y = 0.005f + k * 0.001f;
				LyingCard(mesh, at, rng.Range(0f, 360f), Mathf.Deg2Rad * rng.Range(-6f, 6f), p.Width * rng.Range(0.8f, 1.2f), p.Width * rng.Range(0.8f, 1.3f),
					FoliageCell.NeedleSpray, PlantParts.C32(Pick(in p, rng), 0.3f));
			}
			Color twig = Accent(in p, 0, new Color(0.35f, 0.29f, 0.22f));
			for (int k = 0; k < 5 && Fits(mesh, 2); k++)
			{
				Vector3 at = Disc(rng, p.Radius);
				at.y = 0.012f;
				LyingCard(mesh, at, rng.Range(0f, 360f), Mathf.Deg2Rad * rng.Range(-5f, 5f), p.Width * 0.6f, p.Width * rng.Range(0.8f, 1.2f), FoliageCell.Twigs, PlantParts.C32(twig, 0f));
			}
			Color coneA = Accent(in p, 1, new Color(0.42f, 0.28f, 0.16f)), coneB = Accent(in p, 2, new Color(0.56f, 0.38f, 0.22f));
			Vector2[] cone = { new Vector2(0.006f, -0.045f), new Vector2(0.02f, -0.03f), new Vector2(0.024f, 0f), new Vector2(0.018f, 0.03f), new Vector2(0.004f, 0.05f) };
			for (int k = 0; k < 4; k++)
			{
				if (!Fits(mesh, LatheCost(cone.Length, 6)))
				{
					break;
				}
				Vector3 at = Disc(rng, p.Radius * 0.8f);
				float scale = rng.Range(0.8f, 1.4f);
				at.y = 0.018f * scale;
				Quaternion lie = Quaternion.AngleAxis(rng.Range(0f, 360f), Vector3.up) * Quaternion.AngleAxis(rng.Range(78f, 95f), Vector3.right);
				int salt = rng.Next(1000);
				Solid(mesh, cone, 6, Matrix4x4.TRS(at, lie, Vector3.one * scale),
					(s, u) => PlantParts.C32(((Mathf.RoundToInt(s * 8f) + Mathf.RoundToInt(u * 6f) + salt) & 1) == 0 ? coneA : coneB, 0f));
			}
		}

		/// <summary>
		/// Palm litter: fallen fronds gone dry, tan and grey, their leaflets lying spread either side of the rachis,
		/// and a couple of fibrous husks.
		/// </summary>
		private static void DebrisPalm(MeshBuilder mesh, in DetailPlant p, DeterministicRNG rng)
		{
			Fixed(mesh);
			var spine = new List<Vector3>();
			var widths = new List<float>();
			int fronds = Mathf.Max(1, p.Count);
			for (int f = 0; f < fronds; f++)
			{
				if (!Fits(mesh, StripCost(4, true) * 2))
				{
					break;
				}
				float yaw = rng.NextFloat() * Mathf.PI * 2f;
				Vector3 dir = Flat(yaw);
				Vector3 side = Vector3.Cross(Vector3.up, dir);
				Vector3 start = Disc(rng, p.Radius * 0.4f) - dir * (p.Height * 0.5f);
				float curl = rng.Range(-0.25f, 0.25f);
				spine.Clear(); widths.Clear();
				for (int s = 0; s <= 4; s++)
				{
					float t = s / 4f;
					spine.Add(start + dir * (p.Height * t) + side * (curl * p.Height * t * t) + Vector3.up * (0.02f + 0.04f * Mathf.Sin(Mathf.PI * t)));
					widths.Add(s == 4 ? 0f : p.Width * Mathf.Lerp(0.6f, 1f, Mathf.Sin(Mathf.PI * Mathf.Min(0.9f, t + 0.15f))));
				}
				Color colour = Pick(in p, rng);
				Ribbon(mesh, spine, widths, side, FoliageCell.PalmFrond, t => PlantParts.C32(colour * Mathf.Lerp(0.85f, 1.05f, t), 0f), 0f, 0f, Vector3.up);
				for (int s = 0; s <= 4; s++)
				{
					widths[s] = s == 4 ? 0f : Mathf.Lerp(0.04f, 0.015f, s / 4f);
					spine[s] += Vector3.up * 0.01f;
				}
				Color32 rachis = PlantParts.C32(colour * 0.8f, 0f);
				Ribbon(mesh, spine, widths, side, FoliageCell.Blade, t => rachis, 0f, 0f, Vector3.up);
			}
			Color husk = Accent(in p, 0, new Color(0.4f, 0.28f, 0.17f)), fibre = Accent(in p, 1, new Color(0.55f, 0.4f, 0.24f));
			Vector2[] shape = { new Vector2(0.03f, -0.12f), new Vector2(0.1f, -0.08f), new Vector2(0.12f, 0f), new Vector2(0.09f, 0.08f), new Vector2(0.02f, 0.12f) };
			for (int k = 0; k < 2; k++)
			{
				if (!Fits(mesh, LatheCost(shape.Length, 7)))
				{
					break;
				}
				Vector3 at = Disc(rng, p.Radius * 0.6f);
				at.y = 0.09f;
				Quaternion lie = Quaternion.AngleAxis(rng.Range(0f, 360f), Vector3.up) * Quaternion.AngleAxis(rng.Range(75f, 100f), Vector3.right);
				int salt = rng.Next(1000);
				Solid(mesh, shape, 7, Matrix4x4.TRS(at, lie, Vector3.one * rng.Range(0.85f, 1.1f)),
					(s, u) => PlantParts.C32(Color.Lerp(husk, fibre, Hash(Mathf.RoundToInt(u * 7f) * 5 + Mathf.RoundToInt(s * 4f), salt)), 0f));
			}
		}

		/// <summary>
		/// A strandline: the line of dried wrack the last high tide left, a long narrow band of dark, tangled straps
		/// (bladderwrack and knotted wrack gone black-brown), a few bleached by the sun, with bits of twig.
		/// </summary>
		private static void DebrisWrack(MeshBuilder mesh, in DetailPlant p, DeterministicRNG rng)
		{
			Fixed(mesh);
			var spine = new List<Vector3>();
			var widths = new List<float>();
			Color bleached = Accent(in p, 0, new Color(0.58f, 0.52f, 0.38f));
			int straps = Mathf.Max(1, p.Count);
			float wave = rng.NextFloat() * Mathf.PI * 2f;
			for (int k = 0; k < straps; k++)
			{
				if (!Fits(mesh, StripCost(2, false)))
				{
					break;
				}
				float x = rng.Range(-1f, 1f) * p.Radius;
				float z = rng.Range(-1f, 1f) * p.Radius * 0.16f + Mathf.Sin(x * 1.4f + wave) * p.Radius * 0.08f;
				float pile = 1f - Mathf.Abs(x) / Mathf.Max(0.01f, p.Radius);
				var at = new Vector3(x, 0.005f + pile * rng.Range(0f, 0.03f), z);
				float yaw = rng.Range(-60f, 60f) + (rng.NextFloat() < 0.5f ? 0f : 180f);
				Vector3 dir = Quaternion.AngleAxis(yaw, Vector3.up) * Vector3.right;
				Vector3 side = Vector3.Cross(Vector3.up, dir);
				float length = p.Height * rng.Range(0.5f, 1.2f);
				float bend = rng.Range(-0.3f, 0.3f) * length;
				spine.Clear(); widths.Clear();
				spine.Add(at - dir * (length * 0.5f));
				spine.Add(at + side * bend + Vector3.up * 0.008f);
				spine.Add(at + dir * (length * 0.5f));
				float w = p.Width * rng.Range(0.7f, 1.3f);
				widths.Add(w); widths.Add(w * 1.1f); widths.Add(w * 0.8f);
				Color colour = rng.NextFloat() < 0.15f ? bleached : Pick(in p, rng);
				Ribbon(mesh, spine, widths, side, FoliageCell.Strap, t => PlantParts.C32(colour, 0f), 0f, 0f, Vector3.up);
			}
			Color twig = Accent(in p, 1, new Color(0.5f, 0.44f, 0.36f));
			for (int k = 0; k < 4 && Fits(mesh, 2); k++)
			{
				var at = new Vector3(rng.Range(-1f, 1f) * p.Radius * 0.8f, 0.02f, rng.Range(-0.12f, 0.12f) * p.Radius);
				LyingCard(mesh, at, rng.Range(0f, 360f), Mathf.Deg2Rad * rng.Range(-5f, 5f), 0.12f, rng.Range(0.15f, 0.3f), FoliageCell.Twigs, PlantParts.C32(twig, 0f));
			}
		}

		/// <summary>
		/// Fallen branches (prefab Detail_DebrisBranch): crooked tapering sticks lying on the litter, each with a few
		/// side twigs and its broken thick end closed with a disc, all on the bark the kind wears (no leaves: these are
		/// dead). Light enough to need no collider; the logs proper are deadwood props.
		/// </summary>
		private static void DebrisBranch(MeshBuilder mesh, in DetailPlant p, DeterministicRNG rng)
		{
			Fixed(mesh);
			var path = new List<Vector3>();
			var radii = new List<float>();
			var colours = new List<Color32>();
			var wind = new List<Vector2>();
			int branches = Mathf.Max(1, p.Count);
			for (int b = 0; b < branches; b++)
			{
				float length = p.Height * (b == 0 ? 1f : rng.Range(0.35f, 0.7f));
				float radius = p.Width * (b == 0 ? 1f : rng.Range(0.6f, 0.85f));
				int segments = Mathf.Clamp(Mathf.RoundToInt(length / 0.3f), 3, 6);
				const int sides = 5;
				if (!Fits(mesh, TubeCost(segments + 1, sides, true) + LatheCost(2, sides)))
				{
					break;
				}
				float yaw = rng.NextFloat() * 360f;
				Quaternion turn = Quaternion.AngleAxis(yaw, Vector3.up);
				Vector3 offset = b == 0 ? Vector3.zero : Disc(rng, p.Radius * 0.5f);
				Vector3 a = offset + turn * new Vector3(-length * 0.5f, radius * 0.7f, 0f), c = offset + turn * new Vector3(length * 0.5f, radius * 0.35f, 0f);
				path.Clear(); radii.Clear(); colours.Clear(); wind.Clear();
				PlantParts.CrookedPath(a, c, segments, 0.05f, 0f, rng, path);
				for (int i = 0; i < path.Count; i++)
				{
					float t = (float)i / (path.Count - 1);
					radii.Add(i == path.Count - 1 ? 0f : radius * Mathf.Lerp(1f, 0.35f, t));
					// Kept on the ground: a stick lies along it, not across a hump of its own wandering.
					path[i] = new Vector3(path[i].x, Mathf.Max(radius * 0.25f, radii[i] * 0.6f), path[i].z);
					colours.Add(PlantParts.C32(Color.white * rng.Range(0.82f, 1f), 0f));
					wind.Add(Vector2.zero);
				}
				PlantParts.Tube(mesh, 0, path, radii, sides, 0.5f, colours, wind);
				Vector3 outward = (path[0] - path[1]).normalized;
				float round = Mathf.Max(1f, Mathf.Round(2f * Mathf.PI * radius / 0.5f));
				Solid(mesh, new[] { new Vector2(radius * 1.02f, 0f), new Vector2(radius * 0.05f, radius * 0.25f) }, sides,
					Matrix4x4.TRS(path[0], Quaternion.FromToRotation(Vector3.up, outward), Vector3.one), (s, u) => PlantParts.C32(new Color(1f, 0.95f, 0.85f), 0f), null,
					(u, s) => new Vector2(u * round, s * 0.1f));
				// Side twigs from the outer part, lying along the ground.
				int twigs = b == 0 ? 4 : 2;
				for (int k = 0; k < twigs; k++)
				{
					if (!Fits(mesh, TubeCost(3, 3, true)))
					{
						break;
					}
					float t = rng.Range(0.25f, 0.85f);
					float f = t * (path.Count - 1);
					int i = Mathf.Min(path.Count - 2, (int)f);
					Vector3 at = Vector3.Lerp(path[i], path[i + 1], f - i);
					float r = Mathf.Lerp(radii[i], radii[i + 1], f - i) * 0.5f;
					Vector3 along = (path[i + 1] - path[i]).normalized;
					Vector3 dir = Quaternion.AngleAxis(rng.Range(30f, 60f) * (rng.NextFloat() < 0.5f ? -1f : 1f), Vector3.up) * along;
					float twigLength = length * rng.Range(0.15f, 0.3f);
					var tp = new List<Vector3> { at, at + dir * (twigLength * 0.55f) + Vector3.up * (r * 0.5f), at + dir * twigLength + Vector3.up * (r * 0.2f) };
					var tr = new List<float> { r, r * 0.6f, 0f };
					var tc = new List<Color32> { colours[i], colours[i], colours[i] };
					var tw = new List<Vector2> { Vector2.zero, Vector2.zero, Vector2.zero };
					PlantParts.Tube(mesh, 0, tp, tr, 3, 0.5f, tc, tw);
				}
			}
		}

		/// <summary>
		/// Bamboo litter: fallen culms gone straw-yellow and grey (hollow: their open ends show it), darker at every node,
		/// with papery culm sheaths and dry leaves lying round them.
		/// </summary>
		private static void DebrisBamboo(MeshBuilder mesh, in DetailPlant p, DeterministicRNG rng)
		{
			Fixed(mesh);
			var path = new List<Vector3>();
			var radii = new List<float>();
			var colours = new List<Color32>();
			var wind = new List<Vector2>();
			Color node = Accent(in p, 0, new Color(0.5f, 0.44f, 0.28f));
			int culms = Mathf.Max(1, p.Count);
			for (int k = 0; k < culms; k++)
			{
				const int sides = 6, segments = 6;
				if (!Fits(mesh, TubeCost(segments + 1, sides, false) + 30))
				{
					break;
				}
				float length = p.Height * rng.Range(0.6f, 1f);
				float radius = p.Width * rng.Range(0.8f, 1.15f);
				Quaternion turn = Quaternion.AngleAxis(rng.NextFloat() * 360f, Vector3.up);
				Vector3 offset = Disc(rng, p.Radius * 0.4f);
				Vector3 a = offset + turn * new Vector3(-length * 0.5f, radius, 0f), c = offset + turn * new Vector3(length * 0.5f, radius, 0f);
				path.Clear(); radii.Clear(); colours.Clear(); wind.Clear();
				PlantParts.CrookedPath(a, c, segments, 0.01f, 0f, rng, path);
				Color straw = Pick(in p, rng);
				for (int i = 0; i < path.Count; i++)
				{
					path[i] = new Vector3(path[i].x, radius * 0.9f, path[i].z);
					radii.Add(radius * Mathf.Lerp(1f, 0.8f, (float)i / segments));
					// Every ring a node, darker; the internodes between them shade back to straw.
					colours.Add(PlantParts.C32(i % 2 == 0 ? Color.Lerp(straw, node, 0.6f) : straw, 0f));
					wind.Add(Vector2.zero);
				}
				PlantParts.Tube(mesh, 0, path, radii, sides, 1f, colours, wind, uvMap: SolidUV);
			}
			Color sheath = Accent(in p, 1, new Color(0.62f, 0.48f, 0.3f));
			for (int k = 0; k < 5 && Fits(mesh, 2); k++)
			{
				Vector3 at = Disc(rng, p.Radius);
				at.y = 0.012f;
				LyingCard(mesh, at, rng.Range(0f, 360f), Mathf.Deg2Rad * rng.Range(-10f, 10f), 0.08f, rng.Range(0.15f, 0.25f), FoliageCell.Strap, PlantParts.C32(sheath, 0f));
			}
			Color leaf = Accent(in p, 2, new Color(0.7f, 0.62f, 0.38f));
			for (int k = 0; k < 14 && Fits(mesh, 2); k++)
			{
				Vector3 at = Disc(rng, p.Radius);
				at.y = 0.005f + k * 0.001f;
				LyingCard(mesh, at, rng.Range(0f, 360f), Mathf.Deg2Rad * rng.Range(-8f, 8f), 0.22f, 0.22f, FoliageCell.BambooLeaves, PlantParts.C32(leaf * rng.Range(0.85f, 1.1f), 0.3f));
			}
		}
	}
}
#endif
