using System;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>Split from WorldPassesFeatureTests: the World Simulation module's half.</summary>
	public class WeatherPassesFeatureTests
	{
		[Test]
		public void TheClouds_QueueNoneOfTheSharedPasses()
		{
			string clouds = SourceScanPins.ReadCode("Assets/FishMMO/Modules/WorldSimulation/Client/Weather/Sky/FishCloudsFeature.cs");
			foreach (string pass in new[] { "FishDepthPyramid.Pass", "ShadowCascadeCulling.Pass", "AdditionalLightTable.Pass", "CachedMainLightShadows.CachePass", "CachedMainLightShadows.MergePass" })
			{
				LogAssert.IsFalse(clouds.Contains(pass), $"the clouds no longer queue {pass}");
				LogAssert.IsTrue(SourceScanPins.ReadCode(Folder + "/FishWorldPassesFeature.cs").Contains("renderer.EnqueuePass(" + pass + ")"), $"the world passes do ({pass})");
			}
		}

		private const string Folder = "Assets/FishMMO/Client/Rendering/WorldPasses";
	}
}
