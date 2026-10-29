using System.Collections.Generic;
using System.IO;
using System.Linq;
using FishMMO.Shared;
using FishMMO.Shared.WorldDesign;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace FishMMO.UnitTests
{
	public class SceneLookArrayBakerTests
	{
		[Test]
		public void CutCompactsSlicesAndPrefabDependenciesWithoutChangingSharedArt()
		{
			const string folder = "Assets/__SceneLooksTest";
			if (AssetDatabase.IsValidFolder(folder)) AssetDatabase.DeleteAsset(folder);
			AssetDatabase.CreateFolder("Assets", "__SceneLooksTest");
			AssetDatabase.CreateFolder(folder, "Cut");
			Scene scene = EditorSceneManager.NewPreviewScene();
			try
			{
				var image = new Texture2D(8, 2, TextureFormat.RGBA32, false);
				try
				{
					for (int y = 0; y < 2; y++) for (int x = 0; x < 8; x++) image.SetPixel(x, y, new[] { Color.red, Color.green, Color.blue, Color.yellow }[x / 2]);
					File.WriteAllBytes(folder + "/Looks.png", image.EncodeToPNG());
				}
				finally { Object.DestroyImmediate(image); }
				AssetDatabase.ImportAsset(folder + "/Looks.png");
				var importer = (TextureImporter)AssetImporter.GetAtPath(folder + "/Looks.png");
				var settings = new TextureImporterSettings(); importer.ReadTextureSettings(settings);
				settings.textureShape = TextureImporterShape.Texture2DArray;
				settings.flipbookColumns = 4; settings.flipbookRows = 1;
				importer.SetTextureSettings(settings); importer.textureCompression = TextureImporterCompression.Uncompressed; importer.SaveAndReimport();
				var array = AssetDatabase.LoadAssetAtPath<Texture2DArray>(folder + "/Looks.png");
				Assert.That(array.depth, Is.EqualTo(4));
				var shared = new Material(Shader.Find("FishMMO/Weather Lit")) { name = "Shared Look" };
				shared.enableInstancing = true;
				shared.EnableKeyword(ProceduralArtLookArrays.Keyword);
				shared.SetTexture("_FishLookColour", array);
				shared.SetVector("_FishLookSlices", new Vector4(3, -1, -1, -1));
				shared.SetFloat("_FishLookEmissionSlice", 1);
				AssetDatabase.CreateAsset(shared, folder + "/Shared.mat");
				var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
				go.GetComponent<Renderer>().sharedMaterial = shared;
				GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, folder + "/Shared.prefab");
				Object.DestroyImmediate(go);
				var terrainData = new TerrainData { heightmapResolution = 33 };
				terrainData.detailPrototypes = new[] { new DetailPrototype { prototype = prefab, usePrototypeMesh = true, useInstancing = true, renderMode = DetailRenderMode.VertexLit } };
				AssetDatabase.CreateAsset(terrainData, folder + "/Cut/Terrain.asset");
				var bake = ScriptableObject.CreateInstance<TerrainDetailBake>();
				bake.Tiles.Add(new TerrainDetailBake.Tile { Data = terrainData, Scatter = new TerrainDetailBake.Scatter { ChannelPrefabs = new[] { prefab } } });
				AssetDatabase.CreateAsset(bake, folder + "/Cut/Bake.asset");
				GameObject terrainObject = Terrain.CreateTerrainGameObject(terrainData); SceneManager.MoveGameObjectToScene(terrainObject, scene);
				var set = ScriptableObject.CreateInstance<ScenePropSet>();
				set.Prototypes = new[] { new ScenePropSet.Prototype { Prefab = prefab, Materials = new[] { shared } } };
				AssetDatabase.CreateAsset(set, folder + "/Cut/Props.asset");
				go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
				var host = new GameObject("Props"); SceneManager.MoveGameObjectToScene(host, scene);
				host.AddComponent<SceneProps>().Sets.Add(set);
				var wrote = new List<string>();
				SceneLookArrayBaker.Bake(scene, folder + "/Cut", wrote);
				var compactMaterial = go.GetComponent<Renderer>().sharedMaterial;
				Assert.That(compactMaterial, Is.Not.EqualTo(shared));
				Assert.That(compactMaterial.GetVector("_FishLookSlices").x, Is.EqualTo(1));
				Assert.That(compactMaterial.GetFloat("_FishLookEmissionSlice"), Is.Zero);
				Assert.That(((Texture2DArray)compactMaterial.GetTexture("_FishLookColour")).depth, Is.EqualTo(2));
				Assert.That(shared.GetVector("_FishLookSlices").x, Is.EqualTo(3));
				Assert.That(shared.GetTexture("_FishLookColour"), Is.EqualTo(array));
				Assert.That(set.Prototypes[0].Materials[0], Is.EqualTo(compactMaterial));
				Assert.That(set.Prototypes[0].Prefab, Is.Not.EqualTo(prefab));
				Assert.That(set.Prototypes[0].Prefab.name, Is.EqualTo(prefab.name), "terrain biome tables use exact prefab names");
				Assert.That(terrainData.detailPrototypes[0].prototype, Is.EqualTo(set.Prototypes[0].Prefab));
				Assert.That(bake.Tiles[0].Scatter.ChannelPrefabs[0], Is.EqualTo(set.Prototypes[0].Prefab));
				string prefabPath = AssetDatabase.GetAssetPath(set.Prototypes[0].Prefab);
				Assert.That(AssetDatabase.GetDependencies(prefabPath), Does.Not.Contain(folder + "/Looks.png"));
				Assert.That(AssetDatabase.GetDependencies(prefabPath), Does.Not.Contain(folder + "/Shared.prefab"));
				Assert.That(PrefabUtility.IsPartOfPrefabInstance(go), Is.False);
				var packed = new Texture2D(2, 2);
				try
				{
					packed.LoadImage(File.ReadAllBytes(AssetDatabase.GetAssetPath(compactMaterial.GetTexture("_FishLookColour"))));
					Assert.That(packed.GetPixel(0, packed.height - 1), Is.EqualTo(Color.green));
					Assert.That(packed.GetPixel(0, 0), Is.EqualTo(Color.yellow));
				}
				finally { Object.DestroyImmediate(packed); }
			}
			finally { EditorSceneManager.ClosePreviewScene(scene); AssetDatabase.DeleteAsset(folder); }
		}
	}
}
