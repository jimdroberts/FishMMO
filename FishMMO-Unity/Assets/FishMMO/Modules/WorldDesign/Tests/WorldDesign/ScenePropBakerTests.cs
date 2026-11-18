using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using FishMMO.Shared;
using FishMMO.Shared.WorldDesign;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// Baked props: a prefab's colliders become one shared collision mesh, only props big enough to block get
	/// collision, a source's sets replace its last, and the terrain's trees move into props. All on an unsaved scene
	/// with in-memory prefabs, so nothing is written to the project.
	/// </summary>
	[TestFixture]
	public class ScenePropBakerTests
	{
		private Scene scene;
		private readonly List<Object> made = new List<Object>();

		[SetUp]
		public void SetUp()
		{
			// Single, like the repo's other scene fixtures: the runner's own untitled scene is dirty, so an additive one is refused.
			scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
		}

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
			if (scene.IsValid() && SceneManager.sceneCount > 1)
			{
				EditorSceneManager.CloseScene(scene, true);
			}
		}

		/// <summary>A prop-like prefab (in memory): a capsule trunk on the root, a box on a child half a metre up, and a trigger.</summary>
		private GameObject TreeLike(int layer = 5)
		{
			var go = new GameObject("Tree Like") { layer = layer };
			made.Add(go);
			var capsule = go.AddComponent<CapsuleCollider>();
			capsule.radius = 0.3f;
			capsule.height = 4f;
			capsule.center = new Vector3(0f, 2f, 0f);
			var child = new GameObject("Branch") { layer = layer };
			child.transform.SetParent(go.transform, false);
			child.transform.localPosition = new Vector3(0f, 0.5f, 0f);
			child.AddComponent<BoxCollider>().size = Vector3.one;
			var trigger = child.AddComponent<SphereCollider>();
			trigger.isTrigger = true;
			trigger.radius = 10f;
			return go;
		}

		/// <summary>A small prefab: a 0.4 m box.</summary>
		private GameObject Pebble()
		{
			var go = new GameObject("Pebble");
			made.Add(go);
			go.AddComponent<BoxCollider>().size = Vector3.one * 0.4f;
			return go;
		}

		private static ScenePropSet.Prop At(int prototype, Vector3 position, float scale = 1f) =>
			new ScenePropSet.Prop { Prototype = prototype, Position = position, Rotation = Quaternion.identity, Scale = Vector3.one * scale };

		[Test]
		public void APrefabsCollidersBecomeOneSharedCollisionMesh()
		{
			Mesh mesh = ScenePropBaker.PrototypeCollision(TreeLike(), out int layer);
			made.Add(mesh);
			Assert.That(mesh, Is.Not.Null);
			Assert.That(layer, Is.EqualTo(5), "its first collider's layer");
			// The capsule from the foot to 4 m; the box a metre wide, centred half a metre up; the trigger left out.
			Assert.That(mesh.bounds.min.y, Is.EqualTo(0f).Within(0.05f));
			Assert.That(mesh.bounds.max.y, Is.EqualTo(4f).Within(0.05f));
			Assert.That(mesh.bounds.size.x, Is.EqualTo(1f).Within(0.05f), "the box is wider than the trunk; the 10 m trigger is not there");
			var bare = new GameObject("Bare");
			made.Add(bare);
			Assert.That(ScenePropBaker.PrototypeCollision(bare, out _), Is.Null, "a prefab with no collider has no collision");
		}

		[Test]
		public void OnlyPropsBigEnoughToBlockGetCollision()
		{
			var bare = new GameObject("Bare");
			made.Add(bare);
			var prototypes = new List<ScenePropSet.Prototype>
			{
				new ScenePropSet.Prototype { Prefab = TreeLike(), Layer = -1 },
				new ScenePropSet.Prototype { Prefab = Pebble(), Layer = -1 },
				new ScenePropSet.Prototype { Prefab = bare, Layer = -1 },
			};
			var props = new List<ScenePropSet.Prop>
			{
				At(0, new Vector3(10f, 0f, 10f)),
				At(1, new Vector3(20f, 0f, 20f)),          // 0.4 m: walked through
				At(1, new Vector3(30f, 0f, 30f), 3f),      // 1.2 m: blocks
				At(2, new Vector3(40f, 0f, 40f)),          // no collider at all
			};
			int collidable = ScenePropBaker.Write(scene, "Test", prototypes, props, 9);
			Assert.That(collidable, Is.EqualTo(2));
			ScenePropColliders colliders = Object.FindAnyObjectByType<ScenePropColliders>();
			Assert.That(colliders, Is.Not.Null);
			Assert.That(colliders.GetComponent<ClientOnlyObject>(), Is.Null, "collision stays in server builds");
			Assert.That(colliders.transform.parent, Is.Null, "at the scene's root");
			ScenePropCollisionSet set = colliders.Sets[0];
			made.Add(set);
			made.Add(Object.FindAnyObjectByType<SceneProps>().Sets[0]);
			Assert.That(set.Instances.Length, Is.EqualTo(2));
			Assert.That(set.Prototypes[0].Layer, Is.EqualTo(9), "on the layer asked for");
			Assert.That(set.Prototypes[2].Mesh, Is.Null);
			Assert.That(System.Array.Exists(set.Instances, i => i.Position.x == 30f && i.Scale.x == 3f), Is.True);
			Assert.That(System.Array.Exists(set.Instances, i => i.Position.x == 20f), Is.False);
		}

		[Test]
		public void ASourcesSetsReplaceItsLastAndSortByChunk()
		{
			var prototypes = new List<ScenePropSet.Prototype> { new ScenePropSet.Prototype { Prefab = TreeLike(), Layer = -1 } };
			var props = new List<ScenePropSet.Prop> { At(0, new Vector3(500f, 0f, 500f)), At(0, new Vector3(1f, 0f, 1f)), At(0, new Vector3(500f, 0f, 501f)) };
			ScenePropBaker.Write(scene, "Test", prototypes, props);
			SceneProps owner = Object.FindAnyObjectByType<SceneProps>();
			Assert.That(owner, Is.Not.Null);
			Assert.That(owner.GetComponent<ClientOnlyObject>(), Is.Not.Null, "props are drawn by the client only: server builds drop them");
			ScenePropSet set = owner.Sets[0];
			made.Add(set);
			int a = System.Array.FindIndex(set.Props, p => p.Position.z == 500f), b = System.Array.FindIndex(set.Props, p => p.Position.z == 501f);
			Assert.That(Mathf.Abs(a - b), Is.EqualTo(1), "the two props in the same chunk lie together");
			ScenePropBaker.Write(scene, "Test", prototypes, props.GetRange(0, 1));
			Assert.That(owner.Sets.Count, Is.EqualTo(1), "the source's last set is replaced");
			made.Add(owner.Sets[0]);
			Assert.That(Object.FindAnyObjectByType<ScenePropColliders>().Sets.Count, Is.EqualTo(1));
			ScenePropBaker.Clear(scene, "Test");
			Assert.That(owner.Sets, Is.Empty);
			Assert.That(Object.FindAnyObjectByType<ScenePropColliders>().Sets, Is.Empty);
		}

		[Test]
		public void TheTerrainsTreesMoveIntoPropsAndOffTheTerrain()
		{
			GameObject prefab = TreeLike();
			var mesh = new Mesh();
			mesh.SetVertices(new List<Vector3> { Vector3.zero, Vector3.up, Vector3.right });
			mesh.SetTriangles(new[] { 0, 1, 2 }, 0);
			made.Add(mesh);
			prefab.AddComponent<MeshFilter>().sharedMesh = mesh;
			prefab.AddComponent<MeshRenderer>();
			var data = new TerrainData { heightmapResolution = 33, size = new Vector3(100f, 50f, 100f) };
			made.Add(data);
			data.treePrototypes = new[] { new TreePrototype { prefab = prefab } };
			data.RefreshPrototypes();
			data.SetTreeInstances(new[]
			{
				new TreeInstance { prototypeIndex = 0, position = new Vector3(0.5f, 0.2f, 0.25f), widthScale = 1.5f, heightScale = 2f, rotation = Mathf.PI * 0.5f, color = Color.white, lightmapColor = Color.white },
			}, false);
			GameObject terrainObject = Terrain.CreateTerrainGameObject(data);
			SceneManager.MoveGameObjectToScene(terrainObject, scene);
			terrainObject.transform.position = new Vector3(1000f, 5f, 2000f);
			terrainObject.layer = 6;
			Terrain terrain = terrainObject.GetComponent<Terrain>();
			terrain.preserveTreePrototypeLayers = false;

			int baked = ScenePropBaker.BakeTerrainTrees(scene, new[] { terrain }, null);
			Assert.That(baked, Is.EqualTo(1));
			Assert.That(data.treeInstanceCount, Is.EqualTo(0), "the terrain neither draws nor collides with it now");
			Assert.That(data.treePrototypes, Is.Empty, "nor lists its prefab, which would carry its art into the server build");
			ScenePropSet set = Object.FindAnyObjectByType<SceneProps>().Sets[0];
			made.Add(set);
			ScenePropSet.Prop prop = set.Props[0];
			Assert.That((prop.Position - new Vector3(1050f, 15f, 2025f)).magnitude, Is.LessThan(0.01f), "where the terrain stood it");
			Assert.That(prop.Scale, Is.EqualTo(new Vector3(1.5f, 2f, 1.5f)));
			Assert.That(Quaternion.Angle(prop.Rotation, Quaternion.Euler(0f, 90f, 0f)), Is.LessThan(0.1f));
			Assert.That(set.Prototypes[0].Prefab, Is.SameAs(prefab));
			Assert.That(set.Prototypes[0].Layer, Is.EqualTo(6), "drawn on the terrain's layer, as the terrain drew it");
			ScenePropCollisionSet collision = Object.FindAnyObjectByType<ScenePropColliders>().Sets[0];
			made.Add(collision);
			Assert.That(collision.Instances.Length, Is.EqualTo(1), "a 4 m tree blocks");
			Assert.That(collision.Prototypes[0].Layer, Is.EqualTo(6), "on the terrain's layer, as the terrain's tree colliders were");
		}
	}
}
