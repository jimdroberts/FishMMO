using System.Collections.Generic;
using FishMMO.Shared;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.WorldDesign;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// I2h (2026-10-18): ridge and furrow carved into a real terrain, taken out and carved again: the ground comes back bit for
	/// bit, the second carve is the first bit for bit, the record is editor-only and goes with the restore, and a tree standing
	/// in the field keeps its height over the ground. Unity-only (it makes a TerrainData and scene objects).
	/// </summary>
	public class BiomeFieldCarveTerrainTests
	{
		private readonly List<Object> made = new List<Object>();

		[TearDown]
		public void TearDown()
		{
			foreach (Object o in made)
			{
				if (o != null)
				{
					Object.DestroyImmediate(o);
				}
			}
			made.Clear();
		}

		[Test]
		public void CarveRestoreCarve_OnATerrain_IsBitForBit()
		{
			const int res = 129;
			var data = new TerrainData { heightmapResolution = res, size = new Vector3(256f, 300f, 256f) };
			made.Add(data);
			var ground = new float[res, res];
			for (int j = 0; j < res; j++)
			{
				for (int i = 0; i < res; i++)
				{
					ground[j, i] = 0.3f + 0.01f * Mathf.Sin(i * 0.11f) * Mathf.Cos(j * 0.07f);
				}
			}
			data.SetHeights(0, 0, ground);
			float[,] original = data.GetHeights(0, 0, res, res);
			GameObject tile = Terrain.CreateTerrainGameObject(data);
			made.Add(tile);
			Terrain terrain = tile.GetComponent<Terrain>();
			var root = new GameObject(PointOfInterestGenerator.RootName);
			made.Add(root);
			var site = new GameObject("Site");
			site.transform.SetParent(root.transform, false);
			var terrains = new Terrain[1, 1];
			terrains[0, 0] = terrain;
			Scene scene = root.scene;
			var context = new PointOfInterestSiteContext { Scene = scene, Root = site.transform, Terrains = terrains };

			var centre = new Vector2(128f, 120f);
			var size = new Vector2(60f, 30f);
			int raised = PointOfInterestFieldCarve.Carve(context, centre, 21f, size, BiomeFieldSurface.RidgeAndFurrow);
			Assert.That(raised, Is.GreaterThan(100));
			float[,] carved = data.GetHeights(0, 0, res, res);
			Assert.That(carved, Is.Not.EqualTo(original));
			GeneratedFieldCarve record = root.GetComponentInChildren<GeneratedFieldCarve>(true);
			Assert.That(record, Is.Not.Null);
			Assert.That(record.gameObject.CompareTag("EditorOnly"), Is.True, "the record is left out of builds");

			Assert.That(PointOfInterestFieldCarve.Restore(scene, terrains, new List<string>()), Is.EqualTo(raised));
			Assert.That(data.GetHeights(0, 0, res, res), Is.EqualTo(original), "the ground as it was, bit for bit");
			Assert.That(root.GetComponentInChildren<GeneratedFieldCarve>(true), Is.Null, "the record goes with the restore");

			PointOfInterestFieldCarve.Carve(context, centre, 21f, size, BiomeFieldSurface.RidgeAndFurrow);
			Assert.That(data.GetHeights(0, 0, res, res), Is.EqualTo(carved), "carved again: the first carve, bit for bit");
			PointOfInterestFieldCarve.Restore(scene, terrains, null);

			// A tree standing on a ridge keeps its height over the ground.
			data.treePrototypes = new[] { new TreePrototype { prefab = GameObject.CreatePrimitive(PrimitiveType.Cube) } };
			made.Add(data.treePrototypes[0].prefab);
			var at = new Vector2(centre.x / 256f, centre.y / 256f);
			float under = data.GetInterpolatedHeight(at.x, at.y);
			data.SetTreeInstances(new[] { new TreeInstance { position = new Vector3(at.x, under / 300f - 0.001f, at.y), widthScale = 1f, heightScale = 1f, prototypeIndex = 0 } }, false);
			PointOfInterestFieldCarve.Carve(context, centre, 21f, size, BiomeFieldSurface.RidgeAndFurrow);
			float rose = data.GetInterpolatedHeight(at.x, at.y) - under;
			Assert.That(data.treeInstances[0].position.y * 300f, Is.EqualTo(under - 0.3f + rose).Within(0.01f), "the tree follows the ground");
			PointOfInterestFieldCarve.Restore(scene, terrains, null);
			Assert.That(data.treeInstances[0].position.y * 300f, Is.EqualTo(under - 0.3f).Within(0.01f), "and back");
		}
	}
}
