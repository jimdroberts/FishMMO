using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using FishMMO.Server.Implementation.World.SceneServer.Navigation;
using FishMMO.Shared;
using NUnit.Framework;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace FishMMO.UnitTests.AI
{
	/// <summary>
	/// NavMeshes ship to the scene server only, through the catalogue: every baked scene has an entry, every NavMesh
	/// and the system are in the server-only group, the scene server runs the system, builds strip the surfaces, and
	/// a scene's NavMesh is in the world while any instance of it is loaded.
	/// </summary>
	[TestFixture]
	public class SceneNavMeshCatalogueTests
	{
		private const string SceneRoot = "Assets/Scenes/WorldScene";
		private const string SystemPath = "Assets/Prefabs/Server/SceneServer/NavMeshSystem.asset";
		private const string SceneServerPath = "Assets/Scenes/Server/SceneServer.unity";

		private readonly List<Object> created = new List<Object>();

		[TearDown]
		public void TearDown()
		{
			foreach (Object o in created)
			{
				if (o != null)
				{
					Object.DestroyImmediate(o);
				}
			}
			created.Clear();
		}

		private static SceneNavMeshCatalogue Catalogue()
		{
			SceneNavMeshCatalogue catalogue = SceneNavMeshCatalogueEditor.Find();
			Assert.That(catalogue, Is.Not.Null, "no SceneNavMeshCatalogue asset");
			return catalogue;
		}

		private static AddressableAssetEntry ServerEntry(string assetPath)
		{
			AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
			Assert.That(settings, Is.Not.Null);
			AddressableAssetEntry entry = settings.FindAssetEntry(AssetDatabase.AssetPathToGUID(assetPath));
			return entry != null && entry.parentGroup != null && entry.parentGroup.Name == ServerAddressables.GroupName
				&& entry.labels.Contains(ServerAddressables.Label) ? entry : null;
		}

		[Test]
		public void EveryBakedSceneHasItsNavMeshInTheCatalogue()
		{
			SceneNavMeshCatalogue catalogue = Catalogue();
			var surface = new Regex(@"^\s+m_NavMeshData:\s*\{fileID:\s*\d+,\s*guid:\s*(\w+)", RegexOptions.Multiline);
			int baked = 0;
			foreach (string guid in AssetDatabase.FindAssets("t:Scene", new[] { SceneRoot }))
			{
				string path = AssetDatabase.GUIDToAssetPath(guid);
				// The scene's own NavMeshSettings block holds a legacy reference too; surfaces are the MonoBehaviours.
				foreach (Match match in surface.Matches(File.ReadAllText(path)))
				{
					string scene = Path.GetFileNameWithoutExtension(path);
					baked++;
					Assert.That(catalogue.TryGet(scene, out SceneNavMeshCatalogue.Entry entry), Is.True, $"'{scene}' has a baked NavMesh surface but no catalogue entry: the scene server would load it with no NavMesh");
					Assert.That(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(entry.Data)), Is.EqualTo(match.Groups[1].Value), $"'{scene}': the catalogue holds another bake than the scene's surface");
				}
			}
			Assert.That(baked, Is.GreaterThan(0), "no baked scene found: the pin would be vacuous");
		}

		[Test]
		public void EveryNavMeshAndTheSystemAreInTheServerGroup()
		{
			foreach (SceneNavMeshCatalogue.Entry entry in Catalogue().Entries)
			{
				Assert.That(entry.Data, Is.Not.Null, $"'{entry.SceneName}' has an empty entry");
				string path = AssetDatabase.GetAssetPath(entry.Data);
				Assert.That(ServerEntry(path), Is.Not.Null, $"'{path}' is not an explicit {ServerAddressables.GroupName} entry");
			}
			Assert.That(ServerEntry(AssetDatabase.GetAssetPath(Catalogue())), Is.Not.Null, "the catalogue is not in the server group");
			Assert.That(ServerEntry(SystemPath), Is.Not.Null, "the NavMesh system is not in the server group");
			var system = AssetDatabase.LoadAssetAtPath<NavMeshSystem>(SystemPath);
			Assert.That(system, Is.Not.Null);
			Assert.That(system.Catalogue, Is.SameAs(Catalogue()));
			string systemGuid = AssetDatabase.AssetPathToGUID(SystemPath);
			Assert.That(File.ReadAllText(SceneServerPath), Does.Contain($"guid: {systemGuid}"), "the scene server does not run the NavMesh system");
		}

		[Test]
		public void BuildsStripTheSurfacesAndKeepTheirObjects()
		{
			Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
			var host = new GameObject("Terrain");
			host.AddComponent<BoxCollider>();
			host.AddComponent<NavMeshSurface>();
			Assert.That(NavMeshSurfaceStripper.Strip(scene), Is.EqualTo(1));
			Assert.That(host.GetComponent<NavMeshSurface>(), Is.Null);
			Assert.That(host.GetComponent<BoxCollider>(), Is.Not.Null, "only the surface goes");
		}

		[Test]
		public void ASceneNavMeshIsInTheWorldWhileAnyInstanceIsLoaded()
		{
			// A 20 m floor, baked in memory and placed 1000 m out, where no other test's NavMesh lies.
			var sources = new List<NavMeshBuildSource>
			{
				new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Box, size = new Vector3(20f, 1f, 20f), transform = Matrix4x4.identity, area = 0 },
			};
			NavMeshData data = NavMeshBuilder.BuildNavMeshData(NavMesh.GetSettingsByID(0), sources, new Bounds(Vector3.zero, new Vector3(30f, 10f, 30f)), Vector3.zero, Quaternion.identity);
			created.Add(data);
			var catalogue = ScriptableObject.CreateInstance<SceneNavMeshCatalogue>();
			created.Add(catalogue);
			var at = new Vector3(1000f, 0f, 1000f);
			catalogue.Entries.Add(new SceneNavMeshCatalogue.Entry { SceneName = "Floor", Data = data, Position = at, Rotation = Quaternion.identity });
			var navMeshes = new SceneNavMeshes(catalogue);
			try
			{
				Assert.That(navMeshes.Add("Elsewhere"), Is.False, "a scene with no NavMesh adds nothing");
				Assert.That(navMeshes.Add("Floor"), Is.True);
				Assert.That(navMeshes.Add("Floor"), Is.True, "a stacked second instance");
				Assert.That(navMeshes.Count, Is.EqualTo(1), "added once, not once per instance");
				Assert.That(NavMesh.SamplePosition(at + Vector3.up, out NavMeshHit hit, 2f, NavMesh.AllAreas), Is.True, "at the entry's position");
				navMeshes.Remove("Floor");
				Assert.That(navMeshes.IsLoaded("Floor"), Is.True, "one instance is still loaded");
				navMeshes.Remove("Floor");
				Assert.That(navMeshes.IsLoaded("Floor"), Is.False);
				Assert.That(NavMesh.SamplePosition(at + Vector3.up, out hit, 2f, NavMesh.AllAreas), Is.False, "out of the world with the last instance");
			}
			finally
			{
				navMeshes.Clear();
			}
		}
	}
}
