using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Client;
using FishMMO.Shared.WorldDesign;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The moss layer in the world lit shader (FishMoss.hlsl; 2026-10-12): the biome dampness that scales it, the
	/// globals it reads, the amounts the art generator gives each material, and the shader-side contracts — one material
	/// constant in UnityPerMaterial, no keyword and no sampler state of its own (the forward pass is at D3D11's sixteen).
	/// </summary>
	public class MossLayerTests
	{
		private const string Shaders = "Assets/Prefabs/Client/Weather/Shaders/";
		private const string MossLibrary = Shaders + "FishMoss.hlsl";
		private const string LitPass = Shaders + "FishWeatherLitForwardPass.hlsl";
		private const string LitInput = Shaders + "FishWeatherLitInput.hlsl";
		private const string LitShader = Shaders + "FishWeatherLit.shader";
		private const string IndirectShader = Shaders + "FishWeatherLitIndirect.shader";

		// A biome's envelope, in BiomeTemplate's terms (-1..1), as the assets under Templates/Entity/Biomes hold them.
		private static float Damp(float minH, float maxH, float minT, float maxT, bool liquid, bool loose = false, bool emits = false, float wetland = 0f)
		{
			return MossClimate.Dampness(minH, maxH, minT, maxT, liquid, loose, emits, wetland);
		}

		private static float Swamp => Damp(0.6f, 1f, -0.1f, 0.6f, true);
		private static float Forest => Damp(0.35f, 1f, 0f, 0.5f, true);
		private static float Taiga => Damp(-0.2f, 1f, -1f, 0.05f, true);
		private static float Tundra => Damp(-1f, 1f, -1f, 0.3f, true);
		private static float Grassland => Damp(0f, 0.25f, 0f, 0.45f, true);
		private static float Savanna => Damp(-0.1f, 0.35f, 0.45f, 1f, true);
		private static float Desert => Damp(-1f, -0.45f, 0.05f, 1f, false, loose: true);
		private static float Volcanic => Damp(-1f, -0.45f, 0.2f, 1f, false, emits: true);
		private static float Glacier => Damp(-1f, 1f, -1f, 0.3f, false);
		private static float IceSheet => Damp(-1f, 1f, -1f, 0.1f, false);
		private static float Snow => Damp(-1f, 1f, -1f, -0.7f, false);

		[Test]
		public void WetBiomes_GrowTheMost()
		{
			Assert.Greater(Swamp, 0.9f, "a swamp is as damp as it gets");
			Assert.Greater(Forest, 0.8f, "a forest's rocks are mossy");
			Assert.Greater(Taiga, 0.6f, "the boreal forest is carpeted in moss");
			Assert.Greater(Forest, Grassland);
			Assert.Greater(Grassland, Savanna, "heat bakes a dry-ish biome drier");
		}

		[Test]
		public void DryHotAndFrozenBiomes_GrowLittleOrNone()
		{
			Assert.Less(Desert, 0.02f, "a desert grows no moss");
			Assert.Less(Volcanic, 0.02f, "an ashy volcanic slope grows no moss");
			Assert.Less(Snow, 0.06f, "snowfields grow none");
			Assert.Less(IceSheet, 0.15f, "an ice sheet next to none");
			Assert.Less(Glacier, 0.2f, "a glacier little");
			Assert.Greater(Tundra, Glacier, "tundra keeps its moss: it needs liquid water, the ice does not");
		}

		[Test]
		public void HeatLeavesAWetBiomeAlone()
		{
			float temperateWet = Damp(0.6f, 1f, 0f, 0.5f, true);
			float tropicalWet = Damp(0.6f, 1f, 0.6f, 1f, true);
			Assert.AreEqual(temperateWet, tropicalWet, 0.02f, "a jungle is as mossy as a temperate swamp");
		}

		[Test]
		public void AWetland_IsWetWhateverItsEnvelopeSays()
		{
			float plain = Damp(-0.25f, 0.05f, 0f, 0.45f, true);
			float marsh = Damp(-0.25f, 0.05f, 0f, 0.45f, true, wetland: 0.95f);
			Assert.Less(plain, 0.5f);
			Assert.GreaterOrEqual(marsh, 0.95f);
		}

		[Test]
		public void NoBiome_IsNeutral_AndEveryAnswerIsInRange()
		{
			Assert.AreEqual(MossClimate.Neutral, MossClimate.Dampness((FishMMO.Shared.Biomes.BiomeTemplate)null));
			for (float h = -1f; h <= 1f; h += 0.25f)
			{
				for (float t = -1f; t <= 1f; t += 0.25f)
				{
					float d = Damp(h, Mathf.Min(1f, h + 0.5f), t, Mathf.Min(1f, t + 0.5f), h > 0f, h < -0.5f, t > 0.75f, Mathf.Abs(h) * 0.5f);
					Assert.That(d, Is.InRange(0f, 1f), $"h {h} t {t}");
				}
			}
		}

		[Test]
		public void Easing_LandsFirstThenFollows()
		{
			Assert.AreEqual(0.8f, MossClimate.Ease(float.NaN, 0.8f, 0.016f), 1e-6f, "nothing shown yet: lands at once");
			float step = MossClimate.Ease(0f, 1f, 1f);
			Assert.That(step, Is.InRange(0.01f, 0.1f), "a second covers a small part of an ecotone's change");
			float settled = 0f;
			for (int i = 0; i < 120; i++)
			{
				settled = MossClimate.Ease(settled, 1f, 1f);
			}
			Assert.Greater(settled, 0.99f, "two minutes settles it");
		}

		[Test]
		public void TheParams_CarryStrengthTileTexturesAndPoleward()
		{
			Vector4 p = MossShaderGlobals.ParamsFor(1.5f, 0.6f, 2f, true, 50f);
			Assert.AreEqual(0.9f, p.x, 1e-5f, "strength × dampness");
			Assert.AreEqual(0.5f, p.y, 1e-5f, "one over the tile");
			Assert.AreEqual(1f, p.z, "textures bound");
			Assert.AreEqual(1f, p.w, "north of the equator the north faces are the shaded ones");
			Vector4 south = MossShaderGlobals.ParamsFor(1f, 2f, 0f, false, -30f);
			Assert.AreEqual(1f, south.x, 1e-5f, "dampness clamps to 1");
			Assert.AreEqual(20f, south.y, 1e-3f, "a zero tile clamps instead of dividing by zero");
			Assert.AreEqual(0f, south.z, "no textures: the shader's plain green");
			Assert.AreEqual(-1f, south.w, "south of the equator, the south faces");
			Assert.AreEqual(0f, MossShaderGlobals.ParamsFor(-1f, 1f, 2f, true, 0f).x, "a negative strength grows none");
			Assert.AreEqual(0f, MossShaderGlobals.ParamsFor(1f, 1f, 2f, true, 0f).w, "on the equator no side is the shaded one");
		}

		[Test]
		public void TheHeightDecode_MatchesTheMossRecipe()
		{
			// The shader reads the moss's height back from the albedo's alpha with these; SurfaceSynth wrote it with the recipe's.
			Assert.IsTrue(SurfaceCatalogue.TryGround(Ground.Moss, out SurfaceRecipe recipe), "the moss recipe is gone");
			Assert.AreEqual(recipe.Smoothness, MossShaderGlobals.RecipeSmoothness, 1e-6f);
			Assert.AreEqual(recipe.SmoothnessVariation, MossShaderGlobals.RecipeSmoothnessVariation, 1e-6f);
			StringAssert.EndsWith(Ground.Moss + "_Albedo.png", MossShaderGlobals.AlbedoPath);
			StringAssert.EndsWith(Ground.Moss + "_Normal.png", MossShaderGlobals.NormalPath);
			Assert.AreEqual(ProceduralArtCatalogue.GroundTexture(Ground.Moss, "Albedo"), MossShaderGlobals.AlbedoPath);
			Assert.AreEqual(ProceduralArtCatalogue.GroundTexture(Ground.Moss, "Normal"), MossShaderGlobals.NormalPath);
		}

		[Test]
		public void RockAmounts_FollowTheStone()
		{
			foreach (RockType type in RockTypes.All)
			{
				Assert.That(MossAmounts.ForRockType(type.Name), Is.InRange(0f, 1f), type.Name);
			}
			Assert.Greater(MossAmounts.ForRockType("Limestone"), MossAmounts.ForRockType("Quartzite"), "lime stone holds water, quartzite sheds it");
			foreach (string none in new[] { "Halite", "Sulphur", "Sulphide", "Pyrite", "Firn", "WaterIce" })
			{
				Assert.AreEqual(0f, MossAmounts.ForRockType(none), none);
			}
			Assert.AreEqual(MossAmounts.ForRockType("Granite"), MossAmounts.ForLegacyRock("Grey"), "the legacy grey wears granite");
			Assert.AreEqual(0f, MossAmounts.ForLegacyRock("Coral"), "under the sea");
			Assert.AreEqual(0f, MossAmounts.ForLegacyRock("Ice"));
			Assert.AreEqual(MossAmounts.DefaultRock, MossAmounts.ForRockType("SomeFutureStone"));
		}

		[Test]
		public void StructureAmounts_FollowMaterialAndFinish()
		{
			foreach (StructureMaterial material in System.Enum.GetValues(typeof(StructureMaterial)))
			{
				foreach (StructureFinish finish in System.Enum.GetValues(typeof(StructureFinish)))
				{
					Assert.That(MossAmounts.ForStructure(material, finish), Is.InRange(0f, 1f), $"{material} {finish}");
				}
			}
			Assert.Greater(MossAmounts.ForStructure(StructureMaterial.Fieldstone, StructureFinish.None), 0.5f, "rough stone greens over");
			Assert.Greater(MossAmounts.ForStructure(StructureMaterial.Stone, StructureFinish.Mossy), MossAmounts.ForStructure(StructureMaterial.Stone, StructureFinish.None), "the Mossy finish is the heaviest");
			Assert.Less(MossAmounts.ForStructure(StructureMaterial.Timber, StructureFinish.Charred), MossAmounts.ForStructure(StructureMaterial.Timber, StructureFinish.None), "burnt wood grows little");
			foreach (StructureMaterial bare in new[] { StructureMaterial.Glass, StructureMaterial.Iron, StructureMaterial.Canvas, StructureMaterial.Dyed, StructureMaterial.Ash })
			{
				Assert.AreEqual(0f, MossAmounts.ForStructure(bare, StructureFinish.Mossy), $"{bare} never grows moss");
			}
		}

		[Test]
		public void TheMaterialAmount_IsInTheMaterialBuffer_AndBothShadersDeclareIt()
		{
			string input = File.ReadAllText(LitInput);
			int start = input.IndexOf("CBUFFER_START(UnityPerMaterial)");
			int end = input.IndexOf("CBUFFER_END", start);
			Assert.Greater(start, -1);
			StringAssert.Contains("half _FishMoss;", input.Substring(start, end - start), "outside UnityPerMaterial the SRP batcher drops every material on the shader");
			foreach (string shader in new[] { LitShader, IndirectShader })
			{
				StringAssert.Contains("_FishMoss(\"Moss on this surface\", Range(0.0, 1.0)) = 0.0", File.ReadAllText(shader), $"{shader}: off unless a material opts in");
			}
		}

		[Test]
		public void TheMossLibrary_AddsNoKeywordAndNoSamplerState()
		{
			string library = File.ReadAllText(MossLibrary);
			Assert.IsFalse(Regex.IsMatch(library, @"^\s*SAMPLER\s*\(", RegexOptions.Multiline), "a sampler of its own would be the forward pass's seventeenth on D3D11");
			Assert.IsFalse(library.Contains("#pragma"), "no keywords: a uniform branch on the amount instead");
			StringAssert.Contains("#define FISH_MOSS_SAMPLER sampler_BaseMap", library, "read with the base map's sampler, bound in every forward program");
			StringAssert.Contains("UNITY_BRANCH", library);
		}

		[Test]
		public void TheForwardPass_GrowsMossBeforeTheWeather()
		{
			string pass = File.ReadAllText(LitPass);
			StringAssert.Contains("#include \"FishMoss.hlsl\"", pass);
			int moss = pass.IndexOf("FishMossSurface(");
			int weather = pass.IndexOf("FishWeatherSurface(");
			Assert.Greater(moss, -1, "the forward pass no longer grows moss");
			Assert.Less(moss, weather, "moss must come before the weather, so rain darkens it and snow covers it");
		}
	}
}
