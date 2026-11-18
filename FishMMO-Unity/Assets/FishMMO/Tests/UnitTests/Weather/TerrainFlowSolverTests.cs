using System;
using NUnit.Framework;
using FishMMO.Client;

namespace FishMMO.UnitTests.Weather
{
	/// <summary>
	/// The flow the clouds and fog are read along round the terrain (TerrainFlowSolver): pinned to
	/// potential flow past a cylinder, whose answer is known in closed form — the wind doubles at the
	/// flanks, stops at the front and back, and the obstacle's outline is a streamline.
	/// </summary>
	/// <remarks>
	/// The disc is drawn in cells, so its outline is a staircase and the grid's edge is a few radii
	/// out: the figures are held to the closed form within what those cost, not to the last digit.
	/// </remarks>
	[TestFixture]
	public class TerrainFlowSolverTests
	{
		private const int N = 128;
		private const float Cell = 50f;
		private const float Radius = 600f;
		private const float Centre = N * Cell * 0.5f;

		private static float[] solved;

		private static float[] Disc()
		{
			var ground = new float[N * N];
			for (int z = 0; z < N; z++)
			{
				for (int x = 0; x < N; x++)
				{
					float dx = (x + 0.5f) * Cell - Centre, dz = (z + 0.5f) * Cell - Centre;
					ground[z * N + x] = dx * dx + dz * dz < Radius * Radius ? 1000f : 0f;
				}
			}
			return ground;
		}

		private static float[] Solved => solved ??= TerrainFlowSolver.SolveLevel(Disc(), N, Cell, 500f);

		/// <summary>The wind along the solved axis at a cell, as a multiple of the undisturbed: 1 + d(offset)/d(coordinate).</summary>
		private static float Along(float[] d, int x, int z, bool alongX)
		{
			int channel = alongX ? 0 : 2;
			int ahead = alongX ? z * N + x + 1 : (z + 1) * N + x;
			int behind = alongX ? z * N + x - 1 : (z - 1) * N + x;
			return 1f + (d[ahead * TerrainFlowSolver.Channels + channel] - d[behind * TerrainFlowSolver.Channels + channel]) / (2f * Cell);
		}

		private static int Middle => (int)(Centre / Cell);
		private static int Out => (int)(Radius / Cell) + 2;

		[Test]
		public void WithNoGroundAboveTheLevel_TheAirIsUndisturbed()
		{
			float[] d = TerrainFlowSolver.SolveLevel(new float[64 * 64], 64, Cell, 10f);
			foreach (float v in d)
			{
				Assert.That(v, Is.EqualTo(0f));
			}
		}

		[Test]
		public void GroundBelowTheLevel_DoesNotTurnTheAir()
		{
			float[] d = TerrainFlowSolver.SolveLevel(Disc(), N, Cell, 1500f);
			foreach (float v in d)
			{
				Assert.That(v, Is.EqualTo(0f), "air above a hill it can clear is not turned by it");
			}
		}

		[Test]
		public void TheWindQuickensAtTheFlanks()
		{
			// Closed form at r from the centre on the flank: U (1 + a²/r²).
			float r = Out * Cell;
			float expected = 1f + Radius * Radius / (r * r);
			Assert.That(Along(Solved, Middle, Middle + Out, true), Is.EqualTo(expected).Within(0.25f));
			Assert.That(Along(Solved, Middle, Middle - Out, true), Is.EqualTo(expected).Within(0.25f));
			Assert.That(Along(Solved, Middle, Middle + Out, true), Is.GreaterThan(1.4f), "visibly faster round the side");
		}

		[Test]
		public void TheWindStallsAgainstTheFaceAndInTheLee()
		{
			float r = Out * Cell;
			float expected = 1f - Radius * Radius / (r * r);
			Assert.That(Along(Solved, Middle - Out, Middle, true), Is.EqualTo(expected).Within(0.15f), "the windward face");
			Assert.That(Along(Solved, Middle + Out, Middle, true), Is.EqualTo(expected).Within(0.15f), "the lee");
		}

		[Test]
		public void FarFromTheObstacle_TheWindIsItsOwn()
		{
			Assert.That(Along(Solved, 8, 8, true), Is.EqualTo(1f).Within(0.02f));
			Assert.That(Along(Solved, 8, 8, false), Is.EqualTo(1f).Within(0.02f));
		}

		[Test]
		public void TheFlowSplitsEvenlyRoundASymmetricObstacle()
		{
			// The stream function's offset either side of the centreline is equal and opposite.
			float above = Solved[((Middle + Out) * N + Middle) * TerrainFlowSolver.Channels + 1];
			float below = Solved[((Middle - Out - 1) * N + Middle) * TerrainFlowSolver.Channels + 1];
			Assert.That(above, Is.EqualTo(-below).Within(1f));
			Assert.That(Math.Abs(above), Is.GreaterThan(100f), "the streamlines are pushed out round it");
		}

		[Test]
		public void TheObstaclesOutlineIsOneStreamline()
		{
			// Inside the rock the stream function is the outline's constant: the centre's z.
			int i = (Middle * N + Middle) * TerrainFlowSolver.Channels;
			float psiAtCentre = Solved[i + 1] + (Middle + 0.5f) * Cell;
			Assert.That(psiAtCentre, Is.EqualTo(Centre).Within(Cell));
		}

		[Test]
		public void AWindAcrossTheOtherAxis_IsSolvedTheSameWay()
		{
			float r = Out * Cell;
			float expected = 1f + Radius * Radius / (r * r);
			Assert.That(Along(Solved, Middle + Out, Middle, false), Is.EqualTo(expected).Within(0.25f));
		}

		[Test]
		public void AirShutInByRock_IsLeftUndisturbed()
		{
			// A ring of rock with a hollow inside: no wind reaches the hollow.
			var ground = new float[64 * 64];
			for (int z = 0; z < 64; z++)
			{
				for (int x = 0; x < 64; x++)
				{
					float dx = x - 31.5f, dz = z - 31.5f;
					float r = (float)Math.Sqrt(dx * dx + dz * dz);
					ground[z * 64 + x] = r > 8f && r < 14f ? 1000f : 0f;
				}
			}
			float[] d = TerrainFlowSolver.SolveLevel(ground, 64, Cell, 500f);
			int hollow = (32 * 64 + 32) * TerrainFlowSolver.Channels;
			for (int c = 0; c < TerrainFlowSolver.Channels; c++)
			{
				Assert.That(d[hollow + c], Is.EqualTo(0f));
			}
		}
	}
}
