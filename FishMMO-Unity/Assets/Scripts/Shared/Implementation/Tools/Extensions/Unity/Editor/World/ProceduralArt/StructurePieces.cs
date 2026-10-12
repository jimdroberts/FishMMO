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

	/// <summary>A material treatment every piece is generated in: the same meshes, other textures (2026-10-10: tint variants via material, not new meshes).</summary>
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
		/// <summary>Where the piece gives off smoke, fire or light, in its own frame (<see cref="StructurePieces.EmittersOf"/>).</summary>
		public readonly List<(Vector3 at, SceneEmitterKind kind)> Emitters = new List<(Vector3, SceneEmitterKind)>();

		/// <summary>Marks where the piece gives off smoke, fire or light.</summary>
		public void Emit(Vector3 at, SceneEmitterKind kind) => Emitters.Add((at, kind));

		/// <summary>The piece's rooms, in its own frame (<see cref="StructurePieces.RoomsOf"/>): what PointOfInterestInteriors furnishes.</summary>
		public readonly List<StructureRoom> Rooms = new List<StructureRoom>();

		/// <summary>Marks a rectangular room: its floor's centre (at floor height), its clear size inside the walls (x, z), its height and what it is.</summary>
		public void Room(Vector3 floorCentre, Vector2 size, float height, StructureRoomKind kind)
		{
			Rooms.Add(new StructureRoom { Centre = floorCentre, Size = size, Height = height, Kind = kind });
		}

		/// <summary>
		/// Gives a room its fireplace's place, against the chimney breast (<see cref="StructureRoom.Hearth"/>): the last room
		/// marked whose floor is at <paramref name="floorY"/> (a house's ground floor, not the storey over it), or the last room
		/// marked when no height is given.
		/// </summary>
		public void RoomHearth(Vector2 at, float yaw, float floorY = float.NaN)
		{
			for (int i = Rooms.Count - 1; i >= 0; i--)
			{
				StructureRoom room = Rooms[i];
				if (!float.IsNaN(floorY) && Mathf.Abs(room.Centre.y - floorY) > 0.05f)
				{
					continue;
				}
				room.HasHearth = true;
				room.Hearth = at;
				room.HearthYaw = yaw;
				Rooms[i] = room;
				return;
			}
		}

		/// <summary>Keeps a rectangle of the last room marked clear of furniture (a stair, its landing, a stairwell): x, z in the building's frame.</summary>
		public void RoomClear(Vector2 min, Vector2 max)
		{
			if (Rooms.Count == 0)
			{
				return;
			}
			StructureRoom room = Rooms[Rooms.Count - 1];
			int n = room.Clear != null ? room.Clear.Length : 0;
			var clear = new Vector4[n + 1];
			if (n > 0)
			{
				System.Array.Copy(room.Clear, clear, n);
			}
			clear[n] = new Vector4(Mathf.Min(min.x, max.x), Mathf.Min(min.y, max.y), Mathf.Max(min.x, max.x), Mathf.Max(min.y, max.y));
			room.Clear = clear;
			Rooms[Rooms.Count - 1] = room;
		}

		/// <summary>Marks a round room: its floor's centre, its clear radius inside the wall, its height and what it is.</summary>
		public void RoundRoom(Vector3 floorCentre, float radius, float height, StructureRoomKind kind)
		{
			Rooms.Add(new StructureRoom { Centre = floorCentre, Size = new Vector2(radius * 2f, radius * 2f), Height = height, Kind = kind, Round = true });
		}

		/// <summary>The piece's doors, in its own frame (<see cref="StructurePieces.DoorsOf"/>): each a real, server-run door in the scene.</summary>
		public readonly List<StructureDoor> Doors = new List<StructureDoor>();

		/// <summary>
		/// Marks a doorway's door: hinged at <paramref name="hinge"/> (the foot of the jamb it hangs on, on the inside face),
		/// its leaf running <paramref name="across"/> to the latch, opening toward <paramref name="inward"/>.
		/// </summary>
		public void Door(Vector3 hinge, Vector3 across, Vector3 inward, float width, float height, StructureDoorKind kind = StructureDoorKind.Planks,
			StructureDoorMotion motion = StructureDoorMotion.Swing, bool startsOpen = false)
		{
			across.y = 0f;
			inward.y = 0f;
			Doors.Add(new StructureDoor
			{
				Hinge = hinge,
				Across = across.normalized,
				Inward = inward.normalized,
				Width = width,
				Height = height,
				Kind = kind,
				Motion = motion,
				StartsOpen = startsOpen,
			});
		}
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
	/// The structure kit's pieces (2026-10-10: a procedural structure kit for generated points of interest, any piece
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
		/// Every tag a piece may carry. The POI templates query by these (the requested list first: tent, camp, sacred, dead,
		/// building, wall, ruin, portal, swamp); a test keeps every piece's tags inside it and every tag resolving.
		/// </summary>
		public static readonly string[] Tags =
		{
			"tent", "camp", "sacred", "dead", "building", "wall", "ruin", "portal", "swamp",
			"fire", "storage", "banner", "bedding", "palisade", "gate", "standing-stone", "altar", "shrine", "statue",
			"column", "grave", "crypt", "bones", "fence", "house", "tower", "keep", "stilt", "well", "lighthouse",
			"market", "dock", "bridge", "rubble", "monument", "military", "water", "underwater",
			"church", "temple", "tavern", "smithy", "stable", "notice", "farm", "clutter", "town", "light", "door", "furniture", "bed", "table", "chair", "chest", "shelf", "hearth", "bar", "pew", "lectern", "sarcophagus", "stall", "step", "portcullis", "wallstair", "mine", "torch", "boat", "ship", "wreck", "shipyard", "wild", "cave",
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
					AddTown(list);
					AddClutter(list);
					AddInterior(list);
					AddMine(list);
					AddLights(list);
					AddShips(list);
					AddBridges(list);
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

		private static readonly Dictionary<(string, int), IReadOnlyList<(Vector3 at, SceneEmitterKind kind)>> emitters =
			new Dictionary<(string, int), IReadOnlyList<(Vector3, SceneEmitterKind)>>();

		/// <summary>
		/// Where a variant gives off smoke, fire or light (a chimney's top, a fire's bed, a forge, a lantern's pane), in its
		/// own frame: what the cut writes into the scene so the client draws the smoke and lights the lamps. Built once.
		/// </summary>
		private static readonly Dictionary<(string, int), IReadOnlyList<StructureDoor>> doors = new Dictionary<(string, int), IReadOnlyList<StructureDoor>>();
		private static readonly Dictionary<(string, int), IReadOnlyList<StructureRoom>> rooms = new Dictionary<(string, int), IReadOnlyList<StructureRoom>>();

		/// <summary>A piece's rooms, in its own frame, from its finest build (cached). A ruin has none.</summary>
		public static IReadOnlyList<StructureRoom> RoomsOf(StructurePiece piece, int variant)
		{
			if (piece == null || variant < 0 || variant >= piece.Variants || piece.RuinOf != null)
			{
				return Array.Empty<StructureRoom>();
			}
			lock (rooms)
			{
				if (!rooms.TryGetValue((piece.Id, variant), out IReadOnlyList<StructureRoom> list))
				{
					var draft = new StructureDraft(piece, variant, 0, DefaultSeed);
					piece.Builder(draft);
					list = draft.Rooms.ToArray();
					rooms[(piece.Id, variant)] = list;
				}
				return list;
			}
		}

		/// <summary>
		/// A piece's doors, in its own frame, from its finest build at the default seed (cached). A ruin has none: its
		/// doorways stand empty.
		/// </summary>
		public static IReadOnlyList<StructureDoor> DoorsOf(StructurePiece piece, int variant)
		{
			if (piece == null || variant < 0 || variant >= piece.Variants || piece.RuinOf != null)
			{
				return Array.Empty<StructureDoor>();
			}
			lock (doors)
			{
				if (!doors.TryGetValue((piece.Id, variant), out IReadOnlyList<StructureDoor> list))
				{
					var draft = new StructureDraft(piece, variant, 0, DefaultSeed);
					piece.Builder(draft);
					list = draft.Doors.ToArray();
					doors[(piece.Id, variant)] = list;
				}
				return list;
			}
		}

		public static IReadOnlyList<(Vector3 at, SceneEmitterKind kind)> EmittersOf(StructurePiece piece, int variant)
		{
			if (piece == null || variant < 0 || variant >= piece.Variants)
			{
				return Array.Empty<(Vector3, SceneEmitterKind)>();
			}
			lock (emitters)
			{
				if (!emitters.TryGetValue((piece.Id, variant), out IReadOnlyList<(Vector3, SceneEmitterKind)> list))
				{
					var draft = new StructureDraft(piece, variant, 0, DefaultSeed);
					piece.Builder(draft);
					list = draft.Emitters.ToArray();
					emitters[(piece.Id, variant)] = list;
				}
				return list;
			}
		}

		/// <summary>The piece and variant a generated prefab's name names ("Structure_Tavern_1_Mossy"), or false.</summary>
		public static bool TryParsePrefab(string prefabName, out StructurePiece piece, out int variant)
		{
			piece = null;
			variant = 0;
			if (string.IsNullOrEmpty(prefabName) || !prefabName.StartsWith(Prefix, StringComparison.Ordinal))
			{
				return false;
			}
			string rest = prefabName.Substring(Prefix.Length);
			// The id may itself hold underscores in future; the variant is the first all-digit part after it.
			string[] parts = rest.Split('_');
			for (int i = 1; i < parts.Length; i++)
			{
				if (int.TryParse(parts[i], out int v))
				{
					piece = Find(string.Join("_", parts, 0, i));
					variant = v;
					return piece != null && v >= 0 && v < piece.Variants;
				}
			}
			return false;
		}

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
namespace FishMMO.Shared.WorldDesign
{
	/// <summary>What a room is for: what PointOfInterestInteriors furnishes it with.</summary>
	public enum StructureRoomKind
	{
		/// <summary>A home: a hearth, beds, a table and stools, a chest, a shelf.</summary>
		Dwelling,
		/// <summary>A long hall (a longhouse, a keep's great hall): a fire down the middle, long tables and benches, beds along the walls.</summary>
		Hall,
		/// <summary>A tavern's taproom: the bar and its barrels, tables with stools, a hearth.</summary>
		Tavern,
		/// <summary>A church's nave: pews facing the altar at the far end, a lectern.</summary>
		Church,
		/// <summary>A temple's cella: the statue at the back, an altar before it, braziers.</summary>
		Temple,
		/// <summary>A stable: stalls along the back wall, hay, a trough.</summary>
		Stable,
		/// <summary>A guard room (a tower, a watchtower's base): a rack of arms, a table and stool, crates and barrels, a cot.</summary>
		Guard,
		/// <summary>A tomb chamber: the sarcophagus.</summary>
		Tomb,
		/// <summary>A lighthouse keeper's room: a bed, a table and stool, a chest, oil barrels.</summary>
		Keeper,
		/// <summary>A house's upper storey: beds, chests, a shelf, a small table.</summary>
		Chamber,
		/// <summary>A tavern's upper storey: its guests' beds along the walls, chests, a table.</summary>
		Lodging,
	}

	/// <summary>One room of a structure piece, in its own frame: its floor's centre, clear size, height and kind; round or rectangular.</summary>
	public struct StructureRoom
	{
		public Vector3 Centre;
		/// <summary>Clear floor inside the walls (x, z); a round room's diameter in both.</summary>
		public Vector2 Size;
		public float Height;
		public StructureRoomKind Kind;
		public bool Round;
		/// <summary>
		/// Where its fireplace is built, against the chimney breast (<see cref="HasHearth"/>): the hearth's foot in the
		/// building's frame (x, z) and the way it faces into the room (degrees about +y). The interior puts its Hearth here,
		/// so the fire is under the chimney its smoke leaves by.
		/// </summary>
		public bool HasHearth;
		public Vector2 Hearth;
		public float HearthYaw;
		/// <summary>
		/// Floor kept clear of furniture, in the building's frame: each (min x, min z, max x, max z) — a stair, its landing,
		/// the well it climbs through (<see cref="StructureDraft.RoomClear"/>).
		/// </summary>
		public Vector4[] Clear;
	}

	/// <summary>How a door opens.</summary>
	public enum StructureDoorMotion
	{
		/// <summary>On hinges at one jamb, into the building.</summary>
		Swing,
		/// <summary>Straight up into the arch over it: a portcullis.</summary>
		Lift,
		/// <summary>
		/// Hinged along its foot: raised it stands shut across the gateway, lowered it lies out over the ditch as the way in.
		/// <see cref="StructureDoor.Height"/> is its length.
		/// </summary>
		Drawbridge,
	}

	/// <summary>What a door's leaf is made of and how it is built (the DoorLeaf piece's variant).</summary>
	public enum StructureDoorKind
	{
		/// <summary>Vertical planks on ledges and a brace, iron strap hinges and a ring: cottages, barns, longhouses.</summary>
		Planks = 0,
		/// <summary>Heavy boarded oak studded with iron: a church, a keep, a tavern, a temple.</summary>
		Studded = 1,
		/// <summary>An iron-plated door: a tower, a mausoleum, a crypt.</summary>
		Iron = 2,
	}

	/// <summary>
	/// One door of a structure piece, in the piece's own frame: the foot of its hinge jamb on the inside face of the wall,
	/// the way the leaf runs from the hinge to the latch, the way it swings open (into the building), its size and kind.
	/// The baked building keeps the doorway open; the door itself is a scene object of its own (PointOfInterestDoors).
	/// </summary>
	public struct StructureDoor
	{
		public Vector3 Hinge;
		/// <summary>Horizontal unit vector from the hinge along the closed leaf to the latch side.</summary>
		public Vector3 Across;
		/// <summary>Horizontal unit vector into the building: the leaf swings toward it.</summary>
		public Vector3 Inward;
		public float Width, Height;
		public StructureDoorKind Kind;
		public StructureDoorMotion Motion;
		/// <summary>Stands open when the scene loads (a town's gates by day, a lowered drawbridge).</summary>
		public bool StartsOpen;
	}
}
#endif
