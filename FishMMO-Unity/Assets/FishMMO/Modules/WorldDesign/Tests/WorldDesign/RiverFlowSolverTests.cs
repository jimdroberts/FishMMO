using NUnit.Framework;
using FishMMO.Shared.WorldDesign;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// The solved river flow: fastest mid-channel and slow at the banks, at about the section's mean speed overall; a
	/// slack wake behind a boulder with faster water past its sides; a long river solved in windows with no seam where
	/// they meet.
	/// </summary>
	[TestFixture]
	public class RiverFlowSolverTests
	{
		private const int Ny = RiverFlowSolver.Across;

		private static float U(float[] v, int length, int x, int y) => v[(y * length + x) * 2];

		[Test]
		public void AnOpenChannelRunsFastestInItsMiddle()
		{
			const int length = 160;
			float[] v = RiverFlowSolver.Solve(length, new bool[length * Ny]);
			int x = 120;
			float middle = U(v, length, x, Ny / 2), bank = U(v, length, x, 0);
			Assert.That(middle, Is.GreaterThan(bank * 1.2f), "the banks drag the water back");
			float mean = 0f;
			for (int y = 0; y < Ny; y++)
			{
				mean += U(v, length, x, y);
			}
			mean /= Ny;
			Assert.That(mean, Is.InRange(0.8f, 1.2f), "the section carries its discharge");
		}

		[Test]
		public void ABoulderLeavesASlackWakeAndSpeedsTheWaterPastIt()
		{
			const int length = 160;
			var solid = new bool[length * Ny];
			// A boulder four cells across in the middle of the channel, at 60 m.
			for (int y = Ny / 2 - 2; y < Ny / 2 + 2; y++)
			{
				for (int x = 58; x < 62; x++)
				{
					solid[y * length + x] = true;
				}
			}
			float[] v = RiverFlowSolver.Solve(length, solid);
			float wake = U(v, length, 64, Ny / 2);
			float beside = U(v, length, 60, 2);
			float open = U(v, length, 30, Ny / 2);
			Assert.That(wake, Is.LessThan(open * 0.5f), "slack water behind the rock");
			Assert.That(beside, Is.GreaterThan(U(v, length, 30, 2)), "squeezed past its sides, the water speeds up");
			Assert.That(v[((Ny / 2) * length + 60) * 2], Is.EqualTo(0f), "nothing inside the rock");
		}

		[Test]
		public void TheShearPastARockIsSpreadByTurbulenceNotACellWide()
		{
			// A turbulent river mixes the fast water past a rock into the slack behind it over a metre or two. Laminar at
			// τ 0.56, the jet stood a cell from dead water (a step of 0.87 of the mean between neighbouring cells, measured
			// on a port of this solve), and the ripples riding it tore into lines along the flow.
			const int length = 160;
			var solid = new bool[length * Ny];
			for (int y = Ny / 2 - 2; y < Ny / 2 + 2; y++)
			{
				for (int x = 58; x < 62; x++)
				{
					solid[y * length + x] = true;
				}
			}
			float[] v = RiverFlowSolver.Solve(length, solid);
			float steepest = 0f;
			for (int x = 62; x < 110; x++)
			{
				for (int y = 0; y + 1 < Ny; y++)
				{
					steepest = System.Math.Max(steepest, System.Math.Abs(U(v, length, x, y + 1) - U(v, length, x, y)));
				}
			}
			Assert.That(steepest, Is.LessThan(0.6f), "no step across the current sharper than about half the mean speed between cells");
		}

		[Test]
		public void ALongRiverSolvedInWindowsHasNoSeam()
		{
			int length = RiverFlowSolver.Window * 3;
			float[] v = RiverFlowSolver.Solve(length, new bool[length * Ny]);
			int seam = RiverFlowSolver.Window - RiverFlowSolver.Overlap;
			for (int y = 0; y < Ny; y++)
			{
				float before = U(v, length, seam - 1, y), after = U(v, length, seam, y);
				Assert.That(after, Is.EqualTo(before).Within(0.08f), $"row {y}: the flow runs on across where two windows meet");
			}
		}
	}
}
