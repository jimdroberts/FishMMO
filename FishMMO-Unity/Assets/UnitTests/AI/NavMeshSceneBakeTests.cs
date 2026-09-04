using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;

namespace FishMMO.UnitTests.AI
{
	/// <summary>
	/// Pins the NavMesh bake settings a terrain scene needs for NPCs to stand on the ground.
	/// </summary>
	/// <remarks>
	/// Measured 2026-09-03 (issue #220): the one scene baked with a height mesh placed the
	/// NavMesh within 1 cm of the terrain everywhere; the three without it floated NPCs a mean
	/// 10–19 cm above the ground, a quarter of the map by more than 25 cm, with 0.7 m outliers in
	/// both directions. Without a height mesh a NavMeshAgent stands on the simplified polygon,
	/// not the terrain. Read from the scene YAML so the pin survives without loading the scene.
	/// </remarks>
	[TestFixture]
	public class NavMeshSceneBakeTests
	{
		private const string SceneRoot = "Assets/Scenes/WorldScene";

		private struct SurfaceScene
		{
			public string Path;
			public bool HasTerrain;
			public bool BuildHeightMesh;
			public bool HasNavMeshData;
		}

		private static List<SurfaceScene> ReadScenes()
		{
			List<SurfaceScene> results = new List<SurfaceScene>();
			foreach (string guid in AssetDatabase.FindAssets("t:Scene", new[] { SceneRoot }))
			{
				string path = AssetDatabase.GUIDToAssetPath(guid);
				if (string.IsNullOrEmpty(path) || !path.EndsWith(".unity"))
				{
					continue;
				}
				string text = File.ReadAllText(path);
				Match heightMesh = Regex.Match(text, @"^\s+m_BuildHeightMesh:\s*(\d)\s*$", RegexOptions.Multiline);
				if (!heightMesh.Success)
				{
					// No NavMeshSurface in this scene.
					continue;
				}
				results.Add(new SurfaceScene
				{
					Path = path,
					HasTerrain = Regex.IsMatch(text, @"^Terrain:\s*$", RegexOptions.Multiline),
					BuildHeightMesh = heightMesh.Groups[1].Value == "1",
					HasNavMeshData = Regex.IsMatch(text, @"^\s+m_NavMeshData:\s*\{fileID:\s*\d+,\s*guid:", RegexOptions.Multiline),
				});
			}
			return results;
		}

		[Test]
		public void TerrainScenesWithANavMeshSurface_Exist()
		{
			Assert.That(ReadScenes().FindAll(s => s.HasTerrain), Is.Not.Empty, "the pin below would be vacuous");
		}

		[Test]
		public void EveryTerrainScene_BakesAHeightMesh()
		{
			List<string> failures = new List<string>();
			foreach (SurfaceScene scene in ReadScenes())
			{
				if (scene.HasTerrain && !scene.BuildHeightMesh)
				{
					failures.Add(scene.Path);
				}
			}
			Assert.That(failures, Is.Empty, "NavMeshSurface.buildHeightMesh must be on for terrain scenes, then rebake");
		}

		[Test]
		public void EveryNavMeshSurface_ReferencesBakedData()
		{
			List<string> failures = new List<string>();
			foreach (SurfaceScene scene in ReadScenes())
			{
				if (!scene.HasNavMeshData)
				{
					failures.Add(scene.Path);
				}
			}
			Assert.That(failures, Is.Empty, "a NavMeshSurface with no baked data gives NPCs nothing to stand on");
		}
	}
}
