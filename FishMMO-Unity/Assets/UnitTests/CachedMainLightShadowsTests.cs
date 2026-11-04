using System;
using System.Linq;
using FishMMO.Client;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FishMMO.UnitTests
{
	/// <summary>The cached main-light shadows (CachedMainLightShadows.cs, TerrainGpuRenderer.ShadowCache.cs).</summary>
	public class CachedMainLightShadowsTests
	{
		private const string Shaders = "Assets/Prefabs/Client/Weather/Shaders/";

		// ── The sky lights' lattice: a pure function, so every client steps at the same world time ──

		[Test]
		public void TheLattice_IsDeterministicIdempotentAndClose()
		{
			var random = new System.Random(4021);
			for (int i = 0; i < 2000; i++)
			{
				var d = new Vector3((float)(random.NextDouble() * 2 - 1), (float)random.NextDouble() * 0.98f + 0.01f, (float)(random.NextDouble() * 2 - 1)).normalized;
				Vector3 a = CachedMainLightShadows.LatticeDirection(d, 0.1f);
				Assert.That(CachedMainLightShadows.LatticeDirection(d, 0.1f), Is.EqualTo(a), "same input, same lattice point");
				Assert.That(a.magnitude, Is.EqualTo(1f).Within(1e-5f));
				Assert.That(Vector3.Angle(a, d), Is.LessThan(0.1f), "within a step of the true direction");
				Vector3 again = CachedMainLightShadows.LatticeDirection(a, 0.1f);
				Assert.That(Vector3.Angle(again, a), Is.LessThan(1e-3f), "a lattice point stays where it is");
			}
		}

		[Test]
		public void AMovingSun_StepsInsteadOfTurningEveryFrame()
		{
			// A sun moving 0.0005° a frame (about 1° a world minute at 30 frames a second): far fewer distinct lattice directions than frames.
			int frames = 2000, distinct = 1;
			Vector3 last = CachedMainLightShadows.LatticeDirection(Quaternion.Euler(-40f, 30f, 0f) * Vector3.forward, 0.1f);
			for (int f = 1; f < frames; f++)
			{
				Vector3 d = CachedMainLightShadows.LatticeDirection(Quaternion.Euler(-40f - f * 0.0005f, 30f + f * 0.0005f, 0f) * Vector3.forward, 0.1f);
				if (!d.Equals(last))
				{
					distinct++;
					last = d;
				}
			}
			Assert.That(distinct, Is.LessThan(frames / 50), "steps, not a change every frame");
			Assert.That(distinct, Is.GreaterThan(1), "it still follows the sun");
		}

		[Test]
		public void SwitchedOff_TheSkyLightsAreUntouched()
		{
			bool was = CachedMainLightShadows.Enabled;
			try
			{
				CachedMainLightShadows.Enabled = false;
				var d = new Vector3(0.3123f, 0.7777f, 0.5432f).normalized;
				Assert.That(CachedMainLightShadows.StableDirection(d), Is.EqualTo(d));
			}
			finally { CachedMainLightShadows.Enabled = was; }
		}

		// ── The windows: URP's slice read from a wider cached tile, redrawn only when it leaves it ──

		private const int Resolution = 1024, Margin = Resolution / CachedMainLightShadows.MarginDivisor, Size = Resolution + 2 * Margin;
		private static readonly Vector3 Travel = new Vector3(0.2f, -0.9f, 0.3f).normalized;

		/// <summary>A slice as URP gives one: a light view (looking along the sun's travel), centred, 40 m across, 0.1–200 m deep.</summary>
		private static CachedMainLightShadows.CascadeKey Slice(float shiftTexelsX = 0f, float shiftTexelsY = 0f, float shiftDepth = 0f)
		{
			Matrix4x4 view = Matrix4x4.TRS(new Vector3(10f, 120f, -30f), Quaternion.LookRotation(Travel, Vector3.up), new Vector3(1f, 1f, -1f)).inverse;
			float texel = 40f / Resolution;
			// A camera that moved: the slice's grid moved by whole (or not) texels across, and its near plane along the light.
			view.m03 -= shiftTexelsX * texel;
			view.m13 -= shiftTexelsY * texel;
			view.m23 -= shiftDepth;
			return new CachedMainLightShadows.CascadeKey
			{
				View = view,
				Projection = Matrix4x4.Ortho(-20f, 20f, -20f, 20f, 0.1f, 200f),
				OffsetX = 1024, OffsetY = 0, Resolution = Resolution,
				Bias = new Vector4(-0.1f, -0.2f, 0f, 0f),
				LightDirection = -Travel,
			};
		}

		private static CachedMainLightShadows.Window WindowOf(in CachedMainLightShadows.CascadeKey slice)
		{
			Vector2Int tile = CachedMainLightShadows.TileOf(1, 4, Size);
			Assert.That(CachedMainLightShadows.TryWindowFor(in slice, Margin, tile.x, tile.y, out CachedMainLightShadows.Window w), Is.True);
			return w;
		}

		/// <summary>The cache texel URP's first texel centre reads, within the window's tile.</summary>
		private static Vector2 FirstTexel(in CachedMainLightShadows.Mapping map, in CachedMainLightShadows.Window w)
		{
			Vector2Int atlas = CachedMainLightShadows.AtlasSize(4, Size);
			float ndc = 1f / Resolution - 1f;
			return new Vector2((ndc * map.Scale.x + map.Offset.x) * atlas.x - w.TileX, (ndc * map.Scale.y + map.Offset.y) * atlas.y - w.TileY);
		}

		[Test]
		public void AWindow_IsTheSliceWidenedByItsMargin()
		{
			CachedMainLightShadows.CascadeKey slice = Slice();
			Assert.That(CachedMainLightShadows.TryOrthoBox(slice.Projection, out float l, out float r, out float b, out float t, out float n, out float f), Is.True);
			Assert.That(new[] { l, r, b, t, n, f }, Is.EqualTo(new[] { -20f, 20f, -20f, 20f, 0.1f, 200f }).Within(1e-3f));
			CachedMainLightShadows.Window w = WindowOf(in slice);
			Assert.That(w.Size, Is.EqualTo(Size));
			Assert.That(w.Right - w.Left, Is.EqualTo(40f * Size / Resolution).Within(1e-3f), "the same texel size, 1.5× the tiles");
			Assert.That(w.Far - w.Near, Is.EqualTo(1.5f * 199.9f).Within(1e-2f), "a quarter of the depth range more at each end");
			Vector2Int atlas = CachedMainLightShadows.AtlasSize(4, Size);
			Assert.That(CachedMainLightShadows.TryMap(in w, in slice, atlas.x, atlas.y, out CachedMainLightShadows.Mapping map), Is.True);
			Vector2 first = FirstTexel(in map, in w);
			Assert.That(first.x, Is.EqualTo(Margin + 0.5f).Within(1e-2f), "URP's first texel is the margin's next");
			Assert.That(first.y, Is.EqualTo(Margin + 0.5f).Within(1e-2f).Or.EqualTo(Size - Margin - 0.5f).Within(1e-2f), "either way up, as the platform renders");
		}

		[TestCase(1f, 0f)]
		[TestCase(-37f, 12f)]
		[TestCase(255f, -255f)]
		public void ASliceMovedByWholeTexels_ReadsTheWindowShifted(float dx, float dy)
		{
			CachedMainLightShadows.Window w = WindowOf(Slice());
			Vector2Int atlas = CachedMainLightShadows.AtlasSize(4, Size);
			Assert.That(CachedMainLightShadows.TryMap(in w, Slice(dx, dy), atlas.x, atlas.y, out CachedMainLightShadows.Mapping map), Is.True);
			Vector2 first = FirstTexel(in map, in w);
			Assert.That(Mathf.Abs(first.x - (Margin + 0.5f)), Is.EqualTo(Mathf.Abs(dx)).Within(1e-2f));
			// Either way up, as the platform renders: from the window's first or last row in.
			float fromStart = Mathf.Abs(first.y - (Margin + 0.5f)), fromEnd = Mathf.Abs(first.y - (Size - Margin - 0.5f));
			Assert.That(Mathf.Min(Mathf.Abs(fromStart - Mathf.Abs(dy)), Mathf.Abs(fromEnd - Mathf.Abs(dy))), Is.LessThan(1e-2f));
			Assert.That(first.x - Mathf.Floor(first.x), Is.EqualTo(0.5f).Within(0.02f), "texel centre on texel centre");
		}

		[Test]
		public void ASliceThatLeavesItsWindow_OrIsNotOnItsTexels_Redraws()
		{
			CachedMainLightShadows.Window w = WindowOf(Slice());
			Vector2Int atlas = CachedMainLightShadows.AtlasSize(4, Size);
			Assert.That(CachedMainLightShadows.TryMap(in w, Slice(Margin + 1f), atlas.x, atlas.y, out _), Is.False, "past the margin");
			Assert.That(CachedMainLightShadows.TryMap(in w, Slice(0f, -(Margin + 1f)), atlas.x, atlas.y, out _), Is.False);
			Assert.That(CachedMainLightShadows.TryMap(in w, Slice(0.5f), atlas.x, atlas.y, out _), Is.False, "half a texel: no exact copy");
			Assert.That(CachedMainLightShadows.TryMap(in w, Slice(0f, 0f, 60f), atlas.x, atlas.y, out _), Is.False, "the near plane past the window's depth margin");
			CachedMainLightShadows.CascadeKey other = Slice();
			other.Bias.x = -0.11f;
			Assert.That(CachedMainLightShadows.TryMap(in w, in other, atlas.x, atlas.y, out _), Is.False, "a bias change");
			other = Slice();
			other.Projection = Matrix4x4.Ortho(-21f, 21f, -21f, 21f, 0.1f, 200f);
			Assert.That(CachedMainLightShadows.TryMap(in w, in other, atlas.x, atlas.y, out _), Is.False, "a texel size change");
			other = Slice();
			other.View = Matrix4x4.Rotate(Quaternion.Euler(0f, 0.2f, 0f)) * other.View;
			Assert.That(CachedMainLightShadows.TryMap(in w, in other, atlas.x, atlas.y, out _), Is.False, "a sun step turns the view");
		}

		[Test]
		public void TheDepthMap_GivesURPsOwnDepthForEveryCachedPoint()
		{
			CachedMainLightShadows.Window w = WindowOf(Slice());
			CachedMainLightShadows.CascadeKey moved = Slice(3f, -7f, 12.5f);
			Vector2Int atlas = CachedMainLightShadows.AtlasSize(4, Size);
			Assert.That(CachedMainLightShadows.TryMap(in w, in moved, atlas.x, atlas.y, out CachedMainLightShadows.Mapping map), Is.True);
			Matrix4x4 toSlice = GL.GetGPUProjectionMatrix(moved.Projection, true) * moved.View;
			Matrix4x4 toCache = GL.GetGPUProjectionMatrix(w.Projection, true) * w.View;
			var random = new System.Random(77);
			for (int i = 0; i < 200; i++)
			{
				var p = new Vector3((float)random.NextDouble() * 30f - 5f, (float)random.NextDouble() * 60f + 60f, (float)random.NextDouble() * 30f - 45f);
				float urp = toSlice.MultiplyPoint(p).z, cached = toCache.MultiplyPoint(p).z;
				Assert.That(cached * map.DepthScale + map.DepthOffset, Is.EqualTo(urp).Within(1e-4f));
			}
		}

		[Test]
		public void TheCullPlanes_HoldTheWindowAlongTheLight()
		{
			CachedMainLightShadows.Window w = WindowOf(Slice());
			var planes = new Vector4[6];
			CachedMainLightShadows.WindowPlanes(in w, planes);
			Matrix4x4 toWorld = w.View.inverse;
			bool Inside(Vector3 p)
			{
				foreach (Vector4 plane in planes)
				{
					if (Vector3.Dot(plane, p) + plane.w < -1e-3f) return false;
				}
				return true;
			}
			Assert.That(Inside(toWorld.MultiplyPoint(new Vector3(w.Left + 0.01f, w.Top - 0.01f, -5f))), Is.True);
			Assert.That(Inside(toWorld.MultiplyPoint(new Vector3(w.Right - 0.01f, w.Bottom + 0.01f, -400f))), Is.True, "any distance along the light");
			Assert.That(Inside(toWorld.MultiplyPoint(new Vector3(w.Right + 1f, 0f, -5f))), Is.False);
			Assert.That(Inside(toWorld.MultiplyPoint(new Vector3(0f, w.Bottom - 1f, -5f))), Is.False);
		}

		[Test]
		public void TheCachesContent_NoLongerFollowsTheCamera()
		{
			string cache = SourceScanPins.ReadCode("Assets/Scripts/Client/World/Terrain/TerrainGpuRenderer.ShadowCache.cs");
			StringAssert.Contains("public int ShadowContentVersion => HashCode.Combine(layoutSerial, flushSerial);", cache);
			string kernel = System.IO.File.ReadAllText(Shaders + "FishTerrainInstancing.compute");
			StringAssert.Contains("if (!cacheCull && (work.padding & 1u) == 0 && FishSphereVisible(centre, radius) && !FishOccluded(centre, radius))", kernel);
			StringAssert.Contains("partner = -1;", kernel);
		}

		// ── Which casters are cached: never the swaying vegetation ──

		[Test]
		public void OnlyTheShadowViewsNonVegetationCommandsAreStatic()
		{
			var lit = new Material(AssetDatabase.LoadAssetAtPath<Shader>(Shaders + "FishWeatherLitIndirect.shader"));
			var vegetation = new Material(AssetDatabase.LoadAssetAtPath<Shader>(Shaders + "FishVegetationIndirect.shader"));
			try
			{
				Assert.That(TerrainGpuRenderer.IsStaticShadowCaster(1, lit), Is.True);
				Assert.That(TerrainGpuRenderer.IsStaticShadowCaster(0, lit), Is.False, "the camera view is never cached");
				Assert.That(TerrainGpuRenderer.IsStaticShadowCaster(1, vegetation), Is.False, "sway stays live");
				Assert.That(TerrainGpuRenderer.IsStaticShadowCaster(1, null), Is.False);
			}
			finally { Object.DestroyImmediate(lit); Object.DestroyImmediate(vegetation); }
		}

		// ── The merge shader compiles everywhere, within the uniform budgets ──

		[TestCase(ShaderCompilerPlatform.WebGPU, BuildTarget.WebGL)]
		[TestCase(ShaderCompilerPlatform.Vulkan, BuildTarget.StandaloneLinux64)]
		[TestCase(ShaderCompilerPlatform.D3D, BuildTarget.StandaloneWindows64)]
		public void TheMergeShaderCompiles(ShaderCompilerPlatform api, BuildTarget target)
		{
			var shader = AssetDatabase.LoadAssetAtPath<Shader>(Shaders + "FishShadowCacheMerge.shader");
			Assert.That(shader, Is.Not.Null);
			Assert.That(shader.name, Is.EqualTo(CachedMainLightShadows.MergeShaderName));
			var data = ShaderUtil.GetShaderData(shader).GetSubshader(0);
			Assert.That(data.PassCount, Is.EqualTo(2));
			for (int pass = 0; pass < data.PassCount; pass++)
			{
				foreach (var stage in new[] { ShaderType.Vertex, ShaderType.Fragment })
				{
					var result = data.GetPass(pass).CompileVariant(stage, Array.Empty<string>(), api, target);
					Assert.That(result.Success, Is.True, data.GetPass(pass).Name + " " + stage + ": " + string.Join("\n", result.Messages.Select(m => m.message)));
					Assert.That(result.ConstantBuffers.Length, Is.LessThanOrEqualTo(10), "within the WebGPU dynamic-uniform limit");
				}
			}
		}

		// ── The wiring ──

		[Test]
		public void TheBatchGroupLeavesOutStaticCastersOnlyForTheMainLightOfACachedCamera()
		{
			string brg = SourceScanPins.ReadCode("Assets/Scripts/Client/World/Terrain/TerrainGpuRenderer.Brg.cs");
			SourceScanPins.HoldsAndFires("omission", brg,
				s => SourceScanPins.InOrder(s, "bool omitStatic = view == 1 && context.projectionType == BatchCullingProjectionType.Orthographic",
					"CachedMainLightShadows.AppliesTo(brgCameraObject);", "if (omitStatic && brgDraws[c].StaticShadow)", "if (omitStatic && brgDraws[c].StaticShadow) continue;"),
				SourceScanPins.Replace("context.projectionType == BatchCullingProjectionType.Orthographic", "true"),
				"spot and point light shadows losing their static casters");
			StringAssert.Contains("StaticShadow = IsStaticShadowCaster(command.View, command.Params.material),", brg);
			string cached = SourceScanPins.ReadCode("Assets/Scripts/Client/World/Terrain/CachedMainLightShadows.cs");
			SourceScanPins.HoldsAndFires("cache cull", cached,
				s => SourceScanPins.InOrder(s, "renderer.RecordShadowCacheCull(native, d.Camera, d.Anchor, d.Planes, d.Light);", "renderGraph.AddRasterRenderPass<CachePassData>"),
				SourceScanPins.Replace("renderer.RecordShadowCacheCull(native, d.Camera, d.Anchor, d.Planes, d.Light);", string.Empty),
				"the cache drawing instance counts it never culled");
			string cacheCode = SourceScanPins.ReadCode("Assets/Scripts/Client/World/Terrain/TerrainGpuRenderer.ShadowCache.cs");
			SourceScanPins.HoldsAndFires("cache visible list", cacheCode,
				s => SourceScanPins.InOrder(s, "cmd.SetComputeBufferParam(compute, cullKernel, VisibleOutId, shadowCacheVisible);", "BindPackedOff(cmd);"),
				SourceScanPins.Replace("BindPackedOff(cmd);", string.Empty),
				"the cache's culling overwriting the batch group's packed visible list");
			string feature = SourceScanPins.ReadCode("Assets/Scripts/Client/World/Weather/Sky/FishCloudsFeature.cs");
			StringAssert.Contains("renderer.EnqueuePass(CachedMainLightShadows.CachePass);", feature);
			StringAssert.Contains("renderer.EnqueuePass(CachedMainLightShadows.MergePass);", feature);
			string sky = SourceScanPins.ReadCode("Assets/Scripts/Client/World/Weather/Sky/SkySystem.cs");
			StringAssert.Contains("LightRotation(CachedMainLightShadows.StableDirection(sunDirection))", sky);
			StringAssert.Contains("LightRotation(CachedMainLightShadows.StableDirection(body.Direction))", sky);
			// The renderer asset carries the shader into builds.
			string guid = AssetDatabase.AssetPathToGUID(Shaders + "FishShadowCacheMerge.shader");
			StringAssert.Contains("ShadowCacheMergeShader: {fileID: 4800000, guid: " + guid, System.IO.File.ReadAllText("Assets/Settings/URP-HighFidelity-Renderer.asset"));
		}

		[Test]
		public void TheMergeKeepsTheNearerDepth_WrittenForAForwardBufferBecauseUnityReversesIt()
		{
			// Unity reverses a material's ZTest under a reversed depth buffer itself (the casters' "ZTest LEqual" reaches the GPU as
			// greater-equal there), so the merge names LessEqual everywhere: GreaterEqual came out as less-equal in the browser and
			// kept the farther depth (2026-11-02).
			Assert.AreEqual(UnityEngine.Rendering.CompareFunction.LessEqual, CachedMainLightShadows.MergeDepthTest(false));
			Assert.AreEqual(UnityEngine.Rendering.CompareFunction.Always, CachedMainLightShadows.MergeDepthTest(true));
			string source = System.IO.File.ReadAllText("Assets/Scripts/Client/World/Terrain/CachedMainLightShadows.cs");
			StringAssert.DoesNotContain("CompareFunction.GreaterEqual", source);
			// The shader's own default is LessEqual too (4), for a material made before the code sets it.
			StringAssert.Contains("_FishShadowCacheZTest(\"Merge depth test\", Float) = 4", System.IO.File.ReadAllText(Shaders + "FishShadowCacheMerge.shader"));
		}

		// Off everywhere (2026-11-04) until the browser's wrong structure shadows are fixed; the owner wants it on in browsers then.
		[Test]
		public void OffByDefaultUntilTheBrowserLookIsFixed()
		{
			string code = System.IO.File.ReadAllText("Assets/Scripts/Client/World/Terrain/CachedMainLightShadows.cs");
			StringAssert.Contains("public static bool Enabled = DefaultOn;", code);
			StringAssert.Contains("public const bool DefaultOn = false;", code);
			StringAssert.DoesNotContain("public const bool DefaultOn = true;", code);
		}
	}
}
