using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using FishMMO.Shared;
using Object = UnityEngine.Object;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// The server build's terrain copy: the ground and the trees survive, the art does not, and the
	/// committed terrain data is never touched.
	/// </summary>
	[TestFixture]
	public class ServerTerrainStripperTests
	{
		private const int HeightResolution = 33;
		private const int AlphaResolution = 32;
		private const int DetailResolution = 32;

		private readonly List<Object> created = new List<Object>();

		[TearDown]
		public void TearDown()
		{
			for (int i = created.Count - 1; i >= 0; i--)
			{
				if (created[i] != null)
				{
					Object.DestroyImmediate(created[i]);
				}
			}
			created.Clear();
		}

		private T Track<T>(T obj) where T : Object
		{
			created.Add(obj);
			return obj;
		}

		/// <summary>A terrain data with everything a generated scene has: heights, a hole, layers, a detail layer and trees.</summary>
		private TerrainData Generated()
		{
			var data = Track(new TerrainData { name = "Generated" });
			data.heightmapResolution = HeightResolution;
			data.size = new Vector3(64f, 40f, 64f);
			var heights = new float[HeightResolution, HeightResolution];
			for (int y = 0; y < HeightResolution; y++)
			{
				for (int x = 0; x < HeightResolution; x++)
				{
					heights[y, x] = 0.25f + 0.5f * Mathf.Sin(x * 0.3f) * Mathf.Cos(y * 0.2f) * 0.5f;
				}
			}
			data.SetHeights(0, 0, heights);

			var holes = new bool[data.holesResolution, data.holesResolution];
			for (int y = 0; y < data.holesResolution; y++)
			{
				for (int x = 0; x < data.holesResolution; x++)
				{
					holes[y, x] = !(x == 5 && y == 7);
				}
			}
			data.SetHoles(0, 0, holes);

			data.alphamapResolution = AlphaResolution;
			data.terrainLayers = new[] { Track(new TerrainLayer { name = "Grass" }), Track(new TerrainLayer { name = "Rock" }) };

			var blade = Track(new Texture2D(4, 4) { name = "Blade" });
			data.detailPrototypes = new[] { new DetailPrototype { prototypeTexture = blade, usePrototypeMesh = false, renderMode = DetailRenderMode.GrassBillboard } };
			data.SetDetailResolution(DetailResolution, 8);
			var detail = new int[DetailResolution, DetailResolution];
			detail[3, 4] = 7;
			data.SetDetailLayer(0, 0, 0, detail);

			GameObject tree = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
			tree.name = "Tree";
			data.treePrototypes = new[] { new TreePrototype { prefab = tree } };
			data.RefreshPrototypes();
			data.SetTreeInstances(new[]
			{
				new TreeInstance { prototypeIndex = 0, position = new Vector3(0.25f, 0.3f, 0.75f), widthScale = 1.2f, heightScale = 0.9f, rotation = 1f, color = Color.white, lightmapColor = Color.white },
				new TreeInstance { prototypeIndex = 0, position = new Vector3(0.6f, 0.2f, 0.4f), widthScale = 0.8f, heightScale = 1.1f, rotation = 2f, color = Color.white, lightmapColor = Color.white },
			}, false);
			return data;
		}

		[Test]
		public void TheCopyKeepsTheGroundAndTheTreesAndDropsTheArt()
		{
			TerrainData original = Generated();
			TerrainData copy = Track(ServerTerrainStripper.ServerCopy(original));

			Assert.That(copy, Is.Not.SameAs(original));
			Assert.That(copy.heightmapResolution, Is.EqualTo(original.heightmapResolution));
			Assert.That(copy.size, Is.EqualTo(original.size));
			float[,] before = original.GetHeights(0, 0, HeightResolution, HeightResolution);
			float[,] after = copy.GetHeights(0, 0, HeightResolution, HeightResolution);
			for (int y = 0; y < HeightResolution; y++)
			{
				for (int x = 0; x < HeightResolution; x++)
				{
					Assert.That(after[y, x], Is.EqualTo(before[y, x]).Within(1e-4f), $"height at {x},{y}");
				}
			}
			Assert.That(copy.IsHole(5, 7), Is.True, "the hole the server must not stand on is kept");
			Assert.That(copy.IsHole(6, 7), Is.False);

			Assert.That(copy.treePrototypes.Length, Is.EqualTo(1));
			Assert.That(copy.treePrototypes[0].prefab, Is.SameAs(original.treePrototypes[0].prefab), "the collider builds tree colliders from the prefab");
			TreeInstance[] trees = copy.treeInstances;
			Assert.That(trees.Length, Is.EqualTo(2));
			for (int i = 0; i < trees.Length; i++)
			{
				Assert.That(Vector3.Distance(trees[i].position, original.treeInstances[i].position), Is.LessThan(1e-4f), $"tree {i} stays where it was");
				Assert.That(trees[i].widthScale, Is.EqualTo(original.treeInstances[i].widthScale).Within(1e-4f));
				Assert.That(trees[i].heightScale, Is.EqualTo(original.treeInstances[i].heightScale).Within(1e-4f));
			}

			Assert.That(copy.terrainLayers.Length, Is.EqualTo(0), "no terrain layers, so no textures behind them");
			Assert.That(copy.alphamapTextureCount, Is.EqualTo(0), "no splat maps");
			Assert.That(copy.alphamapResolution, Is.EqualTo(ServerTerrainStripper.MinimumAlphamapResolution));
			Assert.That(copy.detailPrototypes.Length, Is.EqualTo(0), "no detail layers");
			Assert.That(copy.detailResolution, Is.LessThanOrEqualTo(ServerTerrainStripper.MinimumDetailResolution));
			Assert.That(copy.name, Does.EndWith(ServerTerrainStripper.CopySuffix));
		}

		[Test]
		public void TheCommittedTerrainDataIsNeverTouched()
		{
			TerrainData original = Generated();
			float[,] heights = original.GetHeights(0, 0, HeightResolution, HeightResolution);
			TreeInstance[] trees = original.treeInstances;
			TerrainLayer[] layers = original.terrainLayers;
			Texture2D firstAlphamap = original.GetAlphamapTexture(0);

			Scene scene = EditorSceneManager.NewPreviewScene();
			try
			{
				GameObject tile = Terrain.CreateTerrainGameObject(original);
				SceneManager.MoveGameObjectToScene(tile, scene);
				Terrain terrain = tile.GetComponent<Terrain>();
				TerrainCollider collider = tile.GetComponent<TerrainCollider>();
				Assume.That(collider, Is.Not.Null, "CreateTerrainGameObject adds a collider");

				// A second tile sharing the data, and a collider-only object pointing at it.
				GameObject twin = Terrain.CreateTerrainGameObject(original);
				SceneManager.MoveGameObjectToScene(twin, scene);
				var colliderOnly = new GameObject("Collider only");
				SceneManager.MoveGameObjectToScene(colliderOnly, scene);
				colliderOnly.AddComponent<TerrainCollider>().terrainData = original;

				int copied = ServerTerrainStripper.Strip(scene);

				Assert.That(copied, Is.EqualTo(1), "one data shared by every tile gets one copy");
				TerrainData copy = terrain.terrainData;
				Track(copy);
				Assert.That(copy, Is.Not.SameAs(original), "the build's scene points at the copy");
				Assert.That(collider.terrainData, Is.SameAs(copy), "and so does its collider, or the original would ship anyway");
				Assert.That(twin.GetComponent<Terrain>().terrainData, Is.SameAs(copy));
				Assert.That(twin.GetComponent<TerrainCollider>().terrainData, Is.SameAs(copy));
				Assert.That(colliderOnly.GetComponent<TerrainCollider>().terrainData, Is.SameAs(copy));

				// The committed asset: exactly as it was.
				Assert.That(original.terrainLayers, Is.EqualTo(layers));
				Assert.That(original.alphamapResolution, Is.EqualTo(AlphaResolution));
				Assert.That(original.alphamapTextureCount, Is.EqualTo(1));
				Assert.That(original.GetAlphamapTexture(0), Is.SameAs(firstAlphamap), "its splat map is its own, not dropped with the copy's layers");
				Assert.That(original.detailPrototypes.Length, Is.EqualTo(1));
				Assert.That(original.detailResolution, Is.EqualTo(DetailResolution));
				Assert.That(original.GetDetailLayer(0, 0, DetailResolution, DetailResolution, 0)[3, 4], Is.EqualTo(7));
				Assert.That(original.treeInstances.Length, Is.EqualTo(trees.Length));
				Assert.That(original.GetHeights(0, 0, HeightResolution, HeightResolution)[10, 12], Is.EqualTo(heights[10, 12]));
				Assert.That(original.IsHole(5, 7), Is.True);
			}
			finally
			{
				EditorSceneManager.ClosePreviewScene(scene);
			}
		}

		[Test]
		public void OnlyAServerBuildStripsTerrain()
		{
			// Play mode (no report) and client builds keep the art: it is what the client draws.
			TerrainData original = Generated();
			Scene scene = EditorSceneManager.NewPreviewScene();
			try
			{
				GameObject tile = Terrain.CreateTerrainGameObject(original);
				SceneManager.MoveGameObjectToScene(tile, scene);
				new ServerTerrainStripper().OnProcessScene(scene, null);
				Assert.That(tile.GetComponent<Terrain>().terrainData, Is.SameAs(original));
			}
			finally
			{
				EditorSceneManager.ClosePreviewScene(scene);
			}
		}

		[Test]
		public void ItRunsAfterTheClientOnlyStripper()
		{
			Assert.That(new ServerTerrainStripper().callbackOrder, Is.GreaterThan(new ClientOnlySceneStripper().callbackOrder));
		}
	}
}
