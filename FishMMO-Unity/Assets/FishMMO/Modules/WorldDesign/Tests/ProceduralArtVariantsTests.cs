using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.Rendering;
using FishMMO.Shared.WorldDesign;
using Variant = FishMMO.Shared.WorldDesign.ProceduralArtVariants.Variant;
using MaterialKeywords = FishMMO.Shared.WorldDesign.ProceduralArtVariants.MaterialKeywords;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The indirect shaders' variant collection: material keyword sets mapped onto the twin shaders, per
	/// pass type, cut to what each type declares, deduplicated and in a fixed order, with LOCAL materials
	/// left out — and the pure readers and writer around it.
	/// </summary>
	[TestFixture]
	public class ProceduralArtVariantsTests
	{
		private const string Vegetation = "FishMMO/Vegetation";
		private const string VegetationIndirect = "FishMMO/Vegetation Indirect";
		private const string WeatherLit = "FishMMO/Weather Lit";
		private const string WeatherLitIndirect = "FishMMO/Weather Lit Indirect";

		/// <summary>The twins' pass tables as their sources declare them (shader_feature keywords only).</summary>
		private static IReadOnlyDictionary<PassType, HashSet<string>> Passes(string twin)
		{
			if (twin == VegetationIndirect)
			{
				return new Dictionary<PassType, HashSet<string>>
				{
					{ PassType.ScriptableRenderPipeline, new HashSet<string> { "_ALPHATEST_ON", "_NORMALMAP" } },
					{ PassType.ShadowCaster, new HashSet<string> { "_ALPHATEST_ON" } },
				};
			}
			if (twin == WeatherLitIndirect)
			{
				return new Dictionary<PassType, HashSet<string>>
				{
					{ PassType.ScriptableRenderPipeline, new HashSet<string> { "_ALPHATEST_ON", "_NORMALMAP", "_OCCLUSIONMAP", "_METALLICSPECGLOSSMAP" } },
					{ PassType.ShadowCaster, new HashSet<string> { "_ALPHATEST_ON", "_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A" } },
					{ PassType.MotionVectors, new HashSet<string> { "_ALPHATEST_ON" } },
				};
			}
			return null;
		}

		private static MaterialKeywords Mat(string path, string shader, params string[] keywords) => new MaterialKeywords(path, shader, keywords);

		[Test]
		public void MapsOntoTheIndirectTwin_ForBothPassTypes()
		{
			List<Variant> variants = ProceduralArtVariants.Variants(new[]
			{
				Mat("Assets/Prefabs/Shared/Biomes/Generated/Materials/Grass.mat", Vegetation, "_ALPHATEST_ON", "_NORMALMAP"),
			}, Passes);

			Assert.That(variants, Is.EqualTo(new[]
			{
				new Variant(VegetationIndirect, PassType.ShadowCaster, "_ALPHATEST_ON"),
				new Variant(VegetationIndirect, PassType.ScriptableRenderPipeline, "_ALPHATEST_ON _NORMALMAP"),
			}));
			Assert.That(variants.TrueForAll(v => v.Shader != Vegetation), "never the source shader");
		}

		[Test]
		public void WeatherLit_MapsToItsTwin_AndCutsEachPassTypeToItsKeywords()
		{
			List<Variant> variants = ProceduralArtVariants.Variants(new[]
			{
				Mat("Assets/Art/Rock.mat", WeatherLit, "_NORMALMAP", "_OCCLUSIONMAP", "_ALPHATEST_ON"),
			}, Passes);

			Assert.That(variants, Is.EqualTo(new[]
			{
				new Variant(WeatherLitIndirect, PassType.ShadowCaster, "_ALPHATEST_ON"),
				new Variant(WeatherLitIndirect, PassType.MotionVectors, "_ALPHATEST_ON"),
				new Variant(WeatherLitIndirect, PassType.ScriptableRenderPipeline, "_ALPHATEST_ON _NORMALMAP _OCCLUSIONMAP"),
			}));
		}

		[Test]
		public void DuplicateSets_AppearOnce_WhateverTheKeywordOrder()
		{
			List<Variant> variants = ProceduralArtVariants.Variants(new[]
			{
				Mat("Assets/A.mat", Vegetation, "_NORMALMAP", "_ALPHATEST_ON"),
				Mat("Assets/B.mat", Vegetation, "_ALPHATEST_ON", "_NORMALMAP", "_ALPHATEST_ON"),
				Mat("Assets/C.mat", Vegetation, " _ALPHATEST_ON ", "", "_NORMALMAP"),
				// Same shadow-caster set as the others once _NORMALMAP is cut: still one ShadowCaster entry.
				Mat("Assets/D.mat", Vegetation, "_ALPHATEST_ON"),
			}, Passes);

			Assert.That(variants, Is.EqualTo(new[]
			{
				new Variant(VegetationIndirect, PassType.ShadowCaster, "_ALPHATEST_ON"),
				new Variant(VegetationIndirect, PassType.ScriptableRenderPipeline, "_ALPHATEST_ON"),
				new Variant(VegetationIndirect, PassType.ScriptableRenderPipeline, "_ALPHATEST_ON _NORMALMAP"),
			}));
		}

		[Test]
		public void SameMaterialsInAnyOrder_GiveTheSameListAndTheSameFile()
		{
			var a = new[]
			{
				Mat("Assets/Rock.mat", WeatherLit, "_NORMALMAP"),
				Mat("Assets/Grass.mat", Vegetation, "_ALPHATEST_ON"),
				Mat("Assets/Bark.mat", Vegetation, "_NORMALMAP"),
			};
			var b = new[] { a[2], a[0], a[1] };
			var guids = new Dictionary<string, string>
			{
				{ VegetationIndirect, "cd73c4ae024445f4ae5906f81f9924cb" },
				{ WeatherLitIndirect, "4b173f2533cf4a9bb0ef6cb07f841651" },
			};

			List<Variant> first = ProceduralArtVariants.Variants(a, Passes);
			List<Variant> second = ProceduralArtVariants.Variants(b, Passes);

			Assert.That(second, Is.EqualTo(first));
			Assert.That(ProceduralArtVariants.Serialize(second, guids), Is.EqualTo(ProceduralArtVariants.Serialize(first, guids)));
			var sorted = new List<Variant>(first);
			sorted.Sort();
			Assert.That(first, Is.EqualTo(sorted), "sorted by shader, pass type, keywords");
		}

		[Test]
		public void LocalMaterialsAreIncluded_OtherShadersAreLeftOut()
		{
			List<Variant> variants = ProceduralArtVariants.Variants(new[]
			{
				Mat("Assets/LOCAL/Biomes/Grass.mat", Vegetation, "_ALPHATEST_ON"),
				Mat("Assets/Art/Lit.mat", "Universal Render Pipeline/Lit", "_NORMALMAP"),
				Mat("Assets/Art/Indirect.mat", VegetationIndirect, "_NORMALMAP"),
			}, Passes);

			// A LOCAL scene can be built (Enable Local Directory), so its materials' keyword sets ship too.
			Assert.That(variants, Is.Not.Empty);
			Assert.That(ProceduralArtVariants.IsLocal("Assets/LOCAL/x.mat"), Is.True);
			Assert.That(ProceduralArtVariants.IsLocal("Assets/Prefabs/LOCAL/x.mat"), Is.False);
		}

		[Test]
		public void UnknownPasses_FallBackToTheDefaultPassTypes_KeywordsUncut()
		{
			List<Variant> variants = ProceduralArtVariants.Variants(new[]
			{
				Mat("Assets/Grass.mat", Vegetation, "_NORMALMAP", "_ALPHATEST_ON"),
			});

			Assert.That(variants.Count, Is.EqualTo(ProceduralArtVariants.DefaultPassTypes.Length));
			Assert.That(variants.Exists(v => v.PassType == PassType.ScriptableRenderPipeline), Is.True);
			Assert.That(variants.Exists(v => v.PassType == PassType.ShadowCaster), Is.True);
			Assert.That(variants.TrueForAll(v => v.Shader == VegetationIndirect && v.Keywords == "_ALPHATEST_ON _NORMALMAP"), Is.True);
		}

		[Test]
		public void KeywordlessMaterial_GivesTheEmptyVariant()
		{
			List<Variant> variants = ProceduralArtVariants.Variants(new[] { Mat("Assets/Plain.mat", Vegetation) }, Passes);

			Assert.That(variants, Is.EqualTo(new[]
			{
				new Variant(VegetationIndirect, PassType.ShadowCaster, ""),
				new Variant(VegetationIndirect, PassType.ScriptableRenderPipeline, ""),
			}));
		}

		[TestCase("UniversalForward", PassType.ScriptableRenderPipeline)]
		[TestCase("DepthOnly", PassType.ScriptableRenderPipeline)]
		[TestCase("DepthNormals", PassType.ScriptableRenderPipeline)]
		[TestCase("UniversalGBuffer", PassType.ScriptableRenderPipeline)]
		[TestCase("XRMotionVectors", PassType.ScriptableRenderPipeline)]
		[TestCase("ShadowCaster", PassType.ShadowCaster)]
		[TestCase("MotionVectors", PassType.MotionVectors)]
		[TestCase("SRPDefaultUnlit", PassType.ScriptableRenderPipelineDefaultUnlit)]
		[TestCase("", PassType.ScriptableRenderPipelineDefaultUnlit)]
		public void LightModes_MapToUnitysPassTypes(string lightMode, PassType expected)
		{
			Assert.That(ProceduralArtVariants.PassTypeOf(lightMode), Is.EqualTo(expected));
		}

		[Test]
		public void MetaPasses_AreLeftOut()
		{
			Assert.That(ProceduralArtVariants.PassTypeOf("Meta"), Is.Null);
		}

		[Test]
		public void Serialize_WritesUnitysCollectionFormat()
		{
			var variants = new List<Variant>
			{
				new Variant(VegetationIndirect, PassType.ShadowCaster, ""),
				new Variant(VegetationIndirect, PassType.ScriptableRenderPipeline, "_ALPHATEST_ON _NORMALMAP"),
				new Variant(WeatherLitIndirect, PassType.ScriptableRenderPipeline, "_NORMALMAP"),
			};
			var guids = new Dictionary<string, string>
			{
				{ VegetationIndirect, "cd73c4ae024445f4ae5906f81f9924cb" },
			};

			string text = ProceduralArtVariants.Serialize(variants, guids);

			Assert.That(text, Does.StartWith("%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!200 &20000000\nShaderVariantCollection:\n"));
			Assert.That(text, Does.Contain("  m_Name: FishIndirectVariants\n"));
			Assert.That(text, Does.Contain(
				"  m_Shaders:\n" +
				"  - first: {fileID: 4800000, guid: cd73c4ae024445f4ae5906f81f9924cb, type: 3}\n" +
				"    second:\n" +
				"      variants:\n" +
				"      - keywords:\n" +
				"        passType: 8\n" +
				"      - keywords: _ALPHATEST_ON _NORMALMAP\n" +
				"        passType: 13\n"));
			Assert.That(text.Split(new[] { "  - first:" }, System.StringSplitOptions.None).Length - 1, Is.EqualTo(1), "a shader with no GUID is left out");
			Assert.That(text, Does.Not.Contain("\r"));
			Assert.That(ProceduralArtVariants.Serialize(new List<Variant>(), guids), Does.EndWith("  m_Shaders: []\n"));
			Assert.That(ProceduralArtPayload.ShaderVariantCollectionFileId, Is.EqualTo(200L * 100000L));
		}

		[Test]
		public void TryParseMaterial_ReadsTheShaderGuidAndValidKeywords_OfTheMaterialObjectOnly()
		{
			const string text =
				"%YAML 1.1\n" +
				"%TAG !u! tag:unity3d.com,2011:\n" +
				"--- !u!114 &-1\n" +
				"MonoBehaviour:\n" +
				"  m_Shader: {fileID: 4800000, guid: ffffffffffffffffffffffffffffffff, type: 3}\n" +
				"--- !u!21 &2100000\n" +
				"Material:\n" +
				"  m_Name: Grass\n" +
				"  m_Shader: {fileID: 4800000, guid: 6AFB82F4835DA552494DF473A0F4BEE9, type: 3}\n" +
				"  m_ValidKeywords:\n" +
				"  - _ALPHATEST_ON\n" +
				"  - _NORMALMAP\n" +
				"  m_InvalidKeywords:\n" +
				"  - _STALE\n";

			Assert.That(ProceduralArtVariants.TryParseMaterial(text, out string guid, out List<string> keywords), Is.True);
			Assert.That(guid, Is.EqualTo("6afb82f4835da552494df473a0f4bee9"));
			Assert.That(keywords, Is.EqualTo(new[] { "_ALPHATEST_ON", "_NORMALMAP" }));
		}

		[Test]
		public void TryParseMaterial_ReadsEmptyFlowListsAndLegacyKeywords_AndRejectsBinary()
		{
			const string empty = "%YAML 1.1\n--- !u!21 &2100000\nMaterial:\n  m_Shader: {fileID: 4800000, guid: 6afb82f4835da552494df473a0f4bee9, type: 3}\n  m_ValidKeywords: []\n  m_InvalidKeywords: []\n";
			Assert.That(ProceduralArtVariants.TryParseMaterial(empty, out _, out List<string> none), Is.True);
			Assert.That(none, Is.Empty);

			const string legacy = "%YAML 1.1\n--- !u!21 &2100000\nMaterial:\n  m_Shader: {fileID: 4800000, guid: 6afb82f4835da552494df473a0f4bee9, type: 3}\n  m_ShaderKeywords: _NORMALMAP _ALPHATEST_ON\n";
			Assert.That(ProceduralArtVariants.TryParseMaterial(legacy, out _, out List<string> old), Is.True);
			Assert.That(old, Is.EqualTo(new[] { "_NORMALMAP", "_ALPHATEST_ON" }));

			Assert.That(ProceduralArtVariants.TryParseMaterial("\0\0binary", out _, out _), Is.False);
		}

		[Test]
		public void TheCollection_IsGeneratedArt_WithAPathDerivedGuid()
		{
			Assert.That(ProceduralArtVariants.AssetPath, Is.EqualTo("Assets/Prefabs/Shared/Biomes/Generated/Variants/FishIndirectVariants.shadervariants"));
			Assert.That(ProceduralArtPayload.IsGenerated(ProceduralArtVariants.AssetPath), Is.True);
			Assert.That(ProceduralArtPayload.IsPayload(ProceduralArtVariants.AssetPath), Is.False, "beside the wrappers, not in the payload folder");
			Assert.That(ProceduralArtPayload.GuidFor(ProceduralArtVariants.AssetPath), Does.Match("^[0-9a-f]{32}$"));
		}
	}
}
