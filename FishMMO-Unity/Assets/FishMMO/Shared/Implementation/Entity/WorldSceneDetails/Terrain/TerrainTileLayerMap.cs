using System.Collections.Generic;

namespace FishMMO.Shared
{
	/// <summary>
	/// The arithmetic of per-tile terrain layers (<see cref="TerrainTileLayers"/>): which of a tile's alphamap channels it keeps,
	/// the compacted alphamaps, and how a tile's channel numbers become the scene's layer ids wherever the ground is read by
	/// layer (the index map bake, the ground colour, the grass's surface layers). Pure, so the tests run it without Unity.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why.</b> Every tile of a cut used to carry the scene's whole palette: Moron 2's 210 layers are 53 control maps of 512²
	/// RGBA with mips, 74 MB a tile, when no tile paints more than 62 of them (measured over the saved tiles, 2026-10-27). A tile
	/// now keeps exactly the layers it paints: those with a non-zero weight byte anywhere on it. No weight changes, so every
	/// texel — the ones on a seam too — holds the bytes it held, and two tiles meeting at a seam agree there as before, whatever
	/// either keeps. Dropping light layers by a threshold as well was measured and refused: 1–5% fewer maps for a visible change.
	/// </para>
	/// <para>
	/// <b>The scene's numbering stays the truth.</b> The texture arrays, the layer table, the index maps, the backdrop's channels
	/// and the path slices all count the scene's layers (the binder's provenance, one entry per array slice). A tile's channel
	/// <c>c</c> is scene layer <c>SceneLayers[c]</c>, its channels kept in scene order, so ties between equal weights still go to
	/// the lower scene layer as they did. Null means a tile that carries the whole palette in scene order (every scene cut
	/// before this): channel and scene layer are the same number.
	/// </para>
	/// </remarks>
	public static class TerrainTileLayerMap
	{
		/// <summary>
		/// The most layers a tile may still paint after it is compacted, where the cut reports an ERROR: 24 control maps (33 MB a
		/// 512² tile). The busiest tile measured paints 65 (Moron 3 1_1, 2026-10-27).
		/// </summary>
		public const int MaximumTileLayers = 96;

		/// <summary>The scene layer a tile's channel holds (itself for a tile without a map).</summary>
		public static int SceneLayerOf(int[] sceneLayers, int channel)
		{
			return sceneLayers != null && channel >= 0 && channel < sceneLayers.Length ? sceneLayers[channel] : channel;
		}

		/// <summary>True when the map changes nothing: null, or every channel its own scene layer.</summary>
		public static bool IsIdentity(int[] sceneLayers)
		{
			if (sceneLayers == null)
			{
				return true;
			}
			for (int c = 0; c < sceneLayers.Length; c++)
			{
				if (sceneLayers[c] != c)
				{
					return false;
				}
			}
			return true;
		}

		/// <summary>
		/// The table an index map bake renames its ids by (null for identity): a tile's own scene layers, then — for the slots a
		/// tile of fewer than <see cref="TerrainIndexMap.Slots"/> layers fills with zero-weight phantoms (channels L..3) — the
		/// lowest scene layers it does not hold, so a texel's four ids stay distinct after the rename as they were before it.
		/// </summary>
		public static int[] BakeTable(int[] sceneLayers)
		{
			if (IsIdentity(sceneLayers))
			{
				return null;
			}
			int length = System.Math.Max(sceneLayers.Length, TerrainIndexMap.Slots);
			var table = new int[length];
			var used = new HashSet<int>();
			for (int c = 0; c < sceneLayers.Length; c++)
			{
				table[c] = sceneLayers[c];
				used.Add(sceneLayers[c]);
			}
			int next = 0;
			for (int c = sceneLayers.Length; c < length; c++)
			{
				while (used.Contains(next))
				{
					next++;
				}
				table[c] = next;
				used.Add(next);
			}
			return table;
		}

		/// <summary>A texel's id word with every id renamed by <paramref name="table"/> (ids past it unchanged).</summary>
		public static uint RemapIds(uint ids, int[] table)
		{
			if (table == null)
			{
				return ids;
			}
			uint result = 0u;
			for (int s = 0; s < TerrainIndexMap.Slots; s++)
			{
				int id = TerrainIndexMap.IdOf(ids, s);
				int to = id < table.Length ? table[id] : id;
				result |= (uint)(to & 255) << (8 * s);
			}
			return result;
		}

		/// <summary>
		/// A whole index map (two words a texel) renamed in place: the CPU half of what the GPU bake does with the same table, so
		/// a stored map and a live one say the same.
		/// </summary>
		public static uint[] Remap(uint[] map, int[] table)
		{
			if (map == null || table == null)
			{
				return map;
			}
			for (int i = 0; i + 1 < map.Length; i += 2)
			{
				map[i] = RemapIds(map[i], table);
			}
			return map;
		}

		/// <summary>
		/// The index map of a tile's alphamaps in scene layers: <see cref="TerrainIndexMap.Bake(float[,,])"/> of its channels, the
		/// ids renamed by its map. What the cut stores and what the client build checks it against.
		/// </summary>
		public static uint[] Bake(float[,,] alphas, int[] sceneLayers)
		{
			return Remap(TerrainIndexMap.Bake(alphas), BakeTable(sceneLayers));
		}

		/// <summary>
		/// The channels a tile keeps: every one painted (<paramref name="painted"/>: a non-zero weight byte anywhere on the tile),
		/// ordered by the scene layer each holds (<paramref name="sceneOfChannel"/>, -1 where unknown: those last, in channel
		/// order), at least one (channel 0 for a tile painted nowhere). A channel naming the same scene layer as an earlier kept
		/// one is still listed: <see cref="Compact"/> folds it into that one.
		/// </summary>
		public static int[] Keep(bool[] painted, int[] sceneOfChannel)
		{
			var keep = new List<int>();
			for (int c = 0; c < painted.Length; c++)
			{
				if (painted[c])
				{
					keep.Add(c);
				}
			}
			if (keep.Count == 0 && painted.Length > 0)
			{
				keep.Add(0);
			}
			// Stable by scene layer: a tile's channels are already in scene order unless a tool appended one by hand.
			var order = new List<int>(keep.Count);
			for (int i = 0; i < keep.Count; i++)
			{
				order.Add(i);
			}
			order.Sort((a, b) =>
			{
				int sa = SceneOf(sceneOfChannel, keep[a]), sb = SceneOf(sceneOfChannel, keep[b]);
				int bySlice = (sa < 0 ? int.MaxValue : sa).CompareTo(sb < 0 ? int.MaxValue : sb);
				return bySlice != 0 ? bySlice : a.CompareTo(b);
			});
			var result = new int[keep.Count];
			for (int i = 0; i < order.Count; i++)
			{
				result[i] = keep[order[i]];
			}
			return result;
		}

		private static int SceneOf(int[] sceneOfChannel, int channel) => sceneOfChannel != null && channel < sceneOfChannel.Length ? sceneOfChannel[channel] : channel;

		/// <summary>
		/// The scene layers of the kept channels, one per output channel of <see cref="Compact"/> (a scene layer named twice is
		/// listed once, at its first channel). Unknown channels (-1) are given scene layers past <paramref name="sceneLayerCount"/>,
		/// distinct, so they still read as themselves.
		/// </summary>
		public static int[] SceneLayersOf(int[] keep, int[] sceneOfChannel, int sceneLayerCount, out int[] outputOf)
		{
			var result = new List<int>();
			outputOf = new int[keep.Length];
			int unknown = sceneLayerCount;
			for (int i = 0; i < keep.Length; i++)
			{
				int scene = SceneOf(sceneOfChannel, keep[i]);
				if (scene < 0)
				{
					scene = unknown++;
				}
				int at = result.IndexOf(scene);
				if (at < 0)
				{
					at = result.Count;
					result.Add(scene);
				}
				outputOf[i] = at;
			}
			return result.ToArray();
		}

		/// <summary>
		/// One strip of a tile's alphamaps (<c>[row, x, channel]</c> as GetAlphamaps returns them) into the compacted tile's rows
		/// <paramref name="rowBase"/>..: output channel <paramref name="outputOf"/>[i] takes input channel <paramref name="keep"/>[i]
		/// — exactly, a weight read from a byte writes back to that byte — and two kept channels of one scene layer are summed.
		/// </summary>
		public static void Compact(float[,,] strip, int rowBase, int[] keep, int[] outputOf, float[,,] compact)
		{
			int rows = strip.GetLength(0), width = strip.GetLength(1);
			for (int r = 0; r < rows; r++)
			{
				for (int x = 0; x < width; x++)
				{
					for (int i = 0; i < keep.Length; i++)
					{
						float w = strip[r, x, keep[i]];
						if (w != 0f)
						{
							compact[rowBase + r, x, outputOf[i]] += w;
						}
					}
				}
			}
		}

		/// <summary>Marks every channel of a strip that has a non-zero weight byte anywhere in it.</summary>
		public static void MarkPainted(float[,,] strip, bool[] painted)
		{
			int rows = strip.GetLength(0), width = strip.GetLength(1), layers = System.Math.Min(strip.GetLength(2), painted.Length);
			for (int r = 0; r < rows; r++)
			{
				for (int x = 0; x < width; x++)
				{
					for (int l = 0; l < layers; l++)
					{
						if (!painted[l] && TerrainIndexMap.ByteOf(strip[r, x, l]) != 0)
						{
							painted[l] = true;
						}
					}
				}
			}
		}

		/// <summary>
		/// A tile's scene layer per channel: the index in <paramref name="scene"/> (the scene's layers, the binder's provenance) of
		/// each of <paramref name="own"/> (the tile's terrain layers), the first where one is listed twice; a layer the scene does
		/// not name, or a missing one, gets a distinct number past the scene's count.
		/// </summary>
		public static int[] Resolve<T>(IList<T> own, IList<T> scene) where T : class
		{
			var index = new Dictionary<T, int>();
			for (int i = 0; scene != null && i < scene.Count; i++)
			{
				if (scene[i] != null && !index.ContainsKey(scene[i]))
				{
					index[scene[i]] = i;
				}
			}
			var result = new int[own != null ? own.Count : 0];
			int unknown = scene != null ? scene.Count : 0;
			for (int c = 0; c < result.Length; c++)
			{
				result[c] = own[c] != null && index.TryGetValue(own[c], out int at) ? at : unknown++;
			}
			return result;
		}

		/// <summary>
		/// The scene's layers once its tiles carry their own: every layer <paramref name="recorded"/> names, in its order (none
		/// dropped or moved: the arrays, the stored index maps and the tiles' maps count by it), then every layer a tile carries
		/// that it does not, in tile order.
		/// </summary>
		public static List<T> Union<T>(IList<T> recorded, IEnumerable<IList<T>> tiles) where T : class
		{
			var result = new List<T>();
			var seen = new HashSet<T>();
			if (recorded != null)
			{
				foreach (T layer in recorded)
				{
					result.Add(layer);
					if (layer != null)
					{
						seen.Add(layer);
					}
				}
			}
			foreach (IList<T> own in tiles)
			{
				if (own == null)
				{
					continue;
				}
				foreach (T layer in own)
				{
					if (layer != null && seen.Add(layer))
					{
						result.Add(layer);
					}
				}
			}
			return result;
		}

		/// <summary>The control maps a tile of <paramref name="layers"/> layers carries (four channels each).</summary>
		public static int MapsFor(int layers) => (layers + 3) / 4;

		/// <summary>Bytes of a tile's control maps: <see cref="MapsFor"/> RGBA32 maps of <paramref name="resolution"/>², every mip.</summary>
		public static long ControlBytes(int layers, int resolution)
		{
			long bytes = 0;
			for (int r = resolution; r >= 1; r >>= 1)
			{
				bytes += 4L * r * r;
				if (r == 1)
				{
					break;
				}
			}
			return MapsFor(layers) * bytes;
		}
	}
}
