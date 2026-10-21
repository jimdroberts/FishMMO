using System.Collections.Generic;
using System.IO;
using FishMMO.Shared;
using FishMMO.Shared.WorldDesign;
using NUnit.Framework;
using UnityEngine;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// Trees and bushes kept off building floors (2026-10-19, PointOfInterestTreeClearance): the floors are read from the same
	/// Floor yards the grass is kept out by, in the path field's own shape, and a plant stands off them by its crown's reach.
	/// </summary>
	[TestFixture]
	public class PointOfInterestTreeClearanceTests
	{
		private static ScenePath Yard(Vector3 a, Vector3 b, float half, ScenePathPointFlags shape)
		{
			byte flags = (byte)(ScenePathPointFlags.InSite | shape);
			return new ScenePath
			{
				Class = ScenePathClass.Yard,
				Points = new[] { a, b },
				HalfWidth = new[] { half, half },
				Wear = new[] { 1f, 1f },
				Surface = new[] { (byte)ScenePathSurface.Earth, (byte)ScenePathSurface.Earth },
				Flags = new[] { flags, flags },
			};
		}

		[Test]
		public void FloorsAreTheFloorYardsOnly()
		{
			var paths = new List<ScenePath>
			{
				// A house's apron, its floor, and a round tower's floor.
				Yard(new Vector3(-5f, 0f, 0f), new Vector3(5f, 0f, 0f), 4f, ScenePathPointFlags.Squared),
				Yard(new Vector3(-4f, 0f, 0f), new Vector3(4f, 0f, 0f), 3f, ScenePathPointFlags.Squared | ScenePathPointFlags.Floor),
				Yard(new Vector3(20f, 0f, 20f), new Vector3(20f, 0f, 20f), 2f, ScenePathPointFlags.Floor),
			};
			List<PointOfInterestTreeClearance.Floor> floors = PointOfInterestTreeClearance.FloorsOf(paths);
			Assert.That(floors.Count, Is.EqualTo(2));
			Assert.That(floors[0].Squared, Is.True);
			// One point has no direction: drawn round, as the path field draws it.
			Assert.That(floors[1].Squared, Is.False);
		}

		[Test]
		public void SquaredFloorIsTheRectangle()
		{
			var floor = new PointOfInterestTreeClearance.Floor { A = new Vector2(-4f, 0f), B = new Vector2(4f, 0f), HalfA = 3f, HalfB = 3f, Squared = true };
			Assert.That(PointOfInterestTreeClearance.Edge(floor, Vector2.zero), Is.EqualTo(-3f).Within(1e-4f));
			// Its corner, inside: a round-ended strip left grass (and would leave a tree) there.
			Assert.That(PointOfInterestTreeClearance.Edge(floor, new Vector2(3.9f, 2.9f)), Is.LessThan(0f));
			Assert.That(PointOfInterestTreeClearance.Edge(floor, new Vector2(6f, 0f)), Is.EqualTo(2f).Within(1e-4f));
			Assert.That(PointOfInterestTreeClearance.Edge(floor, new Vector2(0f, -4f)), Is.EqualTo(1f).Within(1e-4f));
			Assert.That(PointOfInterestTreeClearance.Edge(floor, new Vector2(7f, 7f)), Is.EqualTo(5f).Within(1e-4f));
		}

		[Test]
		public void RoundFloorIsTheCircle()
		{
			var floor = new PointOfInterestTreeClearance.Floor { A = new Vector2(10f, 10f), B = new Vector2(10f, 10f), HalfA = 2f, HalfB = 2f };
			Assert.That(PointOfInterestTreeClearance.Edge(floor, new Vector2(10f, 10f)), Is.EqualTo(-2f).Within(1e-4f));
			Assert.That(PointOfInterestTreeClearance.Edge(floor, new Vector2(13f, 14f)), Is.EqualTo(3f).Within(1e-4f));
		}

		[Test]
		public void ClearanceIsTheCrownHeldToItsBounds()
		{
			Assert.That(PointOfInterestTreeClearance.Clearance(0f), Is.EqualTo(PointOfInterestTreeClearance.MinClearanceMetres));
			Assert.That(PointOfInterestTreeClearance.Clearance(float.NaN), Is.EqualTo(PointOfInterestTreeClearance.MinClearanceMetres));
			Assert.That(PointOfInterestTreeClearance.Clearance(1.2f), Is.EqualTo(1.2f));
			Assert.That(PointOfInterestTreeClearance.Clearance(6f), Is.EqualTo(PointOfInterestTreeClearance.MaxClearanceMetres));
		}

		[Test]
		public void PlacingClearsTheFloorsBeforeTheTreesAreBaked()
		{
			string stage = File.ReadAllText("Assets/Scripts/Shared/Implementation/Tools/Extensions/Unity/Editor/World/PointsOfInterest/PointOfInterestStage.cs");
			int clear = stage.IndexOf("PointOfInterestTreeClearance.Clear(terrains, asset.Paths", System.StringComparison.Ordinal);
			Assert.That(clear, Is.GreaterThan(0), "Place clears the trees off the floors");
			Assert.That(clear, Is.GreaterThan(stage.IndexOf("PointOfInterestLinearFeatures.LayScene(", System.StringComparison.Ordinal)),
				"after every site and linear feature has written its yards");
		}
	}
}
