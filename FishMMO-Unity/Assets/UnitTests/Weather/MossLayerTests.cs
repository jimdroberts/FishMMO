using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using FishMMO.Client;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.WorldDesign;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The moss layer in the world lit shader (FishMoss.hlsl; 2026-10-12): the biome dampness that scales it, the
	/// globals it reads, the amounts the art generator gives each material, and the shader-side contracts — one material
	/// constant in UnityPerMaterial, no keyword and no sampler state of its own (the forward pass is at D3D11's sixteen).
	/// The vegetation shader grows the same layer for the deadwood, from the same library and the same constant.
	/// </summary>
	public class MossLayerTests
	{
		private const string Shaders = "Assets/Prefabs/Client/Weather/Shaders/";
		private const string MossLibrary = Shaders + "FishMoss.hlsl";
		private const string LitPass = Shaders + "FishWeatherLitForwardPass.hlsl";
		private const string LitInput = Shaders + "FishWeatherLitInput.hlsl";
		private const string LitShader = Shaders + "FishWeatherLit.shader";
		private const string IndirectShader = Shaders + "FishWeatherLitIndirect.shader";
		private const string VegetationPasses = Shaders + "FishVegetationPasses.hlsl";
		private static readonly string[] VegetationShaders =
		{
			Shaders + "FishVegetation.shader", Shaders + "FishVegetationIndirect.shader", Shaders + "FishVegetationPrime.shader",
		};
		private const string SurfaceLibrary = Shaders + "FishSurface.hlsl";
		private const string GroundContactLit = Shaders + "FishGroundContactLit.hlsl";
		private static readonly string[] TerrainPasses =
		{
			Shaders + "FishWeatherTerrainPasses.hlsl", Shaders + "FishWeatherTerrainArrayPasses.hlsl",
		};
		private const string DeadwoodGenerator = "Assets/Scripts/Shared/Implementation/Tools/Extensions/Unity/Editor/World/ProceduralArt/BiomeArtGenerator.Deadwood.cs";
		private const string RockyCoastAsset = "Assets/Templates/Entity/Biomes/Rocky Coast.asset";

		// A biome's envelope, in BiomeTemplate's terms (-1..1), as the assets under Templates/Entity/Biomes hold them.
		private static float Damp(float minH, float maxH, float minT, float maxT, bool liquid, bool loose = false, bool emits = false, float wetland = 0f, bool seaShore = false)
		{
			return MossClimate.Dampness(minH, maxH, minT, maxT, liquid, loose, emits, wetland, seaShore);
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
		// The shore tier: a cold rocky coast that takes any humidity (needs the world's water, not liquid water itself),
		// and a warm sandy beach whose own envelope is dry.
		private static float RockyCoast => Damp(-1f, 1f, -1f, 0.15f, false, seaShore: true);
		private static float Beach => Damp(-1f, 0.4f, 0.1f, 1f, true, seaShore: true);

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
		public void ASeaShore_BreathesTheSeaAir()
		{
			Assert.Greater(RockyCoast, 0.6f, "spray and sea fog keep a rocky coast's stone damp");
			Assert.Greater(RockyCoast, Grassland, "a rocky coast is damper than open grassland");
			Assert.Less(RockyCoast, Forest, "and drier than a forest");
			float inland = Damp(-1f, 1f, -1f, 0.15f, false);
			Assert.Less(inland, 0.2f, "the same envelope away from the sea is cold ground with no liquid water: ice");
			Assert.Less(Beach, 0.4f, "a dry sandy beach keeps what its own envelope says");
			float wetShore = Damp(0.4f, 1f, -0.3f, 0.6f, true, seaShore: true);
			Assert.AreEqual(Damp(0.4f, 1f, -0.3f, 0.6f, true), wetShore, 1e-6f, "an estuary's envelope already says wet: the sea changes nothing");
		}

		[Test]
		public void TheSeaShore_IsTheShoreTierBesideOpenWater()
		{
			var biome = ScriptableObject.CreateInstance<BiomeTemplate>();
			try
			{
				biome.ElevationTier = MossClimate.ShoreTier;
				biome.RequiresLiquidWater = false;
				biome.Requires = BiomeWorldRequirement.SurfaceWater;
				Assert.IsTrue(MossClimate.IsSeaShore(biome), "a rocky coast: the world's water beside it");
				biome.Requires = BiomeWorldRequirement.None;
				biome.RequiresLiquidWater = true;
				Assert.IsTrue(MossClimate.IsSeaShore(biome), "a beach: liquid water");
				biome.RequiresLiquidWater = false;
				Assert.IsFalse(MossClimate.IsSeaShore(biome), "a shore-tier biome on a world with no water has no sea");
				biome.Requires = BiomeWorldRequirement.IceWorld;
				Assert.IsFalse(MossClimate.IsSeaShore(biome), "an ice shelf: the sea is frozen through, no spray");
				biome.Requires = BiomeWorldRequirement.SurfaceWater;
				biome.ElevationTier = MossClimate.ShoreTier + 1;
				Assert.IsFalse(MossClimate.IsSeaShore(biome), "lowland is not the shore");
			}
			finally
			{
				Object.DestroyImmediate(biome);
			}
			Assert.IsFalse(MossClimate.IsSeaShore(null));
		}

		[Test]
		public void TheRockyCoastAsset_IsDamp()
		{
			var coast = AssetDatabase.LoadAssetAtPath<BiomeTemplate>(RockyCoastAsset);
			Assert.IsNotNull(coast, RockyCoastAsset);
			Assert.IsTrue(MossClimate.IsSeaShore(coast), "the Rocky Coast is a sea shore");
			Assert.Greater(MossClimate.Dampness(coast), 0.6f, "the Rocky Coast's spray keeps its rocks mossy");
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
					float shore = Damp(h, Mathf.Min(1f, h + 0.5f), t, Mathf.Min(1f, t + 0.5f), h > 0f, h < -0.5f, t > 0.75f, Mathf.Abs(h) * 0.5f, true);
					Assert.That(shore, Is.InRange(0f, 1f), $"shore h {h} t {t}");
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
		public void TheSurfaceVector_CarriesTheSprayStrength_WithoutTheDampness()
		{
			Vector4 surface = MossShaderGlobals.SurfaceFor(1.5f);
			Assert.AreEqual(MossShaderGlobals.RecipeSmoothness, surface.x, 1e-6f);
			Assert.AreEqual(1f / MossShaderGlobals.RecipeSmoothnessVariation, surface.y, 1e-5f);
			Assert.AreEqual(1.5f, surface.z, 1e-6f, "a fall's spray grows moss at the profile's strength, damp in any climate");
			Assert.AreEqual(0f, MossShaderGlobals.SurfaceFor(-1f).z, "a negative strength grows none, spray or not");
			Assert.AreEqual(0f, MossShaderGlobals.SurfaceFor(0f).z, "moss off is off round a fall too");
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
		public void DeadwoodAmounts_FollowTheBark()
		{
			foreach (string family in new[] { Bark.Brown, Bark.Pine, Bark.Birch, Bark.Palm, Bark.Cactus, Bark.Bamboo, Bark.Dead,
				Bark.Smooth, Bark.Aspen, Bark.Olive, Bark.DatePalm, Bark.Fibrous })
			{
				Assert.That(MossAmounts.ForDeadwoodBark(family), Is.InRange(0f, 1f), family);
			}
			foreach (DeadwoodSpecies species in ProceduralArtCatalogue.Deadwood)
			{
				// Wood with its bark gone (driftwood, a petrified log, and the authored rounds' silver snags, stakes and bones on
				// Bark.Dead, salted and bleached) is bare; every barked log or stump greens over.
				if (species.Form != DeadwoodForm.PetrifiedLog && species.Form != DeadwoodForm.Driftwood && species.BarkFamily != Bark.Dead)
				{
					Assert.Greater(MossAmounts.ForDeadwoodBark(species.BarkFamily), 0.5f, $"{species.Name}: a rotting log or stump greens over");
				}
			}
			Assert.Greater(MossAmounts.ForDeadwoodBark(Bark.Brown), MossAmounts.ForDeadwoodBark(Bark.Birch), "furrowed bark holds more than papery birch");
			Assert.Greater(MossAmounts.ForDeadwoodBark(Bark.Brown), MossAmounts.ForRockType("Granite"), "a rotting log greens over before the stone beside it");
			Assert.Less(MossAmounts.ForDeadwoodBark(Bark.Dead), 0.2f, "driftwood: bark gone, salted and bleached");
			Assert.That(MossAmounts.DeadwoodWood, Is.InRange(0.01f, 1f), "a stump's top and a root plate's earth green over");
			Assert.AreEqual(MossAmounts.ForRockType("Quartzite"), MossAmounts.PetrifiedLog, "a petrified log is silica");
			Assert.AreEqual(MossAmounts.DefaultDeadwoodBark, MossAmounts.ForDeadwoodBark("SomeFutureBark"));
		}

		[Test]
		public void TheDeadwoodMaterials_AskForMoss()
		{
			// Every material the deadwood step writes sets its amount: the bark by family, the petrified stone, the wood.
			string code = File.ReadAllText(DeadwoodGenerator);
			StringAssert.Contains("m.SetFloat(\"_FishMoss\", MossAmounts.ForDeadwoodBark(family));", code);
			StringAssert.Contains("m.SetFloat(\"_FishMoss\", MossAmounts.PetrifiedLog);", code);
			StringAssert.Contains("m.SetFloat(\"_FishMoss\", MossAmounts.DeadwoodWood);", code);
			Assert.That(ProceduralArtFamilies.FamiliesOfFile("MossAmounts.cs"), Does.Contain(ArtFamily.Deadwood), "a change to the amounts rebuilds the deadwood's materials");
		}

		[Test]
		public void TheVegetationShader_GrowsTheSameMoss_ForTheMaterialsThatAsk()
		{
			string passes = File.ReadAllText(VegetationPasses);
			int start = passes.IndexOf("CBUFFER_START(UnityPerMaterial)");
			int end = passes.IndexOf("CBUFFER_END", start);
			Assert.Greater(start, -1);
			StringAssert.Contains("half _FishMoss;", passes.Substring(start, end - start), "outside UnityPerMaterial the SRP batcher drops every vegetation material");
			foreach (string shader in VegetationShaders)
			{
				StringAssert.Contains("_FishMoss(\"Moss on this surface\", Range(0.0, 1.0)) = 0.0", File.ReadAllText(shader), $"{shader}: off unless a material opts in, and a clone keeps the value");
			}
			StringAssert.Contains("#include \"FishMoss.hlsl\"", passes, "the same library, not a copy");
			int repeat = passes.IndexOf("#define FISH_MOSS_SAMPLER sampler_TrilinearRepeat");
			Assert.Greater(repeat, -1, "the deadwood's wood wears the Clamp foliage atlas: its base map sampler would smear world-projected moss");
			Assert.Less(repeat, passes.IndexOf("#include \"FishMoss.hlsl\""), "defined before the library, or the library takes the base map's");
			int sampler = passes.IndexOf("#define FISH_GROUND_MAP_SAMPLER sampler_LinearClamp");
			int groundMap = passes.IndexOf("#include \"FishGroundMap.hlsl\"");
			Assert.Greater(sampler, -1, "the fine ground map is read with a sampler the pass already binds");
			Assert.Less(sampler, groundMap, "defined before the ground map is included, or it declares a sampler of its own");
			int moss = passes.IndexOf("FishMossSurface(");
			int weather = passes.IndexOf("FishWeatherFoliage(");
			Assert.Greater(moss, -1, "the vegetation forward pass no longer grows moss");
			Assert.Less(moss, weather, "moss must come before the weather, so rain darkens it and snow covers it");
			StringAssert.Contains("surface.occlusion = occlusion;", passes, "the moss's own hollows reach the lighting");
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
		public void TheSpraysMoss_IsTheTexturedLayer_NotAFlatTint()
		{
			// The spray's moss used to be a constant dark green mixed in by FishFallSurface: no texture, no normal, no clumps.
			string surface = File.ReadAllText(SurfaceLibrary);
			int start = surface.IndexOf("void FishFallSurface(");
			int end = surface.IndexOf("void FishWeatherSurface(", start);
			Assert.Greater(start, -1);
			string fall = surface.Substring(start, end - start);
			StringAssert.DoesNotContain("mossColour", fall, "the spray paints no moss colour of its own");
			StringAssert.DoesNotContain("half3(0.06, 0.11, 0.035)", fall, "the old flat green is gone");
			StringAssert.Contains("float spray = FishFallSpray(", surface, "the spray still wets what is round a fall");

			string library = File.ReadAllText(MossLibrary);
			int growth = library.IndexOf("half FishFallMossGrowth(");
			Assert.Greater(growth, -1, "the spray's growth lives in the moss library");
			string growthBody = library.Substring(growth, library.IndexOf("\n}", growth) - growth);
			StringAssert.Contains("FishFallSpray(", growthBody, "the same zone the weather wets");
			StringAssert.Contains("_FishMossSurface.z", growthBody, "scaled by the profile's strength");
			StringAssert.DoesNotContain("_FishMossParams.x", growthBody, "and not by the scene's dampness: spray is always damp");
			StringAssert.Contains("smoothstep(", growthBody, "a band with soft edges, never a cut");
			int grow = library.IndexOf("half FishMossGrow(");
			int fallMoss = library.IndexOf("half FishFallMoss(");
			int surfaceMoss = library.IndexOf("half FishMossSurface(");
			Assert.Greater(grow, -1, "one shared look for every moss");
			Assert.That(fallMoss, Is.GreaterThan(grow), "the spray-only entry ends in the shared look");
			Assert.That(surfaceMoss, Is.GreaterThan(grow), "the material entry ends in the shared look");
			StringAssert.Contains("FishFallMossGrowth(positionWS, n)", library.Substring(surfaceMoss), "a material's moss grows by a fall too");
			string growBody = library.Substring(grow, fallMoss - grow);
			StringAssert.DoesNotContain("ddx(", growBody, "the shared look takes no derivative: it runs in the band's divergent branch");
			StringAssert.Contains("_FishMossAlbedo", library.Substring(library.IndexOf("void FishMossPlane(")), "the moss textures");
			Assert.That(library.IndexOf("#if !defined(FISH_MOSS_FALL_ONLY)"), Is.InRange(0, surfaceMoss), "the terrain, with no material amount or ground map, compiles without the material entry");
		}

		[Test]
		public void TheGround_GrowsTheSpraysMoss_BeforeTheWeather()
		{
			foreach (string path in TerrainPasses)
			{
				string pass = File.ReadAllText(path);
				int only = pass.IndexOf("#define FISH_MOSS_FALL_ONLY");
				int sampler = pass.IndexOf("#define FISH_MOSS_SAMPLER ");
				int include = pass.IndexOf("#include \"FishMoss.hlsl\"");
				Assert.Greater(include, -1, $"{path}: the same library, not a copy");
				Assert.That(only, Is.InRange(0, include), $"{path}: the spray's moss only, defined before the library");
				Assert.That(sampler, Is.InRange(0, include), $"{path}: a sampler the pass already binds, or the library takes the base map's, which the terrain has none of");
				Assert.Less(pass.IndexOf("#include \"FishSurface.hlsl\""), include, $"{path}: the library reads the weather's spray zone");
				int moss = pass.IndexOf("FishFallMoss(");
				int weather = pass.IndexOf("FishWeatherSurface(");
				Assert.Greater(moss, -1, $"{path}: the ground round a fall grows no moss");
				Assert.Less(moss, weather, $"{path}: moss before the weather, so the spray's wet darkens it");
			}
			Assert.That(File.ReadAllText(TerrainPasses[0]), Does.Contain("#define FISH_MOSS_SAMPLER sampler_Splat0"), "SplatmapMix reads every layer with the first's sampler");
			Assert.That(File.ReadAllText(TerrainPasses[1]), Does.Contain("#define FISH_MOSS_SAMPLER sampler_FishAlbedoArray"), "the layers' own repeat sampler");

			string contact = File.ReadAllText(GroundContactLit);
			int bandMoss = contact.IndexOf("FishFallMoss(");
			Assert.Greater(bandMoss, -1, "a rock's or a trunk's foot renders the terrain: it must grow the terrain's moss, or the band is a bare ring");
			Assert.Less(bandMoss, contact.IndexOf("FishWeatherSurface("));
			foreach (string path in new[] { LitPass, VegetationPasses })
			{
				string pass = File.ReadAllText(path);
				int library = pass.IndexOf("#include \"FishMoss.hlsl\"");
				int lit = pass.IndexOf("#include \"FishGroundContactLit.hlsl\"");
				Assert.Greater(lit, library, $"{path}: the ground band's lighting comes after the moss it calls");
				StringAssert.Contains("FishGroundLit(inputData, ", pass);
			}
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
