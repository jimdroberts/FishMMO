#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>Compacts generated prop surfaces to the slices used by one cut, without changing shared art.</summary>
	public static class SceneLookArrayBaker
	{
		private sealed class ArrayUse
		{
			public Texture2DArray Source;
			public readonly SortedSet<int> Slices = new SortedSet<int>();
			public readonly Dictionary<int, int> Remap = new Dictionary<int, int>();
			public Texture2DArray Compact;
		}

		public static void Bake(Scene scene, string folder, List<string> wrote)
		{
			// Material overrides still retain their source prefab's dependency graph. The cut owns these instances.
			foreach (GameObject root in scene.GetRootGameObjects())
				foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
					if (renderer.sharedMaterials.Any(m => m != null && m.IsKeywordEnabled(ProceduralArtLookArrays.Keyword)))
					{
						GameObject instance = PrefabUtility.GetOutermostPrefabInstanceRoot(renderer.gameObject);
						if (instance != null) PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
					}
			var owned = new List<Object>();
			foreach (GameObject root in scene.GetRootGameObjects()) owned.AddRange(root.GetComponentsInChildren<Component>(true).Where(c => c != null));
			foreach (string guid in AssetDatabase.FindAssets("", new[] { folder }))
				owned.AddRange(AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(guid)).Where(o => o is ScriptableObject || o is Material));

			var sets = owned.OfType<ScenePropSet>().Distinct().ToArray();
			var terrains = owned.OfType<Terrain>().Select(t => t.terrainData).Where(t => t != null).Distinct().ToArray();
			var prefabs = sets.SelectMany(s => s.Prototypes).Select(p => p.Prefab)
				.Concat(terrains.SelectMany(t => t.treePrototypes).Select(p => p.prefab))
				.Concat(terrains.SelectMany(t => t.detailPrototypes).Select(p => p.prototype))
				.Where(p => p != null).Distinct().ToArray();
			var materials = new HashSet<Material>(owned.OfType<Material>());
			foreach (Component c in owned.OfType<Component>())
				if (c is Renderer renderer) foreach (Material m in renderer.sharedMaterials) if (m != null) materials.Add(m);
			foreach (ScenePropSet set in sets)
				foreach (var prototype in set.Prototypes)
					if (prototype.Materials != null) foreach (Material m in prototype.Materials) if (m != null) materials.Add(m);
			foreach (GameObject prefab in prefabs)
				foreach (Renderer renderer in prefab.GetComponentsInChildren<Renderer>(true))
					foreach (Material m in renderer.sharedMaterials) if (m != null) materials.Add(m);

			materials.RemoveWhere(m => !m.IsKeywordEnabled(ProceduralArtLookArrays.Keyword));
			var arrays = new Dictionary<Texture2DArray, ArrayUse>();
			foreach (Material m in materials)
			{
				Vector4 slices = m.GetVector(ProceduralArtLookArrays.SlicesProperty);
				for (int slot = 0; slot < 5; slot++)
				{
					int slice = Mathf.RoundToInt(slot == 4 ? m.GetFloat(ProceduralArtLookArrays.EmissionSliceProperty) : slices[slot]);
					var source = m.GetTexture(ProceduralArtLookArrays.ArrayProperty(ProceduralArtLookArrays.KindOfSlot(slot))) as Texture2DArray;
					if (source == null || slice < 0) continue;
					if (slice >= source.depth) throw new InvalidOperationException($"{m.name}: look slice {slice} exceeds {source.name}");
					if (!arrays.TryGetValue(source, out ArrayUse use)) arrays.Add(source, use = new ArrayUse { Source = source });
					use.Slices.Add(slice);
				}
			}
			if (arrays.Count == 0) return;
			// Keep the cut payload flat, matching its backup/restore and Addressables registration.
			string target = folder;
			foreach (ArrayUse use in arrays.Values) WriteArray(use, target, wrote);

			var remap = new Dictionary<Material, Material>();
			foreach (Material source in materials.OrderBy(m => AssetDatabase.GetAssetPath(m), StringComparer.Ordinal).ThenBy(m => m.name, StringComparer.Ordinal))
			{
				// Embedded scene materials must be updated too: even an unused subasset retains its dependencies.
				bool local = AssetDatabase.GetAssetPath(source).StartsWith(folder + "/", StringComparison.Ordinal);
				Material copy = local ? source : new Material(source) { name = source.name };
				Vector4 slices = copy.GetVector(ProceduralArtLookArrays.SlicesProperty);
				for (int slot = 0; slot < 5; slot++)
				{
					string property = ProceduralArtLookArrays.ArrayProperty(ProceduralArtLookArrays.KindOfSlot(slot));
					var array = source.GetTexture(property) as Texture2DArray;
					int slice = Mathf.RoundToInt(slot == 4 ? source.GetFloat(ProceduralArtLookArrays.EmissionSliceProperty) : slices[slot]);
					if (array == null || !arrays.TryGetValue(array, out ArrayUse use)) continue;
					if (slice >= 0)
					{
						if (slot == 4) copy.SetFloat(ProceduralArtLookArrays.EmissionSliceProperty, use.Remap[slice]);
						else slices[slot] = use.Remap[slice];
					}
				}
				copy.SetVector(ProceduralArtLookArrays.SlicesProperty, slices);
				foreach (var kind in ProceduralArtLookArrays.Kinds)
				{
					string property = ProceduralArtLookArrays.ArrayProperty(kind);
					var array = source.GetTexture(property) as Texture2DArray;
					// Drop arrays with no sampled slices as well.
					copy.SetTexture(property, array != null && arrays.TryGetValue(array, out ArrayUse use) ? use.Compact : null);
				}
				if (!local)
				{
					string path = AssetDatabase.GenerateUniqueAssetPath(target + "/" + source.name.Replace('/', '_') + " (scene look).mat");
					AssetDatabase.CreateAsset(copy, path);
					wrote.Add(path);
				}
				EditorUtility.SetDirty(copy);
				remap.Add(source, copy);
			}

			// A prototype still references its prefab, even with material overrides. Make independent scene prefabs so
			// the dependency graph cannot pull the original whole-family arrays back into the player.
			var prefabMap = new Dictionary<GameObject, GameObject>();
			foreach (GameObject prefab in prefabs)
			{
				if (!prefab.GetComponentsInChildren<Renderer>(true).Any(r => r.sharedMaterials.Any(m => m != null && remap.ContainsKey(m)))) continue;
				GameObject contents = PrefabUtility.LoadPrefabContents(AssetDatabase.GetAssetPath(prefab));
				try
				{
					foreach (Transform child in contents.GetComponentsInChildren<Transform>(true))
						if (child != null && PrefabUtility.IsAnyPrefabInstanceRoot(child.gameObject))
							PrefabUtility.UnpackPrefabInstance(child.gameObject, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
					foreach (Renderer renderer in contents.GetComponentsInChildren<Renderer>(true)) Replace(renderer, remap);
					// TerrainDetailBiomes and other baked tables use the exact prefab name, not only its prefix.
					string sourcePath = AssetDatabase.GetAssetPath(prefab);
					string path = sourcePath.StartsWith(folder + "/", StringComparison.Ordinal) ? sourcePath : target + "/" + Path.GetFileName(sourcePath);
					if (path != sourcePath && File.Exists(path)) throw new InvalidOperationException($"Scene prop prefab name collision: {path}");
					prefabMap.Add(prefab, PrefabUtility.SaveAsPrefabAsset(contents, path));
					wrote.Add(path);
				}
				finally { PrefabUtility.UnloadPrefabContents(contents); }
			}
			foreach (ScenePropSet set in sets)
			{
				for (int i = 0; i < set.Prototypes.Length; i++)
					if (set.Prototypes[i].Prefab != null && prefabMap.TryGetValue(set.Prototypes[i].Prefab, out GameObject prefab)) set.Prototypes[i].Prefab = prefab;
			}
			foreach (TerrainData terrain in terrains)
			{
				var trees = terrain.treePrototypes;
				bool changed = false;
				foreach (var tree in trees)
					if (tree.prefab != null && prefabMap.TryGetValue(tree.prefab, out GameObject prefab)) { tree.prefab = prefab; changed = true; }
				if (changed) terrain.treePrototypes = trees;
				var details = terrain.detailPrototypes;
				changed = false;
				foreach (var detail in details)
					if (detail.prototype != null && prefabMap.TryGetValue(detail.prototype, out GameObject prefab)) { detail.prototype = prefab; changed = true; }
				if (changed) terrain.detailPrototypes = details;
				EditorUtility.SetDirty(terrain);
			}

			foreach (Object obj in owned)
			{
				if (obj == null || obj is Material) continue;
				using var serialized = new SerializedObject(obj);
				var property = serialized.GetIterator();
				bool changed = false;
				bool enterChildren = true;
				while (property.Next(enterChildren))
				{
					enterChildren = property.propertyType == SerializedPropertyType.Generic;
					// Bakes contain millions of primitive samples; none can hold an object reference.
					if (property.isArray && property.propertyType != SerializedPropertyType.String)
						enterChildren = property.arraySize > 0 && (property.GetArrayElementAtIndex(0).propertyType == SerializedPropertyType.Generic ||
							property.GetArrayElementAtIndex(0).propertyType == SerializedPropertyType.ObjectReference);
					if (property.propertyType != SerializedPropertyType.ObjectReference) continue;
					if (property.objectReferenceValue is Material m && remap.TryGetValue(m, out Material copy))
					{ property.objectReferenceValue = copy; changed = true; }
					else if (property.objectReferenceValue is GameObject go && prefabMap.TryGetValue(go, out GameObject prefab))
					{ property.objectReferenceValue = prefab; changed = true; }
				}
				if (changed) serialized.ApplyModifiedPropertiesWithoutUndo();
				EditorUtility.SetDirty(obj);
			}
			AssetDatabase.SaveAssets();
		}

		private static void Replace(Renderer renderer, Dictionary<Material, Material> remap)
		{
			var materials = renderer.sharedMaterials;
			for (int i = 0; i < materials.Length; i++) if (materials[i] != null && remap.TryGetValue(materials[i], out Material copy)) materials[i] = copy;
			renderer.sharedMaterials = materials;
		}

		private static void WriteArray(ArrayUse use, string folder, List<string> wrote)
		{
			string sourcePath = AssetDatabase.GetAssetPath(use.Source);
			var sourceImporter = AssetImporter.GetAtPath(sourcePath) as TextureImporter;
			if (sourceImporter == null || !sourcePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
				throw new InvalidOperationException($"{sourcePath}: expected a generated look flipbook");
			var settings = new TextureImporterSettings();
			sourceImporter.ReadTextureSettings(settings);
			sourceImporter.GetSourceTextureWidthAndHeight(out int sourceWidth, out int sourceHeight);
			int size = sourceWidth / settings.flipbookColumns;
			if (size <= 0 || sourceWidth % settings.flipbookColumns != 0 || sourceHeight != size * settings.flipbookRows)
				throw new InvalidOperationException($"{sourcePath}: expected square source flipbook cells");
			if (!ProceduralArtLookArrays.Grid(use.Slices.Count, size, out int columns, out int rows)) throw new InvalidOperationException("Scene look array exceeds WebGPU limits");
			var source = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
			var packed = new Texture2D(columns * size, rows * size, TextureFormat.RGBA32, false, true);
			try
			{
				if (!source.LoadImage(File.ReadAllBytes(sourcePath))) throw new InvalidOperationException("Could not read " + sourcePath);
				packed.SetPixels32(new Color32[packed.width * packed.height]);
				int next = 0;
				foreach (int slice in use.Slices)
				{
					int x = slice % settings.flipbookColumns * size;
					int y = source.height - (slice / settings.flipbookColumns + 1) * size;
					packed.SetPixels(next % columns * size, packed.height - (next / columns + 1) * size, size, size, source.GetPixels(x, y, size, size));
					use.Remap.Add(slice, next++);
				}
				string path = folder + "/PropLook " + Path.GetFileName(sourcePath);
				File.WriteAllBytes(path, packed.EncodeToPNG());
				AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
				var importer = (TextureImporter)AssetImporter.GetAtPath(path);
				settings.flipbookColumns = columns;
				settings.flipbookRows = rows;
				importer.SetTextureSettings(settings);
				importer.textureCompression = sourceImporter.textureCompression;
				importer.compressionQuality = sourceImporter.compressionQuality;
				importer.maxTextureSize = ProceduralArtLookArrays.MaxSourceSide;
				importer.npotScale = TextureImporterNPOTScale.None;
				importer.isReadable = false;
				// Preserve platform formats, especially browser normal-map packing.
				foreach (string platform in new[] { "Standalone", "WebGL", "Android", "iPhone" }) importer.SetPlatformTextureSettings(sourceImporter.GetPlatformTextureSettings(platform));
				importer.SaveAndReimport();
				use.Compact = AssetDatabase.LoadAssetAtPath<Texture2DArray>(path);
				if (use.Compact == null) throw new InvalidOperationException("Could not import scene look array " + path);
				wrote.Add(path);
			}
			finally { Object.DestroyImmediate(source); Object.DestroyImmediate(packed); }
		}
	}
}
#endif
