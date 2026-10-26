using System;
using FishMMO.Shared;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace FishMMO.UnitTests.WorldDesign
{
	public class SceneAddressablesTests
	{
		[Test]
		public void SavedSceneRegistersRecursiveDependenciesAndKeepsExistingEntries()
		{
			string folder = "Assets/Scenes/WorldScene/AtlasAddressablesTest_" + Guid.NewGuid().ToString("N");
			System.IO.Directory.CreateDirectory(folder + "/Nested");
			AssetDatabase.Refresh();
			Scene previous = SceneManager.GetActiveScene();
			bool single = string.IsNullOrEmpty(previous.path);
			Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, single ? NewSceneMode.Single : NewSceneMode.Additive);
			var settings = AddressableAssetSettings.Create("", "AtlasAddressablesTest", false, false);
			try
			{
				var texture = new Texture2D(2, 2);
				AssetDatabase.CreateAsset(texture, folder + "/Texture.asset");
				var material = new Material(Shader.Find("Hidden/InternalErrorShader"));
				AssetDatabase.CreateAsset(material, folder + "/Material.mat");
				var layer = new TerrainLayer { diffuseTexture = texture };
				AssetDatabase.CreateAsset(layer, folder + "/Layer.terrainlayer");
				var terrain = new TerrainData { heightmapResolution = 33, size = new Vector3(32, 10, 32), terrainLayers = new[] { layer } };
				AssetDatabase.CreateAsset(terrain, folder + "/Terrain.asset");
				// An existing entry's custom address and labels must survive the scene's registration.
				var custom = settings.CreateGroup("Custom", true, false, false, null);
				var existing = settings.CreateOrMoveEntry(AssetDatabase.AssetPathToGUID(folder + "/Material.mat"), custom);
				existing.SetAddress("Authored material");
				existing.SetLabel("Authored label", true, true);
				var navMesh = new NavMeshData();
				string navPath = folder + "/NavMesh.asset";
				AssetDatabase.CreateAsset(navMesh, navPath);
				// Not directly referenced by the scene: the generator also supplies its output paths.
				var duplicate = new Material(material);
				string duplicatePath = folder + "/Nested/Authored material.mat";
				AssetDatabase.CreateAsset(duplicate, duplicatePath);
				var ground = Terrain.CreateTerrainGameObject(terrain);
				SceneManager.MoveGameObjectToScene(ground, scene);
				ground.GetComponent<Terrain>().materialTemplate = material;
				string scenePath = folder + "/Cut scene.unity";
				Assert.That(EditorSceneManager.SaveScene(scene, scenePath), Is.True);
				AssetDatabase.SaveAssets();

				var outputs = new[] { navPath, duplicatePath, "Assets/Scripts/Shared/Implementation/Tools/Extensions/Unity/Editor/World/SceneGenerator.cs" };
				Assert.That(AddressablesDashboard.RegisterSceneAssets(settings, scenePath, outputs), Is.GreaterThan(0));
				AssertEntry(settings, scenePath, "Scene_Shared", "Cut scene");
				AssertEntry(settings, folder + "/Terrain.asset", "Scene_Shared", "Terrain");
				AssertEntry(settings, folder + "/Texture.asset", "Client_Static_Permanent", "Texture");
				AssertEntry(settings, folder + "/Layer.terrainlayer", "Client_Static_Permanent", "Layer");
				AssertEntry(settings, navPath, "Server_Static_Permanent", "NavMesh");
				AssertEntry(settings, duplicatePath, "Client_Static_Permanent", duplicatePath);
				Assert.That(existing.parentGroup, Is.SameAs(custom));
				Assert.That(existing.address, Is.EqualTo("Authored material"));
				Assert.That(existing.labels, Does.Contain("Authored label"));
				Assert.That(settings.FindAssetEntry(AssetDatabase.AssetPathToGUID(outputs[2])), Is.Null);
				Assert.That(AddressablesDashboard.RegisterSceneAssets(settings, scenePath, outputs), Is.Zero,
					"Registering the same cut again must be idempotent.");
			}
			finally
			{
				foreach (var group in settings.groups)
				{
					if (group == null) continue;
					foreach (var schema in group.Schemas) Object.DestroyImmediate(schema);
					Object.DestroyImmediate(group);
				}
				Object.DestroyImmediate(settings);
				if (scene.IsValid())
				{
					if (single) EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
					else EditorSceneManager.CloseScene(scene, true);
				}
				if (previous.IsValid()) SceneManager.SetActiveScene(previous);
				AssetDatabase.DeleteAsset(folder);
			}
		}

		private static void AssertEntry(AddressableAssetSettings settings, string path, string group, string address)
		{
			var entry = settings.FindAssetEntry(AssetDatabase.AssetPathToGUID(path));
			Assert.That(entry, Is.Not.Null, path);
			Assert.That(entry.parentGroup.Name, Is.EqualTo(group), path);
			Assert.That(entry.address, Is.EqualTo(address), path);
			Assert.That(entry.labels, Does.Contain(group), path);
		}
	}
}
