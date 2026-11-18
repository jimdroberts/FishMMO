using System.Collections.Generic;
using System.Reflection;
using FishMMO.Client;
using NUnit.Framework;
using UnityEngine;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The crevice field's shapes and looks belong to the renderers that registered them, and are freed when the last of
	/// those goes. Keyed by mesh and material object, a world's are new every load, so before this the registries grew by a
	/// full set per login until the SDF atlas passed the 3D texture limit (3008 of 2048 slices on the third) and every frame
	/// failed, taking the fillet compute pipeline with it.
	/// </summary>
	public class RockContactFieldOwnershipTests
	{
		private readonly List<Object> made = new List<Object>();

		[SetUp]
		public void SetUp() => Reset();

		[TearDown]
		public void TearDown()
		{
			Reset();
			foreach (Object o in made)
			{
				if (o != null)
				{
					Object.DestroyImmediate(o);
				}
			}
			made.Clear();
		}

		private static void Reset()
		{
			typeof(RockContactField).GetMethod("ResetStatics", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
		}

		/// <summary>A renderer to own slots: only its identity is used.</summary>
		private static TerrainGpuRenderer Owner()
		{
			return (TerrainGpuRenderer)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(TerrainGpuRenderer));
		}

		private Mesh NewMesh()
		{
			var mesh = new Mesh
			{
				vertices = new[] { Vector3.zero, Vector3.right, Vector3.up },
				triangles = new[] { 0, 1, 2 },
			};
			made.Add(mesh);
			return mesh;
		}

		private Material NewMaterial()
		{
			var material = new Material(Shader.Find("Hidden/InternalErrorShader"));
			made.Add(material);
			return material;
		}

		private static int Shape(TerrainGpuRenderer owner, Mesh mesh)
		{
			var levels = new[] { new[] { new TerrainGpuRenderer.PartSource { Mesh = mesh } } };
			return RockContactField.RegisterShape(owner, levels, new Bounds(Vector3.zero, Vector3.one));
		}

		[Test]
		public void AShapeOnlyTheLeavingRendererUsedIsFreed_AndASharedOneKept()
		{
			TerrainGpuRenderer a = Owner(), b = Owner();
			Mesh own = NewMesh(), other = NewMesh(), shared = NewMesh();
			int ownIndex = Shape(a, own);
			Shape(a, shared);
			Shape(b, other);
			LogAssert.AreEqual(Shape(a, shared), Shape(b, shared), "one shape per mesh, whoever registers it");
			RockContactField.AddRenderer(a);
			RockContactField.AddRenderer(b);

			RockContactField.RemoveRenderer(a);

			LogAssert.AreEqual(2, RockContactField.LiveShapes, "the shared shape and the other renderer's stay");
			int reused = Shape(Owner(), NewMesh());
			LogAssert.AreEqual(ownIndex, reused, "a new shape takes the freed slot");
			LogAssert.AreEqual(3, RockContactField.ShapeSlots, "and the list does not grow");
		}

		[Test]
		public void LoadingTheSameWorldAgainAndAgain_DoesNotGrowTheShapes()
		{
			// Each load brings new mesh objects for the same rocks; the next world registers before the last is disposed
			// (a teleport), and a renderer that stays throughout (another scene) keeps the field alive.
			TerrainGpuRenderer stays = Owner();
			Shape(stays, NewMesh());
			RockContactField.AddRenderer(stays);
			const int RocksPerWorld = 20;
			TerrainGpuRenderer previous = null;
			for (int load = 0; load < 6; load++)
			{
				TerrainGpuRenderer world = Owner();
				for (int i = 0; i < RocksPerWorld; i++)
				{
					Shape(world, NewMesh());
				}
				RockContactField.AddRenderer(world);
				if (previous != null)
				{
					RockContactField.RemoveRenderer(previous);
				}
				previous = world;
			}
			LogAssert.AreEqual(1 + RocksPerWorld, RockContactField.LiveShapes, "only the worlds still loaded hold shapes");
			LogAssert.IsTrue(RockContactField.ShapeSlots <= 1 + 2 * RocksPerWorld,
				$"slots are bounded by two worlds overlapping, not by how many loads there were ({RockContactField.ShapeSlots})");
		}

		[Test]
		public void ALookIsFreedWithItsLastRenderer_AndItsSlotReused()
		{
			TerrainGpuRenderer a = Owner(), b = Owner();
			Material mine = NewMaterial(), theirs = NewMaterial();
			Shape(a, NewMesh());
			Shape(b, NewMesh());
			int mineIndex = RockContactField.RegisterLook(a, mine);
			RockContactField.RegisterLook(b, theirs);
			RockContactField.AddRenderer(a);
			RockContactField.AddRenderer(b);

			RockContactField.RemoveRenderer(a);

			LogAssert.AreEqual(1, RockContactField.LiveLooks, "the other renderer's look stays");
			LogAssert.IsNull(RockContactField.Looks[mineIndex], "the freed look forgets its material");
			Material next = NewMaterial();
			LogAssert.AreEqual(mineIndex, RockContactField.RegisterLook(Owner(), next), "a new look takes the freed slot");
			LogAssert.AreEqual(next, RockContactField.Looks[mineIndex]);
		}

		[Test]
		public void TheLastRendererGoing_EmptiesEverything()
		{
			TerrainGpuRenderer a = Owner();
			Shape(a, NewMesh());
			RockContactField.RegisterLook(a, NewMaterial());
			RockContactField.AddRenderer(a);

			RockContactField.RemoveRenderer(a);

			LogAssert.AreEqual(0, RockContactField.ShapeSlots, "no shape slots left");
			LogAssert.AreEqual(0, RockContactField.Looks.Count, "no look slots left");
		}
	}
}
