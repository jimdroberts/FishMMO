#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// The questions LOCAL reference resolution asks of the project, behind an interface so the
	/// decision can be tested on in-memory objects with no AssetDatabase.
	/// </summary>
	public interface ILocalArtReferences
	{
		/// <summary>The biome's sidecar override for a layer slot, or null.</summary>
		BiomeLocalArt.SlotOverride Slot(BiomeTemplate biome, string slot);

		/// <summary>The biome's sidecar override for a spawn rule, or null.</summary>
		BiomeLocalArt.RuleOverride Rule(BiomeTemplate biome, string slot, PrefabSpawnRule rule);

		/// <summary>A LOCAL terrain layer standing in for a committed one by name, or null.</summary>
		TerrainLayer NamedLayer(TerrainLayer committed);

		/// <summary>A LOCAL prefab with this committed prefab's name, or null.</summary>
		GameObject NamedPrefab(string committedName);

		/// <summary>A LOCAL material with this committed material's name, or null.</summary>
		Material NamedMaterial(string committedName);
	}

	/// <summary>The project's own sidecars and LOCAL folders, by the paths <see cref="BiomeLocalArt"/> names.</summary>
	public sealed class ProjectLocalArtReferences : ILocalArtReferences
	{
		private readonly ProjectTerrainArrayLocalLookup layers = new ProjectTerrainArrayLocalLookup();

		public BiomeLocalArt.SlotOverride Slot(BiomeTemplate biome, string slot) => BiomeLocalArtIndex.Find(biome, slot);

		public BiomeLocalArt.RuleOverride Rule(BiomeTemplate biome, string slot, PrefabSpawnRule rule) => BiomeLocalArtIndex.FindRule(biome, slot, rule);

		public TerrainLayer NamedLayer(TerrainLayer committed) => layers.LayerOverride(committed);

		public GameObject NamedPrefab(string committedName)
		{
			return string.IsNullOrEmpty(committedName) ? null
				: AssetDatabase.LoadAssetAtPath<GameObject>($"{BiomeLocalArt.PrefabsFolder}/{committedName}.prefab");
		}

		public Material NamedMaterial(string committedName)
		{
			return string.IsNullOrEmpty(committedName) ? null
				: AssetDatabase.LoadAssetAtPath<Material>($"{BiomeLocalArt.MaterialsFolder}/{committedName}.mat");
		}
	}

	/// <summary>
	/// Whether painting a scene may write references to LOCAL art, and the resolvers that follow
	/// from the answer: the one gate between <c>Assets/LOCAL</c> and the files a scene paint writes.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>The rule it enforces: references point FROM LOCAL to committed, never back.</b> A scene
	/// paint writes references into the scene and its TerrainData — the terrain layers of the splat
	/// palette, the detail and tree prototypes of the scatter, the cliff and ice pieces. Those files
	/// are committed for every scene outside <c>Assets/LOCAL</c>, so for those scenes every resolver
	/// here returns the committed (generated) default unchanged, whatever sidecars this machine has.
	/// Only a scene whose own path AND every TerrainData it paints live under <c>Assets/LOCAL</c>
	/// (gitignored) gets the overrides; a LOCAL scene still painting committed TerrainData — a plain
	/// copy of the .unity file — is treated as committed, because the TerrainData is what would carry
	/// the reference.
	/// </para>
	/// <para>
	/// <b>Textures are not decided here.</b> The terrain arrays (<see cref="TerrainArraySetup"/>,
	/// <see cref="TerrainArrayResolver"/>) resolve every palette layer's textures LOCAL-first for
	/// every scene, committed ones included: the arrays are gitignored build output, so baking LOCAL
	/// textures into them writes no committed reference. That is why a committed scene already shows
	/// LOCAL ground textures on this machine, while LOCAL trees, grass and rock pieces — which can only
	/// reach the screen as references — need a LOCAL copy of the scene
	/// (<see cref="LocalSceneCopyTool"/>).
	/// </para>
	/// <para>
	/// <b>Resolution order inside a LOCAL scope</b>, per kind: a terrain layer — the slot's sidecar
	/// <see cref="BiomeLocalArt.SlotOverride.TerrainLayer"/>, then <c>LOCAL/Biomes/TerrainLayers/&lt;name&gt;</c>,
	/// then the committed layer (a sidecar holding only single textures leaves the committed layer in
	/// the palette: the arrays draw those textures anyway). A spawn rule's prefabs — the sidecar's
	/// rule override, else each prefab swapped for <c>LOCAL/Biomes/Prefabs/&lt;name&gt;.prefab</c> when one
	/// exists. A placed piece's material or prefab — by name, from <c>LOCAL/Biomes/Materials</c> and
	/// <c>LOCAL/Biomes/Prefabs</c>.
	/// </para>
	/// </remarks>
	public sealed class LocalArtScope
	{
		/// <summary>Where private art-preview copies of scenes live (gitignored with the rest of Assets/LOCAL).</summary>
		/// <remarks>
		/// Deliberately NOT <c>Constants.Configuration.LocalScenePath</c> (<c>Assets/LOCAL/Scenes</c>): that
		/// folder holds local world scenes, which the build and <c>WorldEditorAssets.WorldScenePaths</c>
		/// include when the dashboard's Enable Local Directory is on, and a preview copy is not a world
		/// scene to build or list. The gate itself covers any path under Assets/LOCAL.
		/// </remarks>
		public const string LocalScenesFolder = "Assets/LOCAL/SceneCopies";

		/// <summary>
		/// Appended to a LOCAL copy's scene name. A copy must not share its source's name: the terrain
		/// arrays are baked and found by scene name, and the copy's palette can differ (LOCAL layers),
		/// so one name would make the two scenes overwrite each other's arrays.
		/// </summary>
		public const string LocalSceneSuffix = " LOCAL";

		/// <summary>The scope that writes only committed references.</summary>
		public static readonly LocalArtScope Committed = new LocalArtScope(false, null, "committed references only");

		private readonly ILocalArtReferences references;

		private LocalArtScope(bool allowsLocal, ILocalArtReferences references, string reason)
		{
			AllowsLocal = allowsLocal;
			this.references = references;
			Reason = reason;
		}

		/// <summary>True when LOCAL overrides may be written into the scene being painted.</summary>
		public bool AllowsLocal { get; }

		/// <summary>Why the scope is what it is, one line for the paint's notes.</summary>
		public string Reason { get; }

		/// <summary>True when a scene path lives under Assets/LOCAL (any capitalisation, as the gitignore matches it).</summary>
		public static bool IsLocalScenePath(string scenePath) => BiomeLocalArtIndex.IsLocalPath(scenePath);

		/// <summary>
		/// The decision, on paths alone: LOCAL only when the scene path and every asset path the paint
		/// writes into are under Assets/LOCAL. Pure, for tests.
		/// </summary>
		/// <param name="scenePath">The scene's asset path; empty for an unsaved scene, which is never LOCAL.</param>
		/// <param name="writtenAssetPaths">The TerrainData (and any other asset) paths the paint writes references into; an empty entry (an in-memory asset) is ignored.</param>
		/// <param name="references">Where overrides are looked up when the answer is LOCAL.</param>
		public static LocalArtScope ForPaths(string scenePath, IEnumerable<string> writtenAssetPaths, ILocalArtReferences references)
		{
			if (!IsLocalScenePath(scenePath))
			{
				return Committed;
			}
			if (writtenAssetPaths != null)
			{
				foreach (string path in writtenAssetPaths)
				{
					if (!string.IsNullOrEmpty(path) && !BiomeLocalArtIndex.IsLocalPath(path))
					{
						return new LocalArtScope(false, null, $"committed references only: the scene is under Assets/LOCAL but paints '{path}', which is committed");
					}
				}
			}
			if (references == null)
			{
				return new LocalArtScope(false, null, "committed references only: no LOCAL lookup was given");
			}
			return new LocalArtScope(true, references, "LOCAL art overrides (scene and terrain data under Assets/LOCAL)");
		}

		/// <summary>The scope for painting a scene's terrain tiles, against the project's own sidecars.</summary>
		public static LocalArtScope For(Scene scene, IEnumerable<Terrain> tiles)
		{
			return ForPaths(scene.IsValid() ? scene.path : null, TerrainDataPaths(tiles), new ProjectLocalArtReferences());
		}

		/// <summary>The asset paths of the tiles' TerrainData, in tile order (empty for in-memory data).</summary>
		public static List<string> TerrainDataPaths(IEnumerable<Terrain> tiles)
		{
			var paths = new List<string>();
			if (tiles == null)
			{
				return paths;
			}
			foreach (Terrain terrain in tiles)
			{
				if (terrain != null && terrain.terrainData != null)
				{
					paths.Add(AssetDatabase.GetAssetPath(terrain.terrainData));
				}
			}
			return paths;
		}

		/// <summary>
		/// The atlas scene a scene stands for: its own name, or for a LOCAL copy
		/// (<see cref="LocalSceneSuffix"/>) the committed scene it was copied from.
		/// </summary>
		public static string AtlasSceneName(string scenePath, string sceneName)
		{
			if (sceneName != null && IsLocalScenePath(scenePath) && sceneName.EndsWith(LocalSceneSuffix, StringComparison.Ordinal))
			{
				return sceneName.Substring(0, sceneName.Length - LocalSceneSuffix.Length);
			}
			return sceneName;
		}

		// ── Resolvers ─────────────────────────────────────────────────

		/// <summary>
		/// The terrain layer a biome slot paints with, given the committed one. Committed scope: the
		/// committed layer, always.
		/// </summary>
		public TerrainLayer ResolveLayer(TerrainLayer committed, BiomeTemplate biome, string slot)
		{
			if (!AllowsLocal)
			{
				return committed;
			}
			BiomeLocalArt.SlotOverride entry = references.Slot(biome, slot);
			if (entry != null && entry.TerrainLayer != null)
			{
				return entry.TerrainLayer;
			}
			TerrainLayer named = committed != null ? references.NamedLayer(committed) : null;
			return named != null ? named : committed;
		}

		/// <summary>
		/// The palette's slot-aware resolver: <paramref name="committed"/> (normally
		/// <see cref="BiomeTerrainLayers.Resolve(TerrainTextureLayer)"/>) then this scope's override.
		/// </summary>
		public Func<BiomeTemplate, string, TerrainTextureLayer, TerrainLayer> PaletteResolver(Func<TerrainTextureLayer, TerrainLayer> committed)
		{
			if (committed == null)
			{
				throw new ArgumentNullException(nameof(committed));
			}
			if (!AllowsLocal)
			{
				return (biome, slot, layer) => committed(layer);
			}
			return (biome, slot, layer) => ResolveLayer(committed(layer), biome, slot);
		}

		/// <summary>
		/// The prefabs a spawn rule scatters. Committed scope: the rule's own array, the same instance.
		/// </summary>
		public GameObject[] ResolvePrefabs(BiomeTemplate biome, string slot, PrefabSpawnRule rule)
		{
			if (rule == null)
			{
				return null;
			}
			GameObject[] own = rule.prefabs;
			if (!AllowsLocal)
			{
				return own;
			}
			BiomeLocalArt.RuleOverride entry = references.Rule(biome, slot, rule);
			if (entry != null && !entry.IsEmpty)
			{
				return entry.Prefabs;
			}
			if (own == null)
			{
				return null;
			}
			GameObject[] swapped = null;
			for (int i = 0; i < own.Length; i++)
			{
				GameObject named = own[i] != null ? references.NamedPrefab(own[i].name) : null;
				if (named != null)
				{
					swapped ??= (GameObject[])own.Clone();
					swapped[i] = named;
				}
			}
			return swapped ?? own;
		}

		/// <summary>The scatter's prefab resolver, or null (the rule's own prefabs) in committed scope.</summary>
		public Func<SceneTerrainPalette.Entry, PrefabSpawnRule, GameObject[]> ScatterPrefabs
		{
			get
			{
				if (!AllowsLocal)
				{
					return null;
				}
				return (entry, rule) => ResolvePrefabs(entry?.Biome, entry?.Slot, rule);
			}
		}

		/// <summary>A placed piece's material by the generated material's name. Committed scope: the generated one.</summary>
		public Material ResolveMaterial(string committedName, Material committed)
		{
			if (!AllowsLocal)
			{
				return committed;
			}
			Material named = references.NamedMaterial(committedName);
			return named != null ? named : committed;
		}

		/// <summary>The cliff placer's options for this scope; null (the defaults) in committed scope.</summary>
		public CliffPlacerOptions CliffOptions()
		{
			return AllowsLocal ? new CliffPlacerOptions { MaterialFor = ResolveMaterial } : null;
		}

		/// <summary>The ice placer's options for this scope; null (the defaults) in committed scope.</summary>
		public IcePlacerOptions IceOptions()
		{
			return AllowsLocal ? new IcePlacerOptions { Prefabs = new LocalIcePrefabs(this, new ProjectIcePrefabs()) } : null;
		}

		/// <summary>A LOCAL prefab with the committed prefab's name, or null; always null in committed scope.</summary>
		public GameObject NamedPrefab(string committedName) => AllowsLocal ? references.NamedPrefab(committedName) : null;

		/// <summary>The ice placer's prefabs with LOCAL ones of the same name in front.</summary>
		private sealed class LocalIcePrefabs : IIcePrefabSource
		{
			private readonly LocalArtScope scope;
			private readonly IIcePrefabSource committed;

			public LocalIcePrefabs(LocalArtScope scope, IIcePrefabSource committed)
			{
				this.scope = scope;
				this.committed = committed;
			}

			public bool TryMeasure(string prefabName, out Bounds localBounds)
			{
				GameObject local = scope.NamedPrefab(prefabName);
				if (local == null)
				{
					return committed.TryMeasure(prefabName, out localBounds);
				}
				// As the project source measures: the root collider's mesh, else the renderers.
				MeshCollider collider = local.GetComponent<MeshCollider>();
				if (collider != null && collider.sharedMesh != null)
				{
					localBounds = collider.sharedMesh.bounds;
					return true;
				}
				localBounds = default;
				bool any = false;
				foreach (Renderer renderer in local.GetComponentsInChildren<Renderer>(true))
				{
					if (!any)
					{
						localBounds = renderer.bounds;
						any = true;
					}
					else
					{
						localBounds.Encapsulate(renderer.bounds);
					}
				}
				return any || committed.TryMeasure(prefabName, out localBounds);
			}

			public GameObject Instantiate(string prefabName, Transform parent)
			{
				GameObject local = scope.NamedPrefab(prefabName);
				return local != null ? PrefabUtility.InstantiatePrefab(local, parent) as GameObject : committed.Instantiate(prefabName, parent);
			}
		}

		/// <summary>The folder a LOCAL scene's terrain data lives in (the first tile's), or null.</summary>
		public static string TerrainFolderOf(IEnumerable<Terrain> tiles)
		{
			foreach (string path in TerrainDataPaths(tiles))
			{
				if (!string.IsNullOrEmpty(path))
				{
					return Path.GetDirectoryName(path).Replace('\\', '/');
				}
			}
			return null;
		}
	}
}
#endif
