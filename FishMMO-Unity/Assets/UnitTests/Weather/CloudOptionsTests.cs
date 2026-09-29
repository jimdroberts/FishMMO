using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Client;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests.Weather
{
	/// <summary>
	/// The cloud reconstruction options under trial (<see cref="CloudOptions"/>, issue #238's "spotty
	/// towers"): how the probes name them, that off is exactly what was, and the shapes of the pure
	/// arithmetic the shaders twin — the fill kernel, the blur's width, the jitter's phase. Shapes and
	/// ranges, not spot values.
	/// </summary>
	[TestFixture]
	public class CloudOptionsTests
	{
		// ── Naming them ─────────────────────────────────────────────────

		[Test]
		public void NothingNamedIsEveryOptionOff()
		{
			foreach (string list in new[] { null, "", "   ", ",;" })
			{
				CloudOptions options = CloudOptions.Parse(list, out string ignored);
				Assert.IsFalse(options.Any, $"'{list}' turned something on");
				Assert.AreEqual(string.Empty, ignored);
				Assert.AreEqual("none", options.ToString());
			}
		}

		[Test]
		public void EachLetterTurnsOnItsOwnOptionAndNoOther()
		{
			CloudOptions a = CloudOptions.Parse("A");
			Assert.IsTrue(a.SmoothFill);
			Assert.IsFalse(a.ConfidenceBlur || a.LowDiscrepancyJitter || a.FootprintTexels > 0f);

			CloudOptions b = CloudOptions.Parse("b");
			Assert.IsTrue(b.ConfidenceBlur);
			Assert.IsFalse(b.SmoothFill || b.LowDiscrepancyJitter || b.FootprintTexels > 0f);

			CloudOptions c = CloudOptions.Parse(" C ");
			Assert.AreEqual(CloudOptions.DefaultFootprintTexels, c.FootprintTexels);
			Assert.IsFalse(c.SmoothFill || c.ConfidenceBlur || c.LowDiscrepancyJitter);

			CloudOptions d = CloudOptions.Parse("d");
			Assert.IsTrue(d.LowDiscrepancyJitter);
			Assert.IsFalse(d.SmoothFill || d.ConfidenceBlur || d.FootprintTexels > 0f);
		}

		[Test]
		public void AllOfThemTogetherAndTheFloorsNumber()
		{
			CloudOptions all = CloudOptions.Parse("A,B;C=1.5 d", out string ignored);
			Assert.IsTrue(all.SmoothFill && all.ConfidenceBlur && all.LowDiscrepancyJitter);
			Assert.AreEqual(1.5f, all.FootprintTexels, 1e-6f);
			Assert.AreEqual(string.Empty, ignored);

			Assert.AreEqual(0f, CloudOptions.Parse("C=0").FootprintTexels, "C=0 is off");
			Assert.AreEqual(CloudOptions.MaxFootprintTexels, CloudOptions.Parse("C=99").FootprintTexels, "the floor is capped");
			Assert.AreEqual(0f, CloudOptions.Parse("C=-3").FootprintTexels, "a floor is never negative");
		}

		[Test]
		public void WhatIsNotUnderstoodIsHandedBackNotGuessed()
		{
			// E and F are the probes' own switches (a denser march, a longer settle), not options here.
			CloudOptions options = CloudOptions.Parse("A,E,F,C=wide,B=1,zz", out string ignored);
			Assert.IsTrue(options.SmoothFill);
			Assert.IsFalse(options.ConfidenceBlur, "B=1 is not how B is asked for");
			Assert.AreEqual(0f, options.FootprintTexels, "C=wide is not a floor");
			CollectionAssert.AreEquivalent(new[] { "E", "F", "C=wide", "B=1", "zz" }, ignored.Split(','));
		}

		[Test]
		public void TheNameTheProbesPrintReadsBackAsTheSameOptions()
		{
			foreach (string list in new[] { "A", "B", "C", "C=1", "D", "A,B,C=2,D", "B,D", "A,C=0.75" })
			{
				CloudOptions options = CloudOptions.Parse(list);
				CloudOptions again = CloudOptions.Parse(options.ToString(), out string ignored);
				Assert.AreEqual(string.Empty, ignored, list);
				Assert.AreEqual(options.SmoothFill, again.SmoothFill, list);
				Assert.AreEqual(options.ConfidenceBlur, again.ConfidenceBlur, list);
				Assert.AreEqual(options.FootprintTexels, again.FootprintTexels, 1e-3f, list);
				Assert.AreEqual(options.LowDiscrepancyJitter, again.LowDiscrepancyJitter, list);
			}
		}

		// ── Off is exactly what was ─────────────────────────────────────

		[Test]
		public void OffTheShadersSeeZerosAndTheDetailShareIsUntouched()
		{
			var off = new CloudOptions();
			for (int frame = 0; frame < 1024; frame += 37)
			{
				Vector4 vector = off.ShaderVector(frame);
				Assert.AreEqual(Vector3.zero, (Vector3)vector);
				// The rays' phase is the march's own now, option or not.
				Assert.AreEqual(CloudOptions.JitterPhase(frame), vector.w);
			}
			foreach (float share in new[] { 0.25f, SkySystem.CloudDetailConeShare, 1f, 3f })
			{
				Assert.AreEqual(share, off.DetailConeShare(share));
			}
		}

		[Test]
		public void TheFootprintFloorOnlyEverWidensTheDetailsCone()
		{
			for (float floor = 0.25f; floor <= CloudOptions.MaxFootprintTexels; floor += 0.25f)
			{
				var options = new CloudOptions { FootprintTexels = floor };
				foreach (float share in new[] { 0.1f, SkySystem.CloudDetailConeShare, 1f, 4f })
				{
					float widened = options.DetailConeShare(share);
					Assert.GreaterOrEqual(widened, share, "C never draws finer than without it");
					Assert.GreaterOrEqual(widened, floor, "and never finer than its floor");
				}
			}
		}

		// ── A: the fill's kernel ────────────────────────────────────────

		[Test]
		public void TheFillsWeightsAreNeverNegativeAndSumToOneWhereverThePixelSits()
		{
			// The nine samples round a pixel sit at dx − f texels from it, dx = −1..1, for the pixel f
			// from its nearest texel's centre (−½..½): per axis the three weights must partition one.
			for (float f = -0.5f; f <= 0.5f; f += 1f / 64f)
			{
				float sum = 0f;
				for (int dx = -1; dx <= 1; dx++)
				{
					float w = CloudOptions.BSpline(dx - f);
					Assert.GreaterOrEqual(w, 0f, $"a negative weight at f {f}: the fill could ring past its samples");
					sum += w;
				}
				Assert.AreEqual(1f, sum, 1e-5f, $"f {f}");
			}
		}

		[Test]
		public void TheFillHasNoSeamWhereTheNearestTexelChanges()
		{
			// Leaving texel n at f = +½ and entering texel n + 1 at f = −½ is the same place on the
			// screen: every texel's weight must be the same from both sides, or the fill steps there.
			for (int texel = -2; texel <= 3; texel++)
			{
				float fromLeft = Mathf.Abs(texel) <= 1 ? CloudOptions.BSpline(texel - 0.5f) : 0f;
				int fromRight = texel - 1;
				float fromRightWeight = Mathf.Abs(fromRight) <= 1 ? CloudOptions.BSpline(fromRight + 0.5f) : 0f;
				Assert.AreEqual(fromLeft, fromRightWeight, 1e-6f, $"texel {texel}");
			}
			// And the kernel itself is continuous with its slope at its knots.
			foreach (float knot in new[] { 0.5f, 1.5f })
			{
				Assert.AreEqual(CloudOptions.BSpline(knot - 1e-4f), CloudOptions.BSpline(knot + 1e-4f), 1e-3f, $"knot {knot}");
			}
			Assert.AreEqual(0f, CloudOptions.BSpline(1.5f), 1e-7f);
			Assert.AreEqual(0f, CloudOptions.BSpline(2.5f), 1e-7f);
		}

		[Test]
		public void TheFillLastsAtLeastASampleAndAboutACycle()
		{
			// Balanced and High spread a texel over four and 3⅓ pixels (AutoPixelsPerTexel); the fill
			// fades out over what a pixel's own samples bring in a cycle (16 / perTexel², each sample
			// counting only on the pixel it fell in), and never sooner than one sample.
			foreach (float perTexel in new[] { 1f, 2.5f, 1f / 0.3f, 4f, 6f })
			{
				Assert.GreaterOrEqual(CloudOptions.FillCycleWeight(perTexel), 1f, $"perTexel {perTexel}");
			}
			float balanced = CloudOptions.FillCycleWeight(4f);
			Assert.AreEqual(1f, balanced, 1e-6f, "Balanced: each pixel's own sample once in sixteen frames");
			Assert.Greater(CloudOptions.FillCycleWeight(1f / 0.3f), balanced, "finer texels land more on each pixel");
		}

		// ── B: the blur's width ─────────────────────────────────────────

		[Test]
		public void TheBlurNarrowsAsThePixelSettles()
		{
			foreach (float perTexel in new[] { 2.5f, 1f / 0.3f, 4f, 6f })
			{
				float sharp = FishCloudsFeature.SampleSigmaPixels * FishCloudsFeature.SampleSigmaPixels;
				float previous = float.MaxValue;
				for (float confidence = 0f; confidence <= 50f; confidence += 0.25f)
				{
					float sigma2 = CloudOptions.BlurSigma2(confidence, perTexel);
					Assert.LessOrEqual(sigma2, previous + 1e-6f, $"perTexel {perTexel}: wider at confidence {confidence}");
					Assert.GreaterOrEqual(sigma2, sharp - 1e-6f, "never narrower than the narrow kernel");
					previous = sigma2;
				}
				Assert.AreEqual(sharp, CloudOptions.BlurSigma2(1e6f, perTexel), 1e-3f, "a settled pixel is back to its own samples");
			}
		}

		[Test]
		public void AnEmptyPixelGathersAboutOneSampleAFrame()
		{
			// A Gaussian of σ gathers 2πσ² / perTexel² of a sample a frame: the halo is as wide as a
			// pixel with nothing behind it needs to take a whole sample every frame, and no wider.
			foreach (float perTexel in new[] { 2.5f, 1f / 0.3f, 4f, 6f })
			{
				float gathered = 2f * Mathf.PI * CloudOptions.BlurSigma2(0f, perTexel) / (perTexel * perTexel);
				Assert.That(gathered, Is.InRange(0.95f, 1.05f), $"perTexel {perTexel}");
				Assert.Less(Mathf.Sqrt(CloudOptions.BlurSigma2(0f, perTexel)), perTexel * 0.5f, "the halo stays inside a texel's reach");
			}
		}

		// ── D: the jitter's phase ───────────────────────────────────────

		[Test]
		public void ThePhaseIsAlwaysInsideOneStep()
		{
			var on = new CloudOptions { LowDiscrepancyJitter = true };
			for (int frame = 0; frame < 4096; frame++)
			{
				float phase = CloudOptions.JitterPhase(frame);
				Assert.That(phase, Is.InRange(0f, 1f), $"frame {frame}");
				Assert.Less(phase, 1f, $"frame {frame}");
				Assert.AreEqual(phase, on.ShaderVector(frame).w);
			}
		}

		[Test]
		public void EveryCycleVisitsEachPlaceOnceAndCoversTheStepEvenly()
		{
			for (int cycle = 0; cycle < 64; cycle++)
			{
				var slots = new HashSet<int>();
				var phases = new List<float>();
				for (int frame = cycle * 16; frame < cycle * 16 + 16; frame++)
				{
					slots.Add(FishCloudsFeature.SubPixelSlot(frame));
					phases.Add(CloudOptions.JitterPhase(frame));
				}
				Assert.AreEqual(16, slots.Count, $"cycle {cycle} missed a place");
				phases.Sort();
				for (int i = 0; i < 16; i++)
				{
					float gap = i < 15 ? phases[i + 1] - phases[i] : 1f + phases[0] - phases[15];
					Assert.AreEqual(1f / 16f, gap, 1e-4f, $"cycle {cycle}: the sixteen phases are not stratified");
				}
			}
		}

		[Test]
		public void NeighbouringPlacesAreFarApartInPhase()
		{
			// On one frame every texel is looked through at the same place, so each place's phase is the
			// frame's. A pixel is steadied mostly from its own place and its neighbours': those must not
			// share a phase, or the step error that phase leaves ramps across the texel (2026-09-29, the
			// scale pattern). The Bayer order puts neighbouring places at least three sixteenths apart.
			Assert.Greater(Neighbours(CloudOptions.JitterPhase), 2.5f / 16f, "neighbouring places nearly in phase");
			// The control: the old bit-reversed order is a Morton ramp, neighbours a sixteenth apart.
			Assert.Less(Neighbours(CloudOptions.JitterPhaseLegacy), 1.5f / 16f, "the control no longer fires");
		}

		// The closest two side-by-side places of a texel come in phase, over a cycle (circular distance).
		private static float Neighbours(System.Func<int, float> phaseOf)
		{
			int side = FishCloudsFeature.SubPixelSide;
			var phase = new float[side, side];
			for (int frame = 16 * 5; frame < 16 * 6; frame++)
			{
				Vector2 place = FishCloudsFeature.SubPixelPlace(FishCloudsFeature.SubPixelSlot(frame));
				int x = Mathf.RoundToInt((place.x + 0.5f) * side - 0.5f);
				int y = Mathf.RoundToInt((place.y + 0.5f) * side - 0.5f);
				phase[y, x] = phaseOf(frame);
			}
			float closest = 1f;
			for (int y = 0; y < side; y++)
			{
				for (int x = 0; x < side; x++)
				{
					if (x + 1 < side)
					{
						closest = Mathf.Min(closest, Apart(phase[y, x], phase[y, x + 1]));
					}
					if (y + 1 < side)
					{
						closest = Mathf.Min(closest, Apart(phase[y, x], phase[y + 1, x]));
					}
				}
			}
			return closest;
		}

		private static float Apart(float a, float b)
		{
			float d = Mathf.Abs(a - b);
			return Mathf.Min(d, 1f - d);
		}

		[Test]
		public void EachPlacesOwnPhasesStayEvenlySpread()
		{
			// Three-distance theorem: n golden-ratio steps leave gaps of at most three sizes, the largest
			// at most φ² times the smallest. A pixel's own samples are one place's, a cycle apart.
			for (int slot = 0; slot < 16; slot++)
			{
				var phases = new List<float>();
				for (int frame = 0; frame < 1024; frame++)
				{
					if (FishCloudsFeature.SubPixelSlot(frame) == slot)
					{
						phases.Add(CloudOptions.JitterPhase(frame));
					}
				}
				Assert.AreEqual(64, phases.Count, $"place {slot}");
				phases.Sort();
				float smallest = float.MaxValue;
				float largest = 0f;
				for (int i = 0; i < phases.Count; i++)
				{
					float gap = i < phases.Count - 1 ? phases[i + 1] - phases[i] : 1f + phases[0] - phases[phases.Count - 1];
					smallest = Mathf.Min(smallest, gap);
					largest = Mathf.Max(largest, gap);
				}
				Assert.Greater(smallest, 0f, $"place {slot}: two samples at the same phase");
				Assert.LessOrEqual(largest / smallest, 2.62f, $"place {slot}: its phases bunch");
			}
		}
	}
}
