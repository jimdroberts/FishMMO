using System.Collections.Generic;
using System.Diagnostics;
using FishMMO.Shared;
using FishMMO.Shared.WorldDesign;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

namespace FishMMO.Client
{
	/// <summary>
	/// Bakes a scene's blade grass and GPU detail scatter maps when its terrain is finished (the end of a cut or a repaint,
	/// <see cref="SceneTerrainEvents.Finished"/>), into <c>&lt;Scene&gt; Terrain Details.asset</c> beside the terrain, held by
	/// a client-only <see cref="SceneTerrainBake"/> at the scene's root. The client then takes each terrain from it at once
	/// (GrassTerrain.Builder.TakeBaked, DetailScatterTerrain.Builder.TakeBaked) instead of reading the terrain back over the
	/// first ~1,400 frames of play.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>The same build.</b> Each terrain is built by the runtime's own builders, chosen by the runtime's own selection
	/// (GrassBladeSystem.SelectBlades, DetailScatterSystem.SelectScattered) from the project's render profile, run inline,
	/// and their maps kept as they are made: what play would have built, byte for byte.
	/// </para>
	/// <para>
	/// <b>Index maps.</b> Every array-drawn terrain's index map is stored with it (<see cref="TerrainDetailBake.IndexMap"/>,
	/// the CPU bake <see cref="TerrainIndexMap.Bake(float[,,])"/> of the alphamaps the cut just wrote: the GPU bake's texels
	/// exactly), so play binds it instead of baking, and a client build may leave the alphamaps out. Writing a terrain's
	/// alphamaps afterwards (a brush, an undo, a script) marks its map stale, in edit mode and in play; the editor keeps
	/// baking live from the alphamaps until the scene is repainted or baked again here.
	/// </para>
	/// <para>
	/// <b>Going stale.</b> Editing a terrain's heights or textures by hand marks its entry stale (Unity's terrain callbacks),
	/// and the client builds that terrain as before. Painting details by hand raises no callback: rebake with Dashboard →
	/// Biomes → "Bake terrain grass and scatter (open scene)", or repaint. An entry also goes unused when anything it was
	/// made from differs at load: <see cref="TerrainDetailBake.Version"/>, the prototypes and their prefabs, the
	/// resolutions, the scatter's density scale (the quality level's detail density: a player on another level builds it).
	/// </para>
	/// </remarks>
	[InitializeOnLoad]
	public static class TerrainDetailBaker
	{
		/// <summary>The bake's name after the scene's (ScenePropBaker.AssetPath).</summary>
		public const string AssetName = "Terrain Details";

		/// <summary>Advance calls a terrain's build may take before it is given up as stuck (each is at least a strip).</summary>
		private const int MaxSteps = 1_000_000;

		static TerrainDetailBaker()
		{
			SceneTerrainEvents.Finished -= OnTerrainFinished;
			SceneTerrainEvents.Finished += OnTerrainFinished;
			TerrainCallbacks.heightmapChanged -= OnHeightsChanged;
			TerrainCallbacks.heightmapChanged += OnHeightsChanged;
			TerrainCallbacks.textureChanged -= OnTextureChanged;
			TerrainCallbacks.textureChanged += OnTextureChanged;
		}

		private static void OnTerrainFinished(Scene scene, IReadOnlyList<Terrain> terrains, List<string> notes) => Bake(scene, terrains, notes);

		[DashboardTool(DashboardToolAttribute.Biomes, "Bake terrain grass and scatter (open scene)", Section = "Diagnostics", Order = 117,
			Tooltip = "Bakes the active scene's blade grass and detail scatter maps, and stores each array terrain's index map, beside its terrain (what a cut or repaint does at its end), so play takes them at once instead of building them and client builds can leave the alphamaps out. Run it after painting the ground by hand. Save the scene after.")]
		public static void BakeActiveScene()
		{
			Scene scene = SceneManager.GetActiveScene();
			var terrains = new List<Terrain>();
			foreach (GameObject root in scene.GetRootGameObjects())
			{
				terrains.AddRange(root.GetComponentsInChildren<Terrain>(true));
			}
			var notes = new List<string>();
			TerrainDetailBake bake = Bake(scene, terrains, notes);
			foreach (string note in notes)
			{
				Debug.Log($"[Terrain details] {note}");
			}
			if (bake != null)
			{
				EditorSceneManager.MarkSceneDirty(scene);
			}
		}

		/// <summary>
		/// Bakes the terrains' maps and the scene's texture sets' colour copies into the scene's bake (made, or written into
		/// the one there, so the scene's reference survives) and points the scene at it. Null, with a note, when the scene
		/// has no terrain folder.
		/// </summary>
		public static TerrainDetailBake Bake(Scene scene, IReadOnlyList<Terrain> terrains, List<string> notes)
		{
			string path = ScenePropBaker.AssetPath(scene, AssetName);
			if (path == null)
			{
				notes?.Add("Terrain grass and scatter not baked: the scene has no terrain folder yet (the client builds them in play).");
				return null;
			}
			var watch = Stopwatch.StartNew();
			WeatherRenderProfile profile = WeatherRenderProfile.Active;
			GrassBladeSettings grassSettings = profile != null && profile.Grass != null ? profile.Grass : new GrassBladeSettings();
			DetailScatterSettings scatterSettings = profile != null && profile.DetailScatter != null ? profile.DetailScatter : new DetailScatterSettings();

			var bake = ScriptableObject.CreateInstance<TerrainDetailBake>();
			bake.name = $"{SceneName(scene, path)} {AssetName}";
			bake.BakedVersion = TerrainDetailBake.Version;
			var grassTypes = new List<GrassType>();
			var grassKeys = new List<(GameObject Prefab, string Look)>();
			// The templates as they are now (a cut may have just authored them): the same looks play will choose.
			GrassBladeSystem.ForgetEditorBiomes();
			var scatterTypes = new List<DetailScatterType>();
			var scatterByPrefab = new Dictionary<GameObject, DetailScatterType>();
			var warned = new HashSet<string>();
			var prototypes = new List<int>();
			var chosenGrass = new List<GrassType>();
			var chosenScatter = new List<DetailScatterType>();
			// Every detail kind the GPU renderers cannot draw, or that a cap leaves out (2026-10-27: GPU only, there is no CPU
			// path): the cut reports them as errors, never a quiet fallback.
			var problems = new List<string>();
			int grassTerrains = 0, scatterTerrains = 0, indexTerrains = 0;
			bool wasInline = TerrainBuildQueue.Inline;
			TerrainBuildQueue.Inline = true;
			try
			{
				foreach (Terrain terrain in terrains)
				{
					TerrainData data = terrain != null ? terrain.terrainData : null;
					if (data == null)
					{
						continue;
					}
					var tile = new TerrainDetailBake.Tile { Data = data, HeightResolution = data.heightmapResolution };
					ushort[] heights = null;
					indexTerrains += StoreIndex(terrain, tile.Index) ? 1 : 0;

					GrassBladeSystem.SelectBlades(terrain, grassSettings, grassTypes, grassKeys, warned, prototypes, chosenGrass, problems);
					GrassTerrain.Builder grass = prototypes.Count > 0 ? GrassTerrain.Begin(terrain, prototypes, chosenGrass) : null;
					if (grass != null)
					{
						grass.Capture = true;
						problems.AddRange(grass.Overflow);
						if (Run(grass.Step) && grass.Captured != null)
						{
							tile.Grass = grass.Captured;
							heights = grass.CapturedHeights;
							grassTerrains += tile.Grass.Present ? 1 : 0;
						}
						grass.Result?.Dispose();
					}

					DetailScatterSystem.SelectScattered(data, terrain.gameObject.layer, scatterSettings, grassSettings, scatterTypes, scatterByPrefab, warned,
						prototypes, chosenScatter, problems);
					DetailScatterTerrain.Builder scatter = prototypes.Count > 0
						? DetailScatterTerrain.Begin(terrain, prototypes, chosenScatter, TerrainDetailInstancing.DensityScaleOf(terrain)) : null;
					if (scatter != null)
					{
						scatter.Capture = true;
						foreach (string over in scatter.Overflow)
						{
							problems.Add($"[Detail scatter] '{terrain.name}' paints more than {DetailScatterMath.MaxChannels} scattered types: '{over}' is not drawn there.");
						}
						if (Run(scatter.Step) && scatter.Captured != null)
						{
							tile.Scatter = scatter.Captured;
							heights ??= scatter.CapturedHeights;
							scatterTerrains += tile.Scatter.Present ? 1 : 0;
						}
						scatter.Result?.Dispose();
					}

					if (tile.Grass.Baked || tile.Scatter.Baked || tile.Index.Format != 0)
					{
						tile.Heights = heights ?? new ushort[0];
						bake.Tiles.Add(tile);
					}
				}
				ReportProblems(scene, problems, notes);
				int albedos = BakeAlbedos(scene, path, bake);
				bool ground = BakeGround(scene, path, terrains, bake);
				bake = ScenePropBaker.Store(bake, path);
				SceneTerrainBake holder = EnsureHolder(scene);
				holder.Bake = bake;
				EditorUtility.SetDirty(holder);
				EditorUtility.SetDirty(bake);
				EditorSceneManager.MarkSceneDirty(scene);
				long bytes = Bytes(bake);
				notes?.Add($"Terrain grass and scatter baked for {bake.Tiles.Count} terrain(s) ({grassTerrains} with blade grass, {scatterTerrains} with scatter, {indexTerrains} index map(s) stored, " +
					$"{albedos} texture set colour cop{(albedos == 1 ? "y" : "ies")}, {(ground ? $"the {bake.Ground.Width}×{bake.Ground.Height} ground colour map" : "no ground colour map")}; " +
					$"{bytes / 1048576.0:0.0} MB) in {watch.Elapsed.TotalSeconds:0.0} s: play takes them at once.");
				return bake;
			}
			finally
			{
				TerrainBuildQueue.Inline = wasInline;
			}
		}

		/// <summary>
		/// The cut's errors for detail kinds no GPU renderer draws (unrouted, refused, or past a cap): one note naming each
		/// distinct problem (the first <see cref="MaxProblemNotes"/>), and one error in the log. Nothing when every painted kind
		/// is drawn.
		/// </summary>
		public static void ReportProblems(Scene scene, List<string> problems, List<string> notes)
		{
			var distinct = new List<string>();
			foreach (string problem in problems)
			{
				if (!distinct.Contains(problem))
				{
					distinct.Add(problem);
				}
			}
			if (distinct.Count == 0)
			{
				return;
			}
			string head = $"ERROR: {distinct.Count} terrain detail problem(s) in '{scene.name}' (GPU only: these kinds are not drawn or not drawn as authored)";
			notes?.Add(head + ":");
			for (int i = 0; i < distinct.Count && i < MaxProblemNotes; i++)
			{
				notes?.Add("  " + distinct[i]);
			}
			if (distinct.Count > MaxProblemNotes)
			{
				notes?.Add($"  … and {distinct.Count - MaxProblemNotes} more (the log has each).");
			}
			Debug.LogError($"[Terrain details] {head}. First: {distinct[0]}");
		}

		/// <summary>The most detail problems a cut's notes list one by one.</summary>
		public const int MaxProblemNotes = 20;

		/// <summary>
		/// Stores an array-drawn terrain's index map in <paramref name="index"/>: the CPU bake of its alphamaps as they are now
		/// (read once, whole). False, and <paramref name="index"/> left empty, for a terrain on another material or without
		/// layers.
		/// </summary>
		public static bool StoreIndex(Terrain terrain, TerrainDetailBake.IndexMap index)
		{
			TerrainData data = terrain != null ? terrain.terrainData : null;
			if (data == null || index == null || !ClientTerrainLayerStripper.IsArrayTerrain(terrain) || data.alphamapLayers <= 0
				|| data.alphamapLayers > TerrainIndexMap.MaximumLayers)
			{
				return false;
			}
			int res = data.alphamapResolution;
			// In scene layers: a tile carrying only the layers it paints (TerrainTileLayers) is renamed as the GPU bake renames it.
			index.Map = TerrainTileLayerMap.Bake(data.GetAlphamaps(0, 0, res, res), TerrainTileLayers.Of(terrain));
			index.Resolution = res;
			index.Layers = data.alphamapLayers;
			index.Stale = false;
			index.Format = TerrainIndexMap.StoredFormat;
			return true;
		}

		/// <summary>Steps a build (inline, so nothing waits on a worker) until it is done; false if it never finished.</summary>
		private static bool Run(System.Func<double, bool> step)
		{
			for (int i = 0; i < MaxSteps; i++)
			{
				if (step(double.MaxValue))
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>
		/// The colour copy of each texture set the scene's terrains are drawn with (GrassAlbedoTexels: a blit and a readback a
		/// layer, so only with a graphics device; without one the client reads them back in play as before). Returns how many.
		/// </summary>
		private static int BakeAlbedos(Scene scene, string path, TerrainDetailBake bake)
		{
			if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
			{
				return 0;
			}
			var done = new HashSet<Texture2DArray>();
			foreach (GameObject root in scene.GetRootGameObjects())
			{
				foreach (TerrainArrayBinder binder in root.GetComponentsInChildren<TerrainArrayBinder>(true))
				{
					string sceneName = !string.IsNullOrEmpty(binder.SceneKey) ? binder.SceneKey : SceneName(scene, path);
					var set = AssetDatabase.LoadAssetAtPath<TerrainArraySet>(TerrainArraySet.BakedAssetPath(sceneName));
					if (set == null || !set.IsUsable || !done.Add(set.Albedo))
					{
						continue;
					}
					GrassAlbedoTexels texels = GrassAlbedoTexels.Build(set.Albedo, set.LayerCount, set.LayerST, set.LayerTint, set.LayerFlora());
					if (texels == null)
					{
						continue;
					}
					bake.Albedos.Add(new TerrainDetailBake.Albedo
					{
						Source = set.Albedo,
						Layers = texels.Layers,
						Width = texels.Width,
						Mips = texels.Mips,
						LayerTexels = texels.LayerTexels,
						Pyramids = texels.Pyramids(),
					});
				}
			}
			return bake.Albedos.Count;
		}

		/// <summary>
		/// The ground colour map over the scene's active terrains (GroundColourMap.Compute, as play works it out), drawn with
		/// the texture sets their binders load. Needs a graphics device (each layer's colour is read back off the GPU); false
		/// without one, and play works it out as before.
		/// </summary>
		private static bool BakeGround(Scene scene, string path, IReadOnlyList<Terrain> terrains, TerrainDetailBake bake)
		{
			if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
			{
				return false;
			}
			var live = new List<Terrain>();
			foreach (Terrain t in terrains)
			{
				if (t != null && t.terrainData != null && t.enabled && t.gameObject.activeInHierarchy)
				{
					live.Add(t);
				}
			}
			var sets = new Dictionary<Terrain, TerrainArraySet>();
			foreach (GameObject root in scene.GetRootGameObjects())
			{
				foreach (TerrainArrayBinder binder in root.GetComponentsInChildren<TerrainArrayBinder>(true))
				{
					string sceneName = !string.IsNullOrEmpty(binder.SceneKey) ? binder.SceneKey : SceneName(scene, path);
					var set = AssetDatabase.LoadAssetAtPath<TerrainArraySet>(TerrainArraySet.BakedAssetPath(sceneName));
					if (set == null || set.Albedo == null)
					{
						continue;
					}
					foreach (Terrain t in binder.EffectiveTerrains())
					{
						if (t != null)
						{
							sets[t] = set;
						}
					}
				}
			}
			if (!GroundColourMap.Compute(live, sets, out Rect bounds, out float texel, out int w, out int h, out Color32[] pixels, out Color32[] normals, out Color32[] shares))
			{
				return false;
			}
			TerrainDetailBake.GroundColour g = bake.Ground;
			g.Baked = true;
			g.Stale = false;
			g.Bounds = bounds;
			g.Texel = texel;
			g.Width = w;
			g.Height = h;
			g.Colour = Bytes(pixels);
			g.Normals = Bytes(normals);
			g.Shares = Bytes(shares);
			g.Terrains = new TerrainData[live.Count];
			g.Positions = new Vector3[live.Count];
			g.Albedos = new Texture2DArray[live.Count];
			g.SetKeys = new int[live.Count];
			for (int i = 0; i < live.Count; i++)
			{
				sets.TryGetValue(live[i], out TerrainArraySet set);
				g.Terrains[i] = live[i].terrainData;
				g.Positions[i] = live[i].GetPosition();
				g.Albedos[i] = set != null ? set.Albedo : null;
				g.SetKeys[i] = GroundColourMap.SetKey(set);
			}
			return true;
		}

		/// <summary>RGBA8 texels as bytes, in order.</summary>
		private static byte[] Bytes(Color32[] texels)
		{
			var bytes = new byte[texels.Length * 4];
			for (int i = 0, at = 0; i < texels.Length; i++)
			{
				Color32 c = texels[i];
				bytes[at++] = c.r;
				bytes[at++] = c.g;
				bytes[at++] = c.b;
				bytes[at++] = c.a;
			}
			return bytes;
		}

		/// <summary>The scene's name, or (a scene the cut has not saved yet) the one its terrain folder is named after.</summary>
		private static string SceneName(Scene scene, string assetPath)
		{
			if (!string.IsNullOrEmpty(scene.name))
			{
				return scene.name;
			}
			string file = System.IO.Path.GetFileNameWithoutExtension(assetPath);
			return file.EndsWith(" " + AssetName) ? file.Substring(0, file.Length - AssetName.Length - 1) : file;
		}

		/// <summary>The scene's <see cref="SceneTerrainBake"/>, made (on a client-only object at its root) when it has none.</summary>
		private static SceneTerrainBake EnsureHolder(Scene scene)
		{
			foreach (GameObject root in scene.GetRootGameObjects())
			{
				if (root.name == SceneTerrainBake.ObjectName && root.TryGetComponent(out SceneTerrainBake found))
				{
					return found;
				}
			}
			var go = new GameObject(SceneTerrainBake.ObjectName);
			SceneManager.MoveGameObjectToScene(go, scene);
			go.AddComponent<ClientOnlyObject>();
			return go.AddComponent<SceneTerrainBake>();
		}

		/// <summary>The bake's arrays, in bytes (for the note).</summary>
		private static long Bytes(TerrainDetailBake bake)
		{
			long n = 0;
			foreach (TerrainDetailBake.Tile t in bake.Tiles)
			{
				n += 2L * t.Heights.Length;
				TerrainDetailBake.Grass g = t.Grass;
				n += g.Density.Length + g.Occupied.Length + g.Sward.Length + g.Freeboard.Length + g.Surface.Length + 4L * (g.MinHeights.Length + g.MaxHeights.Length);
				TerrainDetailBake.Scatter s = t.Scatter;
				n += 4L * (s.Entries.Length + s.ItemEntryStart.Length + s.ItemMin.Length + s.ItemMax.Length) + 2L * s.BlockStarts.Length + s.Holes.Length;
				n += t.Index != null && t.Index.Map != null ? 4L * t.Index.Map.Length : 0L;
			}
			foreach (TerrainDetailBake.Albedo a in bake.Albedos)
			{
				n += 4L * a.Pyramids.Length;
			}
			n += bake.Ground.Colour.Length + bake.Ground.Normals.Length + (bake.Ground.Shares != null ? bake.Ground.Shares.Length : 0);
			return n;
		}

		// ── Going stale ───────────────────────────────────────────────

		private static void OnHeightsChanged(Terrain terrain, RectInt region, bool synched) => MarkStale(terrain);

		private static void OnTextureChanged(Terrain terrain, string textureName, RectInt region, bool synched)
		{
			if (textureName == TerrainData.AlphamapTextureName)
			{
				MarkIndexStale(terrain);
			}
			MarkStale(terrain);
		}

		/// <summary>
		/// A terrain's alphamaps were written (a stroke, an undo, a script; in edit mode or play, as both keep the change): its
		/// stored index map no longer says what they say. The binder bakes live meanwhile; a client build keeps the alphamaps
		/// of a tile whose map is stale.
		/// </summary>
		private static void MarkIndexStale(Terrain terrain)
		{
			if (terrain == null || terrain.terrainData == null || !terrain.gameObject.scene.IsValid() || !terrain.gameObject.scene.isLoaded)
			{
				return;
			}
			foreach (GameObject root in terrain.gameObject.scene.GetRootGameObjects())
			{
				if (root.name != SceneTerrainBake.ObjectName || !root.TryGetComponent(out SceneTerrainBake holder) || holder.Bake == null)
				{
					continue;
				}
				TerrainDetailBake.Tile tile = holder.Bake.TileOf(terrain.terrainData);
				if (tile != null && tile.Index != null && tile.Index.Format != 0 && !tile.Index.Stale)
				{
					tile.Index.Stale = true;
					EditorUtility.SetDirty(holder.Bake);
				}
			}
		}

		/// <summary>An edit-mode change to a terrain's heights, paint or holes: its baked entry is no longer what play would build.</summary>
		private static void MarkStale(Terrain terrain)
		{
			if (Application.isPlaying || terrain == null || terrain.terrainData == null)
			{
				return;
			}
			foreach (GameObject root in terrain.gameObject.scene.GetRootGameObjects())
			{
				if (root.name != SceneTerrainBake.ObjectName || !root.TryGetComponent(out SceneTerrainBake holder) || holder.Bake == null)
				{
					continue;
				}
				TerrainDetailBake.Tile tile = holder.Bake.TileOf(terrain.terrainData);
				if (tile != null && !tile.Stale)
				{
					tile.Stale = true;
					EditorUtility.SetDirty(holder.Bake);
				}
				TerrainDetailBake.GroundColour ground = holder.Bake.Ground;
				if (ground != null && ground.Baked && !ground.Stale && System.Array.IndexOf(ground.Terrains, terrain.terrainData) >= 0)
				{
					ground.Stale = true;
					EditorUtility.SetDirty(holder.Bake);
				}
			}
		}
	}
}
