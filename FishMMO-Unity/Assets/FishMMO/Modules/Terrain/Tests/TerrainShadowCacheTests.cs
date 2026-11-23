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
	/// <summary>Split from CachedMainLightShadowsTests: the Terrain module's half.</summary>
	public class TerrainShadowCacheTests
	{
		[Test]
		public void TheCachesContent_NoLongerFollowsTheCamera()
		{
			string cache = SourceScanPins.ReadCode("Assets/FishMMO/Modules/Terrain/Client/TerrainGpuRenderer.ShadowCache.cs");
			// Its static casters (a layout, an upload) and the coverage of the shadow work it culled (ShadowCasterCoverage); not the camera.
			StringAssert.Contains("public int ShadowContentVersion => HashCode.Combine(layoutSerial, flushSerial, CoverageSerial());", cache);
			string kernel = ProjectFiles.ReadAllText(Shaders + "FishTerrainInstancing.compute");
			StringAssert.Contains("if (!cacheCull && (work.padding & 1u) == 0 && FishSphereVisible(centre, radius) && !FishOccluded(centre, radius))", kernel);
			StringAssert.Contains("partner = -1;", kernel);
		}

		[Test]
		public void OnlyTheShadowViewsNonVegetationCommandsAreStatic()
		{
			var lit = new Material(AssetDatabase.LoadAssetAtPath<Shader>(ProjectFiles.Path(Shaders + "FishWeatherLitIndirect.shader")));
			var vegetation = new Material(AssetDatabase.LoadAssetAtPath<Shader>(ProjectFiles.Path(Shaders + "FishVegetationIndirect.shader")));
			try
			{
				Assert.That(TerrainGpuRenderer.IsStaticShadowCaster(1, lit), Is.True);
				Assert.That(TerrainGpuRenderer.IsStaticShadowCaster(0, lit), Is.False, "the camera view is never cached");
				Assert.That(TerrainGpuRenderer.IsStaticShadowCaster(1, vegetation), Is.False, "sway stays live");
				Assert.That(TerrainGpuRenderer.IsStaticShadowCaster(1, null), Is.False);
			}
			finally { Object.DestroyImmediate(lit); Object.DestroyImmediate(vegetation); }
		}

		[Test]
		public void TheBatchGroupLeavesOutStaticCastersOnlyForTheMainLightOfACachedCamera()
		{
			string brg = SourceScanPins.ReadCode("Assets/FishMMO/Modules/Terrain/Client/TerrainGpuRenderer.Brg.cs");
			SourceScanPins.HoldsAndFires("omission", brg,
				s => SourceScanPins.InOrder(s, "bool omitStatic = view == 1 && context.projectionType == BatchCullingProjectionType.Orthographic",
					"CachedMainLightShadows.OmitsStaticCasters(brgCameraObject);", "if (omitStatic && brgDraws[c].StaticShadow)", "if (omitStatic && brgDraws[c].StaticShadow) continue;"),
				SourceScanPins.Replace("context.projectionType == BatchCullingProjectionType.Orthographic", "true"),
				"spot and point light shadows losing their static casters");
			StringAssert.Contains("StaticShadow = IsStaticShadowCaster(command.View, command.Params.material),", brg);
			string cached = SourceScanPins.ReadCode("Assets/FishMMO/Client/Rendering/WorldPasses/CachedMainLightShadows.cs");
			SourceScanPins.HoldsAndFires("cache cull", cached,
				s => SourceScanPins.InOrder(s, "AddCasterCull(renderGraph, camera, in windows[widest]);", "renderGraph.AddRasterRenderPass<CachePassData>",
					"private static void AddCasterCull(", "renderer.RecordShadowCacheCull(native, d.Camera, d.Anchor, d.Planes, d.Light);"),
				SourceScanPins.Replace("AddCasterCull(renderGraph, camera, in windows[widest]);", string.Empty),
				"the cache drawing instance counts it never culled");
			string cacheCode = SourceScanPins.ReadCode("Assets/FishMMO/Modules/Terrain/Client/TerrainGpuRenderer.ShadowCache.cs");
			SourceScanPins.HoldsAndFires("cache visible list", cacheCode,
				s => SourceScanPins.InOrder(s, "cmd.SetComputeBufferParam(compute, cullKernel, VisibleOutId, shadowCacheVisible);", "BindPackedOff(cmd);"),
				SourceScanPins.Replace("BindPackedOff(cmd);", string.Empty),
				"the cache's culling overwriting the batch group's packed visible list");
			// Queued by the core world-passes feature, not the clouds.
			string feature = SourceScanPins.ReadCode("Assets/FishMMO/Client/Rendering/WorldPasses/FishWorldPassesFeature.cs");
			StringAssert.Contains("renderer.EnqueuePass(CachedMainLightShadows.CachePass);", feature);
			StringAssert.Contains("renderer.EnqueuePass(CachedMainLightShadows.MergePass);", feature);
			string sky = SourceScanPins.ReadCode("Assets/FishMMO/Modules/WorldSimulation/Client/Weather/Sky/SkySystem.cs");
			StringAssert.Contains("LightRotation(CachedMainLightShadows.StableDirection(sunDirection))", sky);
			StringAssert.Contains("LightRotation(CachedMainLightShadows.StableDirection(body.Direction))", sky);
			// The renderer asset carries the shader into builds.
			string guid = AssetDatabase.AssetPathToGUID(ProjectFiles.Path(Shaders + "FishShadowCacheMerge.shader"));
			foreach (string tier in new[] { "HighFidelity", "Balanced", "Performant" })
			{
				StringAssert.Contains("ShadowCacheMergeShader: {fileID: 4800000, guid: " + guid, ProjectFiles.ReadAllText($"Assets/Settings/URP-{tier}-Renderer.asset"));
			}
		}

		private const string Shaders = "Assets/Prefabs/Client/Weather/Shaders/";
	}
}
