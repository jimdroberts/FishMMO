using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using FishMMO.Client;
using FishMMO.Shared.WorldDesign;
using Variant = FishMMO.Shared.WorldDesign.ProceduralArtVariants.Variant;
using MaterialKeywords = FishMMO.Shared.WorldDesign.ProceduralArtVariants.MaterialKeywords;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Look families (2026-10-28, TerrainGpuRenderer.Looks.cs): the props' many array-backed Weather Lit materials (their maps
	/// slices of the generated look arrays, ProceduralArtLookArrays) drawn through one family material each, their slices and
	/// scalars as a table row, the look named per vertex of the merged meshes — with no texture copied. Pure parts
	/// (TerrainLookMath), the shader's side of the contract, the variant collection, and the renderer's hooks.
	/// </summary>
	[TestFixture]
	public class TerrainLookArraysTests
	{
		private const string Shaders = "Assets/Prefabs/Client/Weather/Shaders/";
		private const string Terrain = "Assets/Scripts/Client/World/Terrain/";
		private const string WeatherLit = "FishMMO/Weather Lit";
		private const string WeatherLitIndirect = "FishMMO/Weather Lit Indirect";

		private static void Ordered(string text, params string[] anchors) => Assert.That(SourceScanPins.InOrder(text, anchors), Is.Null);

		[Test]
		public void BlockingKeywords_AgreeWithTheVariantCollection()
		{
			Assert.That(ProceduralArtVariants.LookKeyword, Is.EqualTo(TerrainLookMath.Keyword));
			Assert.That(ProceduralArtLookArrays.Keyword, Is.EqualTo(TerrainLookMath.Keyword));
			Assert.That(ProceduralArtVariants.LookTableKeyword, Is.EqualTo(TerrainLookMath.TableKeyword));
			Assert.That(ProceduralArtLookArrays.SlicesProperty, Is.EqualTo(TerrainLookMath.SlicesName));
			Assert.That(ProceduralArtLookArrays.EmissionSliceProperty, Is.EqualTo(TerrainLookMath.EmissionSliceName));
			Assert.That(ProceduralArtLookArrays.ArrayProperty(ProceduralArtLookArrays.Kind.Colour), Is.EqualTo(TerrainLookMath.ColourArrayName));
			Assert.That(ProceduralArtLookArrays.ArrayProperty(ProceduralArtLookArrays.Kind.Normal), Is.EqualTo(TerrainLookMath.NormalArrayName));
			Assert.That(ProceduralArtLookArrays.ArrayProperty(ProceduralArtLookArrays.Kind.Mask), Is.EqualTo(TerrainLookMath.MaskArrayName));
			Assert.That(ProceduralArtVariants.LookBlockingKeywords, Is.EquivalentTo(TerrainLookMath.BlockingKeywords));
			foreach (string k in TerrainLookMath.BlockingKeywords)
			{
				Assert.That(TerrainLookMath.KeywordsAllowLooks(new[] { "_NORMALMAP", k }), Is.False, k);
				Assert.That(ProceduralArtVariants.AllowsLooks(new[] { "_NORMALMAP", k }), Is.False, k);
			}
			Assert.That(TerrainLookMath.KeywordsAllowLooks(new[] { "_NORMALMAP", "_OCCLUSIONMAP", "_METALLICSPECGLOSSMAP", "_EMISSION", "_SPECULAR_SETUP" }), Is.True);
			Assert.That(TerrainLookMath.KeywordsAllowLooks(null), Is.True);
		}

		[Test]
		public void Slices_ComeFromTheMaterial_RoundedAndEmptyBelowZero()
		{
			var v = TerrainLookMath.LookValues.Untextured();
			v.SetSlices(new Vector4(3f, 0f, -1f, 2.0001f), 7f);
			Assert.That(v.AlbedoSlice, Is.EqualTo(3));
			Assert.That(v.NormalSlice, Is.EqualTo(0));
			Assert.That(v.GlossSlice, Is.EqualTo(-1));
			Assert.That(v.OcclusionSlice, Is.EqualTo(2));
			Assert.That(v.EmissionSlice, Is.EqualTo(7));
			v.SetSlices(new Vector4(-1f, -1f, -1f, -1f), -1f);
			Assert.That(v.EmissionSlice, Is.EqualTo(-1));
			Assert.That(TerrainLookMath.TableBytes(16), Is.EqualTo(16 * 7 * 8), "RGBAHalf: 8 bytes a texel");
		}

		[Test]
		public void FamilyKey_LeavesOutTheLooksOwnPropertiesAndTheEditorsLegacyOnes_KeepsTheRest()
		{
			foreach (string p in new[] { "_BaseColor", "_FishMoss", "_FishStructure", "_Smoothness", "_BaseMap", "_BumpMap", "_MetallicGlossMap", "_SpecGlossMap", "_OcclusionMap", "_EmissionMap", "_MainTex", "_Color",
				TerrainLookMath.SlicesName, TerrainLookMath.EmissionSliceName, TerrainLookMath.RowName })
			{
				Assert.That(TerrainLookMath.IsLookOrIgnored(p), Is.True, p);
			}
			// Render state, cave and day/night values, the arrays themselves and textures the table does not carry must match within a family.
			foreach (string p in new[] { "_Cull", "_ZWrite", "_Surface", "_FishVisible", "_FishCaveTiles", "_ParallaxMap", "_DetailAlbedoMap", "_QueueOffset",
				TerrainLookMath.ColourArrayName, TerrainLookMath.NormalArrayName, TerrainLookMath.MaskArrayName })
			{
				Assert.That(TerrainLookMath.IsLookOrIgnored(p), Is.False, p);
			}
		}

		[Test]
		public void TableRow_PutsEachValueInItsColumn()
		{
			var v = TerrainLookMath.LookValues.Untextured();
			v.BaseColor = new Color(0.1f, 0.2f, 0.3f, 0.4f);
			v.SpecColor = new Color(0.5f, 0.6f, 0.7f, 1f);
			v.EmissionColor = new Color(1.9f, 1.05f, 0.42f, 1f);
			v.Cutoff = 0.33f;
			v.Smoothness = 0.85f;
			v.Metallic = 0.25f;
			v.BumpScale = 1.5f;
			v.OcclusionStrength = 0.75f;
			v.Moss = 0.55f;
			v.Structure = 3f;
			v.WeatherAmount = 0.9f;
			v.AlbedoSlice = 7;
			v.NormalSlice = 2;
			v.GlossSlice = -1;
			v.OcclusionSlice = 3;
			v.EmissionSlice = 9;
			var pixels = new Color[TerrainLookMath.TableColumns * 3];
			TerrainLookMath.WriteRow(v, pixels, TerrainLookMath.TableColumns, 2);
			int at = 2 * TerrainLookMath.TableColumns;
			Assert.That(pixels[at + 0], Is.EqualTo(v.BaseColor));
			Assert.That(pixels[at + 1], Is.EqualTo(v.SpecColor));
			Assert.That(pixels[at + 2], Is.EqualTo(new Color(1.9f, 1.05f, 0.42f, 0.33f)), "emission rgb, cutoff in a");
			Assert.That(pixels[at + 3], Is.EqualTo(new Color(0.85f, 0.25f, 1.5f, 0.75f)));
			Assert.That(pixels[at + 4], Is.EqualTo(new Color(0.55f, 3f, 0.9f, 0f)));
			Assert.That(pixels[at + 5], Is.EqualTo(new Color(7f, 2f, -1f, 3f)));
			Assert.That(pixels[at + 6].r, Is.EqualTo(9f));
			Assert.That(pixels[0], Is.EqualTo(default(Color)), "other rows untouched");
			Assert.That(TerrainLookMath.TableColumns, Is.EqualTo(7));
		}

		[Test]
		public void Shader_ReadsTheTableInTheSameColumns()
		{
			string code = SourceScanPins.ReadCode(Shaders + "FishWeatherLitInput.hlsl");
			string setup = SourceScanPins.Body(code, "void FishLookSetup(float vertexLook)");
			Assert.That(setup, Is.Not.Null);
			Ordered(setup,
				"float instanceLook = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float, _FishLook);",
				"look = instanceLook >= 0.0 ? instanceLook : vertexLook;",
				"int row = (int)(look + 0.5);",
				"c0 = (half4)LOAD_TEXTURE2D(_FishLookTable, int2(0, row))",
				"c1 = (half4)LOAD_TEXTURE2D(_FishLookTable, int2(1, row))",
				"c2 = (half4)LOAD_TEXTURE2D(_FishLookTable, int2(2, row))",
				"c3 = (half4)LOAD_TEXTURE2D(_FishLookTable, int2(3, row))",
				"c4 = (half4)LOAD_TEXTURE2D(_FishLookTable, int2(4, row))",
				"FishLookBaseColor = c0",
				"FishLookSpecColor = c1",
				"FishLookEmissionColor = half4(c2.rgb",
				"FishLookCutoff = c2.a",
				"FishLookSmoothness = c3.x",
				"FishLookMetallic = c3.y",
				"FishLookBumpScale = c3.z",
				"FishLookOcclusionStrength = c3.w",
				"FishLookMoss = c4.x",
				"FishLookStructure = c4.y",
				"FishLookWeatherAmount = c4.z",
				"FishLookSlices = LOAD_TEXTURE2D(_FishLookTable, int2(5, row))",
				"FishLookEmissionSlice = LOAD_TEXTURE2D(_FishLookTable, int2(6, row)).x");
			// Every scalar the table carries is redirected to its static, and the tiling baked into uv0 is the identity.
			foreach (string name in TerrainLookMath.LookProperties)
			{
				if (name == TerrainLookMath.SlicesName || name == TerrainLookMath.EmissionSliceName || name == TerrainLookMath.RowName)
				{
					continue; // the slices are read through FISH_LOOK_SLICES, the row by FishLookSetup itself
				}
				StringAssert.IsMatch(@"#define " + name + @"\s+FishLook", code, name);
			}
			StringAssert.Contains("#define _BaseMap_ST             float4(1.0, 1.0, 0.0, 0.0)", code);
		}

		[Test]
		public void LookBlock_MaterialSlicesEverywhere_TheTableNeverOnCaveRock()
		{
			string code = SourceScanPins.ReadCode(Shaders + "FishWeatherLitInput.hlsl");
			// The material's own slices in every look variant (DOTS too: UnityPerMaterial); the table only off cave rock (TEXCOORD3).
			SourceScanPins.HoldsAndFires("FishWeatherLitInput.hlsl table guard", code,
				c => c.Contains("#if defined(_FISH_LOOK_TABLE) && !defined(_FISH_CAVE_ROCK)\n#define FISH_LOOK_TABLE 1") ? null : "the table is not kept off cave rock",
				SourceScanPins.Replace(" && !defined(_FISH_CAVE_ROCK)\n#define FISH_LOOK_TABLE 1", "\n#define FISH_LOOK_TABLE 1"),
				"the cave rock guard dropped");
			StringAssert.Contains("#if defined(_FISH_LOOK_ARRAY) || defined(_FISH_LOOK_TABLE)\n#define FISH_LOOK_ARRAYS 1", code);
			Ordered(code, "#define FISH_LOOK_SLICES        FishLookSlices", "#else", "#define FISH_LOOK_SLICES        _FishLookSlices");
			// The slices live in UnityPerMaterial (the SRP batcher's layout never changes), and the row has a DOTS override.
			Ordered(code, "CBUFFER_START(UnityPerMaterial)", "float4 _FishLookSlices;", "float _FishLookEmissionSlice;", "float _FishLook;", "CBUFFER_END");
			StringAssert.Contains("FISH_DOTS_METADATA(float , _FishLook)", code);
			// The sampled slots never branch round a sample (derivatives across a quad spanning two looks).
			StringAssert.Contains("#define FISH_LOOK_SAMPLE(tex, uv, slice, fallback) ((slice) < 0.0 ? (half4)(fallback) : (half4)SAMPLE_TEXTURE2D_ARRAY(tex, sampler##tex, uv, max(slice, 0.0)))", code);
			// The base map is read by no look program, so what borrowed its sampler borrows the colour array's (memory: the
			// borrowed-sampler trap: a sampler_<Texture> binds only where its texture is read).
			Ordered(code, "#define FISH_LOOK_ARRAYS 1", "#define FISH_MOSS_SAMPLER sampler_FishLookColour", "#define FISH_CAVE_SAMPLER sampler_FishLookColour");
			// Cave rock's empty albedo and normal slots read its own 2D maps (an inner wall over the shell).
			string albedo = SourceScanPins.Body(code, "half4 FishLookAlbedo(float2 uv)");
			Ordered(albedo, "#if defined(_FISH_CAVE_ROCK)", "SampleAlbedoAlpha(uv, TEXTURE2D_ARGS(_BaseMap, sampler_BaseMap))", "FISH_LOOK_SAMPLE(_FishLookColour, uv, FISH_LOOK_SLICES.x, own)");
			string normal = SourceScanPins.Body(code, "half3 FishLookNormal(float2 uv, half scale)");
			Ordered(normal, "#if defined(_FISH_CAVE_ROCK)", "FISH_LOOK_SLICES.y < 0.0 ? (half4)SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uv) : n");
			// URP's stock depth-normals pass reads the normal map through SampleNormal: redirected to the look in look variants.
			StringAssert.Contains("#define SampleNormal(uv, bumpMap, scale) FishLookNormal(uv, scale)", code);
		}

		[Test]
		public void ForwardPass_CarriesTheLookFromVertexToFragment()
		{
			string code = SourceScanPins.ReadCode(Shaders + "FishWeatherLitForwardPass.hlsl");
			Ordered(code,
				"#elif defined(FISH_LOOK_TABLE)",
				"float2 fishLook                 : TEXCOORD3;",
				"nointerpolation float fishLook  : TEXCOORD12;");
			string vertex = SourceScanPins.Body(code, "Varyings LitPassVertex(Attributes input)");
			Ordered(vertex, "UNITY_SETUP_INSTANCE_ID(input);", "FishLookSetup(input.fishLook.x);", "output.fishLook = input.fishLook.x;", "TRANSFORM_TEX(input.texcoord, _BaseMap)");
			string fragment = SourceScanPins.Body(code, "void LitPassFragment(");
			Ordered(fragment, "UNITY_SETUP_INSTANCE_ID(input);", "FishLookSetup(input.fishLook);", "InitializeStandardLitSurfaceData(input.uv, surfaceData);");
		}

		[Test]
		public void BothWeatherLitShaders_DeclareTheLookVariantsAndTheirProperties()
		{
			foreach (string shader in new[] { "FishWeatherLit.shader", "FishWeatherLitIndirect.shader" })
			{
				string code = SourceScanPins.ReadCode(Shaders + shader);
				foreach (string name in new[] { TerrainLookMath.ColourArrayName, TerrainLookMath.NormalArrayName, TerrainLookMath.MaskArrayName })
				{
					StringAssert.Contains($"[HideInInspector][NoScaleOffset] {name}(", code, $"{shader}: {name}");
				}
				StringAssert.Contains($"[HideInInspector][NoScaleOffset] {TerrainLookMath.TableName}(", code, shader);
				StringAssert.Contains($"[HideInInspector] {TerrainLookMath.SlicesName}(\"Look slices (albedo, normal, gloss, occlusion)\", Vector) = (-1, -1, -1, -1)", code, shader);
				StringAssert.Contains($"[HideInInspector] {TerrainLookMath.EmissionSliceName}(\"Look emission slice\", Float) = -1", code, shader);
				StringAssert.Contains($"[HideInInspector] {TerrainLookMath.RowName}(\"Look row (family table)\", Float) = -1", code, shader);
				// The table in ForwardLit only (a family is opaque and unclipped: its other passes sample nothing); the material's
				// arrays in every pass that samples its maps.
				string forward = Pass(code, "ForwardLit", "ShadowCaster");
				StringAssert.Contains("#pragma shader_feature_local _FISH_LOOK_ARRAY", forward, shader);
				StringAssert.Contains("#pragma shader_feature_local _FISH_LOOK_TABLE", forward, shader);
				Assert.That(code.IndexOf("_FISH_LOOK_TABLE", code.IndexOf("Name \"ShadowCaster\""), System.StringComparison.Ordinal), Is.LessThan(0), $"{shader}: only ForwardLit declares the table");
				StringAssert.Contains("#pragma shader_feature_local_fragment _FISH_LOOK_ARRAY", Pass(code, "GBuffer", "DepthOnly"), shader);
				StringAssert.Contains("#pragma shader_feature_local_fragment _FISH_LOOK_ARRAY", Pass(code, "DepthNormals", "Meta"), shader);
				StringAssert.Contains("#pragma shader_feature_local_fragment _FISH_LOOK_ARRAY", Pass(code, "Meta", "Universal2D"), shader);
				StringAssert.DoesNotContain("_FISH_LOOK_ARRAY", Pass(code, "ShadowCaster", "GBuffer"), $"{shader}: the caster samples nothing of an opaque look");
			}
		}

		private static string Pass(string code, string name, string next)
		{
			int a = code.IndexOf($"Name \"{name}\"", System.StringComparison.Ordinal);
			int b = code.IndexOf($"Name \"{next}\"", a, System.StringComparison.Ordinal);
			Assert.That(a, Is.GreaterThanOrEqualTo(0), name);
			Assert.That(b, Is.GreaterThan(a), next);
			return code.Substring(a, b - a);
		}

		[Test]
		public void BakeLook_TilesUv0AndNamesTheLook_OncePerVertexItsSubmeshUses()
		{
			var uv0 = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(2f, 2f), new Vector2(5f, 5f) };
			var uv3 = new Vector2[uv0.Length];
			var baked = new bool[uv0.Length];
			// Part 0 uses vertices 0..2 (vertex 1 twice), part 1 uses 3; vertex 4 is no submesh's.
			int n0 = TerrainLookMath.BakeLook(uv0, uv3, baked, new[] { 0, 1, 2, 2, 1, 0 }, new Vector4(0.5f, 0.5f, 0.25f, 0f), 6);
			int n1 = TerrainLookMath.BakeLook(uv0, uv3, baked, new[] { 3, 3, 3 }, new Vector4(1f / 3f, 1f / 3f, 0f, 0f), 2);
			Assert.That(n0, Is.EqualTo(3));
			Assert.That(n1, Is.EqualTo(1));
			Assert.That(uv0[0], Is.EqualTo(new Vector2(0.25f, 0f)));
			Assert.That(uv0[1], Is.EqualTo(new Vector2(0.75f, 0f)), "tiled once, not once per index");
			Assert.That(uv0[2], Is.EqualTo(new Vector2(0.25f, 0.5f)));
			Assert.That(uv0[3], Is.EqualTo(new Vector2(2f / 3f, 2f / 3f)));
			Assert.That(uv0[4], Is.EqualTo(new Vector2(5f, 5f)), "a vertex no submesh uses is left alone");
			Assert.That(uv3[0].x, Is.EqualTo(6f));
			Assert.That(uv3[2].x, Is.EqualTo(6f));
			Assert.That(uv3[3].x, Is.EqualTo(2f));
			Assert.That(uv3[4].x, Is.EqualTo(0f));
			// A pool whose source mesh had no uv0 still names its looks.
			var only = new Vector2[2];
			TerrainLookMath.BakeLook(null, only, new bool[2], new[] { 0, 1 }, new Vector4(2f, 2f, 0f, 0f), 4);
			Assert.That(only[1].x, Is.EqualTo(4f));
		}

		private static IReadOnlyDictionary<PassType, HashSet<string>> Passes(string twin)
		{
			if (twin != WeatherLitIndirect)
			{
				return null;
			}
			return new Dictionary<PassType, HashSet<string>>
			{
				{ PassType.ScriptableRenderPipeline, new HashSet<string> { "_ALPHATEST_ON", "_NORMALMAP", "_OCCLUSIONMAP", "_METALLICSPECGLOSSMAP", "_EMISSION", ProceduralArtVariants.LookKeyword, ProceduralArtVariants.LookTableKeyword } },
				{ PassType.ShadowCaster, new HashSet<string> { "_ALPHATEST_ON" } },
				{ PassType.MotionVectors, new HashSet<string> { "_ALPHATEST_ON" } },
			};
		}

		[Test]
		public void VariantCollection_ListsTheTableVariantOfEveryArrayBackedFamilySet_ForTheForwardPassOnly()
		{
			List<Variant> variants = ProceduralArtVariants.Variants(new[]
			{
				new MaterialKeywords("Assets/Structure_Timber.mat", WeatherLit, new[] { "_FISH_LOOK_ARRAY", "_NORMALMAP", "_OCCLUSIONMAP", "_METALLICSPECGLOSSMAP" }),
				// Not array-backed (a stone on a ground surface's 2D maps): no family, no table variant.
				new MaterialKeywords("Assets/Rock_Legacy.mat", WeatherLit, new[] { "_NORMALMAP" }),
				new MaterialKeywords("Assets/Leafy.mat", WeatherLit, new[] { "_NORMALMAP", "_ALPHATEST_ON" }),
			}, Passes);

			Assert.That(variants, Is.EqualTo(new[]
			{
				new Variant(WeatherLitIndirect, PassType.ShadowCaster, ""),
				new Variant(WeatherLitIndirect, PassType.ShadowCaster, "_ALPHATEST_ON"),
				new Variant(WeatherLitIndirect, PassType.MotionVectors, ""),
				new Variant(WeatherLitIndirect, PassType.MotionVectors, "_ALPHATEST_ON"),
				new Variant(WeatherLitIndirect, PassType.ScriptableRenderPipeline, "_ALPHATEST_ON _NORMALMAP"),
				new Variant(WeatherLitIndirect, PassType.ScriptableRenderPipeline, "_FISH_LOOK_ARRAY _FISH_LOOK_TABLE _METALLICSPECGLOSSMAP _NORMALMAP _OCCLUSIONMAP"),
				new Variant(WeatherLitIndirect, PassType.ScriptableRenderPipeline, "_FISH_LOOK_ARRAY _METALLICSPECGLOSSMAP _NORMALMAP _OCCLUSIONMAP"),
				new Variant(WeatherLitIndirect, PassType.ScriptableRenderPipeline, "_NORMALMAP"),
			}));
			Assert.That(ProceduralArtVariants.JoinsLookFamily(new[] { "_FISH_LOOK_ARRAY", "_SURFACE_TYPE_TRANSPARENT" }), Is.False, "a blocking keyword");
			Assert.That(ProceduralArtVariants.JoinsLookFamily(new[] { "_NORMALMAP" }), Is.False, "not array-backed");
		}

		[Test]
		public void Renderer_LaysLooksOutFirst_PoolsThemByLook_AndBakesThem()
		{
			string code = SourceScanPins.ReadCode(Terrain + "TerrainGpuRenderer.cs");
			string relayout = SourceScanPins.Body(code, "private void Relayout()");
			Ordered(relayout,
				"LooksReady();",
				"if (looksPending)",
				"batchedState = false;",
				"bool looked = LookCommand(part, out Material family, out int look);",
				"rp.material = looked ? family : part.Material;",
				"Look = looked ? look : -1",
				"GroupCommands();",
				"else if (looksState)",
				"PoolLookCommands();");
			StringAssert.Contains("PoolOf(command.Params.material, command.SourceMesh, command.SourceSubmesh, command.Look)", SourceScanPins.Body(code, "private void GroupCommands()"));
			StringAssert.Contains("c.Submesh = pool.Index[(c.SourceMesh, c.SourceSubmesh, c.Look)];", SourceScanPins.Body(code, "private void GroupCommands()"));
			StringAssert.Contains("PoolOf(command.Params.material, command.SourceMesh, command.SourceSubmesh, command.Look)", SourceScanPins.Body(code, "private bool PoolsReady()"));
			Ordered(SourceScanPins.Body(code, "private void BuildPool(MeshPool pool)"), "mesh.CombineMeshes(combine, false, false, false);", "BakePoolLooks(pool, mesh);");
			StringAssert.Contains("|| LooksChanged ||", code);
			StringAssert.Contains("BindLooks();", SourceScanPins.Body(code, "private void BindBlocks()"));
			StringAssert.Contains("DisposeLooks();", SourceScanPins.Body(code, "public void Dispose()"));
			StringAssert.Contains("case \"nolooks\":", SourceScanPins.ReadCode("Assets/TestHarness/World/Editor/ScenePerfProbe.cs"));
		}

		[Test]
		public void RockFillets_ReadTheRocksArrays_WhenItsMapsAreSlices()
		{
			// The fillets copy a rock material's properties onto their own shader: it must carry the arrays and slices, and read them.
			string shader = SourceScanPins.ReadCode(Shaders + "FishRockFillet.shader");
			foreach (string name in new[] { TerrainLookMath.ColourArrayName, TerrainLookMath.NormalArrayName, TerrainLookMath.MaskArrayName, TerrainLookMath.SlicesName })
			{
				StringAssert.Contains(name + "(", shader, name);
			}
			string pass = SourceScanPins.ReadCode(Shaders + "FishRockFilletPass.hlsl");
			string triplanar = SourceScanPins.Body(pass, "void FishFilletTriplanar(float3 positionWS, float3 n, out SurfaceData surface, out half3 normalWS)");
			Ordered(triplanar, "FISH_FILLET_MAP(_BaseMap, sampler_BaseMap, _FishLookColour, _FishLookSlices.x, uvX)",
				"FISH_FILLET_MAP(_MetallicGlossMap, sampler_MetallicGlossMap, _FishLookMask, _FishLookSlices.z, uvX)",
				"FISH_FILLET_MAP(_OcclusionMap, sampler_OcclusionMap, _FishLookMask, _FishLookSlices.w, uvX)",
				"FISH_FILLET_MAP(_BumpMap, sampler_BumpMap, _FishLookNormal, _FishLookSlices.y, uvX)");
			StringAssert.Contains("source.IsKeywordEnabled(TerrainLookMath.Keyword) ? 1f : 0f", SourceScanPins.ReadCode(Terrain + "RockFillets.cs"));
		}

		[Test]
		public void Families_AreArrayBackedMembers_AndCopyNoTexture()
		{
			string code = SourceScanPins.ReadCode(Terrain + "TerrainGpuRenderer.Looks.cs");
			string make = SourceScanPins.Body(code, "private LookRef MakeLook(Material clone)");
			Ordered(make, "!clone.IsKeywordEnabled(TerrainLookMath.Keyword)", "FindKeyword(TerrainLookMath.TableKeyword).isValid", "TerrainLookMath.KeywordsAllowLooks(clone.shaderKeywords)",
				"FamilyKey(clone)", "values.SetSlices(clone.GetVector(LookSlicesId)");
			Ordered(SourceScanPins.Body(code, "private LookFamily FamilyFor(string key, Material clone)"), "new Material(clone)", "material.EnableKeyword(TerrainLookMath.TableKeyword);");
			// The memory-free rule: no array is made or filled at run time; only the tables are.
			foreach (string banned in new[] { "Texture2DArray", "CopyTexture", "GraphicsFormat" })
			{
				StringAssert.DoesNotContain(banned, code, banned);
			}
		}
	}
}
