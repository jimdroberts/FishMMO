using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared.WorldDesign;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// A layer with only an albedo gets a normal and a mask worked out from it when its arrays are baked
	/// (<see cref="TerrainSurfaceDerivation"/>): relief at the generated layers' strength, dark read as deep, a slice
	/// that still tiles, and the smoothness and metallic it drew with before.
	/// </summary>
	public class TerrainSurfaceDerivationTests
	{
		private const int Size = 128;

		private static Color32[] Noise(uint seed, byte alpha = 255)
		{
			var pixels = new Color32[Size * Size];
			uint h = seed;
			for (int i = 0; i < pixels.Length; i++)
			{
				h = h * 1664525u + 1013904223u;
				byte v = (byte)(64 + (h >> 24) / 2);
				pixels[i] = new Color32(v, v, v, alpha);
			}
			return pixels;
		}

		private static Color32[] Flat(byte grey)
		{
			var pixels = new Color32[Size * Size];
			for (int i = 0; i < pixels.Length; i++)
			{
				pixels[i] = new Color32(grey, grey, grey, 200);
			}
			return pixels;
		}

		[Test]
		public void AFlatAlbedo_GivesAFlatGround()
		{
			TerrainSurfaceDerivation.Result r = TerrainSurfaceDerivation.Derive(Flat(120), Size, 0f, 0.3f, false);
			LogAssert.IsTrue(TerrainSurfaceDerivation.MeanTiltDegrees(r.Normal) < 1f, "no detail, no relief");
			LogAssert.AreEqual((byte)128, r.Mask[0].b, "the middle height");
			LogAssert.AreEqual((byte)255, r.Mask[0].g, "nothing is in a hollow");
		}

		[Test]
		public void TheRelief_IsCalibratedWhateverTheContrast()
		{
			float tilt = TerrainSurfaceDerivation.MeanTiltDegrees(TerrainSurfaceDerivation.Derive(Noise(7), Size, 0f, 0f, false).Normal);
			LogAssert.IsTrue(Mathf.Abs(tilt - TerrainSurfaceDerivation.TargetTiltDegrees) < 2.5f, $"mean tilt {tilt:0.0}°");

			// The same pattern at a fifth of the contrast comes out as strong.
			Color32[] faint = Noise(7);
			for (int i = 0; i < faint.Length; i++)
			{
				byte v = (byte)(128 + (faint[i].r - 128) / 5);
				faint[i] = new Color32(v, v, v, 255);
			}
			float faintTilt = TerrainSurfaceDerivation.MeanTiltDegrees(TerrainSurfaceDerivation.Derive(faint, Size, 0f, 0f, false).Normal);
			LogAssert.IsTrue(Mathf.Abs(faintTilt - tilt) < 2.5f, $"{faintTilt:0.0}° vs {tilt:0.0}°");
		}

		[Test]
		public void TheDark_IsTheDeep()
		{
			Color32[] pixels = Flat(200);
			int centre = (Size / 2) * Size + Size / 2;
			for (int y = -3; y <= 3; y++)
			{
				for (int x = -3; x <= 3; x++)
				{
					pixels[centre + y * Size + x] = new Color32(20, 20, 20, 200);
				}
			}
			TerrainSurfaceDerivation.Result r = TerrainSurfaceDerivation.Derive(pixels, Size, 0f, 0f, false);
			int far = 5 * Size + 5;
			LogAssert.IsTrue(r.Mask[centre].b < r.Mask[far].b, $"the dark pit is lower ({r.Mask[centre].b} vs {r.Mask[far].b})");
			LogAssert.IsTrue(r.Mask[centre].g < r.Mask[far].g, $"and in shade ({r.Mask[centre].g} vs {r.Mask[far].g})");
			// Its walls face into it: the wall on its left faces right (x above the middle), the one on its right faces left.
			LogAssert.IsTrue(r.Normal[centre - 4].r > 128 && r.Normal[centre + 4].r < 128,
				$"the walls face in ({r.Normal[centre - 4].r} left, {r.Normal[centre + 4].r} right)");
		}

		[Test]
		public void TheSlice_StillTiles()
		{
			TerrainSurfaceDerivation.Result r = TerrainSurfaceDerivation.Derive(Noise(11), Size, 0f, 0f, false);
			// Across the seam the normal changes no more than between any two neighbouring columns.
			double seam = 0.0, inside = 0.0;
			for (int y = 0; y < Size; y++)
			{
				seam += Mathf.Abs(r.Normal[y * Size].r - r.Normal[y * Size + Size - 1].r);
				inside += Mathf.Abs(r.Normal[y * Size + Size / 2].r - r.Normal[y * Size + Size / 2 - 1].r);
			}
			LogAssert.IsTrue(seam < inside * 1.5 + Size, $"seam {seam / Size:0.0} vs inside {inside / Size:0.0}");
		}

		[Test]
		public void SmoothnessAndMetallic_AreWhatTheLayerDrewWith()
		{
			TerrainSurfaceDerivation.Result slider = TerrainSurfaceDerivation.Derive(Noise(3, alpha: 40), Size, 0.2f, 0.6f, false);
			LogAssert.AreEqual((byte)153, slider.Mask[17].a, "the smoothness slider, without an alpha");
			LogAssert.AreEqual((byte)51, slider.Mask[17].r, "the metallic slider");
			TerrainSurfaceDerivation.Result alpha = TerrainSurfaceDerivation.Derive(Noise(3, alpha: 40), Size, 0f, 0.6f, true);
			LogAssert.AreEqual((byte)40, alpha.Mask[17].a, "the albedo's alpha, when it holds smoothness");
		}
	}
}
