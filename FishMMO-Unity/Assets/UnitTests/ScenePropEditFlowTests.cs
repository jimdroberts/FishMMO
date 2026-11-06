using System.Collections.Generic;
using System.Linq;
using FishMMO.Shared;
using FishMMO.Shared.WorldDesign;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The hand-edit flow end to end in a throwaway scene: a generated prop moved and another removed with the Scene view
	/// tool's own commit (ScenePropEditTool), the set and its collision changed at once, a fresh bake of the same generation
	/// coming out edited, and Forget putting each back where it was generated.
	/// </summary>
	public class ScenePropEditFlowTests
	{
		private const string Folder = "Assets/__PropEditTest Terrain";
		private const string Source = "EditTest";
		private string bakeFolderWas;

		[SetUp]
		public void SetUp()
		{
			bakeFolderWas = ScenePropBaker.BakeFolder;
			if (!AssetDatabase.IsValidFolder(Folder))
			{
				AssetDatabase.CreateFolder("Assets", "__PropEditTest Terrain");
			}
			ScenePropBaker.BakeFolder = Folder;
			EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
		}

		[TearDown]
		public void TearDown()
		{
			ScenePropBaker.BakeFolder = bakeFolderWas;
			EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
			AssetDatabase.DeleteAsset(Folder);
		}

		private static GameObject Prefab()
		{
			GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
			box.transform.localScale = Vector3.one * 2f;
			// A single mesh collider on its root is used as it is (ScenePropBaker.PrototypeCollision): no merged mesh is written.
			// A readable mesh asset of the test's own, so nothing is written outside its folder.
			Mesh mesh = Object.Instantiate(box.GetComponent<MeshFilter>().sharedMesh);
			AssetDatabase.CreateAsset(mesh, $"{Folder}/Box Mesh.asset");
			box.GetComponent<MeshFilter>().sharedMesh = mesh;
			Object.DestroyImmediate(box.GetComponent<BoxCollider>());
			box.AddComponent<MeshCollider>().sharedMesh = mesh;
			GameObject prefab = PrefabUtility.SaveAsPrefabAsset(box, $"{Folder}/Box.prefab");
			Object.DestroyImmediate(box);
			return prefab;
		}

		private static ScenePropSet SetOf(Scene scene)
		{
			foreach (GameObject root in scene.GetRootGameObjects())
			{
				if (root.TryGetComponent(out SceneProps props))
				{
					return props.Sets.FirstOrDefault(s => s != null && s.Source == Source);
				}
			}
			return null;
		}

		private static ScenePropCollisionSet CollisionOf(Scene scene)
		{
			foreach (GameObject root in scene.GetRootGameObjects())
			{
				if (root.TryGetComponent(out ScenePropColliders colliders))
				{
					return colliders.Sets.FirstOrDefault(s => s != null && s.Source == Source);
				}
			}
			return null;
		}

		private static int IndexAt(ScenePropSet set, Vector3 at)
		{
			for (int i = 0; i < set.Props.Length; i++)
			{
				if ((set.Props[i].Position - at).sqrMagnitude < 1e-6f)
				{
					return i;
				}
			}
			return -1;
		}

		private static void Bake(Scene scene, GameObject prefab)
		{
			var prototypes = new List<ScenePropSet.Prototype> { new ScenePropSet.Prototype { Prefab = prefab, Layer = 0 } };
			var props = new List<ScenePropSet.Prop>
			{
				new ScenePropSet.Prop { Prototype = 0, Position = new Vector3(0f, 0f, 0f), Rotation = Quaternion.identity, Scale = Vector3.one },
				new ScenePropSet.Prop { Prototype = 0, Position = new Vector3(10f, 0f, 0f), Rotation = Quaternion.identity, Scale = Vector3.one },
			};
			ScenePropBaker.Write(scene, Source, prototypes, props);
		}

		[Test]
		public void AMovedAndARemovedProp_AreWrittenAtOnce_KeptByTheNextBake_AndForgotten()
		{
			Scene scene = SceneManager.GetActiveScene();
			GameObject prefab = Prefab();
			Bake(scene, prefab);
			ScenePropSet set = SetOf(scene);
			LogAssert.IsTrue(set != null && set.Props.Length == 2, "the generated set");

			var moved = new Vector3(2f, 0f, 1f);
			Quaternion turned = Quaternion.Euler(0f, 30f, 0f);
			ScenePropEditTool.MoveProp(set, IndexAt(set, Vector3.zero), moved, turned, Vector3.one);
			ScenePropEditTool.RemoveProp(set, IndexAt(set, new Vector3(10f, 0f, 0f)));
			LogAssert.IsTrue(set.Props.Length == 1 && IndexAt(set, moved) >= 0, "the set holds the moved prop only");
			ScenePropCollisionSet collision = CollisionOf(scene);
			LogAssert.IsTrue(collision != null && collision.Instances.Length == 1 && (collision.Instances[0].Position - moved).sqrMagnitude < 1e-6f,
				"its collision moved with it and the removed one's went");
			ScenePropEdits edits = ScenePropEdits.For(scene, false);
			LogAssert.IsTrue(edits != null && edits.Edits.Count == 2, "two edits kept beside the scene's terrain");

			// The same generation baked again comes out edited.
			Bake(scene, prefab);
			set = SetOf(scene);
			LogAssert.IsTrue(set.Props.Length == 1 && IndexAt(set, moved) >= 0 && Quaternion.Angle(set.Props[0].Rotation, turned) < 0.01f,
				"a fresh bake lays both edits again");
			LogAssert.IsTrue(edits.Edits.All(e => e.Matched), "and finds both");

			// Moved again from where it was put: still the one edit.
			var further = new Vector3(3f, 0f, 1f);
			ScenePropEditTool.MoveProp(set, IndexAt(set, moved), further, turned, Vector3.one);
			LogAssert.IsTrue(edits.Edits.Count == 2 && edits.Edits.Count(e => !e.Removed && (e.Position - further).sqrMagnitude < 1e-6f && e.Origin == Vector3.zero) == 1,
				"a second move updates the edit, its origin kept");

			// Forget both: each back where it was generated.
			for (int i = edits.Edits.Count - 1; i >= 0; i--)
			{
				ScenePropEditTool.Forget(edits, i);
			}
			set = SetOf(scene);
			LogAssert.IsTrue(edits.Edits.Count == 0 && set.Props.Length == 2 && IndexAt(set, Vector3.zero) >= 0 && IndexAt(set, new Vector3(10f, 0f, 0f)) >= 0,
				"forgotten, both stand where they were generated");
		}
	}
}
