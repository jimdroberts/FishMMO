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
	/// The client build's terrain copy for array-drawn terrain: the ground, its weights, details and
	/// trees survive exactly; the terrain layers keep their count, order and tiling but lose their
	/// textures; old-material terrain and the committed terrain data are never touched.
	/// </summary>
	[TestFixture]
	public class ClientTerrainLayerStripperTests
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

		private Texture2D Texture(string name) => Track(new Texture2D(4, 4) { name = name });

		/// <summary>A terrain data like a generated scene's: heights, a hole, two textured layers with distinct weights, a detail layer and trees.</summary>
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
					heights[y, x] = 0.25f + 0.25f * Mathf.Sin(x * 0.3f) * Mathf.Cos(y * 0.2f);
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
			data.terrainLayers = new[]
			{
				Track(new TerrainLayer { name = "Grass", diffuseTexture = Texture("GrassA"), normalMapTexture = Texture("GrassN"), maskMapTexture = Texture("GrassM"), tileSize = new Vector2(4f, 4f), tileOffset = new Vector2(1f, 2f), normalScale = 0.7f, smoothness = 0.3f }),
				Track(new TerrainLayer { name = "Rock", diffuseTexture = Texture("RockA"), tileSize = new Vector2(9f, 9f), metallic = 0.1f, diffuseRemapMax = new Vector4(0.9f, 0.8f, 0.7f, 1f) }),
			};
			var weights = new float[AlphaResolution, AlphaResolution, 2];
			for (int y = 0; y < AlphaResolution; y++)
			{
				for (int x = 0; x < AlphaResolution; x++)
				{
					float rock = Mathf.Round(x / (float)(AlphaResolution - 1) * 255f) / 255f;
					weights[y, x, 0] = 1f - rock;
					weights[y, x, 1] = rock;
				}
			}
			data.SetAlphamaps(0, 0, weights);

			GameObject grass = Track(GameObject.CreatePrimitive(PrimitiveType.Quad));
			grass.name = "Grass detail";
			data.detailPrototypes = new[] { new DetailPrototype { prototype = grass, usePrototypeMesh = true, useInstancing = true, renderMode = DetailRenderMode.VertexLit } };
			data.SetDetailResolution(DetailResolution, 8);
			var detail = new int[DetailResolution, DetailResolution];
			detail[3, 4] = 7;
			detail[20, 11] = 3;
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

		private Material ArrayMaterial()
		{
			Shader shader = Shader.Find(TerrainArrayBinder.ShaderName);
			Assume.That(shader, Is.Not.Null, $"'{TerrainArrayBinder.ShaderName}' must be in the project");
			return Track(new Material(shader));
		}

		[Test]
		public void TheCopyKeepsEverythingButTheLayerTextures()
		{
			TerrainData original = Generated();
			var layerCopies = new Dictionary<TerrainLayer, TerrainLayer>();
			TerrainData copy = Track(ClientTerrainLayerStripper.ClientCopy(original, layerCopies));
			foreach (TerrainLayer l in layerCopies.Values)
			{
				Track(l);
			}

			Assert.That(copy, Is.Not.SameAs(original));
			Assert.That(copy.name, Does.EndWith(ClientTerrainLayerStripper.CopySuffix));
			Assert.That(copy.size, Is.EqualTo(original.size));
			float[,] h0 = original.GetHeights(0, 0, HeightResolution, HeightResolution);
			float[,] h1 = copy.GetHeights(0, 0, HeightResolution, HeightResolution);
			for (int y = 0; y < HeightResolution; y++)
			{
				for (int x = 0; x < HeightResolution; x++)
				{
					Assert.That(h1[y, x], Is.EqualTo(h0[y, x]).Within(1e-4f), $"height at {x},{y}");
				}
			}
			Assert.That(copy.IsHole(5, 7), Is.True);
			Assert.That(copy.IsHole(6, 7), Is.False);

			// Layers: same count, order and numbers; no textures.
			TerrainLayer[] before = original.terrainLayers, after = copy.terrainLayers;
			Assert.That(after.Length, Is.EqualTo(before.Length), "the alphamap channel is the layer index is the array slice");
			for (int i = 0; i < before.Length; i++)
			{
				Assert.That(after[i], Is.Not.SameAs(before[i]), $"layer {i} is a fresh object");
				Assert.That(after[i].name, Does.StartWith(before[i].name), $"layer {i} keeps its place");
				Assert.That(after[i].diffuseTexture, Is.Null, $"layer {i} albedo");
				Assert.That(after[i].normalMapTexture, Is.Null, $"layer {i} normal");
				Assert.That(after[i].maskMapTexture, Is.Null, $"layer {i} mask");
				Assert.That(after[i].tileSize, Is.EqualTo(before[i].tileSize));
				Assert.That(after[i].tileOffset, Is.EqualTo(before[i].tileOffset));
				Assert.That(after[i].normalScale, Is.EqualTo(before[i].normalScale));
				Assert.That(after[i].metallic, Is.EqualTo(before[i].metallic));
				Assert.That(after[i].smoothness, Is.EqualTo(before[i].smoothness));
				Assert.That(after[i].diffuseRemapMax, Is.EqualTo(before[i].diffuseRemapMax));
			}

			// Alphamaps: the array shader's control maps, byte for byte.
			Assert.That(copy.alphamapResolution, Is.EqualTo(original.alphamapResolution));
			Assert.That(copy.alphamapTextureCount, Is.EqualTo(original.alphamapTextureCount));
			float[,,] w0 = original.GetAlphamaps(0, 0, AlphaResolution, AlphaResolution);
			float[,,] w1 = copy.GetAlphamaps(0, 0, AlphaResolution, AlphaResolution);
			for (int y = 0; y < AlphaResolution; y++)
			{
				for (int x = 0; x < AlphaResolution; x++)
				{
					for (int c = 0; c < 2; c++)
					{
						Assert.That(w1[y, x, c], Is.EqualTo(w0[y, x, c]).Within(1e-6f), $"control weight {c} at {x},{y}");
					}
				}
			}
			Assert.That(copy.GetAlphamapTexture(0), Is.Not.SameAs(original.GetAlphamapTexture(0)), "the copy owns its own splat map");

			// Details and trees.
			Assert.That(copy.detailPrototypes.Length, Is.EqualTo(1));
			Assert.That(copy.detailPrototypes[0].prototype, Is.SameAs(original.detailPrototypes[0].prototype));
			Assert.That(copy.detailResolution, Is.EqualTo(DetailResolution));
			Assert.That(copy.detailResolutionPerPatch, Is.EqualTo(original.detailResolutionPerPatch));
			Assert.That(copy.detailScatterMode, Is.EqualTo(original.detailScatterMode));
			int[,] d = copy.GetDetailLayer(0, 0, DetailResolution, DetailResolution, 0);
			Assert.That(d[3, 4], Is.EqualTo(7));
			Assert.That(d[20, 11], Is.EqualTo(3));
			Assert.That(copy.treePrototypes.Length, Is.EqualTo(1));
			Assert.That(copy.treePrototypes[0].prefab, Is.SameAs(original.treePrototypes[0].prefab));
			Assert.That(copy.treeInstances.Length, Is.EqualTo(2));
			for (int i = 0; i < 2; i++)
			{
				Assert.That(Vector3.Distance(copy.treeInstances[i].position, original.treeInstances[i].position), Is.LessThan(1e-4f));
			}
		}

		[Test]
		public void ArrayTerrainIsCopied_OldMaterialTerrainIsNot_AndTheOriginalIsUntouched()
		{
			Material array = ArrayMaterial();
			TerrainData original = Generated();
			TerrainData other = Generated();
			TerrainLayer[] layers = original.terrainLayers;
			Texture2D albedo = layers[0].diffuseTexture;
			Texture2D firstAlphamap = original.GetAlphamapTexture(0);

			Scene scene = EditorSceneManager.NewPreviewScene();
			try
			{
				GameObject tile = Terrain.CreateTerrainGameObject(original);
				SceneManager.MoveGameObjectToScene(tile, scene);
				Terrain terrain = tile.GetComponent<Terrain>();
				terrain.materialTemplate = array;
				var colliderOnly = new GameObject("Collider only");
				SceneManager.MoveGameObjectToScene(colliderOnly, scene);
				colliderOnly.AddComponent<TerrainCollider>().terrainData = original;

				// A terrain on Unity's own material draws its layers' textures, and must keep them.
				GameObject plain = Terrain.CreateTerrainGameObject(other);
				SceneManager.MoveGameObjectToScene(plain, scene);

				int copied = ClientTerrainLayerStripper.Strip(scene);

				Assert.That(copied, Is.EqualTo(1));
				TerrainData copy = Track(terrain.terrainData);
				foreach (TerrainLayer l in copy.terrainLayers)
				{
					Track(l);
				}
				Assert.That(copy, Is.Not.SameAs(original));
				Assert.That(tile.GetComponent<TerrainCollider>().terrainData, Is.SameAs(copy), "its collider too, or the original ships anyway");
				Assert.That(colliderOnly.GetComponent<TerrainCollider>().terrainData, Is.SameAs(copy));
				Assert.That(plain.GetComponent<Terrain>().terrainData, Is.SameAs(other), "old-material terrain left alone");
				Assert.That(plain.GetComponent<TerrainCollider>().terrainData, Is.SameAs(other));

				Assert.That(original.terrainLayers, Is.EqualTo(layers), "the committed data keeps its layers");
				Assert.That(original.terrainLayers[0].diffuseTexture, Is.SameAs(albedo), "and its layers keep their textures");
				Assert.That(original.GetAlphamapTexture(0), Is.SameAs(firstAlphamap));
				Assert.That(original.GetDetailLayer(0, 0, DetailResolution, DetailResolution, 0)[3, 4], Is.EqualTo(7));
			}
			finally
			{
				EditorSceneManager.ClosePreviewScene(scene);
			}
		}

		[Test]
		public void LayersSharedByTilesShareOneCopy()
		{
			TerrainData a = Generated();
			var b = Track(new TerrainData { name = "Second" });
			b.alphamapResolution = AlphaResolution;
			b.terrainLayers = a.terrainLayers;
			var copies = new Dictionary<TerrainLayer, TerrainLayer>();
			TerrainData ca = Track(ClientTerrainLayerStripper.ClientCopy(a, copies));
			TerrainData cb = Track(ClientTerrainLayerStripper.ClientCopy(b, copies));
			foreach (TerrainLayer l in copies.Values)
			{
				Track(l);
			}
			Assert.That(cb.terrainLayers[0], Is.SameAs(ca.terrainLayers[0]));
			Assert.That(cb.terrainLayers[1], Is.SameAs(ca.terrainLayers[1]));
		}

		[Test]
		public void PlayModeAndServerBuildsAreLeftAlone()
		{
			Material array = ArrayMaterial();
			TerrainData original = Generated();
			Scene scene = EditorSceneManager.NewPreviewScene();
			try
			{
				GameObject tile = Terrain.CreateTerrainGameObject(original);
				SceneManager.MoveGameObjectToScene(tile, scene);
				tile.GetComponent<Terrain>().materialTemplate = array;
				// No report: a scene processed for play mode.
				new ClientTerrainLayerStripper().OnProcessScene(scene, null);
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
			Assert.That(new ClientTerrainLayerStripper().callbackOrder, Is.GreaterThan(new ClientOnlySceneStripper().callbackOrder));
		}
	}
}
