#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Per-tile terrain layers in the editor: the cut's compaction (every tile keeps only the layers it paints), the tiles' maps
	/// from the binder's provenance (<see cref="TerrainTileLayers.EditorResolve"/>), and the scene's palette for the tools that
	/// used to read it off the first tile.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Measured before it was built (2026-10-27, the saved tiles):</b> Moron 2's 16 tiles carried 210 layers each, 53 control
	/// maps, ~74 MB a tile (1186 MB the scene) when they paint 16–62 (207 MB); Moron 1 783 → 208 MB, Moron 3 537 → 101 MB,
	/// Belron 1 12.6 → 7 MB. The exact rule (a layer stays where any of its weight bytes is not zero) changes no texel, so seams,
	/// the index maps' choices and every reader's weights are what they were; only the channel numbers move, and every reader that
	/// turns a channel into a scene layer renames it (TerrainTileLayerMap).
	/// </para>
	/// <para>
	/// <b>Last in a cut and a repaint</b> (<see cref="SceneGenerator.BakePropsAndNavMesh"/>, before the terrain is declared
	/// finished): everything that paints the splat — the biome painter (which gives every tile the whole palette again), the
	/// margins, the landforms, the cliffs, a cloud world's stone — runs on whole-palette tiles as it always did. The index maps
	/// and the detail bake stored after it name scene layers.
	/// </para>
	/// </remarks>
	[InitializeOnLoad]
	public static class TerrainTileLayerSync
	{
		/// <summary>Rows of alphamaps read at a time (a strip of a 512² tile of 256 layers is 16 MB of floats).</summary>
		private const int StripRows = 32;

		// The binder found for a scene, by its handle: looked up once rather than on every bake.
		private static readonly Dictionary<int, TerrainArrayBinder> binders = new Dictionary<int, TerrainArrayBinder>();

		static TerrainTileLayerSync()
		{
			TerrainTileLayers.EditorResolve = Resolve;
		}

		/// <summary>The array binder of a scene, or null.</summary>
		public static TerrainArrayBinder BinderOf(Scene scene)
		{
			if (!scene.IsValid() || !scene.isLoaded)
			{
				return null;
			}
			if (binders.TryGetValue(scene.handle, out TerrainArrayBinder known) && known != null && known.gameObject.scene == scene)
			{
				return known;
			}
			binders.Remove(scene.handle);
			foreach (GameObject root in scene.GetRootGameObjects())
			{
				TerrainArrayBinder found = root.GetComponentInChildren<TerrainArrayBinder>(true);
				if (found != null)
				{
					binders[scene.handle] = found;
					return found;
				}
			}
			return null;
		}

		/// <summary>
		/// A tile's scene layer per channel from its binder's provenance: the provenance index of each of its terrain layers (the
		/// first, where one is listed twice), and for a layer the provenance does not hold (added by hand, not yet synced) a
		/// distinct number past it. Null where the tile has no alphamaps or its scene no provenance.
		/// </summary>
		public static int[] Resolve(Terrain terrain)
		{
			TerrainData data = terrain != null ? terrain.terrainData : null;
			if (data == null || data.alphamapLayers <= 0)
			{
				return null;
			}
			TerrainArrayBinder binder = BinderOf(terrain.gameObject.scene);
			TerrainArrayProvenance provenance = binder != null ? binder.Provenance : null;
			if (provenance == null || provenance.Layers == null || provenance.Layers.Count == 0)
			{
				return null;
			}
			return Resolve(data.terrainLayers, provenance.Layers);
		}

		/// <inheritdoc cref="Resolve(Terrain)"/>
		public static int[] Resolve(TerrainLayer[] own, List<TerrainArrayLayerSource> scene)
		{
			return TerrainTileLayerMap.Resolve(own, LayersOf(scene));
		}

		/// <summary>The layer of each provenance entry, in order (null for an empty entry).</summary>
		public static List<TerrainLayer> LayersOf(List<TerrainArrayLayerSource> scene)
		{
			var layers = new List<TerrainLayer>(scene != null ? scene.Count : 0);
			if (scene != null)
			{
				foreach (TerrainArrayLayerSource source in scene)
				{
					layers.Add(source != null ? source.Layer : null);
				}
			}
			return layers;
		}

		/// <summary>True when any tile of the binder carries its own map (the scene was compacted: its tiles may differ).</summary>
		public static bool IsCompacted(TerrainArrayBinder binder)
		{
			if (binder == null)
			{
				return false;
			}
			foreach (Terrain terrain in binder.EffectiveTerrains())
			{
				if (terrain != null && terrain.TryGetComponent(out TerrainTileLayers own) && own.SceneLayers != null && own.SceneLayers.Length > 0)
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>
		/// Writes a tile's map on its <see cref="TerrainTileLayers"/> (added where it has none and the map is not the identity).
		/// A null map (channel = scene layer) clears one that is there to the identity of its channels. True when it changed.
		/// </summary>
		public static bool Record(Terrain terrain, int[] sceneLayers)
		{
			if (terrain == null)
			{
				return false;
			}
			TerrainTileLayers own = terrain.GetComponent<TerrainTileLayers>();
			if (own == null && sceneLayers == null)
			{
				return false;
			}
			int[] map = sceneLayers;
			if (map == null)
			{
				int layers = terrain.terrainData != null ? terrain.terrainData.alphamapLayers : 0;
				map = new int[layers];
				for (int c = 0; c < layers; c++)
				{
					map[c] = c;
				}
			}
			if (own == null)
			{
				own = terrain.gameObject.AddComponent<TerrainTileLayers>();
			}
			else if (Same(own.SceneLayers, map))
			{
				return false;
			}
			own.SceneLayers = (int[])map.Clone();
			EditorUtility.SetDirty(own);
			return true;
		}

		private static bool Same(int[] a, int[] b)
		{
			if (a == null || b == null || a.Length != b.Length)
			{
				return false;
			}
			for (int i = 0; i < a.Length; i++)
			{
				if (a[i] != b[i])
				{
					return false;
				}
			}
			return true;
		}

		/// <summary>
		/// Every tile's <see cref="TerrainTileLayers"/> brought in line with its binder's provenance (after the provenance grew, or a
		/// layer was added to a tile by hand). Tiles without one are left without one. Returns how many changed.
		/// </summary>
		public static int Refresh(TerrainArrayBinder binder)
		{
			TerrainArrayProvenance provenance = binder != null ? binder.Provenance : null;
			if (provenance == null || provenance.Layers == null)
			{
				return 0;
			}
			int changed = 0;
			foreach (Terrain terrain in binder.EffectiveTerrains())
			{
				if (terrain == null || terrain.terrainData == null || !terrain.TryGetComponent(out TerrainTileLayers _))
				{
					continue;
				}
				changed += Record(terrain, Resolve(terrain.terrainData.terrainLayers, provenance.Layers)) ? 1 : 0;
			}
			if (changed > 0 && !Application.isPlaying && !BuildPipeline.isBuildingPlayer)
			{
				// The maps are scene data: saved with it.
				EditorSceneManager.MarkSceneDirty(binder.gameObject.scene);
			}
			return changed;
		}

		/// <summary>
		/// The scene's terrain layers in scene order — the slices of its arrays: the binder's provenance where it has one, else
		/// the first tile's layers (a scene that is not drawn by arrays carries one palette on every tile).
		/// </summary>
		public static TerrainLayer[] ScenePalette(Scene scene, IEnumerable<Terrain> tiles)
		{
			TerrainArrayBinder binder = BinderOf(scene);
			TerrainArrayProvenance provenance = binder != null ? binder.Provenance : null;
			if (provenance != null && provenance.Layers != null && provenance.Layers.Count > 0)
			{
				var layers = new TerrainLayer[provenance.Layers.Count];
				for (int i = 0; i < layers.Length; i++)
				{
					layers[i] = provenance.Layers[i] != null ? provenance.Layers[i].Layer : null;
				}
				return layers;
			}
			if (tiles != null)
			{
				foreach (Terrain terrain in tiles)
				{
					if (terrain != null && terrain.terrainData != null)
					{
						return terrain.terrainData.terrainLayers;
					}
				}
			}
			return null;
		}

		/// <summary>What <see cref="Compact"/> did, for the notes and the tests.</summary>
		public sealed class Report
		{
			public int Tiles, Compacted, MaxLayers, LayersBefore, LayersAfter, Unknown, Merged, Mismatched;
			public long BytesBefore, BytesAfter;
			public string Busiest;
			public readonly List<string> OverCap = new List<string>();

			public override string ToString() =>
				$"Terrain layers per tile: {Compacted} of {Tiles} tile(s) compacted to the layers they paint, {LayersBefore} → {LayersAfter} layer channels " +
				$"({BytesBefore / 1048576.0:0.0} → {BytesAfter / 1048576.0:0.0} MB of control maps); at most {MaxLayers} on a tile{(Busiest != null ? $" ('{Busiest}')" : "")}.";
		}

		/// <summary>
		/// Gives every tile of a scene only the terrain layers it paints: those with a non-zero weight byte anywhere on it, in
		/// scene order, every weight byte unchanged; and records which scene layer each channel holds (<see cref="TerrainTileLayers"/>).
		/// A loud ERROR (note and log) for a tile still past <see cref="TerrainTileLayerMap.MaximumTileLayers"/>. Nothing happens in
		/// a scene with no array binder or no provenance (its channels have no scene numbering to keep).
		/// </summary>
		public static Report Compact(Scene scene, IReadOnlyList<Terrain> tiles, List<string> notes)
		{
			var report = new Report();
			TerrainArrayBinder binder = BinderOf(scene);
			if (binder == null || binder.Provenance == null)
			{
				notes?.Add("Terrain layers per tile: the scene has no array binder with provenance, so every tile keeps its layers.");
				return report;
			}
			// Every layer a tile carries named by the provenance first (a cloud world's stone, appended to some tiles).
			TerrainArraySetup.SyncProvenance(binder);
			List<TerrainArrayLayerSource> sceneLayers = binder.Provenance.Layers;
			if (sceneLayers == null || sceneLayers.Count == 0)
			{
				notes?.Add("Terrain layers per tile: the scene's provenance names no layers, so every tile keeps its layers.");
				return report;
			}
			foreach (Terrain terrain in tiles)
			{
				TerrainData data = terrain != null ? terrain.terrainData : null;
				if (data == null || data.alphamapLayers <= 0)
				{
					continue;
				}
				report.Tiles++;
				CompactTile(terrain, sceneLayers, report);
			}
			if (report.Tiles > 0)
			{
				notes?.Add(report.ToString());
			}
			if (report.Unknown > 0)
			{
				notes?.Add($"Terrain layers per tile: {report.Unknown} channel(s) hold a layer the scene's provenance does not name; they keep numbers past its {sceneLayers.Count} layers.");
			}
			if (report.Merged > 0)
			{
				notes?.Add($"Terrain layers per tile: {report.Merged} channel(s) repeated a layer another channel of their tile holds, and were summed into it.");
			}
			if (report.Mismatched > 0)
			{
				string error = $"ERROR: terrain layers per tile: {report.Mismatched} texel weight(s) did not read back as written after compacting. The ground may differ from the cut's paint; repaint the scene.";
				notes?.Add(error);
				Debug.LogError($"[Terrain layers] {error}");
			}
			if (report.OverCap.Count > 0)
			{
				string error = $"ERROR: {report.OverCap.Count} tile(s) still paint more than {TerrainTileLayerMap.MaximumTileLayers} terrain layers after compacting ({string.Join(", ", report.OverCap)}). Each layer is a quarter of a 512² control map on that tile; fewer biomes or detail layers meeting on one tile bring it down.";
				notes?.Add(error);
				Debug.LogError($"[Terrain layers] {error}");
			}
			EditorSceneManager.MarkSceneDirty(scene);
			binder.Push();
			return report;
		}

		private static void CompactTile(Terrain terrain, List<TerrainArrayLayerSource> sceneLayers, Report report)
		{
			TerrainData data = terrain.terrainData;
			int layers = data.alphamapLayers, res = data.alphamapResolution;
			TerrainLayer[] own = data.terrainLayers;
			int[] sceneOf = Resolve(own, sceneLayers);
			for (int c = 0; c < sceneOf.Length; c++)
			{
				report.Unknown += sceneOf[c] >= sceneLayers.Count ? 1 : 0;
				if (sceneOf[c] >= sceneLayers.Count)
				{
					sceneOf[c] = -1;
				}
			}

			var painted = new bool[layers];
			for (int y = 0; y < res; y += StripRows)
			{
				TerrainTileLayerMap.MarkPainted(data.GetAlphamaps(0, y, res, Mathf.Min(StripRows, res - y)), painted);
			}
			int[] keep = TerrainTileLayerMap.Keep(painted, sceneOf);
			int[] map = TerrainTileLayerMap.SceneLayersOf(keep, sceneOf, sceneLayers.Count, out int[] outputOf);
			report.Merged += keep.Length - map.Length;
			bool unchanged = map.Length == layers;
			for (int i = 0; unchanged && i < keep.Length; i++)
			{
				unchanged = keep[i] == i && outputOf[i] == i;
			}

			report.LayersBefore += layers;
			report.LayersAfter += map.Length;
			report.BytesBefore += TerrainTileLayerMap.ControlBytes(layers, res);
			report.BytesAfter += TerrainTileLayerMap.ControlBytes(map.Length, res);
			if (map.Length > report.MaxLayers)
			{
				report.MaxLayers = map.Length;
				report.Busiest = terrain.name;
			}
			if (map.Length > TerrainTileLayerMap.MaximumTileLayers)
			{
				report.OverCap.Add($"'{terrain.name}' {map.Length}");
			}

			if (!unchanged)
			{
				var compact = new float[res, res, map.Length];
				for (int y = 0; y < res; y += StripRows)
				{
					TerrainTileLayerMap.Compact(data.GetAlphamaps(0, y, res, Mathf.Min(StripRows, res - y)), y, keep, outputOf, compact);
				}
				var kept = new TerrainLayer[map.Length];
				for (int i = 0; i < keep.Length; i++)
				{
					kept[outputOf[i]] ??= own[keep[i]];
				}
				// Layers first (which sizes the splat maps to their count), then every weight.
				data.terrainLayers = kept;
				data.SetAlphamaps(0, 0, compact);
				report.Mismatched += ReadBackMismatches(data, compact);
				EditorUtility.SetDirty(data);
				report.Compacted++;
			}
			Record(terrain, map);
		}

		/// <summary>Texel weights that do not read back as the bytes written (a weight read from a byte must write back to it).</summary>
		private static int ReadBackMismatches(TerrainData data, float[,,] written)
		{
			int res = data.alphamapResolution, layers = written.GetLength(2), bad = 0;
			if (data.alphamapLayers != layers)
			{
				return res * res;
			}
			for (int y = 0; y < res; y += StripRows)
			{
				int rows = Mathf.Min(StripRows, res - y);
				float[,,] strip = data.GetAlphamaps(0, y, res, rows);
				for (int r = 0; r < rows; r++)
				{
					for (int x = 0; x < res; x++)
					{
						for (int l = 0; l < layers; l++)
						{
							if (TerrainIndexMap.ByteOf(strip[r, x, l]) != TerrainIndexMap.ByteOf(written[y + r, x, l]))
							{
								bad++;
							}
						}
					}
				}
			}
			return bad;
		}

		[DashboardTool(DashboardToolAttribute.Biomes, "Compact terrain layers per tile (open scene)", Section = "Diagnostics", Order = 118,
			Tooltip = "Gives every terrain tile of the active scene only the terrain layers it paints (every weight kept, so the ground does not change), what a cut or repaint now does at its end, then bakes the scene's terrain details and index maps again. A scene cut before 2026-10-27 carries its whole palette on every tile (Moron 2: 53 control maps, ~74 MB a tile). Save the scene after.",
			Confirm = "Compact the active scene's terrain layers per tile? Each tile's layer list and alphamaps are rewritten to the layers it paints (weights unchanged), and its terrain details and index maps are baked again.")]
		public static void CompactActiveScene()
		{
			Scene scene = SceneManager.GetActiveScene();
			var terrains = new List<Terrain>();
			foreach (GameObject root in scene.GetRootGameObjects())
			{
				terrains.AddRange(root.GetComponentsInChildren<Terrain>(true));
			}
			var notes = new List<string>();
			Report report = Compact(scene, terrains, notes);
			if (report.Compacted > 0)
			{
				// The stored index maps and the detail bake name the old channels: bake them again, beside the terrain (as
				// "Bake terrain grass and scatter (open scene)" does).
				SceneTerrainEvents.RaiseFinished(scene, terrains, notes);
				AssetDatabase.SaveAssets();
			}
			foreach (string note in notes)
			{
				Debug.Log($"[Terrain layers] {note}");
			}
		}
	}
}
#endif
