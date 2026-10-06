using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.WorldDesign;
using FishMMO.Water;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// Falls: a river's falling stretch is found as one fall from the point before it to the point after, small drops
	/// are left to the rapids, and a pool is scoured under every fall's foot.
	/// </summary>
	[TestFixture]
	public class WaterfallTests
	{
		/// <summary>A straight river along x every 4 m, its surface given, falling at the points marked.</summary>
		private static SceneHydrology.River River(float[] surface, params int[] falling)
		{
			int n = surface.Length;
			var river = new SceneHydrology.River
			{
				Id = 3,
				Points = new Vector3[n],
				Width = new float[n],
				Depth = new float[n],
				Speed = new float[n],
				Reach = new byte[n],
			};
			for (int i = 0; i < n; i++)
			{
				river.Points[i] = new Vector3(i * 4f, surface[i], 0f);
				river.Width[i] = 6f;
				river.Depth[i] = 1f;
				river.Speed[i] = 1.5f;
			}
			foreach (int i in falling)
			{
				river.Reach[i] = InlandWaterRenderer.FallReach;
			}
			return river;
		}

		[Test]
		public void AFallingStretchIsOneFallFromLipToFoot()
		{
			var hydrology = ScriptableObject.CreateInstance<SceneHydrology>();
			try
			{
				// Flat at 50 m, dropping 9 m over points 4–6, flat at 41 m; point 5 is unmarked (a one-point gap is bridged).
				hydrology.Rivers.Add(River(new[] { 50f, 50f, 50f, 50f, 47f, 44f, 41f, 41f, 41f, 41f }, 4, 6));
				List<InlandWaterRenderer.Fall> falls = InlandWaterRenderer.FindFalls(hydrology, 1.5f);
				Assert.That(falls.Count, Is.EqualTo(1));
				InlandWaterRenderer.Fall fall = falls[0];
				Assert.That(fall.River, Is.EqualTo(3));
				Assert.That(fall.Lip, Is.EqualTo(3), "the last point before it drops");
				Assert.That(fall.Foot, Is.EqualTo(7), "the first point after it lands");
				Assert.That(fall.Drop, Is.EqualTo(9f).Within(1e-4f));
				Assert.That(fall.PoolRadius, Is.GreaterThanOrEqualTo(fall.Width * 0.9f));
			}
			finally
			{
				Object.DestroyImmediate(hydrology);
			}
		}

		[Test]
		public void ASmallDropIsLeftToTheRapids()
		{
			var hydrology = ScriptableObject.CreateInstance<SceneHydrology>();
			try
			{
				hydrology.Rivers.Add(River(new[] { 10f, 10f, 9.4f, 9f, 9f }, 2));
				Assert.That(InlandWaterRenderer.FindFalls(hydrology, 1.5f), Is.Empty, "a one-metre drop is a rapid, not a fall");
			}
			finally
			{
				Object.DestroyImmediate(hydrology);
			}
		}

		[Test]
		public void ATallFallDropsOffItsLedgeNotDownAChute()
		{
			// Flo Monolith's 49 m fall: 16 m of run, every point falling. It became an 80° chute 8 m long (a sixth of the drop).
			const int n = 9;
			var surface = new float[n];
			var path = new RiverPath
			{
				X = new float[n], Z = new float[n], S = new float[n], Discharge = new float[n], Width = new float[n], Depth = new float[n],
				Surface = surface, Bed = new float[n], Curvature = new float[n], Reach = new RiverReach[n],
			};
			for (int i = 0; i < n; i++)
			{
				path.X[i] = path.S[i] = i * 2.2f;
				surface[i] = 300f - (i <= 0 ? 0f : i >= 8 ? 49f : i * (49f / 8f));
				path.Width[i] = 3f;
				path.Depth[i] = 0.7f;
				path.Bed[i] = surface[i] - 0.7f;
				path.Reach[i] = i >= 1 && i <= 7 ? RiverReach.Fall : RiverReach.Run;
			}
			RiverShaping.Knickpoints(path, new RiverSettings());
			Assert.That(300f - surface[1], Is.GreaterThan(0.8f * 49f), "over the ledge within its first point: a drop, not a slide");
		}

		[Test]
		public void APlungeBasinWidensThePoolNotTheLipOrTheFall()
		{
			// A 49 m ledge: the lip and the falling points stand within a few metres of the foot, inside the basin's radius.
			const int n = 16;
			var surface = new float[n];
			var path = new RiverPath
			{
				X = new float[n], Z = new float[n], S = new float[n], Discharge = new float[n], Width = new float[n], Depth = new float[n],
				Surface = surface, Bed = new float[n], Curvature = new float[n], Reach = new RiverReach[n],
			};
			for (int i = 0; i < n; i++)
			{
				path.X[i] = path.S[i] = i * 1.5f;
				surface[i] = i <= 2 ? 300f : 251f;
				path.Width[i] = 4f;
				path.Depth[i] = 0.7f;
				path.Bed[i] = surface[i] - 0.7f;
				path.Reach[i] = i >= 3 && i <= 4 ? RiverReach.Fall : RiverReach.Run;
			}
			RiverShaping.PlungePools(path, new RiverSettings());
			Assert.That(path.Width[2], Is.EqualTo(4f), "the lip keeps the river's width: no dry shelf beside the water going over");
			Assert.That(path.Width[4], Is.EqualTo(4f), "a falling point keeps it: the curtain does not flare into the pool");
			Assert.That(path.Width[5], Is.GreaterThan(20f), "the foot is the basin: the river's width and half the drop");
		}

		[Test]
		public void AFallsDropGathersIntoAStepAtItsLip()
		{
			// 40 m of hillside falling 10 m (25 %), every point of it marked falling.
			const int n = 21;
			var surface = new float[n];
			var path = new RiverPath
			{
				X = new float[n], Z = new float[n], S = new float[n], Discharge = new float[n], Width = new float[n], Depth = new float[n],
				Surface = surface, Bed = new float[n], Curvature = new float[n], Reach = new RiverReach[n],
			};
			for (int i = 0; i < n; i++)
			{
				path.X[i] = path.S[i] = i * 2f;
				surface[i] = 20f - (i <= 1 ? 0f : i >= 19 ? 10f : (i - 1) * (10f / 18f));
				path.Width[i] = 4f;
				path.Depth[i] = 0.8f;
				path.Bed[i] = surface[i] - 0.8f;
				path.Reach[i] = i >= 2 && i <= 18 ? RiverReach.Fall : RiverReach.Run;
			}
			var before = (float[])surface.Clone();
			var settings = new RiverSettings();
			RiverShaping.Knickpoints(path, settings);
			// Lip at 1, foot at 19: 85 % of the 10 m drop within the step run (1.5 m) below the lip.
			Assert.That(before[1] - surface[2], Is.GreaterThan(8f), "the ledge: most of the drop in the first two metres");
			for (int i = 0; i < n; i++)
			{
				Assert.That(surface[i], Is.LessThanOrEqualTo(before[i] + 1e-4f), "only ever lowered: the carve cuts, it never fills");
				Assert.That(path.Bed[i], Is.EqualTo(surface[i] - 0.8f).Within(1e-4f));
				if (i > 0)
				{
					Assert.That(surface[i], Is.LessThanOrEqualTo(surface[i - 1] + 1e-4f), "never rising downstream");
				}
			}
			Assert.That(surface[19], Is.EqualTo(before[19]).Within(1e-4f), "the foot where it was");
			int falling = 0;
			for (int i = 0; i < n; i++)
			{
				falling += path.Reach[i] == RiverReach.Fall ? 1 : 0;
			}
			Assert.That(falling, Is.InRange(1, 4), "only the step still falls");
			Assert.That(path.Reach[12], Is.Not.EqualTo(RiverReach.Fall), "the water below the step runs on");
		}

		[Test]
		public void APoolIsScouredUnderAFallsFoot()
		{
			const int n = 12;
			var surface = new float[n];
			var path = new RiverPath
			{
				X = new float[n], Z = new float[n], S = new float[n], Discharge = new float[n], Width = new float[n], Depth = new float[n],
				Surface = surface, Bed = new float[n], Curvature = new float[n], Reach = new RiverReach[n],
			};
			for (int i = 0; i < n; i++)
			{
				path.X[i] = path.S[i] = i * 4f;
				surface[i] = i < 4 ? 30f : i > 5 ? 22f : 30f - (i - 3) * 4f;
				path.Width[i] = 6f;
				path.Depth[i] = 1f;
				path.Bed[i] = surface[i] - 1f;
			}
			path.Reach[4] = path.Reach[5] = RiverReach.Fall;
			RiverShaping.PlungePools(path, new RiverSettings());
			// Lip at 3, foot at 6: an 8 m drop scours about 3.3 m below the river's own bed at the foot.
			Assert.That(path.Bed[6], Is.LessThan(surface[6] - 1f - 3f), "deep under the foot");
			Assert.That(path.Depth[6], Is.EqualTo(surface[6] - path.Bed[6]).Within(1e-4f));
			Assert.That(path.Width[6], Is.GreaterThan(6f), "wider than the river");
			Assert.That(path.Bed[11], Is.EqualTo(surface[11] - 1f).Within(1e-4f), "back to the river's own bed past the pool");
			Assert.That(path.Bed[2], Is.EqualTo(surface[2] - 1f).Within(1e-4f), "nothing scoured above the fall");
		}
	}
}
