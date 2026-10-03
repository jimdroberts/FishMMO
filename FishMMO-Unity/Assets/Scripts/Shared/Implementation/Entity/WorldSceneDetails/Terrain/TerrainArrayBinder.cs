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
	/// Hands each terrain tile of a scene what the array terrain shader reads: the tile's own
	/// alphamaps as control maps, and the scene's baked texture arrays and per-layer numbers.
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
	/// <b>Painting needs nothing.</b> The control maps are the alphamap textures themselves, bound
	/// by reference, so a brush stroke that writes them is on screen the same frame.
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

		/// <summary>The most alphamaps (control maps) the shader reads.</summary>
		public const int MaximumControlMaps = TerrainArrayLayerParams.MaximumLayers / 4;

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
		private static readonly int InfoId = Shader.PropertyToID("_FishArrayInfo");
		private static readonly int AlbedoId = Shader.PropertyToID("_FishAlbedoArray");
		private static readonly int NormalId = Shader.PropertyToID("_FishNormalArray");
		private static readonly int MaskId = Shader.PropertyToID("_FishMaskArray");
		private static readonly int LayerSTId = Shader.PropertyToID("_FishLayerST");
		private static readonly int LayerTintId = Shader.PropertyToID("_FishLayerTint");
		private static readonly int LayerMaskOffsetId = Shader.PropertyToID("_FishLayerMaskOffset");
		private static readonly int LayerMaskScaleId = Shader.PropertyToID("_FishLayerMaskScale");
		private static readonly int LayerSurfaceId = Shader.PropertyToID("_FishLayerSurface");

		// One block per tile, made on first use: Unity refuses to allocate a property block in a field
		// initializer, and a block per tile is right whether or not the terrain copies what it is given.
		private readonly Dictionary<Terrain, MaterialPropertyBlock> blocks = new Dictionary<Terrain, MaterialPropertyBlock>();
		private TerrainArraySet set;
		private readonly List<Terrain> bound = new List<Terrain>();
#if !UNITY_EDITOR
		private AsyncOperationHandle<TerrainArraySet> handle;
#endif

		/// <summary>The set currently bound, or null.</summary>
		public TerrainArraySet BoundSet => set;

		private void OnEnable()
		{
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
			foreach (Terrain terrain in EffectiveTerrains())
			{
				if (PushTo(terrain))
				{
					bound.Add(terrain);
				}
			}
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

		private bool PushTo(Terrain terrain)
		{
			if (terrain == null || terrain.terrainData == null)
			{
				return false;
			}
			MaterialPropertyBlock block = BlockFor(terrain);

			TerrainData data = terrain.terrainData;
			int controls = Mathf.Min(data.alphamapTextureCount, MaximumControlMaps);
			for (int k = 0; k < controls; k++)
			{
				block.SetTexture(ControlIds[k], data.GetAlphamapTexture(k));
			}
			if (controls > 0)
			{
				Texture2D first = data.GetAlphamapTexture(0);
				block.SetVector(ControlTexelSizeId, new Vector4(1f / first.width, 1f / first.height, first.width, first.height));
			}

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
				block.SetVectorArray(LayerSTId, set.LayerST);
				block.SetVectorArray(LayerTintId, set.LayerTint);
				block.SetVectorArray(LayerMaskOffsetId, set.LayerMaskOffset);
				block.SetVectorArray(LayerMaskScaleId, set.LayerMaskScale);
				block.SetVectorArray(LayerSurfaceId, set.LayerSurface);
			}
			block.SetVector(InfoId, new Vector4(
				usable ? set.LayerCount : 0f,
				controls,
				usable && set.Normal != null ? 1f : 0f,
				usable && set.Mask != null ? 1f : 0f));

			terrain.SetSplatMaterialPropertyBlock(block);
			return true;
		}

		/// <summary>The tile's own block, emptied.</summary>
		private MaterialPropertyBlock BlockFor(Terrain terrain)
		{
			if (!blocks.TryGetValue(terrain, out MaterialPropertyBlock block) || block == null)
			{
				block = new MaterialPropertyBlock();
				blocks[terrain] = block;
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
			PushTo(terrain);
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
