using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>What a piece of ground is, as a foot feels it and an animal chooses it: read off a terrain layer's or a prop's name.</summary>
	/// <remarks>Append only: the ordinal is stored (the server's ground maps, <see cref="TerrainGroundKinds"/>).</remarks>
	public enum SurfaceKind
	{
		Dirt = 0,
		Grass = 1,
		Leaves = 2,
		Mud = 3,
		Sand = 4,
		Gravel = 5,
		Stone = 6,
		Wood = 7,
		Snow = 8,
		Ice = 9,
		Water = 10,
		Metal = 11,
	}

	/// <summary>
	/// The keyword table that names a ground: the terrain layers are generated as <c>Ground_&lt;Kind&gt;</c> (Ground_Grass,
	/// Ground_Gravel, Ground_PathMud…) and the props are named for what they are (Tree_Oak, Cliff_Massive…), so one table reads
	/// both. Moved into the core from the footsteps (GroundSurface) so the server build can read its terrain
	/// layers by the same words before it strips them (<see cref="TerrainGroundKinds"/>).
	/// </summary>
	public static class SurfaceNames
	{
		// What each name says, or null for nothing: read once per name, the fallback applied after.
		private static readonly Dictionary<string, SurfaceKind?> byName = new Dictionary<string, SurfaceKind?>();

		/// <summary>The kind a layer's or a prop's name says, or <paramref name="fallback"/>.</summary>
		public static SurfaceKind OfName(string name, SurfaceKind fallback)
		{
			if (string.IsNullOrEmpty(name))
			{
				return fallback;
			}
			if (!byName.TryGetValue(name, out SurfaceKind? said))
			{
				said = TryName(name, out SurfaceKind named) ? named : (SurfaceKind?)null;
				byName[name] = said;
			}
			return said ?? fallback;
		}

		/// <summary>
		/// The keyword table, by whole words of the name (split at case changes, digits and punctuation: "TimberHouseSmall"
		/// is timber, house, small). Kinds are tried in this order, so where a name has words of two kinds the first wins:
		/// "PathMud" is mud before path is earth, "StoneHouse" stone before house is boards, "ForestFloor" litter before
		/// floor is boards, "Cliff_Ice" ice before cliff is rock.
		/// </summary>
		private static readonly (SurfaceKind kind, HashSet<string> words)[] table =
		{
			(SurfaceKind.Snow, Words("snow", "frost", "drift", "firn")),
			(SurfaceKind.Ice, Words("ice", "icy", "glacier", "floe", "rime")),
			(SurfaceKind.Metal, Words("iron", "metal", "grate", "grating", "anvil", "cauldron", "bronze", "steel")),
			(SurfaceKind.Mud, Words("mud", "muddy", "peat", "sphagnum", "bog", "marsh", "swamp", "mire", "fen")),
			(SurfaceKind.Stone, Words("stone", "stones", "sandstone", "limestone", "flagstone", "cobble", "cobbles", "basalt", "cliff", "rock",
				"rocks", "boulder", "boulders", "brick", "lava", "pahoehoe", "sinter", "coral", "lineae", "castle", "keep", "wall", "temple",
				"shrine", "statue", "plinth", "gravestone", "grave", "sarcophagus", "well", "cave", "ledge", "ledges", "crag", "tomb", "crypt",
				"portal", "smithy", "forge", "tower", "granite", "marble", "slate", "bedrock", "outcrop", "tor", "monolith", "dolmen", "cairn",
				"pillar", "column", "arch", "chasm")),
			(SurfaceKind.Gravel, Words("gravel", "pebble", "pebbles", "cinder", "cinders", "grit", "scree", "shingle", "salt", "sulphur", "sulfur",
				"regolith", "reg", "talus", "rubble")),
			(SurfaceKind.Sand, Words("sand", "sandy", "dune", "dunes", "beach", "dust", "ash", "tholin", "loess")),
			(SurfaceKind.Leaves, Words("forest", "jungle", "needle", "needles", "litter", "leaf", "leaves", "fern", "ferns", "bush", "shrub",
				"plant", "plants", "undergrowth", "bracken")),
			(SurfaceKind.Grass, Words("grass", "meadow", "heath", "moss", "lichen", "straw", "hay", "thatch", "turf", "lawn", "sward", "tundra",
				"prairie", "steppe", "savanna", "reed", "reeds", "rushes")),
			(SurfaceKind.Wood, Words("wood", "wooden", "timber", "plank", "planks", "board", "boards", "deck", "dock", "pier", "jetty", "bridge",
				"trestle", "floor", "stair", "stairs", "ladder", "house", "hut", "cabin", "barn", "mill", "sawmill", "watermill", "tavern",
				"stables", "stable", "watchtower", "table", "stool", "bench", "crate", "barrel", "cart", "shipwreck", "wreck", "boat", "ship",
				"skiff", "cog", "slipway", "tree", "log", "logs", "stump", "trunk", "woodpile", "shelf", "rack", "fence", "palisade", "gate",
				"door", "stilt", "hall", "lodge", "shed", "porch", "balcony", "scaffold", "raft", "wharf", "boardwalk", "platform")),
			(SurfaceKind.Dirt, Words("soil", "clay", "silt", "earth", "loam", "dirt", "path", "road", "track", "trail", "tent", "canvas", "ground")),
		};

		private static readonly List<string> tokens = new List<string>();

		/// <summary>The kind a name's words say (<see cref="table"/>), false when no word is known.</summary>
		public static bool TryName(string name, out SurfaceKind kind)
		{
			kind = SurfaceKind.Dirt;
			if (string.IsNullOrEmpty(name))
			{
				return false;
			}
			Tokenise(name, tokens);
			foreach ((SurfaceKind candidate, HashSet<string> words) in table)
			{
				foreach (string token in tokens)
				{
					if (words.Contains(token))
					{
						kind = candidate;
						return true;
					}
				}
			}
			return false;
		}

		/// <summary>A name's words, lower case: split at anything not a letter and where a lower-case letter meets a capital.</summary>
		public static void Tokenise(string name, List<string> into)
		{
			into.Clear();
			int start = -1;
			for (int i = 0; i <= name.Length; i++)
			{
				char c = i < name.Length ? name[i] : ' ';
				bool letter = char.IsLetter(c);
				bool boundary = !letter || (start >= 0 && char.IsUpper(c) && char.IsLower(name[i - 1]));
				if (boundary && start >= 0)
				{
					into.Add(name.Substring(start, i - start).ToLowerInvariant());
					start = -1;
				}
				if (letter && start < 0)
				{
					start = i;
				}
			}
		}

		private static HashSet<string> Words(params string[] words) => new HashSet<string>(words);

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetOnPlay()
		{
			byName.Clear();
		}
	}
}
