using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Every material constant the water shaders read is one the material can actually fill.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>A UnityPerMaterial member that is not also a Property reads zero, silently.</b> The ocean's
	/// <c>_FoamColor</c> sat in its constant buffer and in <c>OceanWater.mat</c> for two days after
	/// dropping out of the shader's Properties block, and every white cap and every line of surf drew as
	/// black lace — which was then chased for a day as "dark patches in the shallows". Nothing compiles
	/// differently and nothing logs; only a picture shows it, and only if someone knows what it should
	/// have looked like.
	/// </para>
	/// <para>
	/// The breakers make it worse by two: their shader includes the ocean's constant buffer whole, so it
	/// must declare every one of the same properties, and its material is filled by copying the ocean
	/// shader's own property list across (WaterBreakers.SyncMaterials) — so the two lists must match.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class FishWaterMaterialTests
	{
		private const string Shaders = "Assets/Plugins/FishMMO Water/Shaders/";

		private static string Read(string file)
		{
			return File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), Shaders + file)).Replace("\r\n", "\n");
		}

		/// <summary>The names in a shader's Properties block.</summary>
		private static HashSet<string> Properties(string shader)
		{
			int start = shader.IndexOf("Properties", StringComparison.Ordinal);
			LogAssert.IsTrue(start >= 0, "the shader has no Properties block");
			int open = shader.IndexOf('{', start);
			int depth = 0, end = open;
			for (; end < shader.Length; end++)
			{
				if (shader[end] == '{')
				{
					depth++;
				}
				else if (shader[end] == '}' && --depth == 0)
				{
					break;
				}
			}
			string block = shader.Substring(open, end - open);
			var names = new HashSet<string>();
			foreach (Match match in Regex.Matches(block, @"^\s*(?:\[[^\]]*\]\s*)*(_\w+)\s*\(", RegexOptions.Multiline))
			{
				names.Add(match.Groups[1].Value);
			}
			return names;
		}

		/// <summary>The members of the first UnityPerMaterial constant buffer in a source.</summary>
		private static List<string> PerMaterial(string source)
		{
			int start = source.IndexOf("CBUFFER_START(UnityPerMaterial)", StringComparison.Ordinal);
			LogAssert.IsTrue(start >= 0, "no UnityPerMaterial block");
			int end = source.IndexOf("CBUFFER_END", start, StringComparison.Ordinal);
			return Regex.Matches(source.Substring(start, end - start), @"^\s*(?:half|float)[1-4]?\s+(_\w+)\s*;", RegexOptions.Multiline)
				.Cast<Match>().Select(m => m.Groups[1].Value).ToList();
		}

		[Test]
		public void EveryOceanConstantIsAPropertyOfTheOcean()
		{
			List<string> members = PerMaterial(Read("FishWaterInput.hlsl"));
			LogAssert.IsTrue(members.Count > 20, $"the scan found only {members.Count} constants; its pattern is stale");
			HashSet<string> declared = Properties(Read("FishWater.shader"));
			string missing = string.Join(", ", members.Where(m => !declared.Contains(m)));
			LogAssert.IsTrue(missing.Length == 0, "FishMMO/Water/Ocean reads these but never declares them, so they are zero: " + missing);
		}

		[Test]
		public void TheBreakersDeclareExactlyTheOceansProperties()
		{
			HashSet<string> ocean = Properties(Read("FishWater.shader"));
			HashSet<string> breaker = Properties(Read("FishWaterBreaker.shader"));
			string missing = string.Join(", ", ocean.Where(p => !breaker.Contains(p)));
			string extra = string.Join(", ", breaker.Where(p => !ocean.Contains(p)));
			LogAssert.IsTrue(missing.Length == 0, "FishMMO/Water/Breaker is missing the ocean's: " + missing);
			LogAssert.IsTrue(extra.Length == 0, "FishMMO/Water/Breaker declares what the ocean does not, which nothing copies: " + extra);
		}

		[Test]
		public void TheSprayDeclaresItsOwnConstants()
		{
			string spray = Read("FishWaterSpray.shader");
			HashSet<string> declared = Properties(spray);
			string missing = string.Join(", ", PerMaterial(spray).Where(m => !declared.Contains(m)));
			LogAssert.IsTrue(missing.Length == 0, "FishMMO/Water/Spray reads these but never declares them, so they are zero: " + missing);
		}
	}
}
