using System.Text.RegularExpressions;
using NUnit.Framework;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The inland water off property blocks (2026-10-28): every lake, river, fall curtain and fall spray drew with a
	/// <see cref="UnityEngine.MaterialPropertyBlock"/> of its own (the solved flow, the falls' foam trail, the biome looks, the
	/// curtains' textures), and a renderer with a block is drawn outside the SRP Batcher, its whole shader state set up again
	/// draw by draw. Each now draws with its own copy of its material (FishMMO.Shared.DrawMaterials) whose numbers sit in the
	/// shader's UnityPerMaterial as loose vectors; the draw order (sortingOrder), the stencil and every number are as before.
	/// </summary>
	[TestFixture]
	public class InlandWaterBatchingTests
	{
		private const string Water = "Assets/Plugins/FishMMO Water/";
		private const string Renderer = Water + "Scripts/InlandWaterRenderer";

		/// <summary>Every part of the renderer.</summary>
		private static readonly string[] Parts =
		{
			Renderer + ".cs", Renderer + ".Channel.cs", Renderer + ".Falls.cs", Renderer + ".FallSheet.cs", Renderer + ".FallSpray.cs",
			Renderer + ".Flow.cs", Renderer + ".Lava.cs", Renderer + ".Looks.cs", Renderer + ".Wakes.cs",
		};

		private static string[] Numbered(string prefix, int count)
		{
			var names = new string[count];
			for (int i = 0; i < count; i++)
			{
				names[i] = prefix + i;
			}
			return names;
		}

		/// <summary>Each shader (or the include that declares its numbers) and the per-surface numbers it must keep in UnityPerMaterial.</summary>
		private static readonly (string File, string[] Names)[] PerSurfaceNumbers =
		{
			(Water + "Shaders/FishInlandWater.hlsl", Concat(new[] { "_RiverFlowInfo", "_RiverFallCount", "_InlandLookOn" },
				Numbered("_RiverFall", 4), Numbered("_InlandLook", 15))),
			(Water + "Shaders/FishWaterfall.hlsl", new[] { "_FallLookOn", "_FallWater", "_FallFoam", "_FallAbsorb" }),
			(Water + "Shaders/FishWaterfallSpray.shader", new[] { "_FallLookOn", "_FallSpray", "_FallMist", "_FallPath", "_FallOrigin", "_FallMistNoiseCells" }),
		};

		private static string[] Concat(params string[][] lists)
		{
			var all = new System.Collections.Generic.List<string>();
			foreach (string[] list in lists)
			{
				all.AddRange(list);
			}
			return all.ToArray();
		}

		private static string PerMaterialBlock(string code)
		{
			int start = code.IndexOf("CBUFFER_START(UnityPerMaterial)", System.StringComparison.Ordinal);
			if (start < 0)
			{
				return null;
			}
			int end = code.IndexOf("CBUFFER_END", start, System.StringComparison.Ordinal);
			return end < 0 ? null : code.Substring(start, end - start);
		}

		private static Regex Declaration(string name) => new Regex(@"\b(float|half|int|uint|bool)[1-4]?(x[1-4])?\s+" + Regex.Escape(name) + @"\s*(\[[^\]]*\])?\s*;");

		/// <summary>Why a shader's per-surface numbers are not all in its UnityPerMaterial (declared once, there, and no array), or null.</summary>
		private static System.Func<string, string> InPerMaterial(string[] names) => code =>
		{
			string block = PerMaterialBlock(code);
			if (block == null)
			{
				return "it has no UnityPerMaterial block";
			}
			if (Regex.IsMatch(block, @"\w\s*\[[^\]]+\]\s*;"))
			{
				return "its UnityPerMaterial declares an array, which is not known to batch";
			}
			foreach (string name in names)
			{
				if (!Declaration(name).IsMatch(block))
				{
					return $"{name} is not in UnityPerMaterial: under the SRP Batcher the draw would not read the copy's value";
				}
				int declared = Declaration(name).Matches(code).Count;
				if (declared != 1)
				{
					return $"{name} is declared {declared} times";
				}
			}
			return null;
		};

		[Test]
		public void TheInlandWaterShaders_KeepTheirPerSurfaceNumbersInUnityPerMaterial()
		{
			foreach ((string file, string[] names) in PerSurfaceNumbers)
			{
				string code = SourceScanPins.ReadCode(file);
				SourceScanPins.HoldsAndFires(file, code, InPerMaterial(names),
					SourceScanPins.InsertBefore("CBUFFER_START(UnityPerMaterial)", "float4 " + names[0] + ";\n"),
					"the first number declared loose again");
				SourceScanPins.HoldsAndFires(file, code, InPerMaterial(names),
					SourceScanPins.InsertBefore("CBUFFER_END", "float4 _PackedAgain[4];\n"),
					"an array in UnityPerMaterial");
			}
		}

		[Test]
		public void TheRenderer_GivesNoSurfaceAPropertyBlock()
		{
			foreach (string path in Parts)
			{
				string code = SourceScanPins.ReadCode(path);
				SourceScanPins.HoldsAndFires(path, code,
					source => source.Contains("MaterialPropertyBlock") ? "it makes a MaterialPropertyBlock"
						: source.Contains("SetPropertyBlock(") ? "a renderer is given a block" : null,
					source => source + "\nprivate void PutBack(MeshRenderer r) { r.SetPropertyBlock(null); }\n",
					"a block for a surface again");
			}
		}

		/// <summary>
		/// A slot's copy is reused build after build, so each surface's numbers start from "none" before its own are set, and the
		/// copy is what the renderer draws with; the looks and the falls are the shader's loose vectors, by the same names.
		/// </summary>
		[Test]
		public void EachSurface_StartsFromNone_OnItsOwnCopy()
		{
			string flow = SourceScanPins.ReadCode(Renderer + ".Flow.cs");
			string copy = SourceScanPins.Body(flow, "private Material SurfaceCopy(MeshRenderer renderer)");
			LogAssert.IsNull(SourceScanPins.InOrder(copy, "surfaceCopies.For(Material, slot)", "SetTexture(FlowFieldId, null)", "SetVector(FlowInfoId, Vector4.zero)",
				"SetVector(RiverFallIds[i], Vector4.zero)", "SetFloat(RiverFallCountId, 0f)", "SetFloat(LookOnId, 0f)", "renderer.sharedMaterial = own"),
				"every per-surface number is cleared on the copy before it is drawn with");
			string apply = SourceScanPins.Body(flow, "private void ApplyFlow()");
			LogAssert.IsNull(SourceScanPins.InOrder(apply, "RememberSources()", "SurfaceCopy(pair.Value)", "ApplyRiverFalls(pair.Key, own)",
				"ApplyRiverLook(pair.Key, own)", "ApplyLakeLooks()", "ApplyFallLooks()"), "ApplyFlow sets every surface's numbers on its copy");

			string looks = SourceScanPins.ReadCode(Renderer + ".Looks.cs");
			LogAssert.IsTrue(looks.Contains("Shader.PropertyToID(\"_InlandLook\" + i)") && looks.Contains("new int[LookVectors * LookSlots]"),
				"the looks are _InlandLook0 … 14");
			LogAssert.IsNull(SourceScanPins.InOrder(SourceScanPins.Body(looks, "private void ApplyLakeLooks()"), "SurfaceCopy(renderer)", "SetLooks(own, Pack(look, look, look))"),
				"a lake's look is on its copy");
			string fallLooks = SourceScanPins.Body(looks, "private void ApplyFallLooks()");
			LogAssert.IsNull(SourceScanPins.InOrder(fallLooks, "sprayCopies.For(SprayMaterial, part.Slot)", "curtainCopies.For(FallMaterial, part.Slot)",
				"SetTexture(FallMistNoiseId", "SetTexture(FallPathTexId, part.Paths)", "SetTexture(FoamTextureId, FallTextures.Streaks)",
				"SetTexture(NormalMapId, FallTextures.Ripples)", "SetFloat(FallLookOnId", "renderer.sharedMaterial = own"),
				"a fall's curtain and spray take their textures and look on their own copies");

			string falls = SourceScanPins.ReadCode(Renderer + ".Falls.cs");
			for (int i = 0; i < 4; i++)
			{
				LogAssert.IsTrue(falls.Contains($"Shader.PropertyToID(\"_RiverFall{i}\")"), $"_RiverFall{i} is set by name");
			}
			LogAssert.IsTrue(falls.Contains("public const int MaxRiverFalls = 4;"), "four falls a river, as the shader reads");
		}

		/// <summary>The order the water is drawn in is untouched: lakes, then rivers by DrawOrder, then curtains and spray over them.</summary>
		[Test]
		public void TheDrawOrder_AndTheStencil_AreAsBefore()
		{
			string main = SourceScanPins.ReadCode(Renderer + ".cs");
			string add = SourceScanPins.Body(main, "private void Add(string name, Mesh mesh, int order)");
			LogAssert.IsNull(SourceScanPins.InOrder(add, "renderer.sharedMaterial = Material", "renderer.sortingOrder = order", "surfaceSlots[renderer] = surfaceSlots.Count"),
				"each surface keeps its sorting order and takes the next slot");
			string falls = SourceScanPins.ReadCode(Renderer + ".Falls.cs");
			LogAssert.IsTrue(falls.Contains("private const int CurtainSortingOrder = 30000, SpraySortingOrder = 30001;"), "falls are drawn over every surface");
			string shader = SourceScanPins.ReadCode(Water + "Shaders/FishInlandWater.shader");
			LogAssert.IsNull(SourceScanPins.InOrder(shader, "Stencil", "Ref 64", "ReadMask 192", "WriteMask 64", "Comp Greater", "Pass Replace"),
				"each pixel of inland water is drawn once");
			LogAssert.IsTrue(Regex.Matches(shader, @"HLSLPROGRAM\s*\n\s*#pragma use_dxc vulkan").Count == 2, "both passes compile with DXC");
		}

		/// <summary>The foam field draws each surface with the surface's own copy, so its pass is found by the shader, not by material.</summary>
		[Test]
		public void TheFoamField_FindsTheSourcePassByShader()
		{
			string foam = SourceScanPins.ReadCode(Water + "Scripts/WaterFoamField.cs");
			LogAssert.IsTrue(foam.Contains("Dictionary<Shader, int> sourcePasses"), "one pass index per shader, however many copies");
			LogAssert.IsTrue(foam.Contains("Material surface = renderer.sharedMaterial;"), "drawn with the renderer's own copy, which carries its numbers");
		}
	}
}
