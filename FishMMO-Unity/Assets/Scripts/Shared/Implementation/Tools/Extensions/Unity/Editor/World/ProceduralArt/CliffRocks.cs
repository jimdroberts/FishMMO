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
		/// <summary>Massive rock parted along two or three joint sets (granite, quartzite, marble): sub-angular blocks, rounder with weathering.</summary>
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
	}

	/// <summary>One cliff rock mesh set (all its levels of detail).</summary>
	public readonly struct CliffPiece : IEquatable<CliffPiece>
	{
		/// <summary>The rock type (<see cref="RockTypes"/> name) or <see cref="CliffRocks.Ice"/>.</summary>
		public readonly string Type;
		public readonly CliffRole Role;
		/// <summary>Index into the role's shapes for the type (<see cref="CliffRocks.ShapesOf"/>).</summary>
		public readonly int Kind;
		/// <summary>Index into <see cref="CliffRocks.RoundnessLevels"/>; -1 for shapes roundness does not change.</summary>
		public readonly int Roundness;
		public readonly int Variant;

		public CliffPiece(string type, CliffRole role, int kind, int roundness, int variant)
		{
			Type = type;
			Role = role;
			Kind = kind;
			Roundness = roundness;
			Variant = variant;
		}

		public bool Equals(CliffPiece o) => Type == o.Type && Role == o.Role && Kind == o.Kind && Roundness == o.Roundness && Variant == o.Variant;
		public override bool Equals(object obj) => obj is CliffPiece p && Equals(p);
		public override int GetHashCode() => ((((Type?.GetHashCode() ?? 0) * 31 + (int)Role) * 31 + Kind) * 31 + Roundness) * 31 + Variant;
		public override string ToString() => CliffRocks.BaseName(in this);
	}

	/// <summary>A cliff rock shape: one formation of a type, built at cliff size.</summary>
	public struct CliffShape
	{
		public string Name;
		public FormationKind Kind;
		/// <summary>Longest extent the mesh is built at, metres.</summary>
		public float Length;
		/// <summary>Height over width; above 1 the length is the height.</summary>
		public float HeightOverWidth;
		public int Count;
		public float Elongation;
		/// <summary>Column diameter multiplier (columnar shapes).</summary>
		public float ColumnScale;
		/// <summary>True for the sub-angular joint block, whose superellipse and joint cuts follow the roundness.</summary>
		public bool SubAngular;
		/// <summary>Ice: a serac (true) or an ice boulder (false).</summary>
		public bool Serac;
		/// <summary>Ice boulder template name (<see cref="IceMeshes.Boulders"/>) or serac template (<see cref="IceMeshes.Seracs"/>).</summary>
		public string IceTemplate;
		public int Variants;
		/// <summary>Relative pick weight among the role's shapes.</summary>
		public float Weight;
	}

	/// <summary>
	/// The cliff rocks: RockFormations shapes (and ice) built AT cliff size — 3 to 44 m — so their
	/// metre UVs keep the rock surface's real density, never scaled up from the 2 m props.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Per structure</b> (<see cref="StructureOf"/>): joint blocks (an implicit boulder made boxy
	/// and cut by six joint planes; rounder edges and a lower superellipse with
	/// <see cref="RoundnessLevels"/>), bedded slabs and ledges (the lathe beds of the type, its
	/// bedding forced where it has none), foliated (the same, with thin beds the placer stands on a
	/// steep shared foliation), column clusters (re-topped: every column of a cluster is given a
	/// near-level or broken top instead of the generator's dome), ice seracs and boulders.
	/// </para>
	/// <para>
	/// <b>Micro relief scales with the rock</b> (<see cref="Scaled"/>): pits, exfoliation sheets,
	/// roughness, cleavage and column diameter in metres grow with the built size; dish and ripples are
	/// already fractions of a face. <b>Resolution</b>: r0 = clamp(8 + 0.55·L, 10, 34) cells per chart
	/// edge for implicit-field rocks (the field's own detail factor divided out), the same r0 as the
	/// resolution of the lathe and polytope engines; LOD1 r0/2, LOD2 r0/4. LOD2 is also the collider.
	/// </para>
	/// <para>
	/// <b>Roundness</b> is baked into three levels for <see cref="Climatic"/> types (granite): the
	/// placer picks the level nearest the scene's climate. Other jointed types use one fixed level.
	/// </para>
	/// </remarks>
	public static class CliffRocks
	{
		/// <summary>Raise when the meshes change in a way the source text does not show.</summary>
		/// <remarks>2: more variants per role (2026-10-04) and per-variant proportions (<see cref="Proportions"/>).</remarks>
		public const int Version = 2;

		/// <summary>The pseudo rock type of glacier-ice cliffs.</summary>
		public const string Ice = "Ice";

		/// <summary>The roundness the levels are built at: alpine/glacial/arid, between, humid temperate/tropical.</summary>
		public static readonly float[] RoundnessLevels = { 0.3f, 0.5f, 0.7f };

		/// <summary>Screen heights at which each level gives way to the next (the last culls).</summary>
		public static readonly float[] LodHeights = { 0.25f, 0.08f, 0.008f };

		/// <summary>The level used as the collider (and by the placer to seat a rock).</summary>
		public const int CollisionLod = 2;

		/// <summary>Every cliff rock type: the rock types, then ice.</summary>
		public static IEnumerable<string> Types()
		{
			foreach (RockType t in RockTypes.All)
			{
				yield return t.Name;
			}
			yield return Ice;
		}

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

		/// <summary>True when a type's roundness follows the scene's climate (granite: its corestones are a weathering product).</summary>
		public static bool Climatic(string type) => type == "Granite";

		/// <summary>
		/// The roundness level a non-climatic jointed type always uses: quartzite and obsidian stay
		/// angular, marble (which dissolves) and pumice weather round.
		/// </summary>
		public static int FixedRoundness(string type) => type == "Marble" || type == "Pumice" ? 2 : 0;

		/// <summary>The level nearest a roundness.</summary>
		public static int RoundnessLevelFor(float roundness)
		{
			int best = 0;
			for (int i = 1; i < RoundnessLevels.Length; i++)
			{
				if (Mathf.Abs(RoundnessLevels[i] - roundness) < Mathf.Abs(RoundnessLevels[best] - roundness))
				{
					best = i;
				}
			}
			return best;
		}

		/// <summary>
		/// Granite roundness from a climate sample (temperature −1…1 with 0 = 0 °C and 33.1 K a unit;
		/// humidity −1…1): 0.3 + 0.4 × warm × wet, warm = smoothstep(−2 °C, +12 °C), wet =
		/// smoothstep(humidity −0.35, +0.15). Alpine, glacial and arid ground stay at 0.3; a humid
		/// temperate or tropical one reaches 0.7 (chemical weathering rounds corestones where it is
		/// warm and wet).
		/// </summary>
		public static float RoundnessFor(float temperature, float humidity)
		{
			float warm = Smooth(-2f / 33.1f, 12f / 33.1f, temperature);
			float wet = Smooth(-0.35f, 0.15f, humidity);
			return 0.3f + 0.4f * warm * wet;
		}

		private static float Smooth(float a, float b, float x)
		{
			float t = Mathf.Clamp01((x - a) / (b - a));
			return t * t * (3f - 2f * t);
		}

		// ── Shapes ────────────────────────────────────────────────────

		private static CliffShape S(string name, FormationKind kind, float length, float hOverW, int variants, float weight = 1f, int count = 0, float elongation = 0f, float columns = 1f, bool subAngular = false)
			=> new CliffShape { Name = name, Kind = kind, Length = length, HeightOverWidth = hOverW, Count = count, Elongation = elongation, ColumnScale = columns, SubAngular = subAngular, Variants = variants, Weight = weight };

		private static CliffShape IceShape(string name, bool serac, string template, float length, float hOverW, int variants, float weight = 1f)
			=> new CliffShape { Name = name, Kind = FormationKind.Boulder, Length = length, HeightOverWidth = hOverW, Serac = serac, IceTemplate = template, Variants = variants, Weight = weight };

		/// <summary>Columns of a cluster: width from the count, so the built size matches the columns' real diameter.</summary>
		private static CliffShape Cols(float length, int count, float columns, int variants) =>
			S("Columns" + count, FormationKind.Columns, length, length / (Mathf.Sqrt(count) * 0.53f * columns * 1.05f), variants, 1f, count, 0f, columns);

		/// <summary>The shapes a type's rocks of a role are built from.</summary>
		/// <remarks>
		/// The variant counts are what keeps a cliff from reading as one rock stamped along its length.
		/// The first pass had a single variant for every footing shape and every titan, and the footing is
		/// what a cliff mostly shows: a canyon in Baoakraal Hyena-den (2026-10-04) held 6,000 rocks made
		/// of four meshes (Conglomerate and Sandstone Base0_0 and Base1_0), and the bedded rotation rule
		/// (one shared bedding plane) turned them all the same way. The roles the eye meets most —
		/// footing, middle face, fill — now have three variants (two for each of the two jointed footing
		/// shapes, and for the bedded ledges), the rare titans two, and every variant past the first is
		/// built at its own proportions (<see cref="Proportions"/>), so variants differ in silhouette and
		/// not only in their noise. Crest and debris keep two: they are small, and debris is already
		/// fall-sorted to random sizes and resting poses. The cost is payload: about 335 pieces instead
		/// of 235, roughly 1.6 times the cliff meshes' bytes, the footing and titan meshes being the
		/// heaviest.
		/// </remarks>
		public static CliffShape[] ShapesOf(string type, CliffRole role)
		{
			switch (StructureOf(type))
			{
				case CliffStructure.Bedded:
					switch (role)
					{
						case CliffRole.Titan: return new[] { S("Slab", FormationKind.Bedded, 44f, 0.36f, 2) };
						case CliffRole.Base: return new[] { S("Slab", FormationKind.Bedded, 30f, 0.4f, 3), S("Ledges", FormationKind.Ledges, 26f, 0.78f, 2, 1f, 7) };
						case CliffRole.Mid: return new[] { S("Block", FormationKind.Bedded, 14f, 0.67f, 3) };
						case CliffRole.Fill: return new[] { S("Block", FormationKind.Bedded, 7f, 0.67f, 3) };
						case CliffRole.Crest: return new[] { S("Block", FormationKind.Bedded, 9f, 0.6f, 2) };
						default: return new[] { S("Slab", FormationKind.Bedded, 3f, 0.45f, 2), S("Slab", FormationKind.Bedded, 6f, 0.4f, 2) };
					}
				case CliffStructure.Foliated:
					// Slabs split along the foliation: sub-angular blocks, thin across it, banded in it. The
					// placer stands them on the cliff's one steep foliation, so the bands line up rock to rock.
					switch (role)
					{
						case CliffRole.Titan: return new[] { S("Slab", FormationKind.Boulder, 44f, 0.4f, 2, 1f, 0, 1.2f, 1f, true) };
						case CliffRole.Base: return new[] { S("Slab", FormationKind.Boulder, 30f, 0.45f, 3, 1f, 0, 1.2f, 1f, true) };
						case CliffRole.Mid: return new[] { S("Slab", FormationKind.Boulder, 15f, 0.45f, 3, 1f, 0, 1.2f, 1f, true) };
						case CliffRole.Fill:
						case CliffRole.Crest: return new[] { S("Slab", FormationKind.Boulder, 7f, 0.5f, 3, 1f, 0, 1.2f, 1f, true) };
						default: return new[] { S("Flake", FormationKind.Bedded, 3f, 0.3f, 2, 1f, 3), S("Flake", FormationKind.Bedded, 6f, 0.3f, 2, 1f, 4) };
					}
				case CliffStructure.Columnar:
					switch (role)
					{
						case CliffRole.Titan: return new[] { Cols(44f, 37, 5f, 2) };
						case CliffRole.Base: return new[] { Cols(30f, 37, 5f, 3) };
						case CliffRole.Mid: return new[] { Cols(16f, 30, 4f, 3) };
						case CliffRole.Fill:
						case CliffRole.Crest: return new[] { Cols(8f, 12, 3.4f, 3) };
						default: return new[] { S("Prism", FormationKind.Faceted, 3f, 0.4f, 2), S("Fallen", FormationKind.FallenColumns, 10f, 0.35f, 2, 1f, 5, 0f, 2.6f) };
					}
				case CliffStructure.Ice:
					switch (role)
					{
						case CliffRole.Titan: return new[] { IceShape("Serac", true, "Block", 44f, 0.75f, 2) };
						case CliffRole.Base: return new[] { IceShape("Serac", true, "Block", 30f, 0.75f, 3) };
						case CliffRole.Mid: return new[] { IceShape("Tower", true, "Tower", 16f, 2.3f, 3) };
						case CliffRole.Fill: return new[] { IceShape("Calved", false, "Calved", 8f, 0.85f, 3) };
						case CliffRole.Crest: return new[] { IceShape("Rounded", false, "Rounded", 10f, 0.72f, 2) };
						default: return new[] { IceShape("Calved", false, "Calved", 3f, 0.85f, 2), IceShape("Calved", false, "Calved", 6f, 0.85f, 2) };
					}
				default:
					switch (role)
					{
						case CliffRole.Titan: return new[] { S("Block", FormationKind.Boulder, 44f, 0.62f, 2, 1f, 0, 1.1f, 1f, true) };
						case CliffRole.Base: return new[] { S("Block", FormationKind.Boulder, 30f, 0.62f, 2, 2f, 0, 1f, 1f, true), S("Block", FormationKind.Boulder, 30f, 0.62f, 2, 1f, 0, 1.3f, 1f, true) };
						case CliffRole.Mid: return new[] { S("Block", FormationKind.Boulder, 15f, 0.62f, 3, 1f, 0, 1.1f, 1f, true) };
						case CliffRole.Fill: return new[] { S("Block", FormationKind.Boulder, 7f, 0.62f, 3, 1f, 0, 1.1f, 1f, true) };
						case CliffRole.Crest: return new[] { S("Corestone", FormationKind.Boulder, 10f, 0.72f, 2) };
						// Fresh fall debris is angular and irregular: conchoidal chunks, never dice.
						default: return new[] { S("Chunk", FormationKind.Faceted, 3f, 0.6f, 2, 1f, 0, 1.3f), S("Chunk", FormationKind.Faceted, 7f, 0.6f, 2, 1f, 0, 1.3f) };
					}
			}
		}

		/// <summary>True when a role's rocks of a type are built at every roundness level.</summary>
		public static bool HasRoundness(string type, CliffRole role) => Climatic(type) && StructureOf(type) == CliffStructure.Jointed && role != CliffRole.Crest && role != CliffRole.Debris;

		/// <summary>Every piece of a type.</summary>
		public static IEnumerable<CliffPiece> PiecesOf(string type)
		{
			foreach (CliffRole role in (CliffRole[])Enum.GetValues(typeof(CliffRole)))
			{
				CliffShape[] shapes = ShapesOf(type, role);
				for (int k = 0; k < shapes.Length; k++)
				{
					if (HasRoundness(type, role))
					{
						for (int r = 0; r < RoundnessLevels.Length; r++)
						{
							for (int v = 0; v < shapes[k].Variants; v++)
							{
								yield return new CliffPiece(type, role, k, r, v);
							}
						}
					}
					else
					{
						for (int v = 0; v < shapes[k].Variants; v++)
						{
							yield return new CliffPiece(type, role, k, -1, v);
						}
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

		/// <summary>Every mesh the art generator writes: each piece at each level of detail.</summary>
		public static IEnumerable<(CliffPiece Piece, int Lod)> AllMeshes()
		{
			foreach (CliffPiece p in All())
			{
				for (int lod = 0; lod < LodHeights.Length; lod++)
				{
					yield return (p, lod);
				}
			}
		}

		/// <summary>The shape a piece is built from.</summary>
		public static CliffShape ShapeOf(in CliffPiece piece) => ShapesOf(piece.Type, piece.Role)[piece.Kind];

		/// <summary>The roundness a piece is built at (its level, or the type's fixed one).</summary>
		public static float RoundnessOf(in CliffPiece piece) => RoundnessLevels[piece.Roundness >= 0 ? piece.Roundness : FixedRoundness(piece.Type)];

		/// <summary><c>Crag_{Type}_{Role}{Kind}[_r{30|50|70}]_{Variant}</c>.</summary>
		public static string BaseName(in CliffPiece piece)
		{
			string r = piece.Roundness >= 0 ? $"_r{Mathf.RoundToInt(RoundnessLevels[piece.Roundness] * 100f)}" : "";
			return $"Crag_{piece.Type}_{piece.Role}{piece.Kind}{r}_{piece.Variant}";
		}

		/// <summary>A piece's mesh name at a level of detail.</summary>
		public static string MeshName(in CliffPiece piece, int lod) => $"{BaseName(in piece)}_LOD{lod}";

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
		/// A rock type copy for a cliff rock built k times its props' size: absolute-metre micro relief
		/// scaled by k (dish and ripples are already fractions of a face), column diameter by
		/// <paramref name="columnK"/>, bedding forced for bedded/foliated types that have none.
		/// </summary>
		public static RockType Scaled(RockType t, float k, float columnK, CliffStructure structure)
		{
			RockGeometryStyle st = t.Style;
			st.Exfoliation.Thickness *= k;
			st.Pits.Spacing *= k;
			st.Pits.Depth *= k;
			st.Fracture.Roughness *= k;
			st.Cleavage.Thickness *= k;
			st.Clasts.Size *= k;
			st.Foliation.Wavelength *= k;
			st.Foliation.Relief *= k;
			if (st.Columns.Diameter.y <= 0f && columnK > 1f)
			{
				st.Columns.Diameter = new Vector2(0.45f, 0.6f);
				st.Columns.TopTilt = new Vector2(4f, 20f);
			}
			st.Columns.Diameter *= columnK;
			st.Columns.Gap *= columnK;
			st.Columns.Cup *= columnK;
			if ((structure == CliffStructure.Bedded || structure == CliffStructure.Foliated) && st.Bedding.Beds.y <= 0f)
			{
				st.Bedding = new BeddingStyle { Beds = new Vector2(3f, 5f), Contrast = 0.15f, Dip = Vector2.zero, Rounding = 0.45f, Crumble = 0.01f, Joints = 2 };
			}
			if (structure == CliffStructure.Columnar)
			{
				// Broken, cupped tops on most columns of a cliff.
				st.Columns.Broken = Mathf.Max(st.Columns.Broken, 0.6f);
				st.Columns.TopTilt = new Vector2(Mathf.Max(6f, st.Columns.TopTilt.x), Mathf.Max(30f, st.Columns.TopTilt.y));
			}
			t.Style = st;
			return t;
		}

		/// <summary>The sub-angular joint block's style at a roundness: six joint cuts, edges softer and the box rounder as it weathers.</summary>
		private static RockType SubAngular(RockType t, float roundness)
		{
			RockGeometryStyle st = t.Style;
			st.Facets = 6;
			st.FacetDepth = Mathf.Lerp(0.35f, 0.15f, roundness);
			st.Sharpness = Mathf.Lerp(0.6f, 0.15f, roundness);
			st.Lumpiness = Mathf.Lerp(0.2f, 0.4f, roundness);
			t.Style = st;
			return t;
		}

		/// <summary>The implicit-field engine's own cells-per-resolution factor (RockFormations.CubeRes), divided out so r0 is the chart's cells.</summary>
		private static float FieldFactor(in RockGeometryStyle st)
		{
			bool detailed = (st.Pits.Spacing > 0f && st.Pits.Depth > 0f) || st.Clasts.Size > 0f || st.Foliation.Relief > 0f;
			return detailed ? 1.8f : 1.4f;
		}

		/// <summary>The FormationShape a piece is built as (size and height from the shape's length).</summary>
		public static FormationShape FormationOf(in CliffPiece piece, in CliffShape shape)
		{
			Proportions(in piece, out float sizeK, out float heightK, out float elongationK);
			float size = (shape.HeightOverWidth <= 1f ? shape.Length : shape.Length / shape.HeightOverWidth) * sizeK;
			float height = (shape.HeightOverWidth <= 1f ? shape.Length * shape.HeightOverWidth : shape.Length) * heightK;
			float elongation = shape.Kind == FormationKind.Boulder || shape.Kind == FormationKind.Faceted
				? (shape.Elongation > 0f ? shape.Elongation : 1f) * elongationK
				: shape.Elongation;
			return new FormationShape
			{
				Name = $"Cliff{piece.Role}{piece.Kind}{shape.Name}{Mathf.RoundToInt(shape.Length)}",
				Kind = shape.Kind,
				Size = size,
				Height = height,
				Count = shape.Count,
				Dip = -1f,
				Elongation = elongation,
				Exponent = shape.SubAngular ? Mathf.Lerp(5f, 2.6f, RoundnessOf(in piece)) : 0f,
			};
		}

		/// <summary>
		/// A variant's own proportions as factors on its shape: variant 0 is the shape as declared; every
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
			uint h = ProceduralNoise.Hash(piece.Variant, Math.Max(0, piece.Roundness) + 1, seed);
			float a = ProceduralNoise.ToUnit(h);
			h = ProceduralNoise.Mix(h);
			float b = ProceduralNoise.ToUnit(h);
			h = ProceduralNoise.Mix(h);
			float c = ProceduralNoise.ToUnit(h);
			size = Mathf.Lerp(0.88f, 1.12f, a);
			height = Mathf.Lerp(0.82f, 1.22f, b);
			elongation = Mathf.Lerp(0.92f, 1.3f, c);
		}

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
			int r = res[Mathf.Clamp(lod, 0, res.Length - 1)];
			if (piece.Type == Ice)
			{
				return BuildIce(in piece, in shape, r, seed);
			}
			RockTypes.TryGet(piece.Type, out RockType baseType);
			CliffStructure structure = StructureOf(piece.Type);
			FormationShape formation = FormationOf(in piece, in shape);
			float k = formation.Size / Mathf.Max(0.5f, OriginalSize(in baseType));
			RockType type = Scaled(baseType, k, shape.ColumnScale, structure);
			if (shape.SubAngular)
			{
				type = SubAngular(type, RoundnessOf(in piece));
			}
			if (structure == CliffStructure.Foliated && shape.Kind == FormationKind.Boulder)
			{
				// Foliation bands parallel to the slab (dip 0 in the rock's own frame); slate has none of its own.
				RockGeometryStyle st = type.Style;
				if (st.Foliation.Relief <= 0f)
				{
					st.Foliation = new FoliationStyle { Wavelength = 0.3f * k, Relief = 0.03f * k, Fold = 0.05f, FoldWavelength = 1.5f * k };
				}
				st.Foliation.Dip = Vector2.zero;
				type.Style = st;
			}
			if (shape.Kind == FormationKind.Faceted && structure != CliffStructure.Columnar)
			{
				// Debris chunks: conchoidal, irregular planes (a jointed box would be a die).
				RockGeometryStyle st = type.Style;
				st.Fracture.Jointed = false;
				st.Columns.Diameter = Vector2.zero;
				st.Facets = Mathf.Max(st.Facets, 9);
				st.FacetDepth = Mathf.Max(st.FacetDepth, 0.5f);
				if (st.Fracture.Dish <= 0f)
				{
					st.Fracture.Dish = 0.03f;
				}
				type.Style = st;
			}
			if (shape.Kind == FormationKind.Columns || shape.Kind == FormationKind.FallenColumns)
			{
				formation = Calibrated(in type, formation, piece.Variant, seed);
			}
			if (shape.Kind == FormationKind.Boulder)
			{
				// The field engine multiplies the resolution by its own detail factor: divide it out.
				r = Mathf.Max(2, Mathf.RoundToInt(r * 1.4f / FieldFactor(in type.Style)));
			}
			MeshBuilder mesh = RockFormations.Build(in type, in formation, r, piece.Variant, seed ^ (piece.Role == CliffRole.Debris ? 0x3a9f : 0));
			if (shape.Kind == FormationKind.Columns)
			{
				Retop(mesh, formation.Height, ProceduralNoise.SeedFor(BaseName(in piece), seed));
			}
			return mesh;
		}

		/// <summary>The first shape's size of a type: what its props are built at, the yardstick for the micro-relief scale.</summary>
		private static float OriginalSize(in RockType type) => type.Shapes != null && type.Shapes.Length > 0 ? type.Shapes[0].Size : 2f;

		/// <summary>
		/// A column shape re-declared at its natural width: the generator lays columns out at their
		/// real diameter and then fits the cluster to the declared size, which would stretch the metre
		/// UVs. Two coarse builds measure the stretch per axis.
		/// </summary>
		private static FormationShape Calibrated(in RockType type, FormationShape shape, int variant, int seed)
		{
			for (int it = 0; it < 2; it++)
			{
				MeshBuilder probe = RockFormations.Build(in type, in shape, 4, variant, seed);
				Stretch(probe, out float rh, out float rv);
				shape.Size *= Mathf.Clamp(rh, 0.4f, 2.5f);
				if (shape.Kind == FormationKind.FallenColumns)
				{
					shape.Height *= Mathf.Clamp(rv, 0.4f, 2.5f);
				}
			}
			return shape;
		}

		/// <summary>UV metres per metre along near-horizontal and near-vertical edges.</summary>
		private static void Stretch(MeshBuilder m, out float horizontal, out float vertical)
		{
			double uh = 0, lh = 0, uv = 0, lv = 0;
			List<int> tri = m.Submeshes[0];
			for (int t = 0; t < tri.Count; t += 3)
			{
				for (int e = 0; e < 3; e++)
				{
					int a = tri[t + e], b = tri[t + (e + 1) % 3];
					Vector3 d = m.Positions[a] - m.Positions[b];
					float len = d.magnitude;
					if (len < 1e-5f)
					{
						continue;
					}
					float duv = (m.UVs[a] - m.UVs[b]).magnitude * RockMeshes.TextureMetres;
					float vy = Mathf.Abs(d.y) / len;
					if (vy < 0.25f) { uh += duv; lh += len; }
					else if (vy > 0.92f) { uv += duv; lv += len; }
				}
			}
			horizontal = lh > 0 ? (float)(uh / lh) : 1f;
			vertical = lv > 0 ? (float)(uv / lv) : 1f;
		}

		/// <summary>
		/// Gives every column of a cluster a new top: the generator shapes a cluster as a dome (centre
		/// tallest), which reads as spires at cliff size. Each column (a closed shell) is stretched
		/// vertically above the ground so its top lands at 88–100 % of the cluster height, a third of
		/// them broken lower (60–85 %); side-face UVs are stretched with it so the texel density holds.
		/// </summary>
		public static void Retop(MeshBuilder mesh, float height, int seed)
		{
			int[] shell = Shells(mesh);
			var top = new Dictionary<int, float>();
			// Columns are appended in order, so the order a shell first appears in is the column's index at
			// every level of detail: the same column gets the same top at LOD0 and LOD2.
			var order = new Dictionary<int, int>();
			float ground = float.MaxValue;
			for (int i = 0; i < mesh.VertexCount; i++)
			{
				Vector3 p = mesh.Positions[i];
				ground = Mathf.Min(ground, p.y);
				top[shell[i]] = top.TryGetValue(shell[i], out float t) ? Mathf.Max(t, p.y) : p.y;
				if (!order.ContainsKey(shell[i]))
				{
					order[shell[i]] = order.Count;
				}
			}
			float clusterTop = float.MinValue;
			foreach (float t in top.Values)
			{
				clusterTop = Mathf.Max(clusterTop, t);
			}
			var factor = new Dictionary<int, float>();
			foreach (KeyValuePair<int, float> kv in top)
			{
				uint h = ProceduralNoise.Hash(order[kv.Key], 17, seed);
				float u = ProceduralNoise.ToUnit(h), w = ProceduralNoise.ToUnit(ProceduralNoise.Mix(h));
				float level = ground + (clusterTop - ground) * (u < 0.33f ? Mathf.Lerp(0.6f, 0.85f, w) : Mathf.Lerp(0.88f, 1f, w));
				factor[kv.Key] = (level - ground) / Mathf.Max(1e-3f, kv.Value - ground);
			}
			for (int i = 0; i < mesh.VertexCount; i++)
			{
				float f = factor[shell[i]];
				Vector3 p = mesh.Positions[i];
				p.y = ground + (p.y - ground) * f;
				mesh.Positions[i] = p;
				Vector3 n = mesh.Normals[i];
				if (Mathf.Abs(n.y) < 0.5f)
				{
					Vector2 uv = mesh.UVs[i];
					mesh.UVs[i] = new Vector2(uv.x, uv.y * f);
				}
				// A vertical stretch by f carries normals by its inverse transpose: the flat faces stay flat.
				mesh.Normals[i] = new Vector3(n.x, n.y / f, n.z).normalized;
			}
			mesh.RecalculateTangents();
		}

		/// <summary>Shell id per vertex: connected components over positions welded at 0.1 mm.</summary>
		public static int[] Shells(MeshBuilder m)
		{
			var ids = new Dictionary<(long, long, long), int>();
			var weld = new int[m.VertexCount];
			for (int i = 0; i < m.VertexCount; i++)
			{
				Vector3 p = m.Positions[i];
				var key = ((long)Math.Round(p.x * 1e4), (long)Math.Round(p.y * 1e4), (long)Math.Round(p.z * 1e4));
				if (!ids.TryGetValue(key, out int id))
				{
					id = ids.Count;
					ids[key] = id;
				}
				weld[i] = id;
			}
			var parent = new int[ids.Count];
			for (int i = 0; i < parent.Length; i++)
			{
				parent[i] = i;
			}
			int Find(int x)
			{
				while (parent[x] != x)
				{
					x = parent[x] = parent[parent[x]];
				}
				return x;
			}
			foreach (List<int> tri in m.Submeshes)
			{
				for (int t = 0; t < tri.Count; t += 3)
				{
					parent[Find(weld[tri[t]])] = Find(weld[tri[t + 1]]);
					parent[Find(weld[tri[t + 1]])] = Find(weld[tri[t + 2]]);
				}
			}
			var shell = new int[m.VertexCount];
			for (int i = 0; i < m.VertexCount; i++)
			{
				shell[i] = Find(weld[i]);
			}
			return shell;
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
			if (RockTypes.TryGet(family, out _))
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

		/// <summary>The rock type a biome's formation rules name most often, or null.</summary>
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
					if (type != null && RockTypes.TryGet(type, out _))
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
	}
}
#endif
