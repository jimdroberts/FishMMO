using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using FishMMO.Shared;
using FishMMO.Shared.WorldDesign;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The cliff placer's scene objects: a rock carries a mesh collider on the terrain's layer and a
	/// client-only level-of-detail group, and re-placing finds its own root by the <see cref="GeneratedCliffs"/>
	/// marker, lowers the cones it raised and replaces it, never touching another object of the same name.
	/// </summary>
	[TestFixture]
	public class CliffPlacerSceneTests
	{
		private Scene scene;
		private readonly List<Object> created = new List<Object>();

		[SetUp]
		public void OpenScene()
		{
			// Single, like the repo's other scene fixtures: the runner's own untitled scene is dirty, so an additive one is refused.
			scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
		}

		[TearDown]
		public void CloseScene()
		{
			foreach (Object o in created)
			{
				if (o != null)
				{
					Object.DestroyImmediate(o);
				}
			}
			created.Clear();
			if (scene.IsValid() && SceneManager.sceneCount > 1)
			{
				EditorSceneManager.CloseScene(scene, true);
			}
		}

		private GameObject Root(string name, bool marked)
		{
			var go = new GameObject(name);
			if (marked)
			{
				go.AddComponent<GeneratedCliffs>();
			}
			SceneManager.MoveGameObjectToScene(go, scene);
			return go;
		}

		[Test]
		public void Clear_RemovesOnlyMarkedCliffRoots()
		{
			Root(CliffPlacer.RootName, true);
			Root(CliffPlacer.RootName, true);
			Root(CliffPlacer.RootName, false);
			Root("Something else", true);

			Assert.That(CliffPlacer.Clear(scene), Is.EqualTo(2));
			var names = new List<string>();
			foreach (GameObject go in scene.GetRootGameObjects())
			{
				names.Add(go.name + (go.GetComponent<GeneratedCliffs>() != null ? "+" : "-"));
			}
			CollectionAssert.AreEquivalent(new[] { CliffPlacer.RootName + "-", "Something else+" }, names);
			Assert.That(CliffPlacer.Clear(scene), Is.EqualTo(0), "clearing twice finds nothing the second time");
		}

		[Test]
		public void Place_WithNothingToPlace_LeavesNoRoot_AndClearsTheOldOne()
		{
			Root(CliffPlacer.RootName, true);
			CliffPlacerReport report = CliffPlacer.Place(scene, new List<Terrain>(), null, null, 1u, null);
			Assert.That(report.Pieces, Is.EqualTo(0));
			Assert.That(report.Notes, Is.Not.Empty);
			foreach (GameObject go in scene.GetRootGameObjects())
			{
				Assert.That(go.GetComponent<GeneratedCliffs>(), Is.Null, "the previous cliffs were replaced, not kept");
			}
		}

		[Test]
		public void BuildRock_KeepsTheColliderForTheServer_AndStripsOnlyTheVisual()
		{
			var piece = new CliffPiece("Granite", CliffRole.Base, 0, 1, 0);
			var meshes = new Mesh[CliffRocks.LodHeights.Length];
			for (int lod = 0; lod < meshes.Length; lod++)
			{
				meshes[lod] = CliffRocks.Build(in piece, lod, 1234).ToMesh("test" + lod);
				created.Add(meshes[lod]);
			}
			Mesh collision = meshes[CliffRocks.CollisionLod];
			GameObject root = Root(CliffPlacer.RootName, true);
			var rock = new PlacedCliffRock
			{
				Piece = piece,
				Position = new Vector3(1f, 2f, 3f),
				Rotation = CliffRockPlacement.Rot(new Vector3(1f, 0.2f, 0.4f), 30f),
				Scale = new Vector3(1.1f, 0.9f, 1.05f),
			};
			GameObject go = CliffPlacer.BuildRock(root.transform, in rock, meshes, collision, null, CliffPlacer.ColliderLayer, CliffRocks.LodHeights);

			// The collider: on the rock itself, on Ground like the terrain, never client-only.
			var collider = go.GetComponent<MeshCollider>();
			Assert.That(collider, Is.Not.Null, "the server needs the rock");
			Assert.That(collider.sharedMesh, Is.SameAs(collision));
			Assert.That(collider.convex, Is.False);
			Assert.That(go.layer, Is.EqualTo(LayerMask.NameToLayer("Ground")), "colliders go on Ground, which KCC stands on");
			Assert.That(go.GetComponentInParent<ClientOnlyObject>(true), Is.Null, "the collider must survive the server build");
			Assert.That(root.GetComponent<ClientOnlyObject>(), Is.Null, "the root must survive the server build");
			Assert.That(go.GetComponent<Renderer>(), Is.Null, "render detail lives on the visual child");

			// The visual: one child under a client-only marker, with the LOD group and every renderer.
			Transform visual = go.transform.Find(CliffPlacer.VisualName);
			Assert.That(visual, Is.Not.Null);
			Assert.That(visual.GetComponent<ClientOnlyObject>(), Is.Not.Null, "server builds strip the visual");
			LOD[] lods = visual.GetComponent<LODGroup>().GetLODs();
			Assert.That(lods.Length, Is.EqualTo(CliffRocks.LodHeights.Length));
			foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
			{
				Assert.That(r.GetComponentInParent<ClientOnlyObject>(true), Is.Not.Null, r.name + " is under the client-only visual");
			}
			Assert.That(go.GetComponentsInChildren<Collider>(true).Length, Is.EqualTo(1), "one collider per rock");
			// Never batching-static: the client GPU-instances the rocks (CliffRockInstancing) from their own meshes.
			Assert.That(GameObjectUtility.GetStaticEditorFlags(visual.gameObject) & StaticEditorFlags.BatchingStatic, Is.EqualTo((StaticEditorFlags)0));

			// The pose: the planner's rotation and per-axis scale, applied as a Transform applies them.
			Assert.That(go.transform.position, Is.EqualTo(rock.Position));
			Assert.That(go.transform.localScale, Is.EqualTo(rock.Scale));
			Vector3 local = new Vector3(2f, 3f, -1f);
			Vector3 expected = rock.World(local), actual = go.transform.TransformPoint(local);
			Assert.That((expected - actual).magnitude, Is.LessThan(1e-3f), "the object's transform is the planner's pose");
		}

		[Test]
		public void Clear_LowersTheConesItsPlacementRaised()
		{
			var data = new TerrainData { heightmapResolution = 33, size = new Vector3(64f, 100f, 64f) };
			created.Add(data);
			float[,] heights = data.GetHeights(0, 0, 33, 33);
			heights[10, 12] = 0.2f;
			data.SetHeights(0, 0, heights);
			// A placement raised one sample by 0.05.
			heights[10, 12] = 0.25f;
			data.SetHeights(0, 0, heights);
			GameObject root = Root(CliffPlacer.RootName, true);
			var record = new GameObject(GeneratedCliffTalus.ObjectName) { tag = "EditorOnly" };
			record.transform.SetParent(root.transform, false);
			var edit = new GeneratedCliffTalus.TerrainEdit { Data = data, Resolution = 33 };
			edit.Samples.Add(10 * 33 + 12);
			edit.Deltas.Add(0.05f);
			record.AddComponent<GeneratedCliffTalus>().Edits.Add(edit);

			Assert.That(CliffPlacer.Clear(scene, out bool lowered), Is.EqualTo(1));
			Assert.That(lowered, Is.True);
			Assert.That(data.GetHeights(0, 0, 33, 33)[10, 12], Is.EqualTo(0.2f).Within(1e-4f), "the cone is taken back out, so re-placing never piles cone on cone");
		}

		[Test]
		public void EveryCliffRock_IsInThePayload_AndWearsAGeneratedMaterial()
		{
			var paths = new HashSet<string>(ProceduralArtCatalogue.RockPayloadPaths());
			var materials = new HashSet<string>(ProceduralArtCatalogue.RockMaterialNames());
			foreach (RockMaterialSpec legacy in ProceduralArtCatalogue.RockMaterials)
			{
				materials.Add(ProceduralArtCatalogue.RockMaterial(legacy.Name));
			}
			foreach ((CliffPiece piece, int lod) in CliffRocks.AllMeshes())
			{
				Assert.That(paths, Does.Contain(CliffPlacer.MeshPath(in piece, lod)));
			}
			foreach (string type in CliffRocks.Types())
			{
				Assert.That(materials, Does.Contain(CliffRocks.MaterialName(type)), type);
			}
		}

		[Test]
		public void SceneGenerator_HandsThePlacerTheClimate_AndKeepsTheLocalMaterialSeam()
		{
			string source = System.IO.File.ReadAllText("Assets/Scripts/Shared/Implementation/Tools/Extensions/Unity/Editor/World/SceneGenerator.cs").Replace("\r\n", "\n");
			Assert.That(source, Does.Contain("scope.CliffOptions() ?? new CliffPlacerOptions()"), "LOCAL scenes still override the rocks' materials");
			Assert.That(source, Does.Contain("cliffOptions.ClimateAt ="), "granite roundness follows the scene's climate");
			Assert.That(source, Does.Contain("CliffPlacer.Place(scene, tiles, palette, field, options.Seed, options.NormalizedHeight, cliffOptions)"));
		}
	}
}
