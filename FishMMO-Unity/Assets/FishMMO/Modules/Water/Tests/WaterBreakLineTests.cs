using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Water;
using Random = System.Random;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The break line: the contour where the shore field's depth equals the break depth, as the
	/// polylines the breaker mesh is built along.
	/// </summary>
	/// <remarks>
	/// <see cref="WaterBreakLine"/> is pure — a grid of depths in, polylines out — so every property
	/// the breaker leans on is checked here without a scene: that a coast is ONE piece rather than a
	/// scatter of segments, that it sits where the depth says, and above all that it runs with the
	/// shallow water on its left, because the breaker takes its shoreward direction from that and
	/// nothing else. A line that runs backwards curls its waves out to sea.
	/// </remarks>
	[TestFixture]
	public class WaterBreakLineTests
	{
		private static float[] Field(int width, int height, Func<float, float, float> depth)
		{
			var grid = new float[width * height];
			for (int y = 0; y < height; y++)
			{
				for (int x = 0; x < width; x++)
				{
					grid[y * width + x] = depth(x, y);
				}
			}
			return grid;
		}

		/// <summary>Signed area in XZ, x right and z up: positive for an anticlockwise loop.</summary>
		private static float SignedArea(List<Vector2> points)
		{
			double area = 0.0;
			for (int i = 0; i < points.Count; i++)
			{
				Vector2 a = points[i];
				Vector2 b = points[(i + 1) % points.Count];
				area += (double)a.x * b.y - (double)b.x * a.y;
			}
			return (float)(area * 0.5);
		}

		private static int SegmentCount(WaterBreakLine.Polyline line)
		{
			return line.Closed ? line.Points.Count : line.Points.Count - 1;
		}

		/// <summary>
		/// The orientation contract, against the depth function itself: a step to the left of every
		/// segment is shallower than the same step to its right.
		/// </summary>
		private static void AssertShallowOnTheLeft(WaterBreakLine.Polyline line, Func<Vector2, float> depth, float step)
		{
			int n = line.Points.Count;
			for (int i = 0; i < SegmentCount(line); i++)
			{
				Vector2 a = line.Points[i];
				Vector2 b = line.Points[(i + 1) % n];
				Vector2 d = (b - a).normalized;
				Vector2 left = new Vector2(-d.y, d.x);
				Vector2 middle = (a + b) * 0.5f;
				Assert.That(depth(middle + left * step), Is.LessThan(depth(middle - left * step)),
					$"segment {i} ({a} -> {b}) has the deep water on its left");
			}
		}

		/// <summary>No NaN, and no two consecutive points the same — the closing pair of a loop included.</summary>
		private static void AssertClean(WaterBreakLine.Polyline line)
		{
			int n = line.Points.Count;
			Assert.That(n, Is.GreaterThanOrEqualTo(line.Closed ? 3 : 2), "a piece too short to have a direction");
			for (int i = 0; i < n; i++)
			{
				Vector2 p = line.Points[i];
				Assert.That(float.IsNaN(p.x) || float.IsNaN(p.y) || float.IsInfinity(p.x) || float.IsInfinity(p.y),
					Is.False, $"point {i} is {p}");
			}
			for (int i = 0; i < SegmentCount(line); i++)
			{
				Vector2 a = line.Points[i];
				Vector2 b = line.Points[(i + 1) % n];
				Assert.That((b - a).sqrMagnitude, Is.GreaterThan(0f), $"zero-length segment at {i} ({a})");
			}
		}

		/// <summary>How many grid edges have one shallow end and one deep end.</summary>
		private static int CrossingEdges(float[] grid, int width, int height, float iso)
		{
			int count = 0;
			for (int y = 0; y < height; y++)
			{
				for (int x = 0; x < width; x++)
				{
					bool here = grid[y * width + x] < iso;
					if (x + 1 < width && here != grid[y * width + x + 1] < iso)
					{
						count++;
					}
					if (y + 1 < height && here != grid[(y + 1) * width + x] < iso)
					{
						count++;
					}
				}
			}
			return count;
		}

		[Test]
		public void ACircularIslandIsOneAnticlockwiseLoopAtTheRightRadius()
		{
			/* Depth grows with the radius, so the island is the shallow middle and the break line is
			 * the ring 35 m out. One loop, anticlockwise, and on the circle to within what linear
			 * interpolation across a 1 m texel can be wrong by. */
			var centre = new Vector2(64f, 64f);
			float[] grid = Field(128, 128, (x, z) => Vector2.Distance(new Vector2(x, z), centre) - 30f);

			List<WaterBreakLine.Polyline> lines = WaterBreakLine.Extract(grid, 128, 128, Vector2.zero, 1f, 5f);

			Assert.That(lines.Count, Is.EqualTo(1), "one island, one line");
			WaterBreakLine.Polyline loop = lines[0];
			Assert.That(loop.Closed, Is.True, "an island's shore goes all the way round");
			AssertClean(loop);
			foreach (Vector2 p in loop.Points)
			{
				Assert.That(Vector2.Distance(p, centre), Is.EqualTo(35f).Within(0.15f), $"{p} is off the circle");
			}
			Assert.That(SignedArea(loop.Points), Is.GreaterThan(0f), "shallow inside means anticlockwise");
			AssertShallowOnTheLeft(loop, p => Vector2.Distance(p, centre), 0.25f);
			Assert.That(loop.Length, Is.EqualTo(2f * Mathf.PI * 35f).Within(1f), "the length is the circumference");
		}

		[Test]
		public void TheBuiltLoopIsEvenlySpacedAndStillRound()
		{
			var centre = new Vector2(64f, 64f);
			float[] grid = Field(128, 128, (x, z) => Vector2.Distance(new Vector2(x, z), centre) - 30f);

			List<WaterBreakLine.Polyline> lines = WaterBreakLine.Build(grid, 128, 128, Vector2.zero, 1f, 5f, 1.5f, 10f);

			Assert.That(lines.Count, Is.EqualTo(1));
			WaterBreakLine.Polyline loop = lines[0];
			Assert.That(loop.Closed, Is.True);
			AssertClean(loop);
			int n = loop.Points.Count;
			for (int i = 0; i < n; i++)
			{
				// The closing segment too: resampling a loop must not leave a short last step.
				float gap = Vector2.Distance(loop.Points[i], loop.Points[(i + 1) % n]);
				Assert.That(gap, Is.EqualTo(1.5f).Within(0.1f), $"gap {i} of {n}");
				Assert.That(Vector2.Distance(loop.Points[i], centre), Is.EqualTo(35f).Within(0.3f), $"point {i} left the circle");
			}
			Assert.That(SignedArea(loop.Points), Is.GreaterThan(0f), "simplify, smooth and resample kept the direction");
			AssertShallowOnTheLeft(loop, p => Vector2.Distance(p, centre), 0.5f);
		}

		[Test]
		public void AStraightBeachIsOneOpenLineWithTheShallowsOnItsLeft()
		{
			/* Depth 0.2x - 10: dry land west of x = 50, deepening eastward, so the 2 m line is at
			 * x = 60 and the SHALLOW side is west (-x). With the shallows on the left, the line runs
			 * north (+z) from the field's south edge to its north edge. Samples at x = 60 are exactly
			 * the iso value, which counts as deep; the crossings land on them, not beside them. */
			float[] grid = Field(64, 32, (x, z) => x * 0.2f - 10f);

			List<WaterBreakLine.Polyline> lines = WaterBreakLine.Extract(grid, 64, 32, Vector2.zero, 1f, 2f);

			Assert.That(lines.Count, Is.EqualTo(1), "one beach, one line");
			WaterBreakLine.Polyline line = lines[0];
			Assert.That(line.Closed, Is.False, "the beach runs off the field at both ends");
			AssertClean(line);
			float minZ = float.MaxValue, maxZ = float.MinValue;
			foreach (Vector2 p in line.Points)
			{
				Assert.That(p.x, Is.EqualTo(60f).Within(0.05f), $"{p} is off the 2 m line");
				minZ = Mathf.Min(minZ, p.y);
				maxZ = Mathf.Max(maxZ, p.y);
			}
			Assert.That(minZ, Is.EqualTo(0f).Within(1e-4f), "it reaches the south edge");
			Assert.That(maxZ, Is.EqualTo(31f).Within(1e-4f), "and the north edge");

			var deeper = new Vector2(0.2f, 0f); // the depth gradient
			for (int i = 0; i < line.Points.Count - 1; i++)
			{
				Vector2 d = line.Points[i + 1] - line.Points[i];
				Vector2 left = new Vector2(-d.y, d.x);
				Assert.That(Vector2.Dot(left, deeper), Is.LessThan(0f), $"segment {i}'s left normal points out to sea");
			}

			// The same beach placed elsewhere at 2 m a texel: the line moves with the grid's mapping.
			List<WaterBreakLine.Polyline> moved = WaterBreakLine.Extract(grid, 64, 32, new Vector2(100f, -50f), 2f, 2f);
			Assert.That(moved.Count, Is.EqualTo(1));
			foreach (Vector2 p in moved[0].Points)
			{
				Assert.That(p.x, Is.EqualTo(220f).Within(0.1f));
			}
			Assert.That(moved[0].Points[0].y, Is.LessThan(moved[0].Points[moved[0].Points.Count - 1].y), "still running north");
		}

		[Test]
		public void TwoIslandsAreTwoLoopsAndATinyOneIsDroppedByBuild()
		{
			var a = new Vector2(40f, 40f);
			var b = new Vector2(120f, 40f);
			var rock = new Vector2(80f, 20f);
			float[] two = Field(160, 80, (x, z) =>
			{
				var p = new Vector2(x, z);
				return Mathf.Min(Vector2.Distance(p, a) - 15f, Vector2.Distance(p, b) - 15f);
			});

			List<WaterBreakLine.Polyline> lines = WaterBreakLine.Extract(two, 160, 80, Vector2.zero, 1f, 5f);
			Assert.That(lines.Count, Is.EqualTo(2), "two islands, two lines");
			foreach (WaterBreakLine.Polyline line in lines)
			{
				Assert.That(line.Closed, Is.True);
				Assert.That(SignedArea(line.Points), Is.GreaterThan(0f));
				AssertClean(line);
			}

			/* A single shallow sample: a rock whose loop is a diamond a few metres round. Extract
			 * reports it, because it is really there; Build drops it, because a breaker curled round
			 * a rock two texels across reads as a glitch, not surf. */
			float[] three = Field(160, 80, (x, z) =>
			{
				var p = new Vector2(x, z);
				float islands = Mathf.Min(Vector2.Distance(p, a) - 15f, Vector2.Distance(p, b) - 15f);
				return Mathf.Min(islands, Vector2.Distance(p, rock) * 10f - 1f);
			});
			List<WaterBreakLine.Polyline> all = WaterBreakLine.Extract(three, 160, 80, Vector2.zero, 1f, 5f);
			Assert.That(all.Count, Is.EqualTo(3), "the rock is found");
			Assert.That(all.Exists(l => l.Length < 10f), Is.True, "and its loop is short");

			List<WaterBreakLine.Polyline> built = WaterBreakLine.Build(three, 160, 80, Vector2.zero, 1f, 5f, 1f, 10f);
			Assert.That(built.Count, Is.EqualTo(2), "Build keeps the islands and drops the rock");
			foreach (WaterBreakLine.Polyline line in built)
			{
				Assert.That(line.Closed, Is.True);
				Assert.That(line.Length, Is.GreaterThan(100f));
				Assert.That(SignedArea(line.Points), Is.GreaterThan(0f));
			}
		}

		[Test]
		public void ALagoonRunsClockwiseInsideItsIsland()
		{
			/* A ring of shallows between 15 m and 35 m from the centre: an atoll. Its outer shore has
			 * the shallows inside and runs anticlockwise; the lagoon's shore has them OUTSIDE, so with
			 * the shallows still on the left it must run clockwise. Getting only the outer loop right
			 * is what a winding-order bug that happens to suit islands looks like. */
			var centre = new Vector2(64f, 64f);
			Func<Vector2, float> depth = p => Mathf.Abs(Vector2.Distance(p, centre) - 25f) - 10f;
			float[] grid = Field(128, 128, (x, z) => depth(new Vector2(x, z)));

			foreach (bool built in new[] { false, true })
			{
				List<WaterBreakLine.Polyline> lines = built
					? WaterBreakLine.Build(grid, 128, 128, Vector2.zero, 1f, 0f, 1f, 10f)
					: WaterBreakLine.Extract(grid, 128, 128, Vector2.zero, 1f, 0f);
				Assert.That(lines.Count, Is.EqualTo(2), built ? "built" : "extracted");
				int outer = 0, inner = 0;
				foreach (WaterBreakLine.Polyline line in lines)
				{
					Assert.That(line.Closed, Is.True);
					AssertClean(line);
					float radius = Vector2.Distance(line.Points[0], centre);
					if (radius > 25f)
					{
						outer++;
						Assert.That(SignedArea(line.Points), Is.GreaterThan(0f), "the outer shore runs anticlockwise");
					}
					else
					{
						inner++;
						Assert.That(SignedArea(line.Points), Is.LessThan(0f), "the lagoon shore runs clockwise");
					}
					AssertShallowOnTheLeft(line, depth, 0.5f);
				}
				Assert.That(outer, Is.EqualTo(1));
				Assert.That(inner, Is.EqualTo(1));
			}
		}

		[Test]
		public void SaddlesAreResolvedByTheCellCentre()
		{
			/* One cell, two shallow corners diagonally opposite. Averaging the corners gives 5. A
			 * break depth of 4 makes the centre deep, so the two SHALLOW corners are cut off, each on
			 * the left of its piece. A break depth of 6 makes the centre shallow, so the DEEP corners
			 * are cut off, each on the right. Both diagonals, both ways. */
			float[][] cells =
			{
				new[] { 0f, 10f, 10f, 0f }, // (0,0) and (1,1) shallow — case 5
				new[] { 10f, 0f, 0f, 10f }, // (1,0) and (0,1) shallow — case 10
			};
			foreach (float[] grid in cells)
			{
				foreach (float iso in new[] { 4f, 6f })
				{
					bool centreShallow = iso > 5f;
					List<WaterBreakLine.Polyline> lines = WaterBreakLine.Extract(grid, 2, 2, Vector2.zero, 1f, iso);
					Assert.That(lines.Count, Is.EqualTo(2), $"iso {iso}: two pieces");
					foreach (WaterBreakLine.Polyline line in lines)
					{
						Assert.That(line.Closed, Is.False);
						Assert.That(line.Points.Count, Is.EqualTo(2));
						AssertClean(line);
						Vector2 a = line.Points[0];
						Vector2 b = line.Points[1];
						Vector2 middle = (a + b) * 0.5f;
						var corner = new Vector2(Mathf.Round(middle.x), Mathf.Round(middle.y));
						float cornerDepth = grid[(int)corner.y * 2 + (int)corner.x];
						bool cornerShallow = cornerDepth < iso;
						Assert.That(cornerShallow, Is.Not.EqualTo(centreShallow),
							$"iso {iso}: the cut-off corner {corner} should be the kind the centre is not");
						Vector2 d = b - a;
						float side = Vector2.Dot(corner - middle, new Vector2(-d.y, d.x));
						if (cornerShallow)
						{
							Assert.That(side, Is.GreaterThan(0f), $"iso {iso}: shallow corner {corner} is not on the left");
						}
						else
						{
							Assert.That(side, Is.LessThan(0f), $"iso {iso}: deep corner {corner} is not on the right");
						}
					}
				}
			}
		}

		[Test]
		public void ACheckerboardOfSaddlesDoesNotThrowOrRepeatPoints()
		{
			/* Every cell a saddle and every edge a crossing: the worst case for the chaining. Every
			 * crossing must be used exactly once, so the pieces' points add up to the crossing
			 * count — a dropped or doubled segment would show up here. */
			const int size = 6;
			float[] grid = Field(size, size, (x, z) => ((int)x + (int)z) % 2 == 0 ? 0f : 10f);
			foreach (float iso in new[] { 4f, 5f, 6f })
			{
				List<WaterBreakLine.Polyline> lines = WaterBreakLine.Extract(grid, size, size, Vector2.zero, 1f, iso);
				Assert.That(lines.Count, Is.GreaterThan(0));
				int points = 0;
				foreach (WaterBreakLine.Polyline line in lines)
				{
					AssertClean(line);
					points += line.Points.Count;
				}
				Assert.That(points, Is.EqualTo(CrossingEdges(grid, size, size, iso)), $"iso {iso}");
			}
		}

		[Test]
		public void EveryCellOfANoisyFieldKeepsTheShallowCornersOnTheLeft()
		{
			/* The orientation contract checked cell by cell, on noise dense with saddles. In an
			 * ordinary cell a piece separates the corners: every shallow one on its left, every deep
			 * one on its right. In a saddle it cuts one corner off, which must be the kind the centre
			 * is not, and on the side that kind belongs. */
			const int size = 40;
			const float iso = 0.1f;
			var random = new Random(238);
			var grid = new float[size * size];
			for (int i = 0; i < grid.Length; i++)
			{
				float depth = (float)(random.NextDouble() * 2.0 - 1.0);
				/* Kept a millimetre clear of the iso value. A sample a hair from it puts two crossings
				 * a hair apart, which Extract rightly merges — and then the merged segment is checked
				 * against a cell it only grazes, with a corner 1e-6 from the line. That is float
				 * noise, not orientation, and it must not decide this test on another runtime. */
				if (Mathf.Abs(depth - iso) < 1e-3f)
				{
					depth = iso + (depth < iso ? -1e-3f : 1e-3f);
				}
				grid[i] = depth;
			}

			List<WaterBreakLine.Polyline> lines = WaterBreakLine.Extract(grid, size, size, Vector2.zero, 1f, iso);
			int points = 0;
			int saddles = 0;
			foreach (WaterBreakLine.Polyline line in lines)
			{
				AssertClean(line);
				points += line.Points.Count;
				for (int i = 0; i < SegmentCount(line); i++)
				{
					Vector2 a = line.Points[i];
					Vector2 b = line.Points[(i + 1) % line.Points.Count];
					Vector2 middle = (a + b) * 0.5f;
					int cx = Mathf.FloorToInt(middle.x);
					int cy = Mathf.FloorToInt(middle.y);
					int[] xs = { cx, cx + 1, cx + 1, cx };
					int[] ys = { cy, cy, cy + 1, cy + 1 };
					float sum = 0f;
					int shallowCount = 0;
					var left = new bool[4];
					var shallow = new bool[4];
					Vector2 d = b - a;
					for (int k = 0; k < 4; k++)
					{
						float depth = grid[ys[k] * size + xs[k]];
						sum += depth;
						shallow[k] = depth < iso;
						shallowCount += shallow[k] ? 1 : 0;
						float cross = d.x * (ys[k] - a.y) - d.y * (xs[k] - a.x);
						Assert.That(cross, Is.Not.EqualTo(0f), "a corner exactly on a piece");
						left[k] = cross > 0f;
					}
					bool saddle = shallowCount == 2 && shallow[0] == shallow[2];
					if (!saddle)
					{
						for (int k = 0; k < 4; k++)
						{
							Assert.That(left[k], Is.EqualTo(shallow[k]), $"cell ({cx},{cy}) corner {k}");
						}
						continue;
					}
					saddles++;
					bool centreShallow = sum * 0.25f < iso;
					int leftCount = 0;
					for (int k = 0; k < 4; k++)
					{
						leftCount += left[k] ? 1 : 0;
					}
					Assert.That(leftCount == 1 || leftCount == 3, Is.True, $"saddle ({cx},{cy}) should cut one corner off");
					bool loneIsLeft = leftCount == 1;
					for (int k = 0; k < 4; k++)
					{
						if (left[k] == loneIsLeft)
						{
							Assert.That(shallow[k], Is.EqualTo(loneIsLeft), $"saddle ({cx},{cy}): the cut corner is on the wrong side");
							Assert.That(shallow[k], Is.Not.EqualTo(centreShallow), $"saddle ({cx},{cy}): the centre should decide");
						}
					}
				}
			}
			Assert.That(saddles, Is.GreaterThan(0), "the noise should contain saddles, or this checked nothing");
			Assert.That(points, Is.EqualTo(CrossingEdges(grid, size, size, iso)), "every crossing used exactly once");
		}

		[Test]
		public void SamplesExactlyAtTheBreakDepthMakeNoZeroLengthSegments()
		{
			// A whole column exactly at iso: one point per row, on the column, nothing doubled.
			float[] ramp = Field(20, 10, (x, z) => x - 10f);
			List<WaterBreakLine.Polyline> column = WaterBreakLine.Extract(ramp, 20, 10, Vector2.zero, 1f, 5f);
			Assert.That(column.Count, Is.EqualTo(1));
			Assert.That(column[0].Points.Count, Is.EqualTo(10));
			AssertClean(column[0]);
			foreach (Vector2 p in column[0].Points)
			{
				Assert.That(p.x, Is.EqualTo(15f));
			}

			/* A saddle whose far corner is exactly at iso: both of the crossings on its two edges
			 * land on that corner, so the segment between them has no length at all. It must be
			 * merged away, not emitted. */
			float[] corner =
			{
				10f, 0f, 10f,
				0f, 5f, 10f,
				10f, 10f, 10f,
			};
			List<WaterBreakLine.Polyline> pieces = WaterBreakLine.Extract(corner, 3, 3, Vector2.zero, 1f, 5f);
			Assert.That(pieces.Count, Is.EqualTo(2));
			foreach (WaterBreakLine.Polyline piece in pieces)
			{
				AssertClean(piece);
			}

			// A lone shallow sample ringed by samples exactly at iso: a diamond through the four.
			float[] ringed = Field(5, 5, (x, z) => x == 2f && z == 2f ? 0f : 5f);
			List<WaterBreakLine.Polyline> diamond = WaterBreakLine.Extract(ringed, 5, 5, Vector2.zero, 1f, 5f);
			Assert.That(diamond.Count, Is.EqualTo(1));
			Assert.That(diamond[0].Closed, Is.True);
			Assert.That(diamond[0].Points.Count, Is.EqualTo(4));
			AssertClean(diamond[0]);
			Assert.That(SignedArea(diamond[0].Points), Is.EqualTo(2f).Within(1e-5f));

			// Integer noise, a third of it exactly at iso.
			var random = new Random(90);
			float[] noise = Field(48, 48, (x, z) => 4 + random.Next(3));
			foreach (WaterBreakLine.Polyline piece in WaterBreakLine.Extract(noise, 48, 48, Vector2.zero, 1f, 5f))
			{
				AssertClean(piece);
			}
		}

		[Test]
		public void NaNSamplesCountAsDeepAndNeverBecomePoints()
		{
			var centre = new Vector2(64f, 64f);
			var bite = new Vector2(99f, 64f);
			float[] grid = Field(128, 128, (x, z) =>
			{
				var p = new Vector2(x, z);
				// A hole of no data in the middle of the island, and another across the shore itself.
				if (Vector2.Distance(p, centre) < 5f || Vector2.Distance(p, bite) < 3f)
				{
					return float.NaN;
				}
				return Vector2.Distance(p, centre) - 30f;
			});

			List<WaterBreakLine.Polyline> lines = WaterBreakLine.Extract(grid, 128, 128, Vector2.zero, 1f, 5f);
			Assert.That(lines.Count, Is.EqualTo(2), "the shore, and a deep hole where the island has no data");
			int anticlockwise = 0, clockwise = 0;
			foreach (WaterBreakLine.Polyline line in lines)
			{
				Assert.That(line.Closed, Is.True);
				AssertClean(line);
				if (SignedArea(line.Points) > 0f)
				{
					anticlockwise++;
				}
				else
				{
					clockwise++;
				}
			}
			Assert.That(anticlockwise, Is.EqualTo(1), "the shore");
			Assert.That(clockwise, Is.EqualTo(1), "the no-data hole, deep water in the middle of the shallows");

			foreach (WaterBreakLine.Polyline line in WaterBreakLine.Build(grid, 128, 128, Vector2.zero, 1f, 5f, 1.5f, 10f))
			{
				AssertClean(line);
			}
		}

		[Test]
		public void DegenerateGridsGiveNothing()
		{
			float[] deep = Field(16, 16, (x, z) => 10f);
			float[] shallow = Field(16, 16, (x, z) => -10f);
			float[] atIso = Field(16, 16, (x, z) => 5f);
			float[] noData = Field(16, 16, (x, z) => float.NaN);

			Assert.That(WaterBreakLine.Extract(deep, 16, 16, Vector2.zero, 1f, 5f), Is.Empty, "all deep");
			Assert.That(WaterBreakLine.Extract(shallow, 16, 16, Vector2.zero, 1f, 5f), Is.Empty, "all shallow");
			Assert.That(WaterBreakLine.Extract(atIso, 16, 16, Vector2.zero, 1f, 5f), Is.Empty, "all exactly at iso, which is deep");
			Assert.That(WaterBreakLine.Extract(noData, 16, 16, Vector2.zero, 1f, 5f), Is.Empty, "all NaN, which is deep");
			Assert.That(WaterBreakLine.Extract(null, 16, 16, Vector2.zero, 1f, 5f), Is.Empty, "no grid");
			Assert.That(WaterBreakLine.Extract(shallow, 1, 16, Vector2.zero, 1f, 5f), Is.Empty, "one column");
			Assert.That(WaterBreakLine.Extract(shallow, 16, 1, Vector2.zero, 1f, 5f), Is.Empty, "one row");
			Assert.That(WaterBreakLine.Extract(shallow, 17, 16, Vector2.zero, 1f, 5f), Is.Empty, "a grid shorter than it claims");
			Assert.That(WaterBreakLine.Extract(Field(16, 16, (x, z) => x - 8f), 16, 16, Vector2.zero, 0f, 0f), Is.Empty, "no texel size");

			Assert.That(WaterBreakLine.Build(deep, 16, 16, Vector2.zero, 1f, 5f, 1f, 1f), Is.Empty);
			Assert.That(WaterBreakLine.Build(shallow, 16, 16, Vector2.zero, 1f, 5f, 1f, 1f), Is.Empty);
			Assert.That(WaterBreakLine.Build(null, 0, 0, Vector2.zero, 1f, 5f, 1f, 1f), Is.Empty);
		}

		[Test]
		public void ResampleAndSmoothKeepAnOpenLinesEndpoints()
		{
			/* An open piece ends where it runs off the field, and the next field window's piece
			 * starts there: an endpoint that drifts opens a gap in the surf. Compared exactly. */
			var line = new List<Vector2>
			{
				new Vector2(0.1f, 0.2f),
				new Vector2(3.3f, 0.2f),
				new Vector2(3.3f, 4.1f),
				new Vector2(7.7f, 5.3f),
			};

			List<Vector2> resampled = WaterBreakLine.Resample(line, false, 1f);
			Assert.That(resampled[0].x, Is.EqualTo(line[0].x));
			Assert.That(resampled[0].y, Is.EqualTo(line[0].y));
			Assert.That(resampled[resampled.Count - 1].x, Is.EqualTo(line[3].x));
			Assert.That(resampled[resampled.Count - 1].y, Is.EqualTo(line[3].y));

			List<Vector2> smoothed = WaterBreakLine.Smooth(line, false, 3);
			Assert.That(smoothed.Count, Is.GreaterThan(line.Count));
			Assert.That(smoothed[0].x, Is.EqualTo(line[0].x));
			Assert.That(smoothed[0].y, Is.EqualTo(line[0].y));
			Assert.That(smoothed[smoothed.Count - 1].x, Is.EqualTo(line[3].x));
			Assert.That(smoothed[smoothed.Count - 1].y, Is.EqualTo(line[3].y));

			// On a straight line the spacing is exactly even: 10 m at 3 m a step is three steps of 3.33.
			List<Vector2> even = WaterBreakLine.Resample(new List<Vector2> { Vector2.zero, new Vector2(10f, 0f) }, false, 3f);
			Assert.That(even.Count, Is.EqualTo(4));
			for (int i = 0; i < 3; i++)
			{
				Assert.That(even[i + 1].x - even[i].x, Is.EqualTo(10f / 3f).Within(1e-4f));
			}

			// A loop: a whole number of equal steps all the way round, first point kept, nothing repeated.
			var square = new List<Vector2> { Vector2.zero, new Vector2(10f, 0f), new Vector2(10f, 10f), new Vector2(0f, 10f) };
			List<Vector2> round = WaterBreakLine.Resample(square, true, 3f);
			Assert.That(round.Count, Is.EqualTo(13), "40 m at 3 m a step is 13 steps");
			Assert.That(round[0], Is.EqualTo(square[0]));
			Assert.That(round[round.Count - 1], Is.Not.EqualTo(round[0]));
			Assert.That(SignedArea(round), Is.GreaterThan(0f));

			List<Vector2> cut = WaterBreakLine.Smooth(square, true, 1);
			Assert.That(cut.Count, Is.EqualTo(8), "every corner becomes two");
			Assert.That(SignedArea(cut), Is.GreaterThan(0f), "and the loop still runs the same way");
		}

		[Test]
		public void SimplifyKeepsEndpointsAndNeverCollapsesALoop()
		{
			// A marching-squares staircase along a straight diagonal: only the endpoints carry shape.
			var stairs = new List<Vector2>();
			for (int i = 0; i <= 20; i++)
			{
				stairs.Add(new Vector2(i * 0.5f, i * 0.5f + (i % 2 == 0 ? 0f : 0.1f)));
			}
			List<Vector2> simple = WaterBreakLine.Simplify(stairs, false, 0.35f);
			Assert.That(simple.Count, Is.EqualTo(2));
			Assert.That(simple[0], Is.EqualTo(stairs[0]));
			Assert.That(simple[1], Is.EqualTo(stairs[stairs.Count - 1]));

			// A sliver of a loop, thinner than the tolerance: still a loop, still three points, still anticlockwise.
			var sliver = new List<Vector2>
			{
				new Vector2(0f, 0f), new Vector2(5f, -0.05f), new Vector2(10f, 0f),
				new Vector2(10f, 0.1f), new Vector2(5f, 0.15f), new Vector2(0f, 0.1f),
			};
			Assert.That(SignedArea(sliver), Is.GreaterThan(0f));
			List<Vector2> kept = WaterBreakLine.Simplify(sliver, true, 1f);
			Assert.That(kept.Count, Is.EqualTo(3));
			Assert.That(kept[0], Is.EqualTo(sliver[0]));
			Assert.That(SignedArea(kept), Is.GreaterThan(0f));
		}
	}
}
