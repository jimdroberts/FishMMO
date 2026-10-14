using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The foam's lace (WaterFoam.png alpha) cut at a coarse mip covers what it is asked: FishWaterFoam.hlsl's per-mip
	/// contrast figures are the texture's own, and the river reads the lace at the pixel's mip with that contrast put back,
	/// never a fixed number of mips coarser.
	/// </summary>
	/// <remarks>
	/// The lace is equalised at full size only. Every mip averages it toward its mean, and the river's foam read it two mips
	/// coarser than its pixels needed: at mip 5 a tenth asked covered half a percent (specks of foam scattered like static
	/// over every rapid) and 0.85 asked nearly all of it (flat blobs with hard edges).
	/// </remarks>
	[TestFixture]
	public class FoamLaceTests
	{
		private const string Shaders = "Assets/Plugins/FishMMO Water/Shaders/";
		private const string LacePath = "Assets/Plugins/FishMMO Water/Textures/WaterFoam.png";

		/// <summary>The lace (alpha, 0 … 1) at full size and each box-filtered mip after it, as the importer builds them (Box).</summary>
		private static double[][] LaceMips(out int size)
		{
			byte[] bytes = File.ReadAllBytes(Path.Combine(Directory.GetCurrentDirectory(), LacePath));
			var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
			try
			{
				Assert.That(texture.LoadImage(bytes, false), Is.True, "WaterFoam.png decodes");
				size = texture.width;
				Assert.That(texture.height, Is.EqualTo(size), "the lace is square");
				Color32[] pixels = texture.GetPixels32();
				var mips = new double[10][];
				var level = new double[size * size];
				for (int i = 0; i < level.Length; i++)
				{
					level[i] = pixels[i].a / 255.0;
				}
				mips[0] = level;
				int width = size;
				for (int m = 1; m < mips.Length && width > 1; m++)
				{
					int half = width / 2;
					var next = new double[half * half];
					for (int y = 0; y < half; y++)
					{
						for (int x = 0; x < half; x++)
						{
							int a = 2 * y * width + 2 * x;
							next[y * half + x] = 0.25 * (level[a] + level[a + 1] + level[a + width] + level[a + width + 1]);
						}
					}
					mips[m] = level = next;
					width = half;
				}
				return mips;
			}
			finally
			{
				Object.DestroyImmediate(texture);
			}
		}

		private static double Deviation(double[] values)
		{
			double mean = 0.0;
			foreach (double v in values)
			{
				mean += v;
			}
			mean /= values.Length;
			double sum = 0.0;
			foreach (double v in values)
			{
				sum += (v - mean) * (v - mean);
			}
			return Math.Sqrt(sum / values.Length);
		}

		/// <summary>FishFoamLaceContrastByMip as the shader holds it.</summary>
		private static double[] ShaderContrast()
		{
			string source = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), Shaders + "FishWaterFoam.hlsl"));
			Match match = Regex.Match(source, @"FishFoamLaceContrastByMip\[(\d+)\]\s*=\s*\{([^}]*)\}");
			Assert.That(match.Success, Is.True, "FishWaterFoam.hlsl declares FishFoamLaceContrastByMip");
			string[] parts = match.Groups[2].Value.Split(',');
			Assert.That(parts.Length, Is.EqualTo(int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture)));
			var values = new double[parts.Length];
			for (int i = 0; i < parts.Length; i++)
			{
				values[i] = double.Parse(parts[i].Trim(), CultureInfo.InvariantCulture);
			}
			return values;
		}

		/// <summary>The share of the lace a cut at 1 − <paramref name="coverage"/> covers, its contrast scaled by <paramref name="contrast"/>.</summary>
		private static double Covered(double[] lace, double coverage, double contrast)
		{
			int over = 0;
			foreach (double v in lace)
			{
				double restored = Math.Min(1.0, Math.Max(0.0, 0.5 + (v - 0.5) * contrast));
				if (restored > 1.0 - coverage)
				{
					over++;
				}
			}
			return over / (double)lace.Length;
		}

		[Test]
		public void TheShadersContrastByMipIsTheLacesOwn()
		{
			double[][] mips = LaceMips(out _);
			double[] table = ShaderContrast();
			double full = Deviation(mips[0]);
			for (int m = 0; m < table.Length; m++)
			{
				double measured = full / Deviation(mips[m]);
				Assert.That(table[m], Is.EqualTo(measured).Within(0.015 * measured),
					$"mip {m}: the shader restores ×{table[m]}, the lace has lost ×{measured:0.000} (re-measure after Bake Water Textures)");
			}
		}

		[Test]
		public void ACutThroughACoarseMipCoversWhatItIsAskedOnceItsContrastIsRestored()
		{
			double[][] mips = LaceMips(out _);
			double[] table = ShaderContrast();
			double[] asked = { 0.1, 0.2, 0.3, 0.5, 0.7, 0.85 };
			// The mips the river reads and still cuts crisp (FlowLaceResolved is 0 from the floor + 3 on).
			for (int m = 2; m <= 5; m++)
			{
				foreach (double coverage in asked)
				{
					Assert.That(Covered(mips[m], coverage, table[m]), Is.EqualTo(coverage).Within(0.04),
						$"mip {m}, {coverage:0.00} asked");
				}
			}
			// The control: the same cut without the contrast put back is the defect, specks at a tenth and blobs at 0.85.
			Assert.That(Covered(mips[5], 0.1, 1.0), Is.LessThan(0.02), "unrestored, a tenth asked covers almost nothing at mip 5");
			Assert.That(Covered(mips[5], 0.85, 1.0), Is.GreaterThan(0.93), "unrestored, 0.85 asked covers nearly all at mip 5");
		}

		[Test]
		public void TheRiversLaceIsReadAtItsPixelsMipWithItsContrastRestoredNotBiasedCoarser()
		{
			string code = SourceScanPins.ReadCode(Shaders + "FishInlandWater.hlsl");
			SourceScanPins.HoldsAndFires("FlowLace", code,
				c =>
				{
					string body = SourceScanPins.Body(c, "half FlowLace(");
					if (body == null)
					{
						return "FlowLace is gone";
					}
					if (body.Contains("SAMPLE_TEXTURE2D_BIAS"))
					{
						return "FlowLace reads the lace mip-biased, a fixed number of mips coarser than its pixels";
					}
					return body.Contains("FishFoamLaceRestore(SAMPLE_TEXTURE2D_LOD(_FoamTexture") ? null : "FlowLace does not restore the contrast of the mip it reads";
				},
				SourceScanPins.Replace("FishFoamLaceRestore(SAMPLE_TEXTURE2D_LOD(_FoamTexture, sampler_FoamTexture, uvA, lod).a, lod)",
					"SAMPLE_TEXTURE2D_BIAS(_FoamTexture, sampler_FoamTexture, uvA, FISH_RIVER_LACE_MIP).a"),
				"the lace read two mips coarser, its contrast left lost");
		}
	}
}
