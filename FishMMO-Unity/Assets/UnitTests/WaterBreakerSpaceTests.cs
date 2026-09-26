using System;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Water;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// How far shoreward a breaker may reach (WaterBreakers.SpaceFor), on the three shapes that decide it.
	/// </summary>
	/// <remarks>
	/// A breaker throws its water square to the break line. Where the line bends toward the shore those
	/// throws converge, and over a shoal the breakers from every side meet in the middle: thrown past
	/// either, the sheets went through each other and the barrels seemed to form outward (Jim,
	/// 2026-09-26). The space is what stops that — and on an ordinary beach it must be the plain room.
	/// </remarks>
	[TestFixture]
	public class WaterBreakerSpaceTests
	{
		private const int Resolution = 256;

		/// <summary>A one-metre field over 256 m, its depths from a function of world XZ, as the GPU has them.</summary>
		private static WaterShoreField.Snapshot Field(Func<float, float, float> depth)
		{
			var halves = new ushort[Resolution * Resolution * 2];
			for (int y = 0; y < Resolution; y++)
			{
				for (int x = 0; x < Resolution; x++)
				{
					halves[(y * Resolution + x) * 2] = Mathf.FloatToHalf(depth(x + 0.5f, y + 0.5f));
				}
			}
			return new WaterShoreField.Snapshot(halves, Resolution, new Rect(0f, 0f, Resolution, Resolution), 1f, 1);
		}

		/// <summary>A loop round a centre, anticlockwise, so the shallow inside is on its left.</summary>
		private static WaterBreakLine.Polyline Circle(Vector2 centre, float radius, float spacing)
		{
			var loop = new WaterBreakLine.Polyline { Closed = true };
			int count = Mathf.CeilToInt(2f * Mathf.PI * radius / spacing);
			for (int i = 0; i < count; i++)
			{
				float angle = 2f * Mathf.PI * i / count;
				loop.Points.Add(centre + radius * new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)));
			}
			return loop;
		}

		private static readonly Vector2 Centre = new Vector2(128f, 128f);

		[Test]
		public void AStraightBeachGivesTheRoom()
		{
			// 1:10 up to dry land at x = 50; the break line at 1.7 m deep is x = 67, 17 m out.
			WaterShoreField.Snapshot field = Field((x, z) => (x - 50f) / 10f);
			var line = new WaterBreakLine.Polyline();
			for (float z = 60f; z <= 200f; z += 1.5f)
			{
				line.Points.Add(new Vector2(67f, z));
			}
			int middle = line.Points.Count / 2;
			float space = WaterBreakers.SpaceFor(field, line, middle, line.Points[middle], Vector2.left, 17f, 0f, 1f);
			LogAssert.IsTrue(Mathf.Abs(space - 17f) < 0.01f, $"a plain beach should give the whole room, 17 m; got {space:0.00}");
		}

		[Test]
		public void AnIslandIsLimitedByItsBend()
		{
			// Dry land out to 3 m round the centre, 1:10 beyond: the break line is the 20 m circle.
			WaterShoreField.Snapshot field = Field((x, z) => (Vector2.Distance(new Vector2(x, z), Centre) - 3f) / 10f);
			WaterBreakLine.Polyline loop = Circle(Centre, 20f, 1.5f);
			Vector2 here = loop.Points[5];
			float space = WaterBreakers.SpaceFor(field, loop, 5, here, (Centre - here).normalized, 17f, 0f, 1f);
			// Most of the bend's 20 m radius: a throw past that crosses the throws either side of it.
			LogAssert.IsTrue(space < 17f && Mathf.Abs(space - 16f) < 0.6f, $"the bend should hold it to 0.8 of 20 m, 16 m; got {space:0.00}");
		}

		[Test]
		public void AShoalStopsAtItsCrest()
		{
			// Never dry: 0.4 m over the middle, deepening 1 in 15 all round. The break line at 1.7 m is
			// the 19.5 m circle, and the nearest dry land is said to be 150 m off.
			WaterShoreField.Snapshot field = Field((x, z) => 0.4f + Vector2.Distance(new Vector2(x, z), Centre) / 15f);
			WaterBreakLine.Polyline loop = Circle(Centre, 19.5f, 1.5f);
			Vector2 here = loop.Points[11];
			float space = WaterBreakers.SpaceFor(field, loop, 11, here, (Centre - here).normalized, 150f, 0f, 1f);
			/* The crest is 19.5 m in and the bend's radius 19.5 m, so the bend binds first: 15.6 m — not
			 * the 40 m the far shore would allow, which carried every side's breakers through the middle. */
			LogAssert.IsTrue(space <= 19.5f && Mathf.Abs(space - 15.6f) < 0.6f, $"a shoal should hold it inside its crest, about 15.6 m; got {space:0.00}");
		}

		[Test]
		public void ARidgeStopsTheMarch()
		{
			/* A sandbar with no bend to it: a straight ridge 0.5 m under at x = 90, deepening either side,
			 * and the real shore far off. From the break line at x = 70, the space is the 20 m to the crest. */
			WaterShoreField.Snapshot field = Field((x, z) => 0.5f + Mathf.Abs(x - 90f) / 15f);
			var line = new WaterBreakLine.Polyline();
			for (float z = 60f; z <= 200f; z += 1.5f)
			{
				line.Points.Add(new Vector2(70.5f, z));
			}
			int middle = line.Points.Count / 2;
			float space = WaterBreakers.SpaceFor(field, line, middle, line.Points[middle], Vector2.right, 150f, 0f, 1f);
			LogAssert.IsTrue(Mathf.Abs(space - 19.5f) < 1f, $"a ridge should stop it at its crest, 19.5 m in; got {space:0.00}");
		}
	}
}
