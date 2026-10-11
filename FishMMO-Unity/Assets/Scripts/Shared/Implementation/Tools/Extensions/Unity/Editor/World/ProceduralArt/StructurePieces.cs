#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>What a piece is mostly built of: the POI templates ask for a style to keep a site consistent.</summary>
	public enum StructureStyle
	{
		Timber,
		Stone,
		/// <summary>Cloth and hide: tents, pavilions, bedrolls, banners.</summary>
		Hide,
		Iron,
		Earth,
	}

	/// <summary>A piece's weight class: its level-0 triangle budget (an instanced prop's, not a hero asset's).</summary>
	public enum StructureSize
	{
		/// <summary>At most 600 triangles at level 0: crates, barrels, gravestones.</summary>
		Small,
		/// <summary>At most 1500: tents, palisades, shrines, statues.</summary>
		Medium,
		/// <summary>At most 3000: houses, towers, keeps.</summary>
		Building,
	}

	/// <summary>Where a piece's origin sits.</summary>
	public enum StructureAnchor
	{
		/// <summary>On the ground: y = 0 is the ground line; foundations reach below it to sit on a slope.</summary>
		Ground,
		/// <summary>At deck level: y = 0 is the walking surface (piers, bridge spans); piles and trestles reach down.</summary>
		Deck,
	}

	/// <summary>A material treatment every piece is generated in: the same meshes, other textures (Jim, 2026-10-10: tint variants via material, not new meshes).</summary>
	/// <remarks>Append-only: names are in generated prefab paths.</remarks>
	public enum StructureFinish
	{
		/// <summary>Weathered but kept.</summary>
		None,
		/// <summary>Moss in the hollows and on the tops: old, damp, forest and graveyard.</summary>
		Mossy,
		/// <summary>Burnt: raided camps and villages.</summary>
		Charred,
		/// <summary>A dark algal film: half-sunk swamp and underwater sites.</summary>
		Algae,
	}

	/// <summary>
	/// One kind of structure piece in the kit: what it is (tags), what it is built of (style), how big it is, and how many
	/// variants it has. The meshes come from its builder (<see cref="StructurePieces.Build"/>); the prefabs from the art
	/// generator (BiomeArtGenerator.Structures.cs).
	/// </summary>
	public sealed class StructurePiece
	{
		/// <summary>The stable name, part of every generated path: never renamed once used.</summary>
		public string Id;
		/// <summary>What it is, for the POI templates' queries (<see cref="StructurePieces.Tags"/> lists every tag in use).</summary>
		public string[] Tags;
		public StructureStyle Style;
		public StructureSize Size;
		/// <summary>Distinct meshes; every one in every finish.</summary>
		public int Variants = 2;
		/// <summary>False for ground clutter a player walks over (a bedroll, a bone pile, a fire ring): no collider.</summary>
		public bool Collides = true;
		public StructureAnchor Anchor;
		/// <summary>
		/// For a piece laid end to end (wall, palisade, fence, pier, bridge): the length along x (along z for a pier or a
		/// bridge span) one module covers, centred on the origin. Zero for a free-standing piece.
		/// </summary>
		public float ModuleLength;
		/// <summary>For a ruin: the intact piece it is made from (its variant <c>v % base variants</c>), else null.</summary>
		public string RuinOf;
		/// <summary>For a ruin: how far gone, 0 intact .. 1 a stump in its rubble.</summary>
		public float Decay;
		/// <summary>One line for designers.</summary>
		public string Description;

		internal Action<StructureDraft> Builder;

		/// <summary>The level-0 triangle budget of its <see cref="Size"/>.</summary>
		public int TriangleBudget => Size == StructureSize.Small ? 600 : Size == StructureSize.Medium ? 1500 : 3000;

		public bool HasTag(string tag) => Array.IndexOf(Tags, tag) >= 0;

		private bool measured;
		private Vector2 footprint;
		private float height, depth;

		/// <summary>The widest x and z extent over every variant (metres, the drawn shape: roofs' eaves and a ruin's rubble included).</summary>
		public Vector2 Footprint { get { Measure(); return footprint; } }
		/// <summary>The tallest variant's top above the origin, metres.</summary>
		public float Height { get { Measure(); return height; } }
		/// <summary>How far below the origin the deepest variant reaches (foundations, piles), metres, positive.</summary>
		public float Depth { get { Measure(); return depth; } }

		/// <summary>Measured once from the coarsest level of every variant at the default seed (milliseconds each).</summary>
		private void Measure()
		{
			if (measured)
			{
				return;
			}
			for (int v = 0; v < Variants; v++)
			{
				Bounds b = StructurePieces.BuildSolidsBounds(this, v, StructurePieces.LevelCount - 1, StructurePieces.DefaultSeed);
				footprint = Vector2.Max(footprint, new Vector2(b.size.x, b.size.z));
				height = Mathf.Max(height, b.max.y);
				depth = Mathf.Max(depth, -b.min.y);
			}
			measured = true;
		}

		public override string ToString() => Id;
	}

	/// <summary>
	/// One level of one variant of a piece while it is being built: the solids so far, and what the level allows. Every
	/// random choice is a hash of a part's key (<see cref="Rand"/>), never a running generator, so a level that leaves a
	/// part out does not change any other part.
	/// </summary>
	public sealed class StructureDraft
	{
		public readonly List<StructureSolid> Solids = new List<StructureSolid>();
		public readonly StructurePiece Piece;
		public readonly int Variant;
		public readonly int Lod;
		/// <summary>This variant's own seed (the piece, the variant and the generation seed).</summary>
		public readonly int Seed;
		/// <summary>The generation's seed, for building another piece this one is made from (a ruin's intact piece).</summary>
		public readonly int GenerationSeed;

		public StructureDraft(StructurePiece piece, int variant, int lod, int seed)
		{
			Piece = piece;
			Variant = variant;
			Lod = lod;
			GenerationSeed = seed;
			Seed = ProceduralNoise.SeedFor($"Structure/{piece.Id}/{variant}", seed);
		}

		/// <summary>Level 0: bevels, trim, the small parts.</summary>
		public bool Fine => Lod == 0;
		/// <summary>Levels 0 and 1: secondary parts (braces, shutters, railings).</summary>
		public bool Mid => Lod <= 1;

		/// <summary>Facets round a lathe at this level: all of them, two thirds, a half (never fewer than <paramref name="min"/>).</summary>
		public int Seg(int full, int min = 4) => Mathf.Max(min, Lod == 0 ? full : Lod == 1 ? full * 2 / 3 : full / 2);

		/// <summary>A bevel at level 0 only.</summary>
		public float Bevel(float metres) => Lod == 0 ? metres : 0f;

		/// <summary>A number in [0, 1) that depends only on the variant, a part's key and a salt.</summary>
		public float Rand(int key, int salt = 0) => ProceduralNoise.ToUnit(ProceduralNoise.Hash(key, salt, Seed));

		public float Range(int key, int salt, float min, float max) => min + (max - min) * Rand(key, salt);

		/// <summary>Adds a solid, giving it a texture offset of its own.</summary>
		public StructureSolid Add(StructureSolid s)
		{
			s.UvOffset = new Vector2(Mathf.Floor(Rand(s.Key, 9101) * 16f) * 0.5f, Mathf.Floor(Rand(s.Key, 9102) * 16f) * 0.5f);
			Solids.Add(s);
			return s;
		}

		/// <summary>Adds every solid of another list (kept keys).</summary>
		public void AddRange(IEnumerable<StructureSolid> solids)
		{
			foreach (StructureSolid s in solids)
			{
				Add(s);
			}
		}
	}

	/// <summary>A built level: its mesh and the material of each submesh.</summary>
	public struct StructureMesh
	{
		public MeshBuilder Mesh;
		public StructureMaterial[] Materials;
	}

	/// <summary>
	/// The structure kit's pieces (Jim, 2026-10-10: a procedural structure kit for generated points of interest, any piece
	/// replaceable by a LOCAL or hand-made prefab), their names and paths, and the builders that turn one into meshes.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Conventions every piece keeps.</b> 1 unit = 1 m. Origin at the centre of the footprint on the ground line
	/// (<see cref="StructureAnchor.Ground"/>) or at deck level (<see cref="StructureAnchor.Deck"/>); the front — door,
	/// gate, face — looks down −z; a modular piece runs along x (piers and bridge spans along z) and covers exactly
	/// <see cref="StructurePiece.ModuleLength"/>. Buildings stand on a plinth reaching half a metre below the ground line so
	/// they can be seated on a slope without a gap.
	/// </para>
	/// <para>
	/// <b>Levels.</b> Three, built by the same builder at falling detail (<see cref="StructureDraft.Fine"/>,
	/// <see cref="StructureDraft.Mid"/>, <see cref="StructureDraft.Seg"/>): the silhouette and every opening a player
	/// walks through stay at every level, because the last level is the collider.
	/// </para>
	/// <para>
	/// <b>Determinism.</b> A variant seeds itself from its piece's id and its index
	/// (<see cref="StructureDraft"/>), so adding a piece or a variant changes no other.
	/// </para>
	/// </remarks>
	public static partial class StructurePieces
	{
		public const int LevelCount = 3;

		/// <summary>The generation seed the measurements and the art use (ProceduralArtCatalogue.DefaultSeed).</summary>
		public const int DefaultSeed = ProceduralArtCatalogue.DefaultSeed;

		/// <summary>Every generated prefab sits in this sub-folder of the generated prefabs.</summary>
		public const string PrefabFolderName = "Structures";

		/// <summary>Every structure mesh, material and prefab starts with this.</summary>
		public const string Prefix = "Structure_";

		/// <summary>
		/// Every tag a piece may carry. The POI templates query by these (Jim's list first: tent, camp, sacred, dead,
		/// building, wall, ruin, portal, swamp); a test keeps every piece's tags inside it and every tag resolving.
		/// </summary>
		public static readonly string[] Tags =
		{
			"tent", "camp", "sacred", "dead", "building", "wall", "ruin", "portal", "swamp",
			"fire", "storage", "banner", "bedding", "palisade", "gate", "standing-stone", "altar", "shrine", "statue",
			"column", "grave", "crypt", "bones", "fence", "house", "tower", "keep", "stilt", "well", "lighthouse",
			"market", "dock", "bridge", "rubble", "monument", "military", "water", "underwater",
		};

		private static List<StructurePiece> all;

		/// <summary>Every piece, in table order.</summary>
		public static IReadOnlyList<StructurePiece> All
		{
			get
			{
				if (all == null)
				{
					var list = new List<StructurePiece>();
					AddCamp(list);
					AddSacred(list);
					AddDead(list);
					AddBuildings(list);
					AddRuins(list);
					all = list;
				}
				return all;
			}
		}

		/// <summary>The piece with this id, or null.</summary>
		public static StructurePiece Find(string id)
		{
			foreach (StructurePiece p in All)
			{
				if (string.Equals(p.Id, id, StringComparison.Ordinal))
				{
					return p;
				}
			}
			return null;
		}

		/// <summary>Every finish, in order.</summary>
		public static readonly StructureFinish[] Finishes = { StructureFinish.None, StructureFinish.Mossy, StructureFinish.Charred, StructureFinish.Algae };

		// ── Names ─────────────────────────────────────────────────────

		/// <summary>A level's payload mesh name.</summary>
		public static string MeshName(string id, int variant, int lod) => $"{Prefix}{id}_{variant}_LOD{lod}";

		/// <summary>A prefab's name under the generated prefabs folder (with its sub-folder), e.g. <c>Structures/Structure_Tent_0_Mossy</c>.</summary>
		public static string PrefabName(string id, int variant, StructureFinish finish = StructureFinish.None)
		{
			return $"{PrefabFolderName}/{Prefix}{id}_{variant}{(finish == StructureFinish.None ? string.Empty : "_" + finish)}";
		}

		/// <summary>A material's name: the material, and its finish unless plain.</summary>
		public static string MaterialName(StructureMaterial material, StructureFinish finish = StructureFinish.None)
		{
			return $"{Prefix}{material}{(finish == StructureFinish.None ? string.Empty : "_" + finish)}";
		}

		// ── Building ──────────────────────────────────────────────────

		/// <summary>A level of a variant as solids.</summary>
		public static List<StructureSolid> BuildSolids(StructurePiece piece, int variant, int lod, int seed)
		{
			if (piece == null) throw new ArgumentNullException(nameof(piece));
			if (variant < 0 || variant >= piece.Variants) throw new ArgumentOutOfRangeException(nameof(variant));
			var draft = new StructureDraft(piece, variant, Mathf.Clamp(lod, 0, LevelCount - 1), seed);
			piece.Builder(draft);
			return draft.Solids;
		}

		/// <summary>A level of a variant as a mesh, one submesh per material.</summary>
		public static StructureMesh Build(StructurePiece piece, int variant, int lod, int seed)
		{
			List<StructureSolid> solids = BuildSolids(piece, variant, lod, seed);
			MeshBuilder mesh = StructureShapes.ToMesh(solids, out StructureMaterial[] materials);
			return new StructureMesh { Mesh = mesh, Materials = materials };
		}

		internal static Bounds BuildSolidsBounds(StructurePiece piece, int variant, int lod, int seed)
		{
			bool any = false;
			var bounds = new Bounds();
			foreach (StructureSolid s in BuildSolids(piece, variant, lod, seed))
			{
				if (s.Faces.Count == 0) continue;
				Bounds b = s.Bounds;
				if (any) bounds.Encapsulate(b); else bounds = b;
				any = true;
			}
			return bounds;
		}

		private static StructurePiece Piece(List<StructurePiece> list, string id, StructureStyle style, StructureSize size, string description,
			Action<StructureDraft> builder, params string[] tags)
		{
			var p = new StructurePiece { Id = id, Style = style, Size = size, Description = description, Builder = builder, Tags = tags };
			list.Add(p);
			return p;
		}
	}
}
#endif
