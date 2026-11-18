using NUnit.Framework;
using FishMMO.Client;

namespace FishMMO.UnitTests.Weather
{
	/// <summary>
	/// Where the terrain shades the air (TerrainShadowSolver): pinned to geometry a protractor can check —
	/// a wall throws a shadow as long as its height over the tangent of the light's elevation, and no
	/// higher than the wall less the drop along it.
	/// </summary>
	[TestFixture]
	public class TerrainShadowSolverTests
	{
		private const int N = 100;
		private const float Size = 1000f;
		private const float Cell = Size / N;

		/// <summary>Flat ground at 0 with a wall 100 m high across x = 600..610 m.</summary>
		private static TerrainShadowSolver.Grid Wall()
		{
			var heights = new float[N * N];
			for (int z = 0; z < N; z++)
			{
				for (int x = 0; x < N; x++)
				{
					float px = (x + 0.5f) * Cell;
					heights[z * N + x] = px >= 600f && px < 610f ? 100f : 0f;
				}
			}
			return TerrainShadowSolver.Grid.Of(heights, N, 0f, 0f, Size);
		}

		private static float TopAt(float[] tops, float x, float z) => tops[(int)(z / Cell) * N + (int)(x / Cell)];

		[Test]
		public void AWallShadesTheGroundOnTheFarSideFromTheLight()
		{
			// The light toward +x at 45 degrees: the wall's shadow runs 100 m back toward −x.
			float s = 0.70710678f;
			float[] tops = TerrainShadowSolver.ShadowTops(Wall(), default, s, s, 0f, 3000f);
			Assert.That(TopAt(tops, 555f, 500f), Is.GreaterThan(0f), "50 m back: the ground is shaded");
			Assert.That(TopAt(tops, 555f, 500f), Is.EqualTo(100f - 50f).Within(Cell * 1.5f), "shaded up to the wall less the drop of the ray");
			Assert.That(TopAt(tops, 450f, 500f), Is.LessThan(0f), "150 m back: past the shadow's end");
			Assert.That(TopAt(tops, 700f, 500f), Is.LessThan(0f), "on the lit side");
		}

		[Test]
		public void ALowLightThrowsALongerShadow()
		{
			// 10 degrees: tan = 0.176, so a 100 m wall shades about 570 m.
			float elevation = 10f * 3.14159265f / 180f;
			float c = (float)System.Math.Cos(elevation), sn = (float)System.Math.Sin(elevation);
			float[] tops = TerrainShadowSolver.ShadowTops(Wall(), default, c, sn, 0f, 3000f);
			Assert.That(TopAt(tops, 200f, 500f), Is.GreaterThan(0f), "400 m back is still shaded");
			Assert.That(TopAt(tops, 15f, 500f), Is.LessThan(0f), "600 m back is not");
		}

		[Test]
		public void AMountainOffTheFineGridStillShades()
		{
			// The fine grid flat; the scene's coarse grid holds a 500 m ridge 2 km toward the light.
			var flat = TerrainShadowSolver.Grid.Of(new float[N * N], N, 0f, 0f, Size);
			int m = 80;
			float coarseSize = 8000f, coarseCell = coarseSize / m;
			var heights = new float[m * m];
			for (int z = 0; z < m; z++)
			{
				for (int x = 0; x < m; x++)
				{
					float px = -2000f + (x + 0.5f) * coarseCell;
					heights[z * m + x] = px > 2900f && px < 3100f ? 500f : 0f;
				}
			}
			var coarse = TerrainShadowSolver.Grid.Of(heights, m, -2000f, -2000f, coarseSize);
			float elevation = 8f * 3.14159265f / 180f;
			float[] tops = TerrainShadowSolver.ShadowTops(flat, coarse, (float)System.Math.Cos(elevation), (float)System.Math.Sin(elevation), 0f, 6000f);
			Assert.That(TopAt(tops, 500f, 500f), Is.GreaterThan(0f), "the evening shadow of a ridge two and a half kilometres off");
		}

		[Test]
		public void TheLightDown_ShadesEverything_AndOverhead_Nothing()
		{
			float[] down = TerrainShadowSolver.ShadowTops(Wall(), default, 1f, -0.1f, 0f, 3000f);
			Assert.That(down[0], Is.EqualTo(TerrainShadowSolver.AllShade));
			float[] overhead = TerrainShadowSolver.ShadowTops(Wall(), default, 0f, 1f, 0f, 3000f);
			Assert.That(overhead[0], Is.EqualTo(TerrainShadowSolver.NoShade));
		}
	}
}
