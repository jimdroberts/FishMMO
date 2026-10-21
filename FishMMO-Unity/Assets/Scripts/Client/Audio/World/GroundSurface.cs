using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared;
using FishMMO.Shared.Weather;

namespace FishMMO.Client
{
	/// <summary>What a foot sounds on.</summary>
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

	/// <summary>The ground at a footfall as it sounds: the main surface, a second where two blend, and what lies on them.</summary>
	public struct SurfaceReading
	{
		public SurfaceKind Primary;
		public SurfaceKind Secondary;
		/// <summary>0..1: how much of the footfall is the second surface (0 when there is one).</summary>
		public float SecondaryShare;
		/// <summary>Metres of water standing over the ground (0 on dry ground).</summary>
		public float WaterDepth;
		/// <summary>0..1: how wet the ground is (rain, a shore), for the splash on a hard floor.</summary>
		public float Wet;
		/// <summary>Metres of standing grass the foot goes through.</summary>
		public float GrassHeight;
	}

	/// <summary>
	/// Says what a footfall lands on, from what the ray struck: a terrain's painted layers by their names, a streamed prop's
	/// kind and name (<see cref="PropColliderStreamer.TryPropAt"/>), any other collider by its names; then the weather's
	/// cover over it (snow, wet), the water standing on it, and the grass growing out of it.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>By name, as the layers are made.</b> The terrain layers are generated as <c>Ground_&lt;Kind&gt;</c>
	/// (Ground_Grass, Ground_Gravel, Ground_PathMud…) and the props are named for what they are (Tree_Oak, Cliff_Massive,
	/// Structure_TimberHouseSmall…), so a keyword table reads both. Each name is read once and remembered.
	/// </para>
	/// <para>
	/// <b>A client build keeps no alphamaps</b> once a scene's stored index map stands in for them
	/// (ClientTerrainLayerStripper): the layers are read off that map instead, and named by the bound
	/// <see cref="TerrainArraySet"/>, as the grass reads them.
	/// </para>
	/// </remarks>
	public static class GroundSurface
	{
		/// <summary>Snow cover over this share reads as snow, whatever is under it.</summary>
		public const float SnowCovers = 0.35f;

		/// <summary>Wetness over this turns bare earth to mud underfoot.</summary>
		public const float MudWetness = 0.6f;

		/// <summary>Water standing deeper than this over the ground is waded, metres.</summary>
		public const float WadeMetres = 0.03f;

		// What each name says, or null for nothing: read once per name, the fallback applied after.
		private static readonly Dictionary<string, SurfaceKind?> byName = new Dictionary<string, SurfaceKind?>();
		private static readonly Dictionary<int, SurfaceKind> byCollider = new Dictionary<int, SurfaceKind>();
		private static readonly int[] ids = new int[4];
		private static readonly float[] weights = new float[4];
		private static readonly float[] kindWeights = new float[12];

		/// <summary>The surface of a footfall: what it struck, then what covers it.</summary>
		public static SurfaceReading Read(in Footfall footfall)
		{
			var reading = new SurfaceReading();
			Vector3 at = footfall.Point;
			if (footfall.Ground is TerrainCollider terrainCollider)
			{
				Terrain terrain = terrainCollider.GetComponent<Terrain>();
				TerrainBlend(terrain, at, ref reading);
			}
			else if (footfall.Ground != null)
			{
				reading.Primary = OfCollider(footfall.Ground, footfall.Point, footfall.Normal);
			}
			else
			{
				reading.Primary = SurfaceKind.Dirt;
			}

			// The weather over it: what the cover map says lies here now (CoverMap, the same for every player).
			WeatherPresentation presentation = WeatherPresentation.Instance;
			if (presentation != null && presentation.CoverMap != null)
			{
				WeatherCover cover = presentation.CoverMap.Sample(at, default);
				reading.Wet = Mathf.Clamp01(cover.Wet);
				bool open = reading.Primary != SurfaceKind.Wood && reading.Primary != SurfaceKind.Metal;
				if (cover.Snow >= SnowCovers && open)
				{
					// Snow lies over the ground; a thin cover still lets the ground under it be heard.
					float under = Mathf.Clamp01(1f - (cover.Snow - SnowCovers) / (1f - SnowCovers)) * 0.5f;
					reading.Secondary = reading.Primary;
					reading.SecondaryShare = under;
					reading.Primary = SurfaceKind.Snow;
				}
				else if (reading.Wet >= MudWetness && (reading.Primary == SurfaceKind.Dirt || reading.Primary == SurfaceKind.Sand && reading.Wet > 0.85f))
				{
					reading.Primary = SurfaceKind.Mud;
				}
			}

			// Water standing over the ground, as the movement reads it (WaterQuery): a puddle's splash to a wade.
			UnityEngine.SceneManagement.Scene scene = footfall.Body != null ? footfall.Body.gameObject.scene : default;
			WaterSample water = scene.IsValid() ? WaterQuery.Sample(scene, at) : WaterSample.Dry;
			// Lava is no water to splash through (a lava tube's river, a lava sea): the step is heard on the rock it crusts.
			reading.WaterDepth = water.Present && water.Body != WaterBody.Lava ? Mathf.Max(0f, water.DepthOf(at.y)) : 0f;
			if (reading.WaterDepth > WadeMetres)
			{
				reading.Secondary = reading.Primary;
				// Ankle deep, the bed is still felt; past the shin, it is all water.
				reading.SecondaryShare = Mathf.Clamp01(1f - reading.WaterDepth / 0.25f) * 0.4f;
				reading.Primary = SurfaceKind.Water;
			}

			if (reading.Primary == SurfaceKind.Grass || reading.Secondary == SurfaceKind.Grass)
			{
				reading.GrassHeight = GrassBladeSystem.CoverAt(at.x, at.z);
			}
			return reading;
		}

		/// <summary>The two strongest kinds under a point of a terrain, from its painted layers.</summary>
		private static void TerrainBlend(Terrain terrain, Vector3 at, ref SurfaceReading reading)
		{
			reading.Primary = SurfaceKind.Dirt;
			if (terrain == null || terrain.terrainData == null)
			{
				return;
			}
			TerrainData data = terrain.terrainData;
			Vector3 origin = terrain.GetPosition();
			float u = Mathf.Clamp01((at.x - origin.x) / Mathf.Max(1f, data.size.x));
			float v = Mathf.Clamp01((at.z - origin.z) / Mathf.Max(1f, data.size.z));
			System.Array.Clear(kindWeights, 0, kindWeights.Length);
			int layers = data.alphamapLayers;
			if (layers > 0)
			{
				int res = data.alphamapResolution;
				int x = Mathf.Clamp(Mathf.RoundToInt(u * (res - 1)), 0, res - 1);
				int y = Mathf.Clamp(Mathf.RoundToInt(v * (res - 1)), 0, res - 1);
				float[,,] maps = data.GetAlphamaps(x, y, 1, 1);
				TerrainLayer[] painted = data.terrainLayers;
				for (int l = 0; l < layers; l++)
				{
					string name = painted != null && l < painted.Length && painted[l] != null ? painted[l].name : null;
					kindWeights[(int)OfName(name, SurfaceKind.Dirt)] += maps[0, 0, l];
				}
			}
			else
			{
				TerrainDetailBake.IndexMap index = SceneTerrainBake.IndexOf(data);
				if (index == null || index.Resolution <= 1 || index.Map == null || index.Map.Length < 2 * index.Resolution * index.Resolution)
				{
					return;
				}
				TerrainIndexMap.Filter(index.Map, index.Resolution, index.Resolution, u, v, ids, weights);
				string[] names = LayerNamesFor(terrain);
				for (int k = 0; k < 4; k++)
				{
					if (weights[k] <= 0f)
					{
						continue;
					}
					string name = names != null && ids[k] >= 0 && ids[k] < names.Length ? names[ids[k]] : null;
					kindWeights[(int)OfName(name, SurfaceKind.Dirt)] += weights[k];
				}
			}
			int first = 0, second = -1;
			for (int k = 1; k < kindWeights.Length; k++)
			{
				if (kindWeights[k] > kindWeights[first])
				{
					first = k;
				}
			}
			for (int k = 0; k < kindWeights.Length; k++)
			{
				if (k != first && kindWeights[k] > 0.001f && (second < 0 || kindWeights[k] > kindWeights[second]))
				{
					second = k;
				}
			}
			reading.Primary = (SurfaceKind)first;
			if (second >= 0)
			{
				float total = kindWeights[first] + kindWeights[second];
				reading.Secondary = (SurfaceKind)second;
				reading.SecondaryShare = total > 0f ? kindWeights[second] / total : 0f;
			}
		}

		/// <summary>The layer names of the set bound to a terrain's scene, for a tile whose own layers were stripped.</summary>
		private static string[] LayerNamesFor(Terrain terrain)
		{
			TerrainArraySet set = TerrainArrayBinder.SetFor(terrain);
			return set != null ? set.LayerNames : null;
		}

		/// <summary>What a collider that is not a terrain sounds like: a streamed prop by its kind and name, anything else by its names.</summary>
		public static SurfaceKind OfCollider(Collider collider, Vector3 point, Vector3 normal)
		{
			if (collider == null)
			{
				return SurfaceKind.Dirt;
			}
			if (PropColliderStreamer.TryPropAt(collider, TriangleUnder(collider, point, normal), out ScenePropCollisionSet set, out Mesh prototype))
			{
				string name = prototype != null ? prototype.name : null;
				SurfaceKind fallback = OfSource(set != null ? set.Source : null);
				return OfName(name, fallback);
			}
			int id = collider.GetInstanceID();
			if (byCollider.TryGetValue(id, out SurfaceKind known))
			{
				return known;
			}
			SurfaceKind kind = SurfaceKind.Stone;
			bool found = false;
			// The collider's own object and a few parents up: a house's floor is a child of the house.
			Transform t = collider.transform;
			for (int depth = 0; depth < 4 && t != null && !found; depth++, t = t.parent)
			{
				found = TryName(t.name, out kind);
			}
			if (!found && collider is MeshCollider mesh && mesh.sharedMesh != null)
			{
				found = TryName(mesh.sharedMesh.name, out kind);
			}
			if (!found && collider.TryGetComponent(out Renderer renderer) && renderer.sharedMaterial != null)
			{
				found = TryName(renderer.sharedMaterial.name, out kind);
			}
			if (!found)
			{
				kind = SurfaceKind.Stone;
			}
			byCollider[id] = kind;
			return kind;
		}

		/// <summary>The triangle a footfall's ray struck, cast again so the prop under it can be told (-1 when none).</summary>
		private static int TriangleUnder(Collider collider, Vector3 point, Vector3 normal)
		{
			if (!(collider is MeshCollider))
			{
				return -1;
			}
			Vector3 up = normal.sqrMagnitude > 0.01f ? normal.normalized : Vector3.up;
			var ray = new Ray(point + up * 0.25f, -up);
			return collider.Raycast(ray, out RaycastHit hit, 0.5f) ? hit.triangleIndex : -1;
		}

		/// <summary>What a streamed prop set's things mostly are, when the prop's own name says nothing.</summary>
		public static SurfaceKind OfSource(string source)
		{
			switch (source)
			{
				case "Scatter":
				case "Boulders":
				case "Cliffs":
				case "FallLedges":
				case "Bed":
					return SurfaceKind.Stone;
				case "Cloud Bridges":
				case "Sky Docks":
					return SurfaceKind.Wood;
				case "Floating Plants":
					return SurfaceKind.Leaves;
				// A settlement's buildings, by what most of their floors are: boards.
				case "POI":
					return SurfaceKind.Wood;
				default:
					return SurfaceKind.Stone;
			}
		}

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
			byCollider.Clear();
		}
	}
}
