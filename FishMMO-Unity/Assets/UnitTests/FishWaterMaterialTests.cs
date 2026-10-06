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
		public void TheInlandWaterDeclaresExactlyItsConstants()
		{
			List<string> members = PerMaterial(Read("FishInlandWater.hlsl"));
			LogAssert.IsTrue(members.Count > 10, $"the scan found only {members.Count} constants; its pattern is stale");
			HashSet<string> declared = Properties(Read("FishInlandWater.shader"));
			string missing = string.Join(", ", members.Where(m => !declared.Contains(m)));
			string extra = string.Join(", ", declared.Where(p => !members.Contains(p) && p != "_NormalMap" && p != "_FoamTexture"));
			LogAssert.IsTrue(missing.Length == 0, "FishMMO/Water/Inland Water reads these but never declares them, so they are zero: " + missing);
			LogAssert.IsTrue(extra.Length == 0, "FishMMO/Water/Inland Water declares these but its material block never reads them: " + extra);
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

		/// <summary>The names of a shader's texture properties (2D, 3D, Cube, 2DArray).</summary>
		private static HashSet<string> TextureProperties(string shader)
		{
			return new HashSet<string>(Regex.Matches(shader,
				@"^\s*(?:\[[^\]]*\]\s*)*(_\w+)\s*\(\s*""[^""]*""\s*,\s*(?:2D|3D|Cube|2DArray)\s*\)", RegexOptions.Multiline)
				.Cast<Match>().Select(m => m.Groups[1].Value));
		}

		/// <summary>
		/// The lava's constant buffer and its non-texture Properties are the same list both ways: a
		/// property outside the buffer is one the SRP Batcher cannot carry and one inside it that is not a
		/// property reads zero — a zero melt temperature is a lake with no glow at all. Its textures (the
		/// baked crust) are properties outside the buffer, as textures are, and each must be declared in
		/// the header: a texture property nothing samples is a material slot that does nothing.
		/// </summary>
		[Test]
		public void TheLavaDeclaresExactlyItsConstants()
		{
			string header = Read("FishLava.hlsl");
			List<string> members = PerMaterial(header);
			LogAssert.IsTrue(members.Count > 10, $"the scan found only {members.Count} constants; its pattern is stale");
			string shader = Read("FishLava.shader");
			HashSet<string> textures = TextureProperties(shader);
			LogAssert.IsTrue(textures.Count == 2, $"expected the two baked crust maps, found {textures.Count} textures; the scan or the shader is stale");
			HashSet<string> declared = Properties(shader);
			declared.ExceptWith(textures);
			string missing = string.Join(", ", members.Where(m => !declared.Contains(m)));
			string extra = string.Join(", ", declared.Where(p => !members.Contains(p)));
			LogAssert.IsTrue(missing.Length == 0, "FishMMO/Water/Lava reads these but never declares them, so they are zero: " + missing);
			LogAssert.IsTrue(extra.Length == 0, "FishMMO/Water/Lava declares these outside its UnityPerMaterial: " + extra);
			string undeclared = string.Join(", ", textures.Where(t => !header.Contains("TEXTURE2D(" + t + ")")));
			LogAssert.IsTrue(undeclared.Length == 0, "FishMMO/Water/Lava has texture properties its header never declares: " + undeclared);
		}

		/// <summary>
		/// The baked crust maps are committed and wired into the lava material, so the lava draws its
		/// crust detail without anyone having to bake first. A missing map falls back to a flat normal
		/// and a white mask — a crust with no detail and no torn borders, which is easy to miss.
		/// </summary>
		[Test]
		public void TheLavaMaterialCarriesTheBakedCrust()
		{
			string folder = Path.Combine(Directory.GetCurrentDirectory(), "Assets/Plugins/FishMMO Water/");
			string material = File.ReadAllText(folder + "Materials/Lava.mat");
			foreach (string map in new[] { "LavaCrustNormal", "LavaCrustMask" })
			{
				string meta = folder + "Textures/" + map + ".png.meta";
				LogAssert.IsTrue(File.Exists(meta), map + ".png is not in the plugin's Textures folder; run FishMMO/Water/Bake Lava Textures");
				string guid = Regex.Match(File.ReadAllText(meta), @"^guid:\s*(\w+)", RegexOptions.Multiline).Groups[1].Value;
				LogAssert.IsTrue(guid.Length == 32 && material.Contains("guid: " + guid), "Lava.mat does not reference " + map + ".png");
			}
		}

		/// <summary>
		/// The lava's glow-and-fume pass includes the lava's constant buffer whole and its material is
		/// copied from the lava's (WaterSurface.UpdateLavaLight), so it must declare exactly the lava's
		/// properties — the breakers' rule, for the same reason.
		/// </summary>
		[Test]
		public void TheLavaLightDeclaresExactlyTheLavasProperties()
		{
			HashSet<string> lava = Properties(Read("FishLava.shader"));
			HashSet<string> light = Properties(Read("FishLavaLight.shader"));
			string missing = string.Join(", ", lava.Where(p => !light.Contains(p)));
			string extra = string.Join(", ", light.Where(p => !lava.Contains(p)));
			LogAssert.IsTrue(missing.Length == 0, "FishMMO/Water/Lava Light is missing the lava's: " + missing);
			LogAssert.IsTrue(extra.Length == 0, "FishMMO/Water/Lava Light declares what the lava does not, which nothing copies: " + extra);
			LogAssert.IsTrue(Read("FishLavaLight.hlsl").Contains("#include \"FishLava.hlsl\""),
				"the light pass must take the lava's constant buffer from its header, not declare its own");
		}

		/// <summary>
		/// Every pass of the lava reaches its constant buffer through the one header, so the SRP Batcher
		/// sees one layout — and every pass that draws depth places the surface with the same function.
		/// </summary>
		[Test]
		public void EveryLavaPassSharesOneHeader()
		{
			string shader = Read("FishLava.shader");
			int passes = Regex.Matches(shader, @"^\s*Pass\s*$", RegexOptions.Multiline).Count;
			int includes = Regex.Matches(shader, "#include \"FishLavaPasses.hlsl\"").Count;
			LogAssert.IsTrue(passes == 3, $"expected ForwardLit, DepthOnly and DepthNormals, found {passes} passes");
			LogAssert.IsTrue(includes == passes, $"{passes} passes but {includes} include FishLavaPasses.hlsl");
			LogAssert.IsTrue(!shader.Contains("CBUFFER_START"), "FishLava.shader declares a constant buffer of its own; it belongs in FishLava.hlsl");
			LogAssert.IsTrue(!shader.Contains("\"ShadowCaster\""), "a flat opaque sheet can only shadow what it hides");
		}
	}
}
