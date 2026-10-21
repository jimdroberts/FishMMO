using System;
using FishMMO.Shared.WorldDesign;
using NUnit.Framework;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// The open sea (<see cref="PointOfInterestOpenSea"/>, 2026-10-20: "Docks, shipyards, etc … land on lakes and inlets instead
	/// of open ocean. Ships are only on seas and the FFT ocean."): the water under sea level the scene's edge reaches through a
	/// ship's depth, not a basin shut in from it; and how much open water a harbour sees.
	/// </summary>
	public class OpenSeaTests
	{
		private const int W = 40, D = 40;

		/// <summary>Land at +10 everywhere, with the cells <paramref name="water"/> picks set to its depth (negative, under sea 0).</summary>
		private static float[] Heights(Func<int, int, float> water)
		{
			var h = new float[W * D];
			for (int z = 0; z < D; z++)
			{
				for (int x = 0; x < W; x++)
				{
					float depth = water(x, z);
					h[z * W + x] = float.IsNaN(depth) ? 10f : depth;
				}
			}
			return h;
		}

		[Test]
		public void TheOceanPastTheEdgeIsOpen_ABasinShutInFromItIsNot()
		{
			// The sea along the west edge (x < 10, 20 m deep, its shallows at x 10..11) and a basin 30 m deep inland, ringed by land.
			float[] h = Heights((x, z) => x < 10 ? -20f : x < 12 ? -1f : (x >= 24 && x < 32 && z >= 14 && z < 26) ? -30f : float.NaN);
			bool[] open = PointOfInterestOpenSea.Flood(W, D, h, 0f);
			Assert.That(open[20 * W + 2], Is.True, "the ocean");
			Assert.That(open[20 * W + 11], Is.True, "its shallows, the same water");
			Assert.That(open[20 * W + 28], Is.False, "a basin under sea level shut in by land is no sea, however deep");
			Assert.That(open[20 * W + 15], Is.False, "dry land");
		}

		[Test]
		public void AWayInMustBeDeepEnoughForAShip()
		{
			// The basin joined to the sea by a channel: over a sand bar 1 m deep, then dug to 3 m.
			Func<float, float[]> scene = bar => Heights((x, z) => x < 10 ? -20f : (z == 20 && x < 24) ? bar : (x >= 24 && x < 32 && z >= 14 && z < 26) ? -30f : float.NaN);
			Assert.That(PointOfInterestOpenSea.Flood(W, D, scene(-1f), 0f)[20 * W + 28], Is.False, "behind a bar no ship crosses: a lagoon");
			Assert.That(PointOfInterestOpenSea.Flood(W, D, scene(-3f), 0f)[20 * W + 28], Is.True, "through a channel a cog can pass: the sea's");
			Assert.That(PointOfInterestOpenSea.Flood(W, D, scene(-1f), 0f)[20 * W + 15], Is.True, "the bar's shallows are the sea's own");
		}

		[Test]
		public void AStraightCoastSeesOpenWater_ANarrowInletsBanksDoNot()
		{
			// The west half sea; a narrow inlet (2 cells wide) running east from it to the middle of the east half.
			float[] h = Heights((x, z) => x < 20 ? -20f : (z >= 30 && z < 32 && x < 34) ? -8f : float.NaN);
			bool[] open = PointOfInterestOpenSea.Flood(W, D, h, 0f);
			int[] summed = PointOfInterestOpenSea.Summed(W, D, open);
			float coast = PointOfInterestOpenSea.OpenShare(W, D, open, summed, 21, 12, 6);
			float inlet = PointOfInterestOpenSea.OpenShare(W, D, open, summed, 32, 29, 6);
			Assert.That(open[31 * W + 33], Is.True, "the inlet is the sea's water");
			Assert.That(coast, Is.GreaterThan(PointOfInterestOpenSea.HarbourShare), "a harbour on the open coast");
			Assert.That(inlet, Is.LessThan(PointOfInterestOpenSea.HarbourShare), "not up an inlet's narrows");
		}

		[Test]
		public void PastTheEdgeTheOpenSeaRunsOn()
		{
			float[] h = Heights((x, z) => x < 10 ? -20f : float.NaN);
			bool[] open = PointOfInterestOpenSea.Flood(W, D, h, 0f);
			int[] summed = PointOfInterestOpenSea.Summed(W, D, open);
			Assert.That(PointOfInterestOpenSea.OpenShare(W, D, open, summed, 0, 20, 4), Is.EqualTo(1f).Within(1e-4f),
				"a square half past the scene's edge in the ocean is all sea, not half nothing");
			Assert.That(PointOfInterestOpenSea.Flood(W, D, h, float.NegativeInfinity)[20 * W + 2], Is.False, "no sea, no open sea");
		}
	}
}
