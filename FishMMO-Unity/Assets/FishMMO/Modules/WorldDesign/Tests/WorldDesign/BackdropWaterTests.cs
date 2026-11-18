using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared.WorldDesign;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// The water past a scene's edge (<see cref="BackdropWater"/>): a pond the scene raised a river into runs on past
	/// the edge, rivers stop at the water they run into, and no river stands over its own ground to meet the scene's.
	/// </summary>
	[TestFixture]
	public class BackdropWaterTests
	{
		private const float HalfW = 500f, HalfD = 500f, Reach = 600f, PondLevel = 98f;

		/// <summary>
		/// Flat at 100 m, with a basin at 95 m from inside the scene's west edge out to 900 m past the centre, rising
		/// beyond: a pond at 98 m stands in it across the edge.
		/// </summary>
		private static float Basin(float east, float north)
		{
			if (east > -400f)
			{
				return 100f;
			}
			return east > -900f ? 95f : 95f + (-900f - east) * 0.1f;
		}

		/// <summary>A scene with one pond at <see cref="PondLevel"/>, which the scene let spread no further west than −800 m.</summary>
		private static SceneWater WithPond()
		{
			var scene = new SceneWater();
			scene.Lakes.Add(new SceneLake
			{
				Id = 0,
				PlanetLake = -1,
				Level = PondLevel,
				SpillLevel = PondLevel,
				Bounds = Rect.MinMaxRect(-800f, -400f, 0f, 400f),
				MayCover = (east, north) => east > -800f && Mathf.Abs(north) < 400f,
			});
			return scene;
		}

		[Test]
		public void ARiverIsNeverLiftedOverItsGroundToMeetHigherWater()
		{
			// Its ground falls from 140 m to 130 m; what it runs into stands at 160 m.
			var ground = new float[20];
			for (int i = 0; i < ground.Length; i++)
			{
				ground[i] = 140f - i * 0.5f;
			}
			float[] surface = BackdropWater.Profile(ground, float.NaN, 160f, 0.25f, BackdropWater.MeetRows);
			for (int i = 0; i < ground.Length; i++)
			{
				Assert.That(surface[i], Is.LessThanOrEqualTo(ground[i] - 0.25f + 1e-4f), $"row {i} stands under its own ground");
			}
		}

		[Test]
		public void ARiverHeldUpByWhatItMeetsBacksUpOnlyAsFarAsItsBanksStand()
		{
			// Low ground mid-way (a dip to 120 m), then high again before it meets water at 130 m.
			float[] ground = { 150f, 145f, 140f, 120f, 135f, 140f, 140f, 140f };
			float[] surface = BackdropWater.Profile(ground, float.NaN, 130f, 0.25f, BackdropWater.MeetRows);
			Assert.That(surface[7], Is.EqualTo(130f).Within(1e-4f), "it meets the water's level");
			Assert.That(surface[4], Is.EqualTo(130f).Within(1e-4f), "backed up while its ground stands above that level");
			Assert.That(surface[3], Is.EqualTo(119.75f).Within(1e-4f), "not over the dip, where it would hang in the air");
		}

		[Test]
		public void ARiverComesDownOntoTheWaterItRunsInto()
		{
			float[] ground = { 150f, 149f, 148f, 147f, 146f, 145f, 144f, 143f };
			float[] surface = BackdropWater.Profile(ground, float.NaN, 135f, 0.25f, BackdropWater.MeetRows);
			Assert.That(surface[surface.Length - 1], Is.EqualTo(135f).Within(1e-4f));
			for (int i = 1; i < surface.Length; i++)
			{
				Assert.That(surface[i], Is.LessThanOrEqualTo(surface[i - 1] + 1e-4f), "never rising downstream");
			}
		}

		[Test]
		public void AScenePondRunsOnPastTheEdgeAsFarAsTheSceneLetItSpread()
		{
			BackdropWater water = BackdropWater.Assemble(WithPond(), Basin, HalfW, HalfD, Reach, null, new List<BackdropWater.Run>());
			Assert.That(water.Empty, Is.False);
			Assert.That(water.LakeLevelOver(-510f, 0f), Is.EqualTo(PondLevel), "just past the edge");
			Assert.That(water.LakeLevelOver(-700f, 0f), Is.EqualTo(PondLevel), "across the basin within its reach");
			Assert.That(float.IsNaN(water.LakeLevelOver(-850f, 0f)), "not past its reach");
			Assert.That(float.IsNaN(water.LakeLevelOver(-510f, 450f)), "nor beside it, where the scene let it stand nowhere");
			Assert.That(float.IsNaN(water.LakeLevelOver(-200f, 0f)), "nor inside the scene, which draws its own");

			// Past its reach the ground is lower than the pond: a sill holds the water in.
			Assert.That(water.Cut(-825f, 0f, Basin(-825f, 0f)), Is.GreaterThanOrEqualTo(PondLevel + BackdropWater.SillMetres - 1e-3f));
			Assert.That(water.Cut(-1050f, 0f, Basin(-1050f, 0f)), Is.EqualTo(Basin(-1050f, 0f)).Within(1e-3f), "and falls back to the ground past it");
		}

		[Test]
		public void ThePondsSheetMeetsTheSceneAtItsEdge()
		{
			BackdropWater water = BackdropWater.Assemble(WithPond(), Basin, HalfW, HalfD, Reach, null, new List<BackdropWater.Run>());
			List<(string name, Mesh mesh, int order)> meshes = water.Meshes(Basin);
			try
			{
				Mesh lakes = meshes.Single(m => m.name == "Backdrop Lakes").mesh;
				Vector3[] vertices = lakes.vertices;
				Assert.That(vertices.All(v => Mathf.Abs(v.y - PondLevel) < 1e-4f), "at the pond's level");
				Assert.That(vertices.All(v => v.x <= -HalfW + 1e-3f || v.x >= HalfW - 1e-3f || Mathf.Abs(v.z) >= HalfD - 1e-3f), "outside the scene");
				Assert.That(vertices.Any(v => Mathf.Abs(v.x + HalfW) < 1e-3f), "right up to the edge, where the scene's pond meets it");
			}
			finally
			{
				foreach ((string _, Mesh mesh, int _) in meshes)
				{
					Object.DestroyImmediate(mesh);
				}
			}
		}

		[Test]
		public void ARiverEndsAtThePondItRunsIntoAndStandsUnderItsGround()
		{
			var run = new BackdropWater.Run { PlanetRiver = 3, EndsAtScene = true };
			for (float x = -1100f; x <= -500f; x += 50f)
			{
				run.Points.Add(new Vector2(x, 0f));
				run.Discharge.Add(4f);
			}
			BackdropWater water = BackdropWater.Assemble(WithPond(), Basin, HalfW, HalfD, Reach, null, new[] { run });
			Assert.That(water.Rivers, Is.Not.Empty);
			foreach (BackdropWater.Line line in water.Rivers)
			{
				for (int i = 0; i < line.Points.Length; i++)
				{
					Vector2 p = line.Points[i];
					Assert.That(line.Surface[i], Is.LessThanOrEqualTo(Basin(p.x, p.y) + 1e-3f), $"point {i} at {p} lies under its ground, not over it");
					if (i < line.Points.Length - 1)
					{
						Assert.That(float.IsNaN(water.LakeLevelOver(p.x, p.y)), $"point {i} at {p} is not under the pond: only its last row runs on into the water");
					}
				}
			}
		}

		[Test]
		public void TheLakeSheetIsClippedAtTheSceneEdgeNotDropped()
		{
			var pieces = new List<Rect>();
			// A cell over the scene's north-west corner.
			var cell = new Rect(-520f, 480f, 50f, 50f);
			BackdropWater.OutsideRect(cell, HalfW, HalfD, pieces);
			float area = pieces.Sum(r => r.width * r.height);
			Assert.That(area, Is.EqualTo(50f * 50f - 30f * 20f).Within(1e-2f), "the cell less its part inside the scene");
			for (int a = 0; a < pieces.Count; a++)
			{
				Assert.That(pieces[a].xMax <= -HalfW + 1e-3f || pieces[a].yMin >= HalfD - 1e-3f, $"piece {pieces[a]} lies outside the scene");
				for (int b = a + 1; b < pieces.Count; b++)
				{
					Assert.That(pieces[a].Overlaps(pieces[b]), Is.False, "pieces do not overlap");
				}
			}

			BackdropWater.OutsideRect(new Rect(-100f, -100f, 50f, 50f), HalfW, HalfD, pieces);
			Assert.That(pieces, Is.Empty, "a cell inside the scene draws nothing");
			BackdropWater.OutsideRect(new Rect(-700f, 0f, 50f, 50f), HalfW, HalfD, pieces);
			Assert.That(pieces.Single(), Is.EqualTo(new Rect(-700f, 0f, 50f, 50f)), "a cell clear of it draws whole");
		}
	}
}
