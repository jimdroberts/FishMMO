using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using FishMMO.Client;
using NUnit.Framework;
using UnityEngine;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The GPU-driven props' and trees' BatchRendererGroup path (TerrainGpuRenderer.Brg.cs, TerrainBrgMath): the packed visible
	/// index and its LOD fade byte, the batch buffer's layout and matrices, and the shader, compute and renderer pieces that
	/// must agree on them. The GPU half cannot run here, so its rules are pinned twice: the C# mirror is tested for what the
	/// look needs (the same dither partition, the same matrices), and the HLSL is pinned to say what the mirror says.
	/// </summary>
	public class TerrainBrgTests
	{
		private const string Shaders = "Assets/Prefabs/Client/Weather/Shaders/";
		private const string Compute = Shaders + "FishTerrainInstancing.compute";
		private const string Include = Shaders + "FishIndirectInstancing.hlsl";
		private const string LodFade = Shaders + "FishLodFade.hlsl";
		private const string GroundColour = Shaders + "FishGroundColour.hlsl";
		private const string CasterCulling = Shaders + "FishCasterCulling.hlsl";
		private const string Renderer = "Assets/Scripts/Client/World/Terrain/TerrainGpuRenderer.cs";
		private const string Brg = "Assets/Scripts/Client/World/Terrain/TerrainGpuRenderer.Brg.cs";
		private const string Stripper = "Assets/Scripts/Shared/Implementation/Tools/Extensions/Unity/Editor/FishMMO Dashboard/CustomBuildTool/Addressables/ClientShaderBuildProcessor.cs";
		private const string DotsInclude = "#include_with_pragmas \"Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl\"";
		private const string IndirectInclude = "#include \"FishIndirectInstancing.hlsl\"";

		// ── The fade byte ────────────────────────────────────────────────

		/// <summary>FishSurface.hlsl FishDitherThreshold's sixteen thresholds.</summary>
		private static IEnumerable<float> Thresholds()
		{
			for (int k = 0; k < TerrainBrgMath.FadeSteps; k++)
			{
				yield return k / 16f;
			}
		}

		/// <summary>Fades across [−1, 1]: a fine sweep and both sides of every step edge.</summary>
		private static IEnumerable<float> Fades()
		{
			for (int i = -20000; i <= 20000; i++)
			{
				yield return i / 20000f;
			}
			for (int k = 1; k <= 16; k++)
			{
				float edge = k / 16f;
				foreach (float f in new[] { edge, edge - 1e-6f, edge + 1e-6f, -edge, -edge + 1e-6f, -edge - 1e-6f })
				{
					yield return Mathf.Clamp(f, -1f, 1f);
				}
			}
			yield return 1e-7f;
			yield return -1e-7f;
		}

		[Test]
		public void TheFadeByte_KeepsExactlyThePixelsTheFloatKept()
		{
			foreach (float fade in Fades())
			{
				float back = TerrainBrgMath.UnpackLodFade(TerrainBrgMath.PackLodFade(fade));
				foreach (float t in Thresholds())
				{
					Assert.AreEqual(TerrainBrgMath.Keeps(fade, t), TerrainBrgMath.Keeps(back, t),
						$"fade {fade:R} packs to {back:R}, which {(TerrainBrgMath.Keeps(back, t) ? "keeps" : "drops")} the pixel at threshold {t}");
				}
			}
		}

		[Test]
		public void TheFadeByte_IsZeroForZeroAndOppositeForOppositeFades()
		{
			Assert.AreEqual(0u, TerrainBrgMath.PackLodFade(0f), "0 is fully drawn and must stay 0");
			Assert.AreEqual(0u, TerrainBrgMath.PackLodFade(-0f));
			foreach (float fade in Fades())
			{
				if (fade == 0f)
				{
					continue;
				}
				Assert.AreNotEqual(0, TerrainBrgMath.FadeByte(fade), $"fade {fade:R} packed to 0, which would draw it whole");
				Assert.AreEqual(-TerrainBrgMath.FadeByte(fade), TerrainBrgMath.FadeByte(-fade), $"+{fade} and -{fade} must stay a partition");
				Assert.LessOrEqual(Mathf.Abs(TerrainBrgMath.FadeByte(fade)), 127);
			}
			// A level fading in at f and its partner fading out at -f split the pattern between them, pixel for pixel.
			foreach (float fade in Fades())
			{
				float a = TerrainBrgMath.UnpackLodFade(TerrainBrgMath.PackLodFade(fade));
				float b = TerrainBrgMath.UnpackLodFade(TerrainBrgMath.PackLodFade(-fade));
				foreach (float t in Thresholds())
				{
					if (fade != 0f)
					{
						Assert.AreNotEqual(TerrainBrgMath.Keeps(a, t), TerrainBrgMath.Keeps(b, t), $"fade {fade:R}: both or neither level draws the pixel at {t}");
					}
				}
			}
		}

		[Test]
		public void TheVisibleIndex_CarriesTheIndexAndTheFade()
		{
			foreach (uint index in new uint[] { 0, 1, 12345, (uint)TerrainBrgMath.MaxInstances - 1 })
			{
				foreach (float fade in new[] { 0f, 0.3f, -0.3f, 1f, -1f })
				{
					uint packed = TerrainBrgMath.PackVisible(index, fade);
					Assert.AreEqual(index, TerrainBrgMath.UnpackIndex(packed));
					Assert.AreEqual(TerrainBrgMath.FadeByte(fade) / 127f, TerrainBrgMath.UnpackLodFade(packed), 1e-6f);
				}
			}
		}

		// ── The batch buffer ─────────────────────────────────────────────

		[Test]
		public void TheBatchBuffer_HasItsHeaderConstantsAndTwoArrays()
		{
			Assert.AreEqual(64, TerrainBrgMath.HeaderBytes);
			Assert.AreEqual(0, TerrainBrgMath.ObjectToWorldAddress % 16, "a float3x4 array starts on 16 bytes");
			Assert.GreaterOrEqual(TerrainBrgMath.ContactOnAddress, TerrainBrgMath.HeaderBytes, "the constants may not sit in the zero header");
			Assert.Less(TerrainBrgMath.FloraOnAddress + 4, TerrainBrgMath.ObjectToWorldAddress + 1);
			Assert.AreEqual(TerrainBrgMath.ObjectToWorldAddress + 48 * 1000, TerrainBrgMath.WorldToObjectAddress(1000));
			Assert.AreEqual((80 + 96 * 1000) / 4, TerrainBrgMath.BufferWords(1000));
			Assert.AreEqual(0x80000050u, TerrainBrgMath.PerInstance(80));
			uint[] header = TerrainBrgMath.Header(1f, 2f);
			Assert.AreEqual(TerrainBrgMath.ObjectToWorldAddress / 4, header.Length);
			for (int w = 0; w < TerrainBrgMath.HeaderBytes / 4; w++)
			{
				Assert.AreEqual(0u, header[w], $"header word {w}: every property without metadata reads here");
			}
			Assert.AreEqual(1f, BitConverter.Int32BitsToSingle((int)header[TerrainBrgMath.ContactOnAddress / 4]));
			Assert.AreEqual(2f, BitConverter.Int32BitsToSingle((int)header[TerrainBrgMath.FloraOnAddress / 4]));
		}

		/// <summary>A T·R·S with a rotation from two angles and a positive (w, h, w) scale, built without native maths.</summary>
		private static FishInstance Instance(float yaw, float tilt, float w, float h, Vector3 t)
		{
			float cy = Mathf.Cos(yaw), sy = Mathf.Sin(yaw), cx = Mathf.Cos(tilt), sx = Mathf.Sin(tilt);
			// R = Ry · Rx, columns scaled by w, h, w.
			var c0 = new Vector3(cy, 0f, -sy) * w;
			var c1 = new Vector3(sy * sx, cx, cy * sx) * h;
			var c2 = new Vector3(sy * cx, -sx, cy * cx) * w;
			return new FishInstance
			{
				Row0 = new Vector4(c0.x, c1.x, c2.x, t.x),
				Row1 = new Vector4(c0.y, c1.y, c2.y, t.y),
				Row2 = new Vector4(c0.z, c1.z, c2.z, t.z),
			};
		}

		[Test]
		public void TheMatrices_LoadAsTheInstanceAndItsInverse()
		{
			var f = new float[12];
			var g = new float[12];
			for (int i = 0; i < 40; i++)
			{
				FishInstance inst = Instance(i * 0.37f, (i % 7) * 0.05f, 0.5f + (i % 5) * 0.4f, 0.6f + (i % 3) * 0.9f, new Vector3(i * 31.5f - 600f, i * 0.7f, 1200f - i * 17f));
				TerrainBrgMath.ObjectToWorld(inst, f);
				TerrainBrgMath.WorldToObject(inst, g);
				Matrix4x4 m = TerrainBrgMath.Load3x4(f);
				Matrix4x4 expected = inst.ToMatrix();
				for (int k = 0; k < 16; k++)
				{
					Assert.AreEqual(expected[k], m[k], 1e-5f, $"instance {i}: unity_ObjectToWorld element {k}");
				}
				Matrix4x4 product = TerrainBrgMath.Load3x4(g) * m;
				for (int r = 0; r < 4; r++)
				{
					for (int c = 0; c < 4; c++)
					{
						Assert.AreEqual(r == c ? 1f : 0f, product[r, c], 1e-4f, $"instance {i}: unity_WorldToObject · unity_ObjectToWorld [{r},{c}]");
					}
				}
			}
		}

		[Test]
		public void ALightsCommands_AreVisibleInEverySplit()
		{
			Assert.AreEqual(1, TerrainBrgMath.SplitMask(0));
			Assert.AreEqual(1, TerrainBrgMath.SplitMask(1));
			Assert.AreEqual(0b111, TerrainBrgMath.SplitMask(3));
			Assert.AreEqual(0b1111, TerrainBrgMath.SplitMask(4));
		}

		// ── The GPU says what the mirror says ────────────────────────────

		private static string CheckCompute(string code)
		{
			string pack = SourceScanPins.Body(code, "uint FishPackFade(float fade)");
			string why = SourceScanPins.InOrder(pack, "if (fade != 0.0)", "ceil(16.0 * min(abs(fade), 1.0))", "round((steps - 0.5) * (127.0 / 16.0))", "fade < 0.0 ? -q : q", "& 0xFFu) << 24");
			if (why != null)
			{
				return "FishPackFade: " + why;
			}
			why = SourceScanPins.InOrder(SourceScanPins.Body(code, "void FishStore(uint slot, uint at, uint index, float fade)"),
				"_FishVisibleOut[s.x + at] = v;", "if (_FishPackedOn != 0)", "_FishVisiblePacked.Store((s.x + at) * 4, (index & 0xFFFFFFu) | FishPackFade(fade));");
			if (why != null)
			{
				return "FishStore: " + why;
			}
			// TerrainBrgMath.Columns: column j = (row0[j], row1[j], row2[j]), the translation last.
			why = SourceScanPins.InOrder(SourceScanPins.Body(code, "void FishStoreDots(uint address, float4 row0, float4 row1, float4 row2)"),
				"float4(row0.x, row1.x, row2.x, row0.y)", "float4(row1.y, row2.y, row0.z, row1.z)", "float4(row2.z, row0.w, row1.w, row2.w)");
			if (why != null)
			{
				return "FishStoreDots: " + why;
			}
			why = SourceScanPins.InOrder(SourceScanPins.Body(code, "void FishDotsInstances(uint3 id : SV_DispatchThreadID)"),
				"_FishUpload[_FishUploadSource + id.x]", "(_FishUploadDestination + id.x) * 48", "FishStoreDots(_FishDotsObjectToWorld + at", "c0 / dot(c0, c0)", "FishStoreDots(_FishDotsWorldToObject + at");
			if (why != null)
			{
				return "FishDotsInstances: " + why;
			}
			return code.Contains("#pragma kernel FishDotsInstances") ? null : "the FishDotsInstances kernel is not declared";
		}

		[Test]
		public void TheCulling_PacksTheFadeAndWritesTheMatricesAsTheMirrorDoes()
		{
			string code = SourceScanPins.ReadCode(Compute);
			SourceScanPins.HoldsAndFires("fade byte", code, CheckCompute,
				SourceScanPins.Replace("round((steps - 0.5) * (127.0 / 16.0))", "round(abs(fade) * 127.0)"),
				"the nearest byte instead of the step's middle (the pattern changes at other fades)");
			SourceScanPins.HoldsAndFires("packed store", code, CheckCompute,
				SourceScanPins.Replace("(index & 0xFFFFFFu) | FishPackFade(fade)", "index"),
				"the fade left out of the packed entry");
			SourceScanPins.HoldsAndFires("column order", code, CheckCompute,
				SourceScanPins.Replace("float4(row0.x, row1.x, row2.x, row0.y)", "float4(row0.x, row0.y, row0.z, row1.x)"),
				"row-major matrices where DOTS loads column-major");
		}

		private static string CheckIncludes(string include, string lodFade, string ground, string caster)
		{
			string dots = include.Substring(Math.Max(0, include.IndexOf("#if defined(UNITY_DOTS_INSTANCING_ENABLED)", StringComparison.Ordinal)));
			string why = SourceScanPins.InOrder(dots, "#define FISH_INDIRECT_DOTS 1", "UNITY_DOTS_INSTANCING_START(UserPropertyMetadata)",
				"UNITY_DOTS_INSTANCED_METADATA_NAME(float, _FishBatchContactOn)", "UNITY_DOTS_INSTANCED_METADATA_NAME(float, _FishBatchFloraOn)",
				"#define FISH_FLORA_ON_DECLARED", "#define _FishContactOn LoadDOTSInstancedData_float(", "#define _FishFloraOn LoadDOTSInstancedData_float(");
			if (why != null)
			{
				return "FishIndirectInstancing.hlsl DOTS block: " + why;
			}
			why = SourceScanPins.InOrder(SourceScanPins.Body(lodFade, "float FishLodFadeValue()"), "return FishIndirectLodFade;", "#elif defined(FISH_INDIRECT_DOTS)",
				"GetDOTSInstanceCrossfadeSnorm8() * (1.0 / 127.0)");
			if (why != null)
			{
				return "FishLodFade.hlsl: " + why;
			}
			if (!Regex.IsMatch(lodFade, @"#elif defined\(FISH_INDIRECT_DOTS\)\s*\n\s*#define FISH_LOD_FADE_INSTANCED 1"))
			{
				return "FishLodFade.hlsl: the DOTS variant does not switch the fade clip on";
			}
			if (!Regex.IsMatch(ground, @"#if !defined\(FISH_INDIRECT_DOTS\)\s*\nfloat _FishContactOn;\s*\n#endif"))
			{
				return "FishGroundColour.hlsl declares _FishContactOn where FishIndirectInstancing.hlsl #defines it";
			}
			why = SourceScanPins.InOrder(SourceScanPins.Body(caster, "bool FishCasterCulled()"), "#if defined(FISH_INDIRECT_DOTS)", "return false;", "#else", "_FishCasterSphere.w <= 0.0");
			return why == null ? null : "FishCasterCulling.hlsl: " + why;
		}

		[Test]
		public void TheDotsVariants_ReadTheBatchesConstantsAndTheFadeByte()
		{
			string include = SourceScanPins.ReadCode(Include);
			string lodFade = SourceScanPins.ReadCode(LodFade);
			string ground = SourceScanPins.ReadCode(GroundColour);
			string caster = SourceScanPins.ReadCode(CasterCulling);
			SourceScanPins.HoldsAndFires("contact constant", include, s => CheckIncludes(s, lodFade, ground, caster),
				SourceScanPins.Replace("#define _FishContactOn LoadDOTSInstancedData_float(", "#define _FishContactOnUnused LoadDOTSInstancedData_float("),
				"_FishContactOn left as a loose uniform (under the SRP Batcher the global's 0, so no contact blend)");
			SourceScanPins.HoldsAndFires("fade byte", lodFade, s => CheckIncludes(include, s, ground, caster),
				SourceScanPins.Replace("GetDOTSInstanceCrossfadeSnorm8() * (1.0 / 127.0)", "0.0"),
				"the DOTS variant reading no fade (both levels drawn whole in a band)");
			SourceScanPins.HoldsAndFires("contact declaration", ground, s => CheckIncludes(include, lodFade, s, caster),
				SourceScanPins.Replace("#if !defined(FISH_INDIRECT_DOTS)\nfloat _FishContactOn;\n#endif", "float _FishContactOn;"),
				"the loose declaration under the DOTS #define");
			// The renderer gives the batch constants under the same names.
			string brg = SourceScanPins.ReadCode(Brg);
			StringAssert.Contains("Shader.PropertyToID(\"_FishBatchContactOn\")", brg);
			StringAssert.Contains("Shader.PropertyToID(\"_FishBatchFloraOn\")", brg);
		}

		private static string EveryIndirectPassHasDots(string shader)
		{
			int passes = 0;
			foreach (Match pass in Regex.Matches(shader, "HLSLPROGRAM(.*?)ENDHLSL", RegexOptions.Singleline))
			{
				string program = pass.Groups[1].Value;
				if (!program.Contains(IndirectInclude))
				{
					continue;
				}
				passes++;
				if (!program.Contains(DotsInclude))
				{
					return "a pass that includes FishIndirectInstancing.hlsl has no DOTS_INSTANCING_ON variant: the batch group would draw it with the error shader";
				}
				if (program.Contains("#pragma use_dxc"))
				{
					return "a pass with the procedural setup compiles with DXC, which rejects its writes to UnityPerDraw";
				}
			}
			return passes == 0 ? "no pass includes FishIndirectInstancing.hlsl" : null;
		}

		[Test]
		public void EveryIndirectPass_HasItsDotsVariant()
		{
			foreach (string name in new[] { "FishVegetationIndirect.shader", "FishVegetationPrime.shader", "FishWeatherLitIndirect.shader" })
			{
				string code = SourceScanPins.ReadCode(Shaders + name);
				SourceScanPins.HoldsAndFires(name, code, EveryIndirectPassHasDots,
					s =>
					{
						int at = s.IndexOf(DotsInclude, StringComparison.Ordinal);
						return at < 0 ? s : s.Remove(at, DotsInclude.Length);
					},
					"a pass without DOTS.hlsl");
			}
		}

		private static string CheckRenderer(string main, string brg)
		{
			string why = SourceScanPins.InOrder(main, "brgState = BrgWanted;", "batchedState = Batching && !brgState;", "looksState = LooksReady();",
				"bool startsCarried = InstanceIdCarriesBase && !brgState;", "BrgLayout(bases);");
			if (why != null)
			{
				return "Relayout: " + why;
			}
			why = SourceScanPins.InOrder(main, "DispatchCompute(compute, copyKernel", "DispatchDots(into, upload, n);");
			if (why != null)
			{
				return "Flush: " + why;
			}
			why = SourceScanPins.InOrder(main, "SyncDots();", "BindPacked(cmd);", "DispatchCompute(compute, cullKernel", "draws = ArmBrg(camera);", "Graphics.RenderMeshIndirect(rp, command.Mesh");
			if (why != null)
			{
				return "Execute: " + why;
			}
			why = SourceScanPins.InOrder(brg, "TerrainBrgMath.PerInstance(TerrainBrgMath.ObjectToWorldAddress)", "TerrainBrgMath.PerInstance(TerrainBrgMath.WorldToObjectAddress(capacity))",
				"NameID = PreviousObjectToWorldId, Value = objectToWorld", "NameID = PreviousWorldToObjectId, Value = worldToObject",
				"Value = TerrainBrgMath.ContactOnAddress", "Value = TerrainBrgMath.FloraOnAddress");
			if (why != null)
			{
				return "SyncDots: " + why;
			}
			return SourceScanPins.InOrder(brg, "flags = BatchDrawCommandFlags.LODCrossFadeValuePacked", "visibleOffset = d.VisibleOffset", "indirectArgsBufferOffset = d.ArgsOffset",
				"shadowCastingMode = view == 0 ? ShadowCastingMode.Off : ShadowCastingMode.ShadowsOnly");
		}

		[Test]
		public void TheRenderer_SubmitsThroughTheBatchGroupWithZeroStartInstance()
		{
			string main = SourceScanPins.ReadCode(Renderer);
			string brg = SourceScanPins.ReadCode(Brg);
			SourceScanPins.HoldsAndFires("start instance", main, s => CheckRenderer(s, brg),
				SourceScanPins.Replace("bool startsCarried = InstanceIdCarriesBase && !brgState;", "bool startsCarried = InstanceIdCarriesBase;"),
				"Vulkan's run start in startInstance, which DOTS would add to the visibleOffset a second time");
			SourceScanPins.HoldsAndFires("crossfade flag", brg, s => CheckRenderer(main, s),
				SourceScanPins.Replace("flags = BatchDrawCommandFlags.LODCrossFadeValuePacked", "flags = BatchDrawCommandFlags.None"),
				"no packed-fade flag (the shader would read the fade byte as part of the index)");
			SourceScanPins.HoldsAndFires("matrices", main, s => CheckRenderer(s, brg),
				SourceScanPins.Replace("DispatchDots(into, upload, n);", string.Empty),
				"uploads that never reach the batch buffer");
		}

		[Test]
		public void TheWebBuild_KeepsTheTwinsDotsVariants()
		{
			string code = SourceScanPins.ReadCode(Stripper);
			SourceScanPins.HoldsAndFires("stripper", code,
				s => SourceScanPins.InOrder(s, "new ShaderKeyword(shader, \"DOTS_INSTANCING_ON\")", "IsEnabled(procedural) || variants[i].shaderKeywordSet.IsEnabled(dots)", "(!usedIndirect ||"),
				SourceScanPins.Replace(" || variants[i].shaderKeywordSet.IsEnabled(dots)", string.Empty),
				"WebGPU builds stripping the batch group's variants");
		}
	}
}
