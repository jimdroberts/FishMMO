using NUnit.Framework;
using UnityEngine;
using FishMMO.Water;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Which water the open sea's waves reach (<see cref="WaterShoreField.OpenSea"/>): a synthetic coast of
	/// one-metre cells — a beach on the left, an island on the right holding a pool with no way out, a
	/// lagoon behind a six-metre channel, one behind a wide bar dry at mean tide, and a sixteen-metre creek.
	/// </summary>
	[TestFixture]
	public class WaterOpenSeaTests
	{
		private const int N = 200;

		private static float Ground(int x, int y)
		{
			if (x < 80)
			{
				return -5f + x * 0.06f;
			}
			float h = 1.5f;
			if (x > 110 && x < 140 && y > 20 && y < 60) h = -1f;            // the pool
			if (x > 110 && x < 150 && y > 80 && y < 120) h = -2f;           // lagoon A
			if (x >= 80 && x <= 110 && y >= 97 && y <= 103) h = -2f;        // its 6 m channel
			if (x > 110 && x < 150 && y > 140 && y < 180) h = -2f;          // lagoon B
			if (x >= 80 && x <= 110 && y >= 145 && y <= 175) h = 0.3f;      // its bar, 30 m wide, dry at mean tide
			if (x >= 80 && x <= 110 && y >= 2 && y <= 18) h = -1.5f;        // a 16 m creek
			return h;
		}

		private static void Build(out float[] join, out float[] shelter)
		{
			var pixels = new Color[N * N];
			for (int y = 0; y < N; y++)
			{
				for (int x = 0; x < N; x++)
				{
					pixels[y * N + x] = new Color(-Ground(x, y), 0f, 0f, 0f);
				}
			}
			// The signed distance to the waterline, brute force over the few thousand edge cells.
			var edges = new System.Collections.Generic.List<Vector2Int>();
			for (int y = 0; y < N; y++)
			{
				for (int x = 0; x < N; x++)
				{
					bool wet = pixels[y * N + x].r > 0f;
					bool border = (x > 0 && (pixels[y * N + x - 1].r > 0f) != wet) || (y > 0 && (pixels[(y - 1) * N + x].r > 0f) != wet)
						|| (x < N - 1 && (pixels[y * N + x + 1].r > 0f) != wet) || (y < N - 1 && (pixels[(y + 1) * N + x].r > 0f) != wet);
					if (border)
					{
						edges.Add(new Vector2Int(x, y));
					}
				}
			}
			for (int y = 0; y < N; y++)
			{
				for (int x = 0; x < N; x++)
				{
					bool wet = pixels[y * N + x].r > 0f;
					float best = float.MaxValue;
					foreach (Vector2Int e in edges)
					{
						if ((pixels[e.y * N + e.x].r > 0f) == wet)
						{
							continue;
						}
						best = Mathf.Min(best, (e - new Vector2Int(x, y)).sqrMagnitude);
					}
					float metres = Mathf.Max(0f, Mathf.Sqrt(best) - 0.5f);
					pixels[y * N + x].g = wet ? metres : -metres;
				}
			}
			WaterShoreField.OpenSea(pixels, N, 1f, N, 8f, 0.5f, 3f, out join, out shelter);
		}

		private static float Open(float[] join, float[] shelter, int x, int y, float tide)
		{
			int i = y * N + x;
			return WaterShoreField.OpenFromJoin(tide, join[i])
				* WaterShoreField.ShelterAt(shelter[i * 3], shelter[i * 3 + 1], shelter[i * 3 + 2], tide, 3f);
		}

		[Test]
		public void OnlyWaterTheSwellCanReachAtThisTideMoves()
		{
			Build(out float[] join, out float[] shelter);
			LogAssert.IsTrue(Open(join, shelter, 40, 100, 0f) > 0.99f, "the open sea");
			LogAssert.IsTrue(Open(join, shelter, 78, 60, 0f) > 0.99f, $"an open beach at its waterline (joins {join[60 * N + 78]:0.00})");
			LogAssert.IsTrue(Open(join, shelter, 125, 40, 1f) < 0.01f, "a pool in the island, cut off at any ordinary tide");
			LogAssert.IsTrue(Open(join, shelter, 125, 40, 3f) > 0.99f, $"until the tide stands well over its rim (joins {join[40 * N + 125]:0.00})");
			LogAssert.IsTrue(Open(join, shelter, 130, 100, 1f) < 0.01f, "a lagoon behind a six-metre channel is sheltered while the island stands");
			LogAssert.IsTrue(Open(join, shelter, 130, 160, 0f) < 0.01f, "a lagoon behind a bar dry at mean tide lies still");
			LogAssert.IsTrue(Open(join, shelter, 130, 160, 1.5f) > 0.99f, $"and opens once the tide is well over its bar (joins {join[160 * N + 130]:0.00})");
			float creek = Open(join, shelter, 100, 10, 0f);
			LogAssert.IsTrue(creek > 0.2f && creek < 0.9f, $"a sixteen-metre creek lets some of it in ({creek:0.00})");
		}
	}
}
