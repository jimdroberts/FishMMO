using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared.Weather;

namespace FishMMO.UnitTests.Weather
{
	/// <summary>
	/// The shapes a storm covers the ground in: a disc, a front, a hurricane's eye, a tornado.
	/// </summary>
	/// <remarks>
	/// <see cref="StormCell.Coverage"/> is pure — an offset from the centre in, a 0..1 out — so every
	/// shape can be checked without a tick, a timeline or a scene. That matters because the whole
	/// difference between a shower and a weather front lives in that one function.
	/// </remarks>
	[TestFixture]
	public class StormCellShapeTests
	{
		private static StormCell Cell(StormCellShape shape, float radius, float extent, Vector2 velocity = default)
		{
			return new StormCell
			{
				Shape = shape,
				RadiusMeters = radius,
				ExtentMeters = extent,
				VelocityX = velocity.x,
				VelocityZ = velocity.y,
				PeakIntensity = 1f,
			};
		}

		[Test]
		public void ADiscIsUnchangedByAnyOfThis()
		{
			/* Every cell authored before shapes existed deserialises as Disc, so this has to behave
			 * exactly as it always did: full inside 55% of the radius, fading to nothing at the edge. */
			StormCell disc = Cell(StormCellShape.Disc, 100f, 0f);

			Assert.That(disc.Coverage(Vector2.zero), Is.EqualTo(1f).Within(1e-4f), "the middle is the whole of it");
			Assert.That(disc.Coverage(new Vector2(50f, 0f)), Is.EqualTo(1f).Within(1e-4f), "still full at 55% out");
			Assert.That(disc.Coverage(new Vector2(100f, 0f)), Is.EqualTo(0f).Within(1e-4f), "and nothing at the edge");
			Assert.That(disc.Coverage(new Vector2(78f, 0f)), Is.EqualTo(0.5f).Within(0.08f), "half way through the band");

			// Radial: direction cannot matter.
			Assert.That(disc.Coverage(new Vector2(0f, 80f)), Is.EqualTo(disc.Coverage(new Vector2(80f, 0f))).Within(1e-4f));
			Assert.That(disc.Coverage(new Vector2(-80f, 0f)), Is.EqualTo(disc.Coverage(new Vector2(80f, 0f))).Within(1e-4f));
		}

		[Test]
		public void AFrontIsAWallAcrossItsOwnHeading()
		{
			/* The thing that makes a front a front: it is long in one direction and thin in the
			 * other, and the thin direction is the way it is travelling. A cell moving east is a
			 * wall running north-south. */
			StormCell front = Cell(StormCellShape.Front, 200f, 1200f, new Vector2(5f, 0f));

			Assert.That(front.Coverage(Vector2.zero), Is.EqualTo(1f).Within(1e-3f), "on the line, in the middle");
			// Far along the wall (north-south) and still in it.
			Assert.That(front.Coverage(new Vector2(0f, 800f)), Is.GreaterThan(0.9f), "800 m along the line is still the front");
			// The same distance ACROSS it and long gone.
			Assert.That(front.Coverage(new Vector2(800f, 0f)), Is.EqualTo(0f).Within(1e-4f), "800 m ahead of it is clear sky");
		}

		[Test]
		public void AFrontArrivesSuddenlyAndClearsSlowly()
		{
			/* A front is sharp at its leading edge and drags a long tail behind. Symmetric depth
			 * reads as a blob drifting past rather than as weather moving IN, which is the entire
			 * reason to have fronts at all. */
			StormCell front = Cell(StormCellShape.Front, 200f, 1000f, new Vector2(5f, 0f));

			float ahead = front.Coverage(new Vector2(150f, 0f));
			float behind = front.Coverage(new Vector2(-150f, 0f));
			Assert.That(behind, Is.GreaterThan(ahead), "the tail reaches further back than the front does forward");

			/* Measured as the asymmetry itself rather than as a coverage at some chosen distance.
			 * A spot value is a number somebody guessed — the first version of this asserted 0.4 at
			 * 200 m behind, where the geometry gives 0.32, and the test was wrong rather than the
			 * front. How far the weather reaches each way is the property that actually matters. */
			float aheadEdge = DistanceWhereItThins(front, 1f);
			float behindEdge = DistanceWhereItThins(front, -1f);
			Assert.That(behindEdge / aheadEdge, Is.GreaterThan(3f),
				$"the trailing edge should run several times further than the leading one (ahead {aheadEdge:0} m, behind {behindEdge:0} m)");
			Assert.That(front.Coverage(new Vector2(200f, 0f)), Is.LessThan(0.05f), "and it is sharp in front");
		}

		/// <summary>
		/// How far along the heading the front is still worth calling weather: where coverage falls
		/// below a tenth. Bisected, because the falloff is a smoothstep and has no tidy inverse.
		/// </summary>
		private static float DistanceWhereItThins(StormCell cell, float sign)
		{
			float lo = 0f, hi = 5000f;
			for (int i = 0; i < 60; i++)
			{
				float mid = (lo + hi) * 0.5f;
				if (cell.Coverage(cell.Facing * (sign * mid)) > 0.1f)
				{
					lo = mid;
				}
				else
				{
					hi = mid;
				}
			}
			return (lo + hi) * 0.5f;
		}

		[Test]
		public void AFrontTurnsWithItsHeadingWithoutBeingTold()
		{
			/* Its orientation is derived from its velocity, so there is no facing to author, to send
			 * or to get out of step with the direction it is actually moving. Rotate the velocity
			 * and the wall rotates with it. */
			StormCell east = Cell(StormCellShape.Front, 200f, 1000f, new Vector2(5f, 0f));
			StormCell north = Cell(StormCellShape.Front, 200f, 1000f, new Vector2(0f, 5f));

			// A point 600 m north is along the east-moving wall, and ahead of the north-moving one.
			Assert.That(east.Coverage(new Vector2(0f, 600f)), Is.GreaterThan(0.9f));
			Assert.That(north.Coverage(new Vector2(0f, 600f)), Is.EqualTo(0f).Within(1e-4f));

			// And the mirror image holds.
			Assert.That(north.Coverage(new Vector2(600f, 0f)), Is.GreaterThan(0.9f));
			Assert.That(east.Coverage(new Vector2(600f, 0f)), Is.EqualTo(0f).Within(1e-4f));
		}

		[Test]
		public void AStationaryFrontStillHasAFacingRatherThanDividingByZero()
		{
			// A front that is not moving is a contradiction, but content can ask for one.
			StormCell still = Cell(StormCellShape.Front, 100f, 500f, Vector2.zero);
			Assert.That(still.Facing, Is.EqualTo(Vector2.right));
			Assert.That(float.IsNaN(still.Coverage(new Vector2(50f, 50f))), Is.False);
		}

		[Test]
		public void AHurricaneIsCalmInItsEyeAndWorstInTheRing()
		{
			/* The shape a disc cannot express at all: the middle is the quietest place in it, and
			 * the worst of it is a ring some way out. */
			StormCell hurricane = Cell(StormCellShape.Eyewall, 2000f, 300f);

			float eye = hurricane.Coverage(Vector2.zero);
			float eyewall = hurricane.Coverage(new Vector2(300f, 0f));
			float outer = hurricane.Coverage(new Vector2(1500f, 0f));

			Assert.That(eyewall, Is.EqualTo(1f).Within(1e-3f), "the eyewall is the worst of it");
			Assert.That(eye, Is.LessThan(0.2f), "and the eye is calm");
			Assert.That(eye, Is.GreaterThan(0f),
				"but not empty — an eye that reported nothing would snap the sky clear in the middle of a hurricane");
			Assert.That(outer, Is.LessThan(eyewall), "decaying outward from the ring");
			Assert.That(hurricane.Coverage(new Vector2(2000f, 0f)), Is.EqualTo(0f).Within(1e-3f), "and gone at the edge");
		}

		[Test]
		public void AHurricaneWithNoEyeIsStillSane()
		{
			// Extent 0, and the clamp against 60% of the radius, both have to hold.
			StormCell noEye = Cell(StormCellShape.Eyewall, 1000f, 0f);
			Assert.That(noEye.Coverage(Vector2.zero), Is.EqualTo(1f).Within(1e-3f));
			Assert.That(noEye.Coverage(new Vector2(1000f, 0f)), Is.EqualTo(0f).Within(1e-3f));

			StormCell hugeEye = Cell(StormCellShape.Eyewall, 1000f, 9000f);
			Assert.That(float.IsNaN(hugeEye.Coverage(new Vector2(100f, 0f))), Is.False, "an eye bigger than the storm is clamped");
			Assert.That(hugeEye.Coverage(new Vector2(2000f, 0f)), Is.EqualTo(0f).Within(1e-3f), "and it still ends");
		}

		[Test]
		public void ATornadoIsAViolentCoreThatFallsAwayFast()
		{
			StormCell tornado = Cell(StormCellShape.Funnel, 60f, 400f);

			Assert.That(tornado.Coverage(Vector2.zero), Is.EqualTo(1f).Within(1e-4f), "inside the core");
			Assert.That(tornado.Coverage(new Vector2(60f, 0f)), Is.EqualTo(1f).Within(1e-4f), "right to the core's edge");

			// Steeper than a disc over the same span: half way out it is well under half strength.
			float half = tornado.Coverage(new Vector2(230f, 0f));
			Assert.That(half, Is.LessThan(0.35f), "it falls away much faster than a disc would");
			Assert.That(tornado.Coverage(new Vector2(400f, 0f)), Is.EqualTo(0f).Within(1e-4f), "and is gone at its reach");
		}

		[Test]
		public void ThingsThatHappenInAStormHappenWhereTheStormIs()
		{
			/* PointInside places whatever has to HAPPEN somewhere in a cell — lightning today. It
			 * used to be a disc around the centre regardless of shape, which put a squall line's
			 * entire display at one point and struck a hurricane in its eye. */
			StormCell front = Cell(StormCellShape.Front, 200f, 1200f, new Vector2(5f, 0f));
			StormCell hurricane = Cell(StormCellShape.Eyewall, 2000f, 400f);
			StormCell tornado = Cell(StormCellShape.Funnel, 60f, 400f);

			float widestAlong = 0f, widestAcross = 0f, meanAcross = 0f;
			float nearestToEye = float.MaxValue;
			const int Samples = 400;
			for (int i = 0; i < Samples; i++)
			{
				float u = i / (float)Samples;
				float v = (i * 7 % Samples) / (float)Samples;

				// A front spreads along its wall, not across it.
				Vector2 p = front.PointInside(u, v);
				widestAlong = Mathf.Max(widestAlong, Mathf.Abs(p.y));
				widestAcross = Mathf.Max(widestAcross, Mathf.Abs(p.x));
				meanAcross += p.x;

				// A hurricane never strikes its own eye.
				nearestToEye = Mathf.Min(nearestToEye, hurricane.PointInside(u, v).magnitude);

				// A tornado keeps to its core.
				Assert.That(tornado.PointInside(u, v).magnitude, Is.LessThanOrEqualTo(60f + 1e-3f));
			}
			meanAcross /= Samples;

			/* Stated as the SHAPE of the spread rather than as bounds in metres. An earlier version
			 * asserted "across < 150", which was simply the number the old symmetric placement
			 * happened to produce; when the placement was corrected to lean into the front's tail
			 * the test failed on correct behaviour. What actually makes a front a front is that it
			 * is far longer than it is deep, and that its weather trails behind its edge. */
			Assert.That(widestAlong, Is.GreaterThan(600f), "a front's strikes run down its length");
			Assert.That(widestAlong, Is.GreaterThan(widestAcross * 4f),
				$"a wall is much longer than it is deep (along {widestAlong:0} m, across {widestAcross:0} m)");
			Assert.That(meanAcross, Is.LessThan(0f),
				"and they sit behind its leading edge, in the tail, not out in front of it");
			Assert.That(nearestToEye, Is.GreaterThanOrEqualTo(400f - 1f), "nothing happens inside the eye");

			// Everything placed must actually be in the weather it was placed in.
			foreach (StormCell cell in new[] { front, hurricane, tornado })
			{
				for (int i = 0; i < 200; i++)
				{
					Vector2 at = cell.PointInside(i / 200f, (i * 13 % 200) / 200f);
					Assert.That(cell.Coverage(at), Is.GreaterThan(0f),
						$"{cell.Shape} placed something at {at} where it has no weather");
				}
			}
		}

		[Test]
		public void EveryShapeStaysBetweenZeroAndOneEverywhere()
		{
			/* Coverage multiplies the envelope and the peak, so anything above 1 would push a cell
			 * past its own authored intensity and anything below 0 would subtract weather from the
			 * biome underneath it. Swept rather than spot-checked because the front's two different
			 * reaches and the eyewall's two branches are exactly where that could slip. */
			StormCell[] cells =
			{
				Cell(StormCellShape.Disc, 300f, 0f),
				Cell(StormCellShape.Front, 200f, 900f, new Vector2(3f, 4f)),
				Cell(StormCellShape.Eyewall, 1500f, 250f),
				Cell(StormCellShape.Funnel, 50f, 350f),
			};

			foreach (StormCell cell in cells)
			{
				for (int x = -2000; x <= 2000; x += 50)
				{
					for (int z = -2000; z <= 2000; z += 50)
					{
						float c = cell.Coverage(new Vector2(x, z));
						Assert.That(c, Is.InRange(0f, 1f), $"{cell.Shape} at ({x},{z}) gave {c}");
						Assert.That(float.IsNaN(c), Is.False, $"{cell.Shape} at ({x},{z}) gave NaN");
					}
				}
			}
		}

		[Test]
		public void ReachIsBigEnoughToHoldTheShape()
		{
			/* Reach is what the director's scene-edge test and any culling use. A front reaches far
			 * further along its line than across it, and answering with the radius would retire a
			 * front as soon as its CENTRE neared the edge, with most of the wall still inside. */
			StormCell front = Cell(StormCellShape.Front, 200f, 1200f, new Vector2(5f, 0f));
			StormCell tornado = Cell(StormCellShape.Funnel, 60f, 400f);
			StormCell disc = Cell(StormCellShape.Disc, 300f, 0f);

			Assert.That(front.ReachMeters, Is.GreaterThanOrEqualTo(1200f), "a front reaches its whole length");
			Assert.That(tornado.ReachMeters, Is.GreaterThanOrEqualTo(400f), "a tornado reaches its outer edge");
			Assert.That(disc.ReachMeters, Is.EqualTo(300f), "and a disc is just its radius");

			// Nothing may have coverage beyond its stated reach, or culling would clip live weather.
			foreach (StormCell cell in new[] { front, tornado, disc })
			{
				float beyond = cell.ReachMeters * 1.05f + 1f;
				for (int degrees = 0; degrees < 360; degrees += 15)
				{
					float r = degrees * Mathf.Deg2Rad;
					var at = new Vector2(Mathf.Cos(r), Mathf.Sin(r)) * beyond;
					Assert.That(cell.Coverage(at), Is.EqualTo(0f).Within(1e-4f),
						$"{cell.Shape} still covers ground past its own reach at {degrees}°");
				}
			}
		}
	}
}
