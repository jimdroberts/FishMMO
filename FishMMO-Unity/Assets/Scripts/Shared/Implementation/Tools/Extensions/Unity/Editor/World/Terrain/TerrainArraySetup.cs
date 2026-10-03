#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>What <see cref="TerrainArraySetup.Apply(Scene, IReadOnlyList{Terrain}, SceneTerrainPalette)"/> did.</summary>
	public sealed class TerrainArraySetupResult
	{
		/// <summary>The array material the terrains now use, or null when its shader is missing.</summary>
		public Material Material;
		/// <summary>The scene's binder, created or updated.</summary>
		public TerrainArrayBinder Binder;
		/// <summary>Assets and scene objects created or changed, one line each.</summary>
		public readonly List<string> Wrote = new List<string>();
		/// <summary>LOCAL overrides that disagreed between biomes sharing a layer, and overrides that fell back.</summary>
		public readonly List<string> LocalConflicts = new List<string>();
		/// <summary>Anything else worth telling the person who generated the scene.</summary>
		public readonly List<string> Notes = new List<string>();
		/// <summary>True when the arrays were baked (or found current) during the call.</summary>
		public bool Baked;
		/// <summary>True when the bake waits for the scene's first save, because an unsaved scene has no name to bake under.</summary>
		public bool BakeDeferred;
		public bool Success => Material != null && Binder != null;
	}

	/// <summary>
	/// Puts a scene's terrain on the array shader: material, binder, provenance, and the first bake.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The scene generator calls this after the splat is written. Everything it puts in the scene is
	/// committed data that references only committed assets — the binder (client-only) and, on an
	/// <c>EditorOnly</c> child, which biome slot each palette layer came from. The arrays themselves
	/// are build output (<see cref="TerrainArrayBaker"/>).
	/// </para>
	/// <para>
	/// A freshly generated scene has not been saved, so it has no name to bake under; the bake then
	/// happens when it is saved (<see cref="TerrainArrayAutoBake"/> watches for that), and the result
	/// says so.
	/// </para>
	/// </remarks>
	public static class TerrainArraySetup
	{
		/// <summary>The committed material every generated scene's terrain uses.</summary>
		public const string MaterialPath = "Assets/Prefabs/Client/Materials/Ground/Weather Terrain Array.mat";

		/// <summary>
		/// Far enough that no tile is ever drawn with the engine's basemap, which cannot see the arrays.
		/// The terrain inspector's own ceiling.
		/// </summary>
		public const float BasemapDistance = 20000f;

		/// <summary>The array material, created on first use. Null (with an error) if the shader is missing.</summary>
		/// <remarks>
		/// Created once and then left alone: a material somebody has tuned (snow depth, height blend)
		/// is theirs, and a generator re-applying defaults over it would undo that on every run.
		/// </remarks>
		public static Material EnsureMaterial()
		{
			Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
			if (material != null)
			{
				return material;
			}
			Shader shader = Shader.Find(TerrainArrayBinder.ShaderName);
			if (shader == null)
			{
				Debug.LogError($"[Terrain arrays] Shader '{TerrainArrayBinder.ShaderName}' was not found, so generated terrain cannot use the array renderer.");
				return null;
			}
			WorldEditorAssets.EnsureFolder(System.IO.Path.GetDirectoryName(MaterialPath).Replace('\\', '/'));
			material = new Material(shader) { name = "Weather Terrain Array" };
			material.SetFloat("_FishWeatherAmount", 1f);
			// The depth the hand-made scenes' terrain material uses.
			material.SetFloat("_FishSnowDepth", 0.12f);
			material.SetFloat("_EnableHeightBlend", 0f);
			material.SetFloat("_HeightTransition", 0.2f);
			// Per-pixel normals when instanced: the tangent frame from the heightmap, not the patch's coarse vertices.
			material.SetFloat("_EnableInstancedPerPixelNormal", 1f);
			material.EnableKeyword("_TERRAIN_INSTANCED_PERPIXEL_NORMAL");
			AssetDatabase.CreateAsset(material, MaterialPath);
			return material;
		}

		/// <summary>The coordinator's entry point; see the overload for <paramref name="biomeCoverage"/>.</summary>
		public static TerrainArraySetupResult Apply(Scene scene, IReadOnlyList<Terrain> terrains, SceneTerrainPalette palette)
		{
			return Apply(scene, terrains, palette, null);
		}

		/// <summary>
		/// Assigns the array material to every tile, records the palette's provenance on the scene's
		/// binder, and bakes the arrays (or defers the bake to the scene's first save).
		/// </summary>
		/// <param name="biomeCoverage">
		/// How much of the scene each biome covers, by the palette's biome index
		/// (<c>SceneBiomeField.Coverage()</c>). Decides which biome's LOCAL override wins on a shared
		/// layer. Null keeps the palette's own entry order.
		/// </param>
		public static TerrainArraySetupResult Apply(Scene scene, IReadOnlyList<Terrain> terrains, SceneTerrainPalette palette, IReadOnlyList<float> biomeCoverage)
		{
			var result = new TerrainArraySetupResult();
			if (!scene.IsValid() || palette == null || terrains == null)
			{
				result.Notes.Add("No scene, palette or terrains: nothing set up.");
				return result;
			}

			bool materialExisted = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath) != null;
			result.Material = EnsureMaterial();
			if (result.Material == null)
			{
				result.Notes.Add($"'{TerrainArrayBinder.ShaderName}' is missing; the terrain keeps its material.");
				return result;
			}
			if (!materialExisted)
			{
				result.Wrote.Add(MaterialPath);
			}

			var tiles = new List<Terrain>();
			foreach (Terrain terrain in terrains)
			{
				if (terrain == null)
				{
					continue;
				}
				terrain.materialTemplate = result.Material;
				terrain.basemapDistance = BasemapDistance;
				tiles.Add(terrain);
			}

			TerrainArrayBinder binder = EnsureBinder(scene, result);
			binder.Terrains.Clear();
			binder.Terrains.AddRange(tiles);
			TerrainArrayProvenance provenance = EnsureProvenance(binder, result);
			provenance.Layers = ProvenanceFrom(palette, biomeCoverage);
			result.Binder = binder;
			result.Wrote.Add($"'{binder.gameObject.name}' in the scene: {tiles.Count} tile(s), {provenance.Layers.Count} layer(s) of provenance");
			EditorSceneManager.MarkSceneDirty(scene);

			// Conflicts are worth reporting whether or not the bake can run yet.
			TerrainArrayPlan plan = TerrainArrayBaker.Plan(scene.name, provenance.Layers, new ProjectTerrainArrayLocalLookup());
			result.LocalConflicts.AddRange(plan.Conflicts);
			result.Notes.AddRange(plan.Problems);

			if (string.IsNullOrEmpty(scene.path))
			{
				result.BakeDeferred = true;
				result.Notes.Add("The terrain arrays are baked when the scene is first saved.");
			}
			else
			{
				foreach (TerrainArrayBakeReport report in TerrainArrayBaker.BakeScene(scene))
				{
					result.Baked |= report.Baked || report.UpToDate;
					result.Wrote.AddRange(report.Written);
					result.Notes.AddRange(report.Problems);
				}
			}
			binder.Bind();
			return result;
		}

		/// <summary>
		/// One provenance entry per palette layer, in channel order, each listing the biome slots that
		/// use it with the biome covering most of the scene first.
		/// </summary>
		public static List<TerrainArrayLayerSource> ProvenanceFrom(SceneTerrainPalette palette, IReadOnlyList<float> biomeCoverage)
		{
			var layers = new List<TerrainArrayLayerSource>();
			if (palette == null)
			{
				return layers;
			}
			var uses = new List<SceneTerrainPalette.Entry>[palette.Layers.Count];
			for (int i = 0; i < uses.Length; i++)
			{
				uses[i] = new List<SceneTerrainPalette.Entry>();
			}
			foreach (SceneTerrainPalette.Entry entry in palette.Entries)
			{
				if (entry != null && entry.LayerIndex >= 0 && entry.LayerIndex < uses.Length)
				{
					uses[entry.LayerIndex].Add(entry);
				}
			}
			for (int i = 0; i < palette.Layers.Count; i++)
			{
				List<SceneTerrainPalette.Entry> list = uses[i];
				if (biomeCoverage != null)
				{
					// Stable: equal coverage keeps the palette's order.
					var order = new List<int>(list.Count);
					for (int k = 0; k < list.Count; k++)
					{
						order.Add(k);
					}
					order.Sort((a, b) =>
					{
						int byCoverage = Coverage(biomeCoverage, list[b].BiomeIndex).CompareTo(Coverage(biomeCoverage, list[a].BiomeIndex));
						return byCoverage != 0 ? byCoverage : a.CompareTo(b);
					});
					var sorted = new List<SceneTerrainPalette.Entry>(list.Count);
					foreach (int k in order)
					{
						sorted.Add(list[k]);
					}
					list = sorted;
				}
				var source = new TerrainArrayLayerSource { Layer = palette.Layers[i] };
				foreach (SceneTerrainPalette.Entry entry in list)
				{
					if (entry.Biome != null)
					{
						source.Uses.Add(new TerrainArrayLayerUse { Biome = entry.Biome, Slot = entry.Slot });
					}
				}
				layers.Add(source);
			}
			return layers;
		}

		private static float Coverage(IReadOnlyList<float> coverage, int index) => index >= 0 && index < coverage.Count ? coverage[index] : 0f;

		/// <summary>
		/// Brings a binder's provenance in line with the layers its terrain actually has, after
		/// somebody adds, removes or reorders layers with the terrain tools. Returns true if it changed
		/// (and dirtied the scene). A layer keeps the biome uses it had; a new one has none, so it
		/// resolves by name only.
		/// </summary>
		public static bool SyncProvenance(TerrainArrayBinder binder)
		{
			if (binder == null)
			{
				return false;
			}
			TerrainLayer[] actual = null;
			foreach (Terrain terrain in binder.EffectiveTerrains())
			{
				if (terrain != null && terrain.terrainData != null)
				{
					actual = terrain.terrainData.terrainLayers;
					break;
				}
			}
			if (actual == null)
			{
				return false;
			}
			TerrainArrayProvenance provenance = binder.Provenance;
			if (provenance != null && Matches(provenance.Layers, actual))
			{
				return false;
			}
			if (provenance == null)
			{
				provenance = EnsureProvenance(binder, null);
			}
			var previous = new Dictionary<TerrainLayer, TerrainArrayLayerSource>();
			foreach (TerrainArrayLayerSource source in provenance.Layers)
			{
				if (source != null && source.Layer != null && !previous.ContainsKey(source.Layer))
				{
					previous[source.Layer] = source;
				}
			}
			var layers = new List<TerrainArrayLayerSource>(actual.Length);
			foreach (TerrainLayer layer in actual)
			{
				layers.Add(layer != null && previous.TryGetValue(layer, out TerrainArrayLayerSource kept)
					? kept
					: new TerrainArrayLayerSource { Layer = layer });
			}
			Undo.RecordObject(provenance, "Sync terrain array provenance");
			provenance.Layers = layers;
			EditorUtility.SetDirty(provenance);
			EditorSceneManager.MarkSceneDirty(binder.gameObject.scene);
			return true;
		}

		private static bool Matches(List<TerrainArrayLayerSource> recorded, TerrainLayer[] actual)
		{
			if (recorded == null || recorded.Count != actual.Length)
			{
				return false;
			}
			for (int i = 0; i < actual.Length; i++)
			{
				if (recorded[i] == null || recorded[i].Layer != actual[i])
				{
					return false;
				}
			}
			return true;
		}

		private static TerrainArrayBinder EnsureBinder(Scene scene, TerrainArraySetupResult result)
		{
			List<TerrainArrayBinder> existing = TerrainArrayBaker.BindersIn(scene);
			if (existing.Count > 0)
			{
				for (int i = 1; i < existing.Count; i++)
				{
					result.Notes.Add($"The scene has more than one terrain array binder; '{existing[0].gameObject.name}' was updated and '{existing[i].gameObject.name}' left alone.");
				}
				return existing[0];
			}
			var host = new GameObject(TerrainArrayBinder.ObjectName);
			SceneManager.MoveGameObjectToScene(host, scene);
			// RequireComponent adds the ClientOnlyObject marker that strips it from server builds.
			TerrainArrayBinder binder = host.AddComponent<TerrainArrayBinder>();
			return binder;
		}

		private static TerrainArrayProvenance EnsureProvenance(TerrainArrayBinder binder, TerrainArraySetupResult result)
		{
			TerrainArrayProvenance provenance = binder.Provenance;
			if (provenance != null)
			{
				if (!provenance.gameObject.CompareTag(TerrainArrayProvenance.EditorOnlyTag))
				{
					provenance.gameObject.tag = TerrainArrayProvenance.EditorOnlyTag;
					result?.Notes.Add("The provenance object was not tagged EditorOnly; it is now, so it stays out of builds.");
				}
				return provenance;
			}
			var child = new GameObject(TerrainArrayProvenance.ObjectName) { tag = TerrainArrayProvenance.EditorOnlyTag };
			child.transform.SetParent(binder.transform, false);
			return child.AddComponent<TerrainArrayProvenance>();
		}
	}
}
#endif
