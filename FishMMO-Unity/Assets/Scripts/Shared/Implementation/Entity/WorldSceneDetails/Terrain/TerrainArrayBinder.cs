using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
#if !UNITY_SERVER && !UNITY_EDITOR
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;
#endif

namespace FishMMO.Shared
{
	/// <summary>
	/// Hands each terrain tile of a scene what the array terrain shader reads: the tile's index map (its four strongest
	/// layers per alphamap texel, baked here from its alphamaps), and the scene's baked texture arrays and layer table.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why a component at all.</b> Unity's terrain engine binds four splat textures per pass and
	/// knows nothing of arrays, so something has to give every tile the rest through
	/// <see cref="Terrain.SetSplatMaterialPropertyBlock"/>. A property block is not serialized, so it
	/// is pushed whenever this enables — on scene load, after every domain reload, in edit mode as
	/// in play mode (<c>ExecuteAlways</c>) — and again when a tile's alphamaps are replaced (a layer
	/// added in the terrain inspector) or the scene's arrays are rebaked.
	/// </para>
	/// <para>
	/// <b>Painting stays live.</b> The alphamaps are still the weights everything edits; each tile's index map is baked from
	/// them on the GPU (<see cref="TerrainIndexMapBaker"/>) when it is bound and, in the editor, again whenever they change
	/// (a brush stroke, a repaint, an undo), so a stroke is on screen the same frame. A scene may carry up to
	/// <see cref="TerrainArrayLayerParams.MaximumLayers"/> layers and a pixel still reads four index texels.
	/// </para>
	/// <para>
	/// <b>Stored for play.</b> In play (the editor's and a player's) a tile takes the index map the cut stored with the
	/// scene (<see cref="SceneTerrainBake.IndexOf"/>) instead of baking one, while it is still the alphamaps' own — the
	/// same texels, so the ground looks the same either way — and a client build may then leave the alphamaps out
	/// (ClientTerrainLayerStripper). A tile with no usable stored map is baked from its alphamaps as before.
	/// </para>
	/// <para>
	/// <b>The arrays are found by the scene's name</b> (<see cref="TerrainArraySet"/>): off disk in
	/// the editor, by address in a player. Nothing here holds a reference to them, because they are
	/// build output that each machine regenerates.
	/// </para>
	/// <para>
	/// <b>Client only.</b> The object carries <see cref="ClientOnlyObject"/>, so server builds strip
	/// it with everything it references (and it destroys itself on a server that was built some other
	/// way), and in a server player this compiles to nothing but its serialized fields — a headless
	/// server has no use for a texture.
	/// </para>
	/// </remarks>
	[ExecuteAlways]
	[DisallowMultipleComponent]
	[RequireComponent(typeof(ClientOnlyObject))]
	public sealed class TerrainArrayBinder : MonoBehaviour
	{
		/// <summary>The binder object's name in a generated scene.</summary>
		public const string ObjectName = "Terrain Arrays";

		/// <summary>The shader every tile drawn by this binder uses.</summary>
		public const string ShaderName = "FishMMO/Weather Terrain Array";

		/// <summary>
		/// The shader of the ground past the scene's edge (<see cref="SceneBackdrop"/>), which draws with this
		/// scene's arrays too: its renderers are bound like the tiles, with their own control maps.
		/// </summary>
		public const string BackdropShaderName = "FishMMO/Backdrop Ground";

		/// <summary>The most control maps the backdrop's shader reads (SceneBackdropBuilder bakes its own); tiles read index maps.</summary>
		public const int MaximumControlMaps = 8;

		[Tooltip("The tiles to bind. Empty means every terrain in this scene.")]
		[SerializeField] private List<Terrain> terrains = new List<Terrain>();

		/// <summary>The tiles as assigned. See <see cref="EffectiveTerrains"/> for the ones bound.</summary>
		public List<Terrain> Terrains => terrains;

		/// <summary>The provenance on the EditorOnly child, or null.</summary>
		public TerrainArrayProvenance Provenance => GetComponentInChildren<TerrainArrayProvenance>(true);

		/// <summary>
		/// The tiles bound: the assigned list, or every terrain in this scene when it is empty, so a
		/// tile added by hand to a scene whose list was never filled is still drawn.
		/// </summary>
		public List<Terrain> EffectiveTerrains()
		{
			var result = new List<Terrain>();
			foreach (Terrain terrain in terrains)
			{
				if (terrain != null)
				{
					result.Add(terrain);
				}
			}
			if (result.Count > 0 || !gameObject.scene.IsValid())
			{
				return result;
			}
			foreach (GameObject root in gameObject.scene.GetRootGameObjects())
			{
				result.AddRange(root.GetComponentsInChildren<Terrain>(true));
			}
			return result;
		}

		/// <summary>The scene name the arrays are baked under.</summary>
		public string SceneKey => gameObject.scene.name;

		/* Compiled out of the dedicated server player, but kept in the editor whatever the active
		 * subtarget: the build tool switches the editor to the Server subtarget (UNITY_SERVER defined)
		 * before a server build, and the editor tools that call BindAll must still compile then. */
#if !UNITY_SERVER || UNITY_EDITOR
		private static readonly List<TerrainArrayBinder> active = new List<TerrainArrayBinder>();

		private static readonly int[] ControlIds =
		{
			Shader.PropertyToID("_FishControl0"), Shader.PropertyToID("_FishControl1"),
			Shader.PropertyToID("_FishControl2"), Shader.PropertyToID("_FishControl3"),
			Shader.PropertyToID("_FishControl4"), Shader.PropertyToID("_FishControl5"),
			Shader.PropertyToID("_FishControl6"), Shader.PropertyToID("_FishControl7"),
		};
		private static readonly int ControlTexelSizeId = Shader.PropertyToID("_FishControl0_TexelSize");
		private static readonly int IndexMapId = Shader.PropertyToID("_FishIndexMap");
		private static readonly int IndexTexelSizeId = Shader.PropertyToID("_FishIndexMap_TexelSize");
		private static readonly int InfoId = Shader.PropertyToID("_FishArrayInfo");
		private static readonly int AlbedoId = Shader.PropertyToID("_FishAlbedoArray");
		private static readonly int NormalId = Shader.PropertyToID("_FishNormalArray");
		private static readonly int MaskId = Shader.PropertyToID("_FishMaskArray");
		private static readonly int LayerTableId = Shader.PropertyToID("_FishLayerTable");
		private static readonly int PathLayersId = Shader.PropertyToID("_FishPathLayers");
		private static readonly int SnowHoleFadeId = Shader.PropertyToID("_FishSnowHoleFade");
		private static readonly int SnowHoleFadeTexelSizeId = Shader.PropertyToID("_FishSnowHoleFade_TexelSize");

		/// <summary>Tiles whose index map the editor has checked against the CPU bake this session (<see cref="TerrainIndexMapBaker.Check"/>): the first few.</summary>
		private static int indexChecks;
		private const int IndexChecksPerSession = 4;

		// One block per tile, made on first use: Unity refuses to allocate a property block in a field
		// initializer, and a block per tile is right whether or not the terrain copies what it is given.
		private readonly Dictionary<Object, MaterialPropertyBlock> blocks = new Dictionary<Object, MaterialPropertyBlock>();
		private TerrainArraySet set;
		private readonly List<Terrain> bound = new List<Terrain>();
		private readonly List<Renderer> boundBackdrop = new List<Renderer>();
		// Each tile's index map and the terrain data it was baked from (a tile given new data is baked again).
		// Stored: the map is the scene's stored one, uploaded (a Texture2D); otherwise one baked here (a render texture).
		private readonly Dictionary<Terrain, (Texture Map, TerrainData Data, bool Stored)> indexMaps = new Dictionary<Terrain, (Texture, TerrainData, bool)>();
		// Tiles told once that they have no index map to draw (no stored map and no alphamaps), and that their stored map was out of date.
		private static readonly HashSet<string> warnedIndex = new HashSet<string>();
		// What the last push bound: tiles from stored maps, tiles baked here (for its one log line in play).
		private int pushedStored, pushedBaked;
		// The scene's per-layer numbers and path slices, one texture for every tile and backdrop renderer (TerrainLayerTable).
		private Texture2D layerTable;
		// How much of the deep-snow lift each tile's vertices keep near the holes (TerrainSnowHoleFade): the tiles with a hole
		// near them; the rest take its blank map. Made on every push (a cut may have holed the ground since).
		private Dictionary<Terrain, Texture2D> snowFades;
#if !UNITY_EDITOR
		private AsyncOperationHandle<TerrainArraySet> handle;
#endif

		/// <summary>The set currently bound, or null.</summary>
		public TerrainArraySet BoundSet => set;

		/// <summary>The set bound to a terrain tile by whichever binder drew it last, or null: what names its layers.</summary>
		public static TerrainArraySet SetFor(Terrain terrain)
		{
			if (terrain == null)
			{
				return null;
			}
			for (int i = 0; i < active.Count; i++)
			{
				TerrainArrayBinder binder = active[i];
				if (binder != null && binder.set != null && binder.bound.Contains(terrain))
				{
					return binder.set;
				}
			}
			return null;
		}

		private void OnEnable()
		{
			// Scene imports also enable this component in headless build workers.
			// Its index maps are visual output and require a real graphics device.
			if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
			{
				return;
			}
			if (!active.Contains(this))
			{
				active.Add(this);
			}
#if UNITY_EDITOR
			TerrainCallbacks.textureChanged += OnTerrainTextureChanged;
#endif
			Bind();
		}

		private void OnDisable()
		{
			active.Remove(this);
#if UNITY_EDITOR
			TerrainCallbacks.textureChanged -= OnTerrainTextureChanged;
#endif
			Unbind();
			ReleaseIndexMaps(null);
			TerrainSnowHoleFade.Release(snowFades);
			snowFades = null;
			if (layerTable != null)
			{
				if (Application.isPlaying)
				{
					Destroy(layerTable);
				}
				else
				{
					DestroyImmediate(layerTable);
				}
				layerTable = null;
			}
#if !UNITY_EDITOR
			if (handle.IsValid())
			{
				Addressables.Release(handle);
			}
			handle = default;
#endif
			set = null;
		}

		/// <summary>
		/// Finds this scene's arrays and pushes everything to every tile, now.
		/// </summary>
		/// <remarks>
		/// Synchronous in the editor (the set is read straight off disk), so a caller about to render —
		/// the world map's overhead capture — sees the arrays on that very render. In a player the set
		/// arrives by address a frame or two later; the tiles draw plain grey ground until it does.
		/// </remarks>
		public void Bind()
		{
#if UNITY_EDITOR
			set = string.IsNullOrEmpty(SceneKey) ? null
				: UnityEditor.AssetDatabase.LoadAssetAtPath<TerrainArraySet>(TerrainArraySet.BakedAssetPath(SceneKey));
			Push();
#else
			Push();
			BeginLoad();
#endif
		}

		/// <summary>Pushes the current set (or none) to every tile without looking for it again.</summary>
		public void Push()
		{
			Unbind();
			FillLayerTable();
			List<Terrain> terrains = EffectiveTerrains();
			ReleaseIndexMaps(terrains);
			TerrainSnowHoleFade.Release(snowFades);
			snowFades = TerrainSnowHoleFade.Build(terrains);
			pushedStored = pushedBaked = 0;
			foreach (Terrain terrain in terrains)
			{
				// Edit mode bakes every push (a cut, a repaint, a brush or an undo may have written the alphamaps since); play
				// takes each tile's stored map where it is still good.
				if (PushTo(terrain, rebake: Application.isEditor && !Application.isPlaying))
				{
					bound.Add(terrain);
				}
			}
			if (!Application.isEditor)
			{
				// A player bakes once per tile: the alphamaps' scratch copy (a map's worth per four layers) is not kept.
				TerrainIndexMapBaker.ReleaseScratch();
			}
			if (Application.isPlaying && pushedStored + pushedBaked > 0)
			{
				Debug.Log($"[Terrain index] '{SceneKey}': {pushedStored} tile(s) drawn from stored index maps, {pushedBaked} baked from their alphamaps.");
			}
			foreach (Renderer renderer in BackdropRenderers())
			{
				PushTo(renderer);
				boundBackdrop.Add(renderer);
			}
		}

		/// <summary>The renderers of this scene's backdrop that draw with <see cref="BackdropShaderName"/>.</summary>
		/// <remarks>
		/// A backdrop baked before it had control maps still uses URP/Lit and is left alone. Looked up on every
		/// push rather than kept, because a rebake swaps the material's shader under the same renderers.
		/// While the scene is still loading (OnEnable runs then) its roots cannot be listed, so the backdrops
		/// are found among the loaded objects instead; they are all deserialized before any OnEnable.
		/// </remarks>
		public List<Renderer> BackdropRenderers()
		{
			var result = new List<Renderer>();
			Scene scene = gameObject.scene;
			if (!scene.IsValid())
			{
				return result;
			}
			var backdrops = new List<SceneBackdrop>();
			if (scene.isLoaded)
			{
				foreach (GameObject root in scene.GetRootGameObjects())
				{
					backdrops.AddRange(root.GetComponentsInChildren<SceneBackdrop>(true));
				}
			}
			else
			{
				foreach (SceneBackdrop backdrop in FindObjectsByType<SceneBackdrop>(FindObjectsInactive.Include))
				{
					if (backdrop.gameObject.scene == scene)
					{
						backdrops.Add(backdrop);
					}
				}
			}
			foreach (SceneBackdrop backdrop in backdrops)
			{
				foreach (Renderer renderer in backdrop.GetComponentsInChildren<Renderer>(true))
				{
					Material material = renderer.sharedMaterial;
					if (material != null && material.shader != null && material.shader.name == BackdropShaderName)
					{
						result.Add(renderer);
					}
				}
			}
			return result;
		}

#if UNITY_EDITOR
		/// <summary>
		/// Set by the editor's array baker (an editor assembly this one cannot reference): bakes a
		/// scene's arrays if they are missing or stale, synchronously. Null outside the editor.
		/// </summary>
		public static System.Func<Scene, int> EditorBakeIfStale;
#endif

		/// <summary>
		/// Binds every binder in a scene, now, and returns how many it bound. In the editor it first
		/// bakes the scene's arrays if they are missing or stale (<paramref name="bakeIfStale"/>).
		/// </summary>
		/// <remarks>
		/// For anything that renders a scene it has just opened without waiting a frame — the world
		/// map's overhead capture. OnEnable has already pushed by then, but only what was on disk at
		/// that moment: a scene whose arrays were never baked on this machine would be photographed
		/// grey. Everything here is synchronous in the editor, so the very next
		/// <c>Camera.Render</c> sees the arrays.
		/// </remarks>
		public static int BindAll(Scene scene, bool bakeIfStale = true)
		{
			if (!scene.IsValid() || !scene.isLoaded)
			{
				return 0;
			}
#if UNITY_EDITOR
			if (bakeIfStale && EditorBakeIfStale != null)
			{
				EditorBakeIfStale(scene);
			}
#endif
			int count = 0;
			foreach (GameObject root in scene.GetRootGameObjects())
			{
				foreach (TerrainArrayBinder binder in root.GetComponentsInChildren<TerrainArrayBinder>(false))
				{
					if (binder.isActiveAndEnabled)
					{
						binder.Bind();
						count++;
					}
				}
			}
			return count;
		}

		/// <summary>Re-binds every enabled binder in every loaded scene; the baker calls it after writing sets.</summary>
		/// <summary>Pushes again to every tile of one scene (its path slices changed or arrived), without looking for the set again.</summary>
		public static void RepushScene(Scene scene)
		{
			for (int i = active.Count - 1; i >= 0; i--)
			{
				if (active[i] != null && active[i].gameObject.scene == scene)
				{
					active[i].Push();
				}
			}
		}

		public static void BindAllLoaded()
		{
			for (int i = active.Count - 1; i >= 0; i--)
			{
				if (active[i] != null)
				{
					active[i].Bind();
				}
			}
		}

		/// <param name="rebake">
		/// Bake the tile's index map from its alphamaps again even when it has one, and never take the stored one: in edit mode
		/// on every push, and in play when the alphamaps were just written (a stroke in play mode). Otherwise (play) the tile
		/// takes its stored map, or keeps the one it has, or is baked when it has none of its data's size.
		/// </param>
		private bool PushTo(Terrain terrain, bool rebake)
		{
			if (terrain == null || terrain.terrainData == null)
			{
				return false;
			}
			MaterialPropertyBlock block = BlockFor(terrain);
			TerrainData data = terrain.terrainData;

			// The index map, only once there are arrays to draw it with (before then the ground is plain grey, and a player has
			// no bake shader until the set brings it).
			Texture index = null;
			if (set != null && set.IsUsable)
			{
				index = IndexMapFor(terrain, data, rebake);
			}
			block.SetTexture(IndexMapId, index != null ? index : TerrainIndexMapBaker.Blank);
			int res = index != null ? index.width : 1;
			block.SetVector(IndexTexelSizeId, new Vector4(1f / res, 1f / res, res, res));

			PushArrays(block, index != null ? 1 : 0);
			Texture2D fade = snowFades != null && snowFades.TryGetValue(terrain, out Texture2D own) && own != null ? own : TerrainSnowHoleFade.Blank;
			block.SetTexture(SnowHoleFadeId, fade);
			block.SetVector(SnowHoleFadeTexelSizeId, new Vector4(1f / fade.width, 1f / fade.height, fade.width, fade.height));
			terrain.SetSplatMaterialPropertyBlock(block);
			return true;
		}

		/// <summary>
		/// The tile's index map. In play, unless <paramref name="rebake"/>: the scene's stored map
		/// (<see cref="SceneTerrainBake.IndexOf"/>), uploaded once, while it is still this data's. Otherwise one baked here from
		/// the alphamaps, again when it has none of its data's size, its data changed, or <paramref name="rebake"/>. Null when
		/// there is neither (the ground then draws its first layer, with one warning).
		/// </summary>
		private Texture IndexMapFor(Terrain terrain, TerrainData data, bool rebake)
		{
			indexMaps.TryGetValue(terrain, out (Texture Map, TerrainData Data, bool Stored) entry);
			if (!rebake && Application.isPlaying)
			{
				if (entry.Stored && entry.Data == data && entry.Map != null)
				{
					pushedStored++;
					return entry.Map;
				}
				TerrainDetailBake.IndexMap stored = SceneTerrainBake.IndexOf(data);
				if (stored != null && StoredStillGood(terrain, data, stored))
				{
					Texture2D uploaded = TerrainIndexMapBaker.Upload(stored, $"{terrain.name} index map (stored)");
					if (uploaded != null)
					{
						FreeMap(entry.Map);
						indexMaps[terrain] = (uploaded, data, true);
						pushedStored++;
						return uploaded;
					}
				}
			}
			if (data.alphamapTextureCount == 0)
			{
				// A client build's copy, whose alphamaps were left out because a stored map stood in for them, but that map
				// is not usable here (this device cannot hold it, or the bake did not load): nothing to bake from.
				FreeMap(entry.Map);
				indexMaps.Remove(terrain);
				if (!Application.isEditor && warnedIndex.Add("none " + terrain.name))
				{
					Debug.LogWarning($"[Terrain index] '{terrain.name}' has no usable stored index map and no alphamaps: it draws its first layer.");
				}
				return null;
			}
			RenderTexture baked = entry.Stored ? null : entry.Map as RenderTexture;
			bool fresh = false;
			if (!TerrainIndexMapBaker.Fits(baked, data))
			{
				FreeMap(entry.Map);
				baked = TerrainIndexMapBaker.Create(data.alphamapResolution, data.alphamapResolution, 1, $"{terrain.name} index map");
				fresh = true;
			}
			if (fresh || rebake || entry.Data != data)
			{
				if (!TerrainIndexMapBaker.Bake(data, TerrainIndexMapBaker.ShaderOf(set), baked))
				{
					FreeMap(baked);
					indexMaps.Remove(terrain);
					return null;
				}
#if UNITY_EDITOR
				if (fresh && indexChecks < IndexChecksPerSession)
				{
					// Bound from data at rest (a load, a reload, a rebind), so the CPU copy is current: worth one look.
					indexChecks++;
					TerrainIndexMapBaker.Check(terrain, baked);
				}
#endif
			}
			indexMaps[terrain] = (baked, data, false);
			pushedBaked++;
			return baked;
		}

		/// <summary>
		/// In the editor, a stored map is drawn only while a sample of its texels still matches the alphamaps (a stroke made
		/// with no terrain callback would otherwise go unseen until the scene is baked again); a player trusts it, as the
		/// build checked every texel before it left the alphamaps out.
		/// </summary>
		private static bool StoredStillGood(Terrain terrain, TerrainData data, TerrainDetailBake.IndexMap stored)
		{
#if UNITY_EDITOR
			if (!TerrainIndexMapBaker.MatchesAlphamaps(data, stored, 32, out string bad))
			{
				if (warnedIndex.Add("stale " + terrain.name))
				{
					Debug.LogWarning($"[Terrain index] '{terrain.name}': its stored index map is out of date ({bad}); baked from its alphamaps instead. Repaint the scene or bake its terrain details again to store it.");
				}
				return false;
			}
#endif
			return true;
		}

		/// <summary>
		/// The index map bound to a terrain by any enabled binder (baked here, or the scene's stored one), or null: for whatever
		/// else reads the ground's layers (the contact blend copies it into its own array).
		/// </summary>
		public static Texture IndexMapOf(Terrain terrain)
		{
			foreach (TerrainArrayBinder binder in active)
			{
				if (binder != null && binder.indexMaps.TryGetValue(terrain, out (Texture Map, TerrainData Data, bool Stored) entry) && entry.Map != null)
				{
					return entry.Map;
				}
			}
			return null;
		}

		/// <summary>Frees the index maps of tiles not in <paramref name="keep"/> (all of them for null).</summary>
		private void ReleaseIndexMaps(List<Terrain> keep)
		{
			var drop = new List<Terrain>();
			foreach (KeyValuePair<Terrain, (Texture Map, TerrainData Data, bool Stored)> pair in indexMaps)
			{
				if (keep == null || pair.Key == null || !keep.Contains(pair.Key))
				{
					drop.Add(pair.Key);
				}
			}
			foreach (Terrain terrain in drop)
			{
				FreeMap(indexMaps[terrain].Map);
				indexMaps.Remove(terrain);
			}
		}

		/// <summary>Frees an index map this binder made (a baked render texture or an uploaded stored map).</summary>
		private static void FreeMap(Texture map)
		{
			if (map == null)
			{
				return;
			}
			if (map is RenderTexture rt)
			{
				rt.Release();
			}
			DestroyMap(map);
		}

		private static void DestroyMap(Object map)
		{
			if (Application.isPlaying)
			{
				Destroy(map);
			}
			else
			{
				DestroyImmediate(map);
			}
		}

		/// <summary>
		/// The scene's layer table from the current set and the scene's path maps (<see cref="TerrainLayerTable"/>): built once
		/// per push, shared by every tile and backdrop renderer.
		/// </summary>
		private void FillLayerTable()
		{
			if (layerTable == null)
			{
				layerTable = TerrainLayerTable.Create($"{SceneKey} layer table");
			}
			Scene scene = gameObject.scene;
			TerrainLayerTable.Fill(layerTable, TerrainLayerTable.Pack(set != null && set.IsUsable ? set : null,
				ScenePathSurfaceBinder.EarthMapOf(scene), ScenePathSurfaceBinder.RoadMapOf(scene), ScenePathSurfaceBinder.StoneMapOf(scene),
				BackdropChannels()));
		}

		/// <summary>The scene layer each channel of the scene's backdrop holds (<see cref="SceneBackdrop.ControlLayers"/>), or null: itself.</summary>
		private int[] BackdropChannels()
		{
			foreach (Renderer renderer in BackdropRenderers())
			{
				SceneBackdrop backdrop = renderer != null ? renderer.GetComponentInParent<SceneBackdrop>(true) : null;
				if (backdrop != null && backdrop.ControlLayers != null && backdrop.ControlLayers.Length > 0)
				{
					return backdrop.ControlLayers;
				}
			}
			return null;
		}

		/// <summary>
		/// A backdrop renderer: its control maps are its material's own (baked over the backdrop by
		/// SceneBackdropBuilder), so only their count and size go in the block, with the arrays.
		/// </summary>
		private void PushTo(Renderer renderer)
		{
			MaterialPropertyBlock block = BlockFor(renderer);
			Material material = renderer.sharedMaterial;
			int controls = 0;
			while (controls < MaximumControlMaps && material.GetTexture(ControlIds[controls]) is Texture2D)
			{
				controls++;
			}
			if (controls > 0)
			{
				Texture first = material.GetTexture(ControlIds[0]);
				block.SetVector(ControlTexelSizeId, new Vector4(1f / first.width, 1f / first.height, first.width, first.height));
			}
			PushArrays(block, controls);
			renderer.SetPropertyBlock(block);
		}

		/// <summary>The scene's arrays and layer table, and the info vector that says what is bound.</summary>
		/// <param name="weights">For a tile, 1 when its index map is bound; for a backdrop renderer, its control maps.</param>
		private void PushArrays(MaterialPropertyBlock block, int weights)
		{
			bool usable = set != null && set.IsUsable;
			if (usable)
			{
				block.SetTexture(AlbedoId, set.Albedo);
				if (set.Normal != null)
				{
					block.SetTexture(NormalId, set.Normal);
				}
				if (set.Mask != null)
				{
					block.SetTexture(MaskId, set.Mask);
				}
			}
			/* Every layer's tiling, tint, mask remap, surface, what it is made of for its planet (FishFlora.hlsl FishFloraGround:
			 * living ground takes its biome's ground shift, bare ground the world's own colour where the air is thin), and its
			 * own biome's path, road and cobble slices (a desert's way is packed sand, a forest's a littered track, and each
			 * biome's streets are paved with its own stone): one texture, a column per layer. */
			if (layerTable != null)
			{
				block.SetTexture(LayerTableId, layerTable);
			}
			// The slices the scene's paths are surfaced from (the scene's own palette); -1 where it has none or nothing is baked.
			block.SetVector(PathLayersId, usable ? ScenePathSurfaceBinder.LayersOf(gameObject.scene) : new Vector4(-1f, -1f, -1f, -1f));
			block.SetVector(InfoId, new Vector4(
				usable ? set.LayerCount : 0f,
				weights,
				usable && set.Normal != null ? 1f : 0f,
				usable && set.Mask != null ? 1f : 0f));
		}

		/// <summary>The tile's (or backdrop renderer's) own block, emptied.</summary>
		private MaterialPropertyBlock BlockFor(Object owner)
		{
			if (!blocks.TryGetValue(owner, out MaterialPropertyBlock block) || block == null)
			{
				block = new MaterialPropertyBlock();
				blocks[owner] = block;
			}
			block.Clear();
			return block;
		}

		/// <summary>Hands every bound tile an empty block, so nothing of a set that is going away stays bound.</summary>
		private void Unbind()
		{
			foreach (Terrain terrain in bound)
			{
				if (terrain != null)
				{
					terrain.SetSplatMaterialPropertyBlock(BlockFor(terrain));
				}
			}
			bound.Clear();
			foreach (Renderer renderer in boundBackdrop)
			{
				if (renderer != null)
				{
					renderer.SetPropertyBlock(null);
				}
			}
			boundBackdrop.Clear();
		}

#if UNITY_EDITOR
		/// <summary>
		/// A tile's alphamaps were written. Painting writes into the bound textures and needs nothing,
		/// but adding or removing a layer replaces them, and a tile still bound to the old ones would
		/// draw a map the terrain no longer has.
		/// </summary>
		private void OnTerrainTextureChanged(Terrain terrain, string textureName, RectInt texelRegion, bool synched)
		{
			if (terrain == null || textureName != TerrainData.AlphamapTextureName || !bound.Contains(terrain))
			{
				return;
			}
			// Its index map baked again from what was just written (on the GPU, so a stroke still in progress shows).
			PushTo(terrain, rebake: true);
		}
#else
		private void BeginLoad()
		{
			string key = SceneKey;
			if (string.IsNullOrEmpty(key) || handle.IsValid())
			{
				return;
			}
			string address = TerrainArraySet.AddressOf(key);
			/* Asked for its locations first, so a scene that was built without arrays (a build whose bake
			 * failed, or a scene added since) draws grey ground with one warning instead of throwing an
			 * InvalidKeyException from inside the addressables system. */
			AsyncOperationHandle<IList<IResourceLocation>> locations = Addressables.LoadResourceLocationsAsync(address, typeof(TerrainArraySet));
			locations.Completed += found =>
			{
				bool exists = found.Status == AsyncOperationStatus.Succeeded && found.Result != null && found.Result.Count > 0;
				Addressables.Release(found);
				if (this == null || !isActiveAndEnabled)
				{
					return;
				}
				if (!exists)
				{
					Debug.LogWarning($"[Terrain arrays] No baked arrays for '{key}' in this build; its ground draws plain.");
					return;
				}
				handle = Addressables.LoadAssetAsync<TerrainArraySet>(address);
				handle.Completed += loaded =>
				{
					if (this == null || !isActiveAndEnabled)
					{
						return;
					}
					if (loaded.Status == AsyncOperationStatus.Succeeded)
					{
						set = loaded.Result;
						Push();
					}
				};
			};
		}
#endif
#endif
	}
}
