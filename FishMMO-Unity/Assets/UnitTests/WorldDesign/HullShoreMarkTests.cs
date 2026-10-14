using System.IO;
using System.Text.RegularExpressions;
using FishMMO.Shared;
using FishMMO.Water;
using NUnit.Framework;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// A floating hull keeps the sea's shoreline off itself (FishHullShoreMark.shader, laid on by <see cref="FloatingBoats"/>):
	/// the mark's stencil bit, the shore pass that skips it, and the passes that must not.
	/// </summary>
	public class HullShoreMarkTests
	{
		private const string Shaders = "Assets/Plugins/FishMMO Water/Shaders/";

		private static string Read(string file)
			=> File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), Shaders + file)).Replace("\r\n", "\n");

		/// <summary>One number of a shader's (single) Stencil block, or -1 when the block or the entry is absent.</summary>
		private static int Stencil(string shader, string entry)
		{
			Match block = Regex.Match(shader, @"Stencil\s*\{([^}]*)\}");
			if (!block.Success)
			{
				return -1;
			}
			Match value = Regex.Match(block.Groups[1].Value, @"\b" + entry + @"\s+(\d+)");
			return value.Success ? int.Parse(value.Groups[1].Value) : -1;
		}

		private static string Compare(string shader)
		{
			Match block = Regex.Match(shader, @"Stencil\s*\{([^}]*)\}");
			Match value = Regex.Match(block.Groups[1].Value, @"\bComp\s+(\w+)");
			return value.Success ? value.Groups[1].Value : null;
		}

		[Test]
		public void TheBoatsLookUpTheShaderThePluginShipsAndTheShoreReferences()
		{
			Assert.AreEqual(WaterShore.HullMarkShaderName, FloatingBoats.ShoreMarkShaderName);
			StringAssert.Contains($"Shader \"{FloatingBoats.ShoreMarkShaderName}\"", Read("FishHullShoreMark.shader"));
		}

		[Test]
		public void TheShoreSkipsBothTheHullsMarkAndItsWaterLid()
		{
			string mark = Read("FishHullShoreMark.shader");
			int bit = Stencil(mark, "Ref");
			Assert.AreEqual(bit, Stencil(mark, "WriteMask"), "the mark writes its own bit alone");
			Assert.IsTrue(bit > 0 && (bit & (bit - 1)) == 0, "the mark is one bit");
			int lid = Stencil(Read("FishHullLid.shader"), "Ref");

			string shore = Read("FishWaterShore.shader");
			Assert.AreEqual(0, Stencil(shore, "Ref"));
			Assert.AreEqual("Equal", Compare(shore));
			int read = Stencil(shore, "ReadMask");
			Assert.AreEqual(bit, read & bit, "the shore reads the hull's mark");
			Assert.AreEqual(lid, read & lid, "the shore reads the hull's water lid");
		}

		[Test]
		public void TheSeaAndItsCausticsStillDrawOverAHull()
		{
			// The planking under the waterline is seen THROUGH the sea and lit by its caustics: those passes ignore the mark.
			int bit = Stencil(Read("FishHullShoreMark.shader"), "Ref");
			foreach (string file in new[] { "FishWater.shader", "FishWaterCaustics.shader", "FishInlandWater.shader" })
			{
				int read = Stencil(Read(file), "ReadMask");
				Assert.AreEqual(0, read & bit, file);
			}
		}
	}
}
