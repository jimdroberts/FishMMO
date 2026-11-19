using NUnit.Framework;
using UnityEngine;
using FishMMO.Client;
using FishMMO.Shared;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>The core's fallbacks for a project without the world modules (Phase 4 follow-ups, 2026-11-18).</summary>
	public class CoreWorldFallbackTests
	{
		[Test]
		public void ServerPreloadLabels_AreModuleContributed_AndTheLauncherNamesNoModuleLabel()
		{
			ServerPreloadLabels.Register("Test_Label");
			ServerPreloadLabels.Register("Test_Label");
			int count = 0;
			foreach (string label in ServerPreloadLabels.All)
			{
				count += label == "Test_Label" ? 1 : 0;
			}
			LogAssert.IsTrue(count == 1, "a label is asked for once");
			string launcher = SourceScanPins.ReadCode("Assets/FishMMO/Server/Implementation/ServerLauncher.cs");
			LogAssert.IsFalse(launcher.Contains("SharedBiomesLabel"), "the core launcher names no module's label");
			LogAssert.IsTrue(launcher.Contains("ServerPreloadLabels.All"), "it preloads what the modules ask for");
		}

		[Test]
		public void TheSkyColour_AppliesOnlyToAGameCameraInAWorldWithNoSkyMaterial()
		{
			var go = new GameObject("Sky Colour Test Camera");
			Material skybox = RenderSettings.skybox;
			var settingsObject = new GameObject("Sky Colour Test World");
			try
			{
				Camera camera = go.AddComponent<Camera>();
				camera.clearFlags = CameraClearFlags.Skybox;
				RenderSettings.skybox = null;
				LogAssert.IsFalse(CoreSkyColour.Applies(camera), "no world scene loaded: the camera keeps its own colour");
				// OnEnable does not run for a component added in edit mode: register it as play mode would.
				var settings = settingsObject.AddComponent<WorldSceneSettings>();
				Enable(settings, "OnEnable");
				LogAssert.IsTrue(CoreSkyColour.Applies(camera), "a world with no sky material: the plain sky");
				RenderSettings.skybox = new Material(Shader.Find("Hidden/InternalErrorShader"));
				LogAssert.IsFalse(CoreSkyColour.Applies(camera), "a sky material is drawn instead");
				Object.DestroyImmediate(RenderSettings.skybox);
				RenderSettings.skybox = null;
				camera.clearFlags = CameraClearFlags.Depth;
				LogAssert.IsFalse(CoreSkyColour.Applies(camera), "a camera that does not clear its colour is left alone");
			}
			finally
			{
				RenderSettings.skybox = skybox;
				var leaving = settingsObject.GetComponent<WorldSceneSettings>();
				if (leaving != null)
				{
					Enable(leaving, "OnDisable");
				}
				Object.DestroyImmediate(settingsObject);
				Object.DestroyImmediate(go);
			}
		}

		private static void Enable(WorldSceneSettings settings, string method)
		{
			typeof(WorldSceneSettings).GetMethod(method, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(settings, null);
		}
	}
}
