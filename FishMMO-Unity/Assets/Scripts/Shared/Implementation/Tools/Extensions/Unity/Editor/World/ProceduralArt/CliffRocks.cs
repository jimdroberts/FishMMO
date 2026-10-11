#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// How a rock stands as a cliff: the structure that decides a cliff rock's shape and how it may be
	/// turned (<see cref="CliffRockPlacement"/>).
	/// </summary>
	public enum CliffStructure
	{
		/// <summary>Massive rock parted along two or three joint sets (granite, quartzite, marble): its debris is conchoidal chunks.</summary>
		Jointed,
		/// <summary>Strata (sandstone, limestone, shale, chalk, conglomerate, tuff): slabs and ledges sharing one gently dipping bedding plane.</summary>
		Bedded,
		/// <summary>Cleaved metamorphic rock (slate, schist, gneiss): banded slabs stood on one steep shared foliation.</summary>
		Foliated,
		/// <summary>Cooled lava (basalt, andesite): vertical clusters of polygonal columns.</summary>
		Columnar,
		/// <summary>Glacier ice: serac blocks and towers, melt-rounded boulders on top, calved blocks below.</summary>
		Ice,
	}

	/// <summary>The part a cliff rock plays, which decides its size class and where it may stand.</summary>
	public enum CliffRole
	{
		/// <summary>The rare giants of the footing (~44 m), only where the band is tall.</summary>
		Titan,
		/// <summary>The footing and the lowest third of the face (~30 m).</summary>
		Base,
		/// <summary>The middle of the face (~15 m).</summary>
		Mid,
		/// <summary>Crevices and the upper face (~7–8 m).</summary>
		Fill,
		/// <summary>Weathered rocks on the crest (~9–10 m).</summary>
		Crest,
		/// <summary>Fresh fall debris on the talus (3–10 m).</summary>
		Debris,
		/// <summary>The sheer wall behind the clutter: as tall as the cliff where it stands (14–50 m), at its foot.</summary>
		Backdrop,
	}

	/// <summary>One cliff rock mesh set (all its levels of detail).</summary>
	public readonly struct CliffPiece : IEquatable<CliffPiece>
	{
		/// <summary>The rock type (<see cref="RockTypes"/> name) or <see cref="CliffRocks.Ice"/>.</summary>
		public readonly string Type;
		public readonly CliffRole Role;
		/// <summary>Index into the role's shapes for the type (<see cref="CliffRocks.ShapesOf"/>).</summary>
		public readonly int Kind;
		public readonly int Variant;

		public CliffPiece(string type, CliffRole role, int kind, int variant)
		{
			Type = type;
			Role = role;
			Kind = kind;
			Variant = variant;
		}

		public bool Equals(CliffPiece o) => Type == o.Type && Role == o.Role && Kind == o.Kind && Variant == o.Variant;
		public override bool Equals(object obj) => obj is CliffPiece p && Equals(p);
		public override int GetHashCode() => (((Type?.GetHashCode() ?? 0) * 31 + (int)Role) * 31 + Kind) * 31 + Variant;
		public override string ToString() => CliffRocks.BaseName(in this);
	}

	/// <summary>A cliff rock shape: a cliff section, or glacier ice.</summary>
	public struct CliffShape
	{
		public string Name;
		/// <summary>Longest extent the mesh is built at, metres.</summary>
		public float Length;
		/// <summary>Height over width; above 1 the length is the height.</summary>
		public float HeightOverWidth;
		/// <summary>The <see cref="CliffSections"/> style a face rock is, or null for a formation or ice.</summary>
		public string Section;
		/// <summary>Ice: a serac (true) or an ice boulder (false).</summary>
		public bool Serac;
		/// <summary>Ice boulder template name (<see cref="IceMeshes.Boulders"/>) or serac template (<see cref="IceMeshes.Seracs"/>).</summary>
		public string IceTemplate;
		public int Variants;
		/// <summary>Relative pick weight among the role's shapes.</summary>
		public float Weight;
	}

	/// <summary>
	/// The cliff rocks: a rock cliff — its face and the fallen blocks on its talus — is built from
	/// <see cref="CliffSections"/>, jointed sections shared by every rock type, which wear the type's
	/// material; glacier ice from <see cref="IceMeshes"/>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Faces are sections</b> (Jim, 2026-10-08: they replace the formation-shaped cliff rocks). A role
	/// picks the style of its size: titans the massif, the footing the bluff, the middle face walls and
	/// pillars, fill the small block, the crest ledges. A section's mesh is the same for every type: the
	/// rock it is made of is its material (<see cref="MaterialName"/>, from the biome's cliff), so a
	/// sandstone canyon and a granite one share geometry and differ in stone.
	/// </para>
	/// <para>
	/// <b>Talus is the wall fallen</b> (Jim, 2026-10-08: the formation debris — round bedded "cake" slabs
	/// for sandstone — still read as the old cliffs): 3 m rubble and 7 m rockfall blocks cut by the same
	/// joints, beds and bevels as the face, at a lighter triangle budget
	/// (<see cref="CliffSections.DebrisLodTriangles"/>). LOD2 is the collider. Ice keeps its seracs and
	/// boulders (<see cref="ResFor"/>: r0 = clamp(8 + 0.55·L, 10, 34), r0/2, r0/4): glacier ice is not
	/// jointed, bedded rock.
	/// </para>
	/// </remarks>
	public static class CliffRocks
	{
		/// <summary>Raise when the meshes change in a way the source text does not show.</summary>
		/// <remarks>2: more variants per role (2026-10-04) and per-variant proportions (<see cref="Proportions"/>). 3: face rocks are cliff sections, granite roundness gone (2026-10-08). 4: talus too (2026-10-08).</remarks>
		public const int Version = 4;

		/// <summary>The pseudo rock type of glacier-ice cliffs.</summary>
		public const string Ice = "Ice";

		/// <summary>Screen heights at which each level gives way to the next (the last culls).</summary>
		public static readonly float[] LodHeights = { 0.25f, 0.08f, 0.008f };

		/// <summary>The level used as the collider (and by the placer to seat a rock).</summary>
		public const int CollisionLod = 2;

		/// <summary>Every cliff rock type: the rock types that can be cliffs (<see cref="IsCliffRock"/>), then ice.</summary>
		public static IEnumerable<string> Types()
		{
			foreach (RockType t in RockTypes.All)
			{
				if (!t.FormationsOnly)
				{
					yield return t.Name;
				}
			}
			yield return Ice;
		}

		/// <summary>
		/// True for a rock that can be a cliff, a river boulder or a biome's bedrock: one of the crust's rock types. The
		/// mineral, ice and alien rocks (<see cref="RockType.FormationsOnly"/>: halite, sulphur, sinter, a termite's clay,
		/// firn …) are scattered formations only — a salt flat's polygons are not a salt cliff — so they are refused here
		/// even where a ground family shares their name (Sulphur, Sinter).
		/// </summary>
		public static bool IsCliffRock(string type) => type != null && RockTypes.TryGet(type, out RockType t) && !t.FormationsOnly;

		/// <summary>The structure a type's cliffs have.</summary>
		public static CliffStructure StructureOf(string type)
		{
			switch (type)
			{
				case "Sandstone":
				case "Limestone":
				case "Shale":
				case "Chalk":
				case "Conglomerate":
				case "Tuff":
					return CliffStructure.Bedded;
				case "Slate":
				case "Schist":
				case "Gneiss":
					return CliffStructure.Foliated;
				case "Basalt":
				case "Andesite":
					return CliffStructure.Columnar;
				case Ice:
					return CliffStructure.Ice;
				default:
					return CliffStructure.Jointed; // Granite, Quartzite, Marble, Obsidian, Pumice
			}
		}

		// ── Shapes ────────────────────────────────────────────────────

		/// <summary>A section style as a shape: its longest extent and height over it as built (variant 0), every variant.</summary>
		private static CliffShape Sec(string style, float length, float hOverW, float weight = 1f)
			=> new CliffShape { Name = style, Section = style, Length = length, HeightOverWidth = hOverW, Variants = CliffSections.VariantCount, Weight = weight };

		private static CliffShape IceShape(string name, bool serac, string template, float length, float hOverW, int variants, float weight = 1f)
			=> new CliffShape { Name = name, Length = length, HeightOverWidth = hOverW, Serac = serac, IceTemplate = template, Variants = variants, Weight = weight };

		/// <summary>The shapes a type's rocks of a role are built from.</summary>
		/// <remarks>
		/// Every role of a rock type is a section (<see cref="FaceShapes"/>), its talus too; ice keeps its own
		/// shapes. Variant counts are what keeps a cliff from reading as one rock stamped along its
		/// length (Baoakraal Hyena-den, 2026-10-04: 6,000 rocks of four meshes): every section style has
		/// <see cref="CliffSections.VariantCount"/>, each at its own proportions.
		/// </remarks>
		public static CliffShape[] ShapesOf(string type, CliffRole role)
		{
			CliffStructure structure = StructureOf(type);
			if (structure == CliffStructure.Ice)
			{
				switch (role)
				{
					case CliffRole.Backdrop: return Array.Empty<CliffShape>();
					case CliffRole.Titan: return new[] { IceShape("Serac", true, "Block", 44f, 0.75f, 2) };
					case CliffRole.Base: return new[] { IceShape("Serac", true, "Block", 30f, 0.75f, 3) };
					case CliffRole.Mid: return new[] { IceShape("Tower", true, "Tower", 16f, 2.3f, 3) };
					case CliffRole.Fill: return new[] { IceShape("Calved", false, "Calved", 8f, 0.85f, 3) };
					case CliffRole.Crest: return new[] { IceShape("Rounded", false, "Rounded", 10f, 0.72f, 2) };
					default: return new[] { IceShape("Calved", false, "Calved", 3f, 0.85f, 2), IceShape("Calved", false, "Calved", 6f, 0.85f, 2) };
				}
			}
			return FaceShapes(role);
		}

		/// <summary>
		/// The section styles of a face role, at their built sizes (longest extent, height over it): titans
		/// the 44 m massif, the footing the 30 m bluff, the middle face 18 m walls and 12 m pillars, fill the
		/// 8 m block, the crest 16 m ledges (low shelves on the rim); the talus 3 m rubble and 7 m rockfall
		/// blocks (the planner picks the one nearest its fall-sorted size and scales it there); the backdrop
		/// the sheer scarps, by height.
		/// </summary>
		public static CliffShape[] FaceShapes(CliffRole role)
		{
			switch (role)
			{
				case CliffRole.Titan: return new[] { Sec("Massif", 44f, 0.55f) };
				case CliffRole.Base: return new[] { Sec("Bluff", 30f, 0.52f) };
				case CliffRole.Mid: return new[] { Sec("Wall", 18f, 0.53f), Sec("Pillars", 12f, 0.95f) };
				case CliffRole.Fill: return new[] { Sec("Block", 8f, 0.65f) };
				case CliffRole.Crest: return new[] { Sec("Ledges", 16f, 0.28f) };
				// By height (the planner picks the one nearest the cliff's and scales it there): about 14, 25 and 40 m.
				case CliffRole.Backdrop: return new[] { Sec("ScarpLow", 22f, 0.63f), Sec("Scarp", 28f, 0.9f), Sec("ScarpHigh", 40f, 1f) };
				default: return new[] { Sec("Rubble", 3f, 0.5f), Sec("Rockfall", 7f, 0.42f) };
			}
		}

		/// <summary>Every piece of a type.</summary>
		public static IEnumerable<CliffPiece> PiecesOf(string type)
		{
			foreach (CliffRole role in (CliffRole[])Enum.GetValues(typeof(CliffRole)))
			{
				CliffShape[] shapes = ShapesOf(type, role);
				for (int k = 0; k < shapes.Length; k++)
				{
					for (int v = 0; v < shapes[k].Variants; v++)
					{
						yield return new CliffPiece(type, role, k, v);
					}
				}
			}
		}

		/// <summary>Every piece of every type.</summary>
		public static IEnumerable<CliffPiece> All()
		{
			foreach (string type in Types())
			{
				foreach (CliffPiece p in PiecesOf(type))
				{
					yield return p;
				}
			}
		}

		/// <summary>
		/// Every mesh the art generator writes for the cliff rocks: each ice piece at each level of detail. The
		/// sections every rock type shares are <see cref="CliffSections.AllMeshes"/>, written once for all of them.
		/// </summary>
		public static IEnumerable<(CliffPiece Piece, int Lod)> AllMeshes()
		{
			foreach (CliffPiece p in All())
			{
				if (IsSection(in p))
				{
					continue;
				}
				for (int lod = 0; lod < LodHeights.Length; lod++)
				{
					yield return (p, lod);
				}
			}
		}

		/// <summary>The shape a piece is built from.</summary>
		public static CliffShape ShapeOf(in CliffPiece piece) => ShapesOf(piece.Type, piece.Role)[piece.Kind];

		/// <summary>True for a face piece built as a <see cref="CliffSections"/> section (shared by every type).</summary>
		public static bool IsSection(in CliffPiece piece) => ShapeOf(in piece).Section != null;

		/// <summary><c>Section_{Style}_{Variant}</c> for a section (no type: the material says it), else <c>Crag_{Type}_{Role}{Kind}_{Variant}</c>.</summary>
		public static string BaseName(in CliffPiece piece)
		{
			string section = ShapeOf(in piece).Section;
			return section != null ? $"Section_{section}_{piece.Variant}" : $"Crag_{piece.Type}_{piece.Role}{piece.Kind}_{piece.Variant}";
		}

		/// <summary>A piece's mesh name at a level of detail: a section's is the shared <see cref="CliffSections.MeshName"/>.</summary>
		public static string MeshName(in CliffPiece piece, int lod)
		{
			string section = ShapeOf(in piece).Section;
			return section != null ? CliffSections.MeshName(section, piece.Variant, lod) : $"{BaseName(in piece)}_LOD{lod}";
		}

		/// <summary>The material a type's cliff rocks wear: the type's formation material, or glacier ice.</summary>
		public static string MaterialName(string type)
		{
			if (type == Ice)
			{
				return RockArtNames.IceMaterial(IceSurfaces.GlacialBlue);
			}
			return RockTypes.TryGet(type, out RockType t) ? RockArtNames.RockTypeMaterial(in t) : null;
		}

		/// <summary>Resolutions per level for a built length: r0 = clamp(8 + 0.55·L, 10, 34), r0/2, r0/4.</summary>
		public static int[] ResFor(float length)
		{
			int r0 = Mathf.Clamp(Mathf.RoundToInt(8f + 0.55f * length), 10, 34);
			return new[] { r0, Mathf.Max(4, r0 / 2), Mathf.Max(2, r0 / 4) };
		}

		// ── Building ──────────────────────────────────────────────────

		/// <summary>
		/// An ice variant's own proportions (a section's are <see cref="CliffSections.StyleOf(string, int)"/>) as
		/// factors on its shape: variant 0 is the shape as declared; every
		/// later one is 0.88–1.12 times as broad, 0.82–1.22 times as tall and (boulders, chunks) 0.92–1.3
		/// times as elongated, from a hash of the piece, so the same piece always gets the same numbers.
		/// </summary>
		/// <remarks>
		/// The generators already vary a variant's outline and relief with its seed, but at cliff size a
		/// rock is read by its silhouette first — its height against its breadth — and two seeds of one
		/// declared size look like the same rock turned. The ranges stay inside what a role means
		/// (a footing slab is still a slab, a middle block still a block), and the placer measures every
		/// piece's own mesh, so its size caps and seating need nothing from these numbers.
		/// </remarks>
		public static void Proportions(in CliffPiece piece, out float size, out float height, out float elongation)
		{
			size = height = elongation = 1f;
			if (piece.Variant <= 0)
			{
				return;
			}
			int seed = ProceduralNoise.SeedFor($"CliffProportions/{piece.Type}/{piece.Role}{piece.Kind}", 0x5c1f);
			uint h = ProceduralNoise.Hash(piece.Variant, 1, seed);
			float a = ProceduralNoise.ToUnit(h);
			h = ProceduralNoise.Mix(h);
			float b = ProceduralNoise.ToUnit(h);
			h = ProceduralNoise.Mix(h);
			float c = ProceduralNoise.ToUnit(h);
			size = Mathf.Lerp(0.88f, 1.12f, a);
			height = Mathf.Lerp(0.82f, 1.22f, b);
			elongation = Mathf.Lerp(0.92f, 1.3f, c);
		}

		private static readonly System.Collections.Concurrent.ConcurrentDictionary<(string, int, int), MeshBuilder[]> sections = new System.Collections.Concurrent.ConcurrentDictionary<(string, int, int), MeshBuilder[]>();

		private static readonly System.Collections.Concurrent.ConcurrentDictionary<(CliffPiece, int), int> retries = new System.Collections.Concurrent.ConcurrentDictionary<(CliffPiece, int), int>();

		/// <summary>
		/// One level of detail of a piece. Pure: the same piece, level and seed always give the same mesh.
		/// </summary>
		/// <remarks>
		/// A cliff-size field with deep scaled pits and sharp joint cuts can leave one sliver triangle
		/// at a crease facing against its smoothed normals. The piece is then carved from the next seed,
		/// decided over all its levels and shared by them, so all three are the same rock.
		/// </remarks>
		public static MeshBuilder Build(in CliffPiece piece, int lod, int seed)
		{
			string section = ShapeOf(in piece).Section;
			if (section != null)
			{
				// One build gives all three levels (one field, one net): shared, never to be changed by a caller.
				int variant = piece.Variant;
				MeshBuilder[] levels = sections.GetOrAdd((section, variant, seed), key => CliffSections.BuildMeshes(key.Item1, key.Item2, key.Item3, out _));
				return levels[Mathf.Clamp(lod, 0, levels.Length - 1)];
			}
			CliffPiece p = piece;
			int offset = retries.GetOrAdd((piece, seed), _ =>
			{
				for (int attempt = 0; attempt < 6; attempt++)
				{
					bool valid = true;
					for (int level = 0; level < LodHeights.Length && valid; level++)
					{
						valid = BuildOnce(in p, level, seed + attempt * 7919).Validate(true).Count == 0;
					}
					if (valid)
					{
						return attempt;
					}
				}
				return 0;
			});
			return BuildOnce(in piece, lod, seed + offset * 7919);
		}

		private static MeshBuilder BuildOnce(in CliffPiece piece, int lod, int seed)
		{
			CliffShape shape = ShapeOf(in piece);
			int[] res = ResFor(shape.Length);
			return BuildIce(in piece, in shape, res[Mathf.Clamp(lod, 0, res.Length - 1)], seed);
		}

		/// <summary>Ice: seracs and boulders from <see cref="IceMeshes"/>'s templates, sized to the piece.</summary>
		private static MeshBuilder BuildIce(in CliffPiece piece, in CliffShape shape, int resolution, int seed)
		{
			string name = $"Cliff{piece.Role}{shape.Name}{Mathf.RoundToInt(shape.Length)}_{piece.Variant}";
			string template = shape.IceTemplate;
			if (shape.Serac)
			{
				SeracShape t = Array.Find(IceMeshes.Seracs, s => s.Name == template);
				float scale = shape.Length / Mathf.Max(t.Width, Mathf.Max(t.Depth, t.Height));
				Proportions(in piece, out float sizeK, out float heightK, out float elongationK);
				t.Name = name;
				t.Width *= scale * sizeK * Mathf.Sqrt(elongationK);
				t.Depth *= scale * sizeK / Mathf.Sqrt(elongationK);
				t.Height *= scale * heightK;
				return IceMeshes.BuildSerac(in t, resolution, seed);
			}
			IceBoulderShape b = Array.Find(IceMeshes.Boulders, s => s.Name == template);
			float k = shape.Length / Mathf.Max(0.1f, b.Size);
			b.Name = name;
			b.Size = shape.Length;
			b.ScallopDepth *= Mathf.Sqrt(k);
			return IceMeshes.BuildBoulder(in b, resolution, seed);
		}

		// ── Which rock a biome's cliff is ─────────────────────────────

		/// <summary>
		/// The rock a biome's cliff layer stands for. A family that IS a rock (Sandstone, Limestone,
		/// Basalt) is that rock and Ice is ice; a generic one (Rock, CliffRock) is the biome's own
		/// bedrock — the rock type its formation scatter rules use most — else granite for Rock and
		/// limestone for CliffRock (its texture is grey strata). Null where the cliff stays terrain
		/// (soil banks, sand).
		/// </summary>
		public static string RockTypeFor(string family, BiomeArtSpec.Entry entry)
		{
			if (string.IsNullOrEmpty(family))
			{
				return null;
			}
			if (family == Ground.Ice)
			{
				return Ice;
			}
			if (IsCliffRock(family))
			{
				return family;
			}
			if (family != Ground.Rock && family != Ground.CliffRock)
			{
				return null;
			}
			string bedrock = BedrockOf(entry);
			return bedrock ?? (family == Ground.Rock ? "Granite" : "Limestone");
		}

		/// <summary>The cliff rock (<see cref="IsCliffRock"/>) a biome's formation rules name most often, or null.</summary>
		public static string BedrockOf(BiomeArtSpec.Entry entry)
		{
			if (entry == null)
			{
				return null;
			}
			var counts = new Dictionary<string, int>(StringComparer.Ordinal);
			foreach ((BiomeArtSpec.Layer _, BiomeArtSpec.Scatter rule) in entry.Rules())
			{
				if (rule.Prefabs == null)
				{
					continue;
				}
				foreach (string prefab in rule.Prefabs)
				{
					const string prefix = "Formation_";
					if (prefab == null || !prefab.StartsWith(prefix, StringComparison.Ordinal))
					{
						continue;
					}
					int end = prefab.IndexOf('_', prefix.Length);
					string type = end > 0 ? prefab.Substring(prefix.Length, end - prefix.Length) : null;
					if (IsCliffRock(type))
					{
						counts[type] = counts.TryGetValue(type, out int n) ? n + 1 : 1;
					}
				}
			}
			string best = null;
			int most = 0;
			foreach (KeyValuePair<string, int> kv in counts)
			{
				if (kv.Value > most || kv.Value == most && string.CompareOrdinal(kv.Key, best) < 0)
				{
					best = kv.Key;
					most = kv.Value;
				}
			}
			return best;
		}

		/// <summary>
		/// A biome's own rock, for its cliffs, river boulders and fall ledges where the geology under it is not one it
		/// accepts: the rock its first cliff layer stands for (<see cref="RockTypeFor"/>), else the rock its formation rules
		/// name most (<see cref="BedrockOf"/>), else granite (a biome with soil banks still has stones in its rivers).
		/// </summary>
		public static string OwnRock(BiomeArtSpec.Entry entry)
		{
			if (entry != null)
			{
				foreach (BiomeArtSpec.Cliff cliff in entry.Cliffs)
				{
					string type = RockTypeFor(cliff.Family, entry);
					if (type != null)
					{
						return type;
					}
				}
			}
			return BedrockOf(entry) ?? "Granite";
		}

		/// <summary>Whether a biome accepts a rock: its own, or one of <see cref="BiomeArtSpec.Entry.Rocks"/>.</summary>
		public static bool Accepts(BiomeArtSpec.Entry entry, string rock)
		{
			if (rock == null)
			{
				return false;
			}
			if (rock == OwnRock(entry))
			{
				return true;
			}
			return entry != null && Array.IndexOf(entry.Rocks, rock) >= 0;
		}

		/// <summary>
		/// The rock a biome's cliff, river boulder or fall ledge is made of where the planet's geology is
		/// <paramref name="geology"/> (a <see cref="RockTypes"/> name, or null where unknown): the geology's where the biome
		/// accepts it (<see cref="Accepts"/>), else the biome's own (<see cref="OwnRock"/>). Ice stays ice.
		/// </summary>
		/// <remarks>
		/// Geology alone (2026-10-05 … 10-08) walled a scene in its province's rock whatever grew there — sandstone under
		/// a bog — and the biome alone would make every scene of a biome one rock. Jim chose the two together (2026-10-08).
		/// </remarks>
		public static string RockFor(BiomeArtSpec.Entry entry, string geology)
		{
			string own = OwnRock(entry);
			if (own == Ice)
			{
				return Ice;
			}
			return IsCliffRock(geology) && Accepts(entry, geology) ? geology : own;
		}
	}
}
#endif
