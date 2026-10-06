using System.Collections;
using System.Collections.Generic;
using FishMMO.Shared;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace FishMMO.UnitTests.PlayMode
{
	/// <summary>
	/// The prop collider streamer: colliders round a focus appear at once, none far from it, and moving the focus
	/// empties the cells it left (back to the pool) and fills the ones it reached.
	/// </summary>
	public class PropColliderStreamerPlayModeTests
	{
		private GameObject owner, focus;
		private ScenePropCollisionSet set;
		private Mesh cube;

		[SetUp]
		public void SetUp()
		{
			// A unit cube for the collision mesh, and one prop every 32 m along x out to 1 km.
			GameObject primitive = GameObject.CreatePrimitive(PrimitiveType.Cube);
			cube = primitive.GetComponent<MeshFilter>().sharedMesh;
			Object.DestroyImmediate(primitive);
			set = ScriptableObject.CreateInstance<ScenePropCollisionSet>();
			set.Source = "Test";
			set.Prototypes = new[] { new ScenePropCollisionSet.Prototype { Mesh = cube, Layer = 0 } };
			var instances = new List<ScenePropCollisionSet.Instance>();
			for (int i = 0; i < 32; i++)
			{
				instances.Add(new ScenePropCollisionSet.Instance { Prototype = 0, Position = new Vector3(i * 32f + 16f, 0f, 16f), Rotation = Quaternion.identity, Scale = Vector3.one * 2f });
			}
			set.Instances = instances.ToArray();
			owner = new GameObject("Prop Colliders");
			owner.SetActive(false);
			owner.AddComponent<ScenePropColliders>().Sets.Add(set);
			owner.SetActive(true);
			focus = new GameObject("Focus");
		}

		[TearDown]
		public void TearDown()
		{
			PropColliderStreamer.RemoveFocus(focus.transform);
			Object.DestroyImmediate(owner);
			Object.DestroyImmediate(focus);
			Object.DestroyImmediate(set);
		}

		private static int CollidersNear(Vector3 at, float radius)
		{
			return Physics.OverlapSphere(at, radius).Length;
		}

		[UnityTest]
		public IEnumerator CollidersFollowTheFocus()
		{
			focus.transform.position = new Vector3(16f, 0f, 16f);
			PropColliderStreamer.AddFocus(focus.transform, 100f);
			// The cells right round it are filled at once, before any frame.
			Assert.That(CollidersNear(new Vector3(16f, 0f, 16f), 2f), Is.EqualTo(1), "the prop at the focus collides immediately");
			yield return new WaitForSeconds(PropColliderStreamer.TickSeconds * 3f);
			Assert.That(CollidersNear(new Vector3(80f, 0f, 16f), 2f), Is.EqualTo(1), "props within the radius collide");
			Assert.That(CollidersNear(new Vector3(496f, 0f, 16f), 2f), Is.EqualTo(0), "props far away do not exist in physics");
			int placed = PropColliderStreamer.ActiveCount;
			Assert.That(placed, Is.InRange(3, 6), "only the cells within 100 m");

			// Move half a kilometre: the old cells empty, the new ones fill.
			focus.transform.position = new Vector3(496f, 0f, 16f);
			yield return new WaitForSeconds(PropColliderStreamer.TickSeconds * 3f);
			Assert.That(CollidersNear(new Vector3(496f, 0f, 16f), 2f), Is.EqualTo(1));
			Assert.That(CollidersNear(new Vector3(16f, 0f, 16f), 2f), Is.EqualTo(0), "left behind, back in the pool");
			Assert.That(PropColliderStreamer.ActiveCount, Is.InRange(5, 9));
		}
	}
}
