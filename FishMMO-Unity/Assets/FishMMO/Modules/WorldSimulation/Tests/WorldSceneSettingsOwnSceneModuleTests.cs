using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>Split from WorldSceneSettingsOwnSceneTests (Phase 4 of the core and modules plan, 2026-11-18): the World Simulation module's half.</summary>
	public class WorldSceneSettingsOwnSceneModuleTests
	{
		[Test]
		public void TheSampler_ReadsTheResolvedScene()
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), "Assets/FishMMO/Modules/WorldSimulation/Runtime/Biomes/BiomeSampler.cs");
			LogAssert.IsTrue(ProjectFiles.Exists(path), $"BiomeSampler.cs not found at {path}.");
			string source = ProjectFiles.ReadAllText(path);

			LogAssert.IsTrue(source.Contains("Scene scene = settings != null ? settings.OwnScene : default;"),
				"BiomeSampler.Read must take the scene the settings component resolved");
			LogAssert.IsFalse(source.Contains("settings.gameObject.scene"),
				"BiomeSampler.Read must not ask the engine for the scene on every sample");
		}

		/// <summary>
		/// At runtime the scene is read once and then served from the component; the atlas lookup
		/// uses the name resolved with it. Outside play mode it stays live.
		/// </summary>
		[Test]
		public void TheSceneIsResolvedOnceAtRuntime_AndLiveInTheEditor()
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), "Assets/FishMMO/Shared/Implementation/Entity/WorldSceneDetails/WorldSceneSettings.cs");
			LogAssert.IsTrue(ProjectFiles.Exists(path), $"WorldSceneSettings.cs not found at {path}.");
			string source = ProjectFiles.ReadAllText(path).Replace("\r\n", "\n");

			LogAssert.IsTrue(source.Contains("public IWorldSceneEntry Entry => WorldSceneEntries.Find(SceneName);"),
				"the entry lookup must use the resolved scene name, not a fresh one per read");
			string atlas = ProjectFiles.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "Assets/FishMMO/Modules/WorldSimulation/Runtime/Atlas/WorldSceneAtlas.cs")).Replace("\r\n", "\n");
			LogAssert.IsTrue(atlas.Contains("WorldAtlasScene.Find(settings.SceneName)"),
				"and so must the world modules' own atlas lookup");
			LogAssert.IsTrue(source.Contains("if (ownScene.handle == SceneHandle.None)"),
				"the scene must be resolved only when it has not been yet");
			LogAssert.IsTrue(source.Contains("if (!Application.isPlaying)\n\t\t\t\t{\n\t\t\t\t\treturn gameObject.scene;"),
				"outside play mode the scene must be read live, as before");
		}
	}
}
