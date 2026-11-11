using System.IO;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Client;

namespace FishMMO.UnitTests.Weather
{
	/// <summary>
	/// The ambient life budget: groups are taken nearest first and charged only the members that will draw inside the camera's
	/// view, and a group drawn last frame may run past the budget by a share of it while a newcomer must fit inside it.
	/// Measured in the browser (2026-11-11): charged their whole head count (groups behind the camera, members past their
	/// distance), the budget ran out with a third of it on screen, and with one limit for every group the farthest flock lost
	/// its draw for single frames as the camera swept, every bird of it together.
	/// </summary>
	[TestFixture]
	public class AmbientLifeBudgetTests
	{
		private const string SystemPath = "Assets/Scripts/Client/World/AmbientLife/AmbientLifeSystem.cs";

		[Test]
		public void AGroupCosts_WhatTheCameraSeesOfIt()
		{
			var flock = new AmbientCreatureKind { Behaviour = AmbientBehaviour.Flock };
			var swarm = new AmbientCreatureKind { Behaviour = AmbientBehaviour.Swarm };
			Assert.That(AmbientLifeSystem.BudgetCost(14, flock), Is.EqualTo(14), "a seen member each");
			Assert.That(AmbientLifeSystem.BudgetCost(200, swarm), Is.EqualTo(1), "a swarm is one instance");
			Assert.That(AmbientLifeSystem.BudgetCost(0, flock), Is.EqualTo(0), "nothing seen costs nothing");
			Assert.That(AmbientLifeSystem.BudgetCost(0, swarm), Is.EqualTo(0), "nor does an unseen swarm");
		}

		[Test]
		public void AnUnseenGroup_IsAlwaysDrawn_EvenPastTheBudget()
		{
			Assert.That(AmbientLifeSystem.Admits(0, 600, 400, held: false), Is.True);
		}

		[Test]
		public void InView_TakesTheAnimalsSize()
		{
			// A box view: x in [-10, 10] (normals point inward, as GeometryUtility's).
			var view = new[] { new Plane(Vector3.right, 10f), new Plane(Vector3.left, 10f) };
			Assert.That(AmbientLifeSystem.InView(view, new Vector3(0f, 0f, 0f), 0.5f), Is.True);
			Assert.That(AmbientLifeSystem.InView(view, new Vector3(11f, 0f, 0f), 0.5f), Is.False, "outside by more than its size");
			Assert.That(AmbientLifeSystem.InView(view, new Vector3(10.4f, 0f, 0f), 0.5f), Is.True, "its edge still inside");
		}

		[Test]
		public void TheBudget_IsSpentOnWhatIsSeen()
		{
			string source = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), SystemPath));
			StringAssert.Contains("int cost = BudgetCost(seen, kind);", source);
			StringAssert.Contains("if (!Admits(cost, spent, maxVisible, c.Held))", source);
			StringAssert.Contains("spent += cost;", source);
			StringAssert.Contains("bool held = drawnLastFrame.Contains(g.Seed);", source);
			// The members are held aside until the group is taken, so a refused group draws nothing.
			StringAssert.Contains("byKind[g.Kind].AddRange(pending);", source);
		}

		[Test]
		public void AGroupDrawnLastFrame_KeepsItsPlace_PastTheBudget()
		{
			Assert.That(AmbientLifeSystem.Admits(28, 380, 400, held: false), Is.False, "a newcomer must fit inside the budget");
			Assert.That(AmbientLifeSystem.Admits(28, 380, 400, held: true), Is.True, "a held group may run past it");
			Assert.That(AmbientLifeSystem.Admits(28, 472, 400, held: true), Is.True, "up to a quarter of it: 500");
			Assert.That(AmbientLifeSystem.Admits(28, 473, 400, held: true), Is.False, "and no further");
			Assert.That(AmbientLifeSystem.Admits(28, 372, 400, held: false), Is.True, "a newcomer that fits is drawn");
		}

		[Test]
		public void OnlyTheMainCamera_WorksOutTheFrame()
		{
			/* The minimap's camera renders on its own, before the main camera, at its own frame cap. Taken as "the first game
			 * camera", it worked the frame's animals out from its eye high over the player and the main camera drew that list:
			 * every frame the minimap rendered, the animals in front of the player were gone (every other frame at 60 Hz). */
			string source = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), SystemPath));
			int context = source.IndexOf("private static void OnBeginContextRendering", System.StringComparison.Ordinal);
			int update = source.IndexOf("private static void UpdateFrame(", System.StringComparison.Ordinal);
			Assert.That(context, Is.GreaterThan(0));
			Assert.That(update, Is.GreaterThan(context));
			string chooser = source.Substring(context, update - context);
			StringAssert.Contains("if (IsMainCamera(camera))", chooser, "the frame is worked out for the main camera");
			StringAssert.DoesNotContain("CameraType.SceneView", chooser, "and for no other camera standing in for it");
			StringAssert.Contains("return camera != null && camera.cameraType == CameraType.Game && camera == Camera.main;", source);
			StringAssert.Contains("if (!IsMainCamera(camera))", source, "nor is it drawn for any other camera");
		}

		[Test]
		public void TheNearestGroup_IsAlwaysDrawn()
		{
			Assert.That(AmbientLifeSystem.Admits(900, 0, 400, held: false), Is.True);
		}
	}
}
