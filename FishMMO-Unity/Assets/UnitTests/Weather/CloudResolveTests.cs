using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Client;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests.Weather
{
	/// <summary>
	/// The clouds' steadying (FishCloudResolve.hlsl), a temporal upsampler, through its C# twin
	/// (<see cref="CloudResolveTwin"/>) and the places the march looks through
	/// (<see cref="FishCloudsFeature.SubPixelPlace"/>): the current estimate's kernel, the variance clip,
	/// the moving average, and the reprojection of a cloud carried by its own band's drift.
	/// </summary>
	/// <remarks>
	/// Issue #238, 2026-09-28: in Jim's editor, on a real GPU with the camera moving, the steadying that
	/// handed each sample only to the pixel it fell in showed a crosshatch and dark blocky specks under an
	/// overcast (single rays, history dropped), and long smears along the wind at night (the middle and
	/// high bands' history reprojected with the low band's drift). These hold the replacement to the
	/// standard design's properties.
	/// </remarks>
	[TestFixture]
	public class CloudResolveTests
	{
		// ── The places ──────────────────────────────────────────────────

		[Test]
		public void EachCycleLooksThroughEveryCellsMiddleOnce()
		{
			int side = FishCloudsFeature.SubPixelSide;
			Assert.AreEqual(FishCloudsFeature.SubPixelPlaces, side * side);
			for (int cycle = 0; cycle < 64; cycle++)
			{
				var cells = new HashSet<Vector2Int>();
				for (int frame = cycle * 16; frame < cycle * 16 + 16; frame++)
				{
					Vector2 place = FishCloudsFeature.SubPixelPlace(FishCloudsFeature.SubPixelSlot(frame));
					LogAssert.IsTrue(place.x > -0.5f && place.x < 0.5f && place.y > -0.5f && place.y < 0.5f, $"frame {frame}: {place} is not inside the texel");
					float cellX = (place.x + 0.5f) * side - 0.5f;
					float cellY = (place.y + 0.5f) * side - 0.5f;
					Assert.AreEqual(Mathf.Round(cellX), cellX, 1e-6f, "a place is a cell's middle");
					Assert.AreEqual(Mathf.Round(cellY), cellY, 1e-6f, "a place is a cell's middle");
					cells.Add(new Vector2Int(Mathf.RoundToInt(cellX), Mathf.RoundToInt(cellY)));
				}
				Assert.AreEqual(16, cells.Count, $"cycle {cycle} looked through a cell twice");
			}
		}

		[Test]
		public void TheFirstFramesOfACycleAreSpreadOverTheTexel()
		{
			// The Bayer order: the first four places one in each quarter of the texel, so the first frames
			// after a reset are not all in one corner.
			var quarters = new HashSet<Vector2Int>();
			for (int slot = 0; slot < 4; slot++)
			{
				Vector2 place = FishCloudsFeature.SubPixelPlace(slot);
				quarters.Add(new Vector2Int(place.x < 0f ? 0 : 1, place.y < 0f ? 0 : 1));
			}
			Assert.AreEqual(4, quarters.Count);
		}

		[Test]
		public void EveryPixelHasAPlaceHoweverFewPixelsATexelSpans()
		{
			// High rebuilds at 3⅓ pixels a texel where the rebuild meets the screen; rounding makes 3.99
			// and the like. Every pixel must still be looked through at least once a cycle.
			foreach (float per in new[] { 1f, 2f, 2.5f, 3f, 10f / 3f, 3.994f, 4f })
			{
				int pixels = Mathf.FloorToInt(per * 7f);
				var hits = new int[pixels, pixels];
				for (int texelY = 0; texelY < 7; texelY++)
				{
					for (int texelX = 0; texelX < 7; texelX++)
					{
						for (int slot = 0; slot < 16; slot++)
						{
							Vector2 place = FishCloudsFeature.SubPixelPlace(slot);
							int px = Mathf.FloorToInt((texelX + 0.5f + place.x) * per);
							int py = Mathf.FloorToInt((texelY + 0.5f + place.y) * per);
							if (px < pixels && py < pixels)
							{
								hits[py, px]++;
							}
						}
					}
				}
				foreach (int count in hits)
				{
					LogAssert.IsTrue(count >= 1, $"{per:0.###} pixels a texel: a pixel never looked through");
				}
			}
		}

		[Test]
		public void TheMarchIsNeverSpreadOverMoreThanFourPixelsATexel()
		{
			foreach (float share in new[] { 0.17f, 0.25f, 0.3f })
			{
				for (int screen = 480; screen <= 4096; screen += 7)
				{
					int rebuilt = Mathf.Max(16, Mathf.RoundToInt(screen * Mathf.Min(1f, share * CloudTierSettings.AutoPixelsPerTexel)));
					int asked = Mathf.Max(16, Mathf.RoundToInt(screen * share));
					int march = FishCloudsFeature.MarchTexelsFor(rebuilt, asked);
					LogAssert.IsTrue((float)rebuilt / march <= FishCloudsFeature.SubPixelSide + 1e-5f, $"{rebuilt} pixels over {march} texels");
					LogAssert.IsTrue(march >= asked, "never fewer rays than the tier asked for");
					LogAssert.IsTrue(march <= asked + 1, $"only rounding moves it: {asked} asked, {march} marched");
				}
			}
		}

		// ── 1. The current estimate's kernel ────────────────────────────

		[Test]
		public void TheKernelsWeightsAreNeverNegativeAndSumToOneWhereverThePixelSits()
		{
			foreach (bool bSpline in new[] { false, true })
			{
				for (float fx = 0f; fx < 1f; fx += 0.03125f)
				{
					for (float fy = 0f; fy < 1f; fy += 0.0625f)
					{
						// The pixel at (n + f) on the grid; texel n + d's ray went through n + d + ½.
						float sum = 0f;
						for (int dy = -1; dy <= 1; dy++)
						{
							for (int dx = -1; dx <= 1; dx++)
							{
								float w = CloudResolveTwin.Kernel(new Vector2(dx + 0.5f - fx, dy + 0.5f - fy), bSpline);
								LogAssert.IsTrue(w >= 0f, $"negative weight {w} at f ({fx}, {fy})");
								sum += w;
							}
						}
						Assert.AreEqual(1f, sum, 1e-5f, $"{(bSpline ? "B-spline" : "tent")} at f ({fx}, {fy})");
					}
				}
			}
		}

		[Test]
		public void TheTentHasNoSeamWhereTheNearestTexelChanges()
		{
			// Crossing a texel's middle the nearest changes and the three-by-three shifts by one: the weight
			// on each texel must be the same from either side.
			for (float at = 0.4f; at <= 0.6f; at += 0.05f)
			{
				float before = CloudResolveTwin.Tent(at - 1e-4f);
				float after = CloudResolveTwin.Tent(at + 1e-4f);
				Assert.AreEqual(before, after, 1e-3f);
			}
			Assert.AreEqual(0f, CloudResolveTwin.Tent(1f), 1e-7f);
			Assert.AreEqual(0f, CloudResolveTwin.Tent(1.5f), 1e-7f);
		}

		// ── 3. The variance clip ────────────────────────────────────────

		[Test]
		public void TheColourSpaceRoundTripsExactly()
		{
			var random = new System.Random(238);
			for (int i = 0; i < 200; i++)
			{
				var value = new Vector4((float)random.NextDouble(), (float)random.NextDouble(), (float)random.NextDouble(), (float)random.NextDouble());
				Vector4 back = CloudResolveTwin.FromYCoCg(CloudResolveTwin.ToYCoCg(value));
				Assert.AreEqual(0f, (back - value).magnitude, 1e-5f);
			}
		}

		[Test]
		public void TheClipIsIdentityOnAConstantField()
		{
			var constant = new Vector4(0.31f, 0.34f, 0.4f, 0.55f);
			var neighbours = new List<Vector4>();
			for (int i = 0; i < 9; i++)
			{
				neighbours.Add(constant);
			}
			Vector4 kept = CloudResolveTwin.Rectify(constant, neighbours, out float units);
			Assert.AreEqual(0f, (kept - constant).magnitude, 1e-6f, "a history that is the field is left alone");
			Assert.AreEqual(0f, units, 1e-4f);
			Assert.AreEqual(1f, CloudResolveTwin.Keeps(units), 1e-6f, "and keeps all its frames");
			// A history of a sky that has gone is pulled onto it, and let go of.
			Vector4 stale = CloudResolveTwin.Rectify(new Vector4(0f, 0f, 0f, 1f), neighbours, out float staleUnits);
			LogAssert.IsTrue((stale - constant).magnitude < 0.01f, $"a stale history stayed {(stale - constant).magnitude:0.0000} off");
			Assert.AreEqual(0f, CloudResolveTwin.Keeps(staleUnits), 1e-6f, "and is started over");
		}

		[Test]
		public void TheClipOnlyEverPullsTowardTheMeanAlongTheLine()
		{
			var random = new System.Random(7);
			var mean = new Vector4(0.4f, 0.1f, -0.05f, 0.5f);
			var extent = new Vector4(0.05f, 0.02f, 0.02f, 0.1f);
			for (int i = 0; i < 500; i++)
			{
				var history = mean + new Vector4((float)random.NextDouble() - 0.5f, (float)random.NextDouble() - 0.5f,
					(float)random.NextDouble() - 0.5f, (float)random.NextDouble() - 0.5f);
				Vector4 clipped = CloudResolveTwin.Clip(history, mean, extent, out float units);
				LogAssert.IsTrue((clipped - mean).magnitude <= (history - mean).magnitude + 1e-6f, "the clip moved a history away from the mean");
				for (int c = 0; c < 4; c++)
				{
					LogAssert.IsTrue(Mathf.Abs(clipped[c] - mean[c]) <= extent[c] + 1e-5f, "a clipped history is outside the box");
				}
				// On the segment from the mean to the history: the offsets are parallel.
				Vector4 a = history - mean, b = clipped - mean;
				float cos = Vector4.Dot(a, b) / Mathf.Max(1e-9f, a.magnitude * b.magnitude);
				LogAssert.IsTrue(b.magnitude < 1e-6f || cos > 0.99999f, "clipped off the line to the mean");
				if (units <= 1f)
				{
					Assert.AreEqual(0f, (clipped - history).magnitude, 1e-7f, "inside the box it is untouched");
				}
			}
		}

		// ── 4. The moving average ──────────────────────────────────────

		[Test]
		public void TheAverageConvergesOnTheMeanOfANoisyConstantWithinACycleOrTwo()
		{
			const float truth = 0.5f, noise = 0.03f;
			const int trials = 400, frames = 48;
			var random = new System.Random(11);
			double sumError = 0.0, sumSquares = 0.0;
			for (int t = 0; t < trials; t++)
			{
				float history = 0f, held = 0f;
				bool usable = false;
				for (int f = 0; f < frames; f++)
				{
					// This frame's nine texels round the pixel, and the tent's estimate from four of them.
					var texels = new float[9];
					float mean = 0f;
					for (int k = 0; k < 9; k++)
					{
						texels[k] = truth + noise * Gaussian(random);
						mean += texels[k] / 9f;
					}
					float variance = 0f;
					for (int k = 0; k < 9; k++)
					{
						variance += (texels[k] - mean) * (texels[k] - mean) / 9f;
					}
					float current = 0.25f * (texels[4] + texels[5] + texels[7] + texels[8]);
					history = CloudResolveTwin.Step(history, held, usable, current, mean, Mathf.Sqrt(variance), FishCloudsFeature.SettledFrames, out held);
					usable = true;
				}
				sumError += history - truth;
				sumSquares += (history - truth) * (history - truth);
			}
			float bias = (float)(sumError / trials);
			float rms = Mathf.Sqrt((float)(sumSquares / trials));
			LogAssert.IsTrue(Mathf.Abs(bias) < 0.002f, $"biased: {bias:0.00000}");
			// A single ray is 0.03 off; the tent's four are 0.015; the average of sixteen frames of them far less.
			LogAssert.IsTrue(rms < noise * 0.2f, $"not settled: {rms:0.00000} against a ray's {noise}");
		}

		[Test]
		public void OnlyNoiseThatDiffersFromTexelToTexelIsAveragedAway()
		{
			// The clip holds a history to this frame's nine texels round the pixel. A noise that differs
			// from texel to texel widens that box and is averaged away; a noise all nine share moves the
			// box with it, and the history — which had averaged it — is clipped back onto it every frame,
			// so it comes through nearly whole. That is why the light march's phase is each pixel's own
			// (FishCloudVolume.hlsl; until 2026-09-29 it was one phase for the whole screen, whose error
			// every texel shared: flicker and sparkle on bright, smooth cloud).
			float shared = SharedNoise(0.02f, 0.0005f);
			float own = SharedNoise(0f, 0.02f);
			LogAssert.IsTrue(shared > 0.5f * 0.02f, $"a noise of 0.02 all nine texels share comes through at {shared:0.0000} rms");
			LogAssert.IsTrue(own < 0.25f * shared, $"the same noise texel by texel is left at {own:0.0000} rms");
			// And a clip that is noise, not change, no longer lets go of the history (the reset threshold):
			// a little better where some of the noise is shared, never worse.
			float gated = SharedNoise(0.006f, 0.004f);
			float everyClip = SharedNoise(0.006f, 0.004f, 0f);
			LogAssert.IsTrue(gated <= everyClip, $"{gated:0.00000} rms kept, against {everyClip:0.00000} when every hard clip let go");
		}

		// The rms a pixel of bright, smooth cloud (0.8) is left off after 48–64 frames of a noise of
		// `shared` all nine texels share plus `own` each texel's own, with a history reset threshold.
		private static float SharedNoise(float shared, float own, float threshold = CloudResolveTwin.ResetChange)
		{
			const float truth = 0.8f;
			var random = new System.Random(5);
			double squares = 0.0;
			int count = 0;
			for (int trial = 0; trial < 200; trial++)
			{
				float history = 0f, held = 0f;
				bool usable = false;
				for (int f = 0; f < 64; f++)
				{
					float offset = shared * Gaussian(random);
					float mean = 0f, variance = 0f;
					var texels = new float[9];
					for (int k = 0; k < 9; k++)
					{
						texels[k] = truth + offset + own * Gaussian(random);
						mean += texels[k] / 9f;
					}
					for (int k = 0; k < 9; k++)
					{
						variance += (texels[k] - mean) * (texels[k] - mean) / 9f;
					}
					float current = 0.25f * (texels[4] + texels[5] + texels[7] + texels[8]);
					history = CloudResolveTwin.Step(history, held, usable, current, mean, Mathf.Sqrt(variance), FishCloudsFeature.SettledFrames, out held, threshold);
					usable = true;
					if (f >= 48)
					{
						squares += (history - truth) * (history - truth);
						count++;
					}
				}
			}
			return (float)System.Math.Sqrt(squares / count);
		}

		[Test]
		public void APixelWithNothingBehindItTakesThisFrameWhole()
		{
			float value = CloudResolveTwin.Step(0.9f, 0f, false, 0.2f, 0.2f, 0f, FishCloudsFeature.SettledFrames, out float frames);
			Assert.AreEqual(0.2f, value, 1e-6f);
			Assert.AreEqual(1f, frames, 1e-6f);
			Vector4 blended = CloudResolveTwin.Blend(Vector4.one, 0f, Vector4.zero, FishCloudsFeature.SettledFrames, out float after);
			Assert.AreEqual(0f, blended.magnitude, 1e-6f, "no frames behind: α = 1");
			Assert.AreEqual(1f, after, 1e-6f);
			CloudResolveTwin.Blend(Vector4.one, 1000f, Vector4.zero, FishCloudsFeature.SettledFrames, out float capped);
			Assert.AreEqual(FishCloudsFeature.SettledFrames, capped, 1e-6f, "settled at one part in sixteen");
		}

		// ── 2. The reprojection ────────────────────────────────────────

		[Test]
		public void ACloudCarriedByItsOwnBandIsFetchedFromWhereItWas()
		{
			Matrix4x4 projection = Matrix4x4.Perspective(60f, 16f / 9f, 0.3f, 1000f);
			var cameraBefore = new Vector3(10f, 2f, -4f);
			var cameraNow = new Vector3(11.5f, 2.1f, -3.2f);
			Matrix4x4 viewBefore = View(cameraBefore, Quaternion.Euler(-20f, 30f, 0f));
			Matrix4x4 viewNow = View(cameraNow, Quaternion.Euler(-21f, 31.5f, 0f));
			Matrix4x4 before = projection * viewBefore;
			Matrix4x4 now = projection * viewNow;
			// The low clouds moved 12 m east and 5 m south this frame; the middle band rides 2.5 times that.
			var lowStep = new Vector2(12f, -5f);
			const float gain = 2.5f;
			foreach (Vector3 cloudBefore in new[] { new Vector3(900f, 4000f, 6000f), new Vector3(-500f, 7800f, 9000f), new Vector3(3000f, 1200f, 4000f) })
			{
				Vector3 cloudNow = cloudBefore + gain * new Vector3(lowStep.x, 0f, lowStep.y);
				Assert.IsTrue(CloudResolveTwin.ScreenOf(now, new Vector4(cloudNow.x, cloudNow.y, cloudNow.z, 1f), out Vector2 uvNow), "in view now");
				Assert.IsTrue(CloudResolveTwin.ScreenOf(before, new Vector4(cloudBefore.x, cloudBefore.y, cloudBefore.z, 1f), out Vector2 expected), "in view before");
				// The pixel sees it down its own ray at the distance the march handed back.
				Vector3 ray = CloudResolveTwin.RayFor(now.inverse, uvNow, cameraNow);
				float distance = (cloudNow - cameraNow).magnitude;
				Vector3 was = CloudResolveTwin.CloudWas(cameraNow, ray, distance, gain, lowStep);
				CloudResolveTwin.ScreenOf(before, new Vector4(was.x, was.y, was.z, 1f), out Vector2 fetched);
				LogAssert.IsTrue((fetched - expected).magnitude * 1080f < 0.05f, $"fetched {(fetched - expected).magnitude * 1080f:0.000} px from where the cloud was");
				// Carried by the low band's drift alone (the old reprojection), it is fetched from elsewhere.
				Vector3 wrong = CloudResolveTwin.CloudWas(cameraNow, ray, distance, 1f, lowStep);
				CloudResolveTwin.ScreenOf(before, new Vector4(wrong.x, wrong.y, wrong.z, 1f), out Vector2 smeared);
				LogAssert.IsTrue((smeared - expected).magnitude > (fetched - expected).magnitude + 1e-4f, "the band's own gain should matter");
			}
		}

		[Test]
		public void OpenSkyIsTheFarPlaneThatOnlyTheCameraTurnMoves()
		{
			Matrix4x4 projection = Matrix4x4.Perspective(60f, 16f / 9f, 0.3f, 1000f);
			Matrix4x4 before = projection * View(new Vector3(0f, 2f, 0f), Quaternion.Euler(-15f, 10f, 0f));
			var cameraNow = new Vector3(40f, 2f, 25f);
			Matrix4x4 now = projection * View(cameraNow, Quaternion.Euler(-16f, 12f, 0f));
			var direction = Quaternion.Euler(-30f, 20f, 0f) * Vector3.forward;
			CloudResolveTwin.ScreenOf(now, new Vector4(direction.x, direction.y, direction.z, 0f), out Vector2 uvNow);
			Vector3 ray = CloudResolveTwin.RayFor(now.inverse, uvNow, cameraNow);
			Assert.AreEqual(0f, (ray - direction).magnitude, 1e-4f, "the pixel's ray is the direction");
			CloudResolveTwin.ScreenOf(before, new Vector4(ray.x, ray.y, ray.z, 0f), out Vector2 fetched);
			CloudResolveTwin.ScreenOf(before, new Vector4(direction.x, direction.y, direction.z, 0f), out Vector2 expected);
			Assert.AreEqual(0f, (fetched - expected).magnitude, 1e-5f, "a camera that walks does not move the far plane");
		}

		// ── The whole steadying on a still ──────────────────────────────

		[Test]
		public void AStillConvergesOnTheSkyFilteredByTheTent()
		{
			// No noise, a smooth sky: after a few cycles every pixel is the average of the tent over the
			// sixteen places, which is the sky filtered by the tent and nothing of the grid.
			var bed = new Bed(8, 0f, 1);
			bed.Fill((x, y) => 0.2f + 0.6f * (0.5f + 0.5f * Mathf.Sin(x / 5f) * Mathf.Cos(y / 7f)));
			bed.Run(16 * 8);
			LogAssert.IsTrue(bed.RmsFromConverged() < 0.004f, $"still {bed.RmsFromConverged():0.00000} from the tent's picture");
		}

		[Test]
		public void ANoisyConstantSkyStaysUnbiasedAndSettles()
		{
			const float noise = 0.03f;
			var bed = new Bed(8, noise, 2);
			bed.Fill((x, y) => 0.5f);
			bed.Run(16 * 8);
			// Every pixel shares the same sixty-four noisy rays, so the image mean wanders by about 0.001 (the
			// Python copy of this bed, six seeds: −0.0009..+0.0019): four of those is bias.
			LogAssert.IsTrue(Mathf.Abs(bed.Mean() - 0.5f) < 0.004f, $"biased: mean {bed.Mean():0.0000}");
			LogAssert.IsTrue(bed.RmsFromConverged() < noise * 0.2f, $"not settled: {bed.RmsFromConverged():0.0000} against noise {noise}");
		}

		[Test]
		public void AChangedSkyIsTakenUpAtOnce()
		{
			var bed = new Bed(8, 0f, 4);
			bed.Fill((x, y) => 0.2f);
			bed.Run(48);
			bed.Fill((x, y) => 0.8f);
			bed.Run(1);
			LogAssert.IsTrue(bed.LargestFromConverged() < 0.01f, $"a frame after the sky changed: {bed.LargestFromConverged():0.0000} off");
		}

		[Test]
		public void AMovedEdgeIsFollowedWithoutAGhost()
		{
			// A hard edge that moved where the reprojection could not follow it: the clip holds each
			// history to what this frame's texels span, and the moving average does the rest within three
			// cycles. MEASURED on a Python copy of this bed: 0.19 after 8 frames, 0.11 after 16, 0.010 after 48.
			var bed = new Bed(8, 0f, 5);
			bed.Fill((x, y) => x < 13 ? 1f : 0f);
			bed.Run(48);
			bed.Fill((x, y) => x < 17 ? 1f : 0f);
			bed.Run(48);
			LogAssert.IsTrue(bed.RmsFromConverged() < 0.03f, $"still {bed.RmsFromConverged():0.000} off three cycles on");
		}

		[Test]
		public void APixelSettlesAtSixteenFrames()
		{
			var bed = new Bed(8, 0f, 6);
			bed.Fill((x, y) => 0.5f);
			bed.Run(1);
			Assert.AreEqual(1f, bed.MeanFrames(), 1e-5f, "the first frame is taken whole");
			bed.Run(40);
			Assert.AreEqual(FishCloudsFeature.SettledFrames, bed.MeanFrames(), 1e-4f);
		}

		// ── The bed ─────────────────────────────────────────────────────

		private static float Gaussian(System.Random random)
		{
			double u = 1.0 - random.NextDouble();
			double v = random.NextDouble();
			return (float)(System.Math.Sqrt(-2.0 * System.Math.Log(u)) * System.Math.Cos(2.0 * System.Math.PI * v));
		}

		/// <summary>A world-to-camera matrix in Unity's convention (camera looks down −z), as Camera.worldToCameraMatrix.</summary>
		private static Matrix4x4 View(Vector3 position, Quaternion rotation) =>
			Matrix4x4.Scale(new Vector3(1f, 1f, -1f)) * Matrix4x4.TRS(position, rotation, Vector3.one).inverse;

		/// <summary>
		/// A square of marched texels, four rebuilt pixels a texel, a still camera and one surface: the march
		/// looks through this frame's place in every texel and reads the sky at the pixel it lands in (plus
		/// Gaussian noise), and every pixel is steadied as FishCloudResolve does it: the tent's estimate of
		/// its three-by-three, the clip to their mean ± γσ, the moving average.
		/// </summary>
		private sealed class Bed
		{
			private const int Per = 4;
			private readonly int texels;
			private readonly int size;
			private readonly float noise;
			private readonly System.Random random;
			private float[,] field;
			private float[,] history;
			private float[,] frames;
			private bool usable;
			private int frame;

			public Bed(int texels, float noise, int seed)
			{
				this.texels = texels;
				size = texels * Per;
				this.noise = noise;
				random = new System.Random(seed);
				field = new float[size, size];
				history = new float[size, size];
				frames = new float[size, size];
			}

			public void Fill(System.Func<int, int, float> at)
			{
				for (int y = 0; y < size; y++)
				{
					for (int x = 0; x < size; x++)
					{
						field[y, x] = at(x, y);
					}
				}
			}

			public void Run(int count)
			{
				for (int i = 0; i < count; i++)
				{
					Step(true);
				}
			}

			/// <summary>One frame's march at <paramref name="slot"/>'s place, noisy or not.</summary>
			private float[,] March(int slot, bool noisy, out Vector2 place)
			{
				place = FishCloudsFeature.SubPixelPlace(slot);
				var marched = new float[texels, texels];
				for (int ty = 0; ty < texels; ty++)
				{
					for (int tx = 0; tx < texels; tx++)
					{
						int lx = Mathf.Clamp(Mathf.FloorToInt((tx + 0.5f + place.x) * Per), 0, size - 1);
						int ly = Mathf.Clamp(Mathf.FloorToInt((ty + 0.5f + place.y) * Per), 0, size - 1);
						marched[ty, tx] = field[ly, lx] + (noisy ? noise * Gaussian(random) : 0f);
					}
				}
				return marched;
			}

			/// <summary>The tent's estimate at a pixel, and the mean and spread of its three-by-three.</summary>
			private float Estimate(float[,] marched, Vector2 place, int px, int py, out float mean, out float sigma)
			{
				float gridX = (px + 0.5f) / Per - place.x;
				float gridY = (py + 0.5f) / Per - place.y;
				int nearestX = Mathf.FloorToInt(gridX);
				int nearestY = Mathf.FloorToInt(gridY);
				float current = 0f, weight = 0f, sum = 0f, squares = 0f;
				for (int dy = -1; dy <= 1; dy++)
				{
					for (int dx = -1; dx <= 1; dx++)
					{
						int tx = Mathf.Clamp(nearestX + dx, 0, texels - 1);
						int ty = Mathf.Clamp(nearestY + dy, 0, texels - 1);
						float value = marched[ty, tx];
						float w = CloudResolveTwin.Kernel(new Vector2(tx + 0.5f - gridX, ty + 0.5f - gridY), false);
						current += value * w;
						weight += w;
						sum += value;
						squares += value * value;
					}
				}
				mean = sum / 9f;
				sigma = Mathf.Sqrt(Mathf.Max(0f, squares / 9f - mean * mean));
				return current / weight;
			}

			private void Step(bool noisy)
			{
				float[,] marched = March(FishCloudsFeature.SubPixelSlot(frame), noisy, out Vector2 place);
				var nextHistory = new float[size, size];
				var nextFrames = new float[size, size];
				for (int py = 0; py < size; py++)
				{
					for (int px = 0; px < size; px++)
					{
						float current = Estimate(marched, place, px, py, out float mean, out float sigma);
						nextHistory[py, px] = CloudResolveTwin.Step(history[py, px], frames[py, px], usable, current, mean, sigma,
							FishCloudsFeature.SettledFrames, out float held);
						nextFrames[py, px] = held;
					}
				}
				history = nextHistory;
				frames = nextFrames;
				usable = true;
				frame++;
			}

			/// <summary>What a still converges on: the noise-free tent estimate averaged over the sixteen places.</summary>
			private float[,] Converged()
			{
				var mean = new float[size, size];
				for (int slot = 0; slot < FishCloudsFeature.SubPixelPlaces; slot++)
				{
					float[,] marched = March(slot, false, out Vector2 place);
					for (int py = 0; py < size; py++)
					{
						for (int px = 0; px < size; px++)
						{
							mean[py, px] += Estimate(marched, place, px, py, out _, out _) / FishCloudsFeature.SubPixelPlaces;
						}
					}
				}
				return mean;
			}

			/// <summary>RMS from the converged picture, over the interior (the edge texels are repeated).</summary>
			public float RmsFromConverged()
			{
				float[,] target = Converged();
				double sum = 0.0;
				int count = 0;
				for (int y = Per * 2; y < size - Per * 2; y++)
				{
					for (int x = Per * 2; x < size - Per * 2; x++)
					{
						double d = history[y, x] - target[y, x];
						sum += d * d;
						count++;
					}
				}
				return (float)System.Math.Sqrt(sum / count);
			}

			public float LargestFromConverged()
			{
				float[,] target = Converged();
				float largest = 0f;
				for (int y = Per * 2; y < size - Per * 2; y++)
				{
					for (int x = Per * 2; x < size - Per * 2; x++)
					{
						largest = Mathf.Max(largest, Mathf.Abs(history[y, x] - target[y, x]));
					}
				}
				return largest;
			}

			public float Mean() => Average(history);

			public float MeanFrames() => Average(frames);

			private static float Average(float[,] values)
			{
				double sum = 0.0;
				foreach (float v in values)
				{
					sum += v;
				}
				return (float)(sum / values.Length);
			}
		}
	}
}
