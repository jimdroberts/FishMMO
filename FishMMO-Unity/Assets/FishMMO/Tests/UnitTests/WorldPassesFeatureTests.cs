using System;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The world's shared render passes are core (Phase 4 of the core and modules plan, 2026-11-17): the depth pyramid, the
	/// shadow cascade bounds, the WebGPU light table and the cached main-light shadows are queued by FishWorldPassesFeature
	/// on every renderer, not by the clouds, so a game without the weather module keeps its lighting and shadows.
	/// </summary>
	[TestFixture]
	public class WorldPassesFeatureTests
	{
		private const string Folder = "Assets/FishMMO/Client/Rendering/WorldPasses";

		[Test]
		public void EveryRenderer_CarriesTheWorldPasses()
		{
			string script = AssetDatabase.AssetPathToGUID(ProjectFiles.Path(Folder + "/FishWorldPassesFeature.cs"));
			string compute = AssetDatabase.AssetPathToGUID(ProjectFiles.Path("Assets/FishMMO/Shaders/FishWorldPasses.compute"));
			foreach (string tier in new[] { "HighFidelity", "Balanced", "Performant" })
			{
				string renderer = ProjectFiles.ReadAllText($"Assets/Settings/URP-{tier}-Renderer.asset").Replace("\r\n", "\n");
				Match doc = Regex.Match(renderer, @"--- !u!114 &(-?\d+)\nMonoBehaviour:\n(?:  .*\n)*?  m_Script: \{fileID: 11500000, guid: " + script + @", type: 3\}\n(?:  .*\n)*");
				LogAssert.IsTrue(doc.Success, $"the {tier} renderer has the world-passes feature");
				LogAssert.IsTrue(renderer.Contains("  - {fileID: " + doc.Groups[1].Value + "}\n"), $"and lists it among its features ({tier})");
				LogAssert.IsTrue(doc.Value.Contains("  m_Active: 1\n"), $"active ({tier})");
				LogAssert.IsTrue(doc.Value.Contains("PassesCompute: {fileID: 7200000, guid: " + compute), $"with its core compute, so a build carries it ({tier})");
			}
		}

		[Test]
		public void TheWorldPassesKernels_AreCore_NotTheTerrains()
		{
			string passes = ProjectFiles.ReadAllText("Assets/FishMMO/Shaders/FishWorldPasses.compute");
			string terrain = ProjectFiles.ReadAllText("Assets/Prefabs/Client/Weather/Shaders/FishTerrainInstancing.compute");
			foreach (string kernel in new[] { "FishHiZCopy", "FishHiZReduce", "FishLightTableGather" })
			{
				LogAssert.IsTrue(passes.Contains("#pragma kernel " + kernel), $"{kernel} is in the core compute");
				LogAssert.IsFalse(terrain.Contains("#pragma kernel " + kernel), $"and not in the terrain's culling compute ({kernel})");
			}
		}

		[Test]
		public void TheClouds_QueueNoneOfTheSharedPasses()
		{
			string clouds = SourceScanPins.ReadCode("Assets/FishMMO/Client/World/Weather/Sky/FishCloudsFeature.cs");
			foreach (string pass in new[] { "FishDepthPyramid.Pass", "ShadowCascadeCulling.Pass", "AdditionalLightTable.Pass", "CachedMainLightShadows.CachePass", "CachedMainLightShadows.MergePass" })
			{
				LogAssert.IsFalse(clouds.Contains(pass), $"the clouds no longer queue {pass}");
				LogAssert.IsTrue(SourceScanPins.ReadCode(Folder + "/FishWorldPassesFeature.cs").Contains("renderer.EnqueuePass(" + pass + ")"), $"the world passes do ({pass})");
			}
		}

		[Test]
		public void TheCoreRenderFolder_NamesNoWorldModuleType()
		{
			var world = new Regex(@"\b(TerrainGpuRenderer|WeatherRenderProfile|WeatherPresentation|SkySystem|FishCloudsFeature|GrassBlade\w*|DetailScatter\w*|SurfaceWater|Biome\w+)\b");
			foreach (string file in Directory.GetFiles(ProjectFiles.Folder(Folder), "*.cs"))
			{
				string code = SourceScanPins.ReadCode(Folder + "/" + Path.GetFileName(file));
				Match hit = world.Match(code);
				LogAssert.IsFalse(hit.Success, $"{Path.GetFileName(file)} names {hit.Value}: core rendering must not depend on a world module");
			}
		}
	}
}
