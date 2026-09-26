using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Tests for the scene a <see cref="WorldSceneSettings"/> resolves once and the biome sampler
	/// reads, in place of asking the engine for <c>gameObject.scene</c> on every sample.
	/// </summary>
	/// <remarks>
	/// The sampler runs on the server and on the owning client for every character every second
	/// (weather exposure), and each sample asked for the scene, and for its name, about ten times.
	/// The answer must stay what it was: the component's own scene.
	/// </remarks>
	[TestFixture]
	public class WorldSceneSettingsOwnSceneTests
	{
		private GameObject probe;

		[TearDown]
		public void TearDown()
		{
			if (probe != null)
			{
				UnityEngine.Object.DestroyImmediate(probe);
				probe = null;
			}
		}

		[Test]
		public void OwnScene_IsTheComponentsScene()
		{
			probe = new GameObject("Own Scene Probe");
			WorldSceneSettings settings = probe.AddComponent<WorldSceneSettings>();

			LogAssert.AreEqual(probe.scene.handle, settings.OwnScene.handle,
				"the resolved scene must be the one the component is in");
			LogAssert.AreEqual(probe.scene.handle, settings.OwnScene.handle,
				"and stay it on a second read");
		}

		[Test]
		public void TheSampler_ReadsTheResolvedScene()
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), "Assets/Scripts/Shared/Implementation/Tools/Biomes/BiomeSampler.cs");
			LogAssert.IsTrue(File.Exists(path), $"BiomeSampler.cs not found at {path}.");
			string source = File.ReadAllText(path);

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
			string path = Path.Combine(Directory.GetCurrentDirectory(), "Assets/Scripts/Shared/Implementation/Entity/WorldSceneDetails/WorldSceneSettings.cs");
			LogAssert.IsTrue(File.Exists(path), $"WorldSceneSettings.cs not found at {path}.");
			string source = File.ReadAllText(path).Replace("\r\n", "\n");

			LogAssert.IsTrue(source.Contains("public WorldAtlasScene AtlasEntry => WorldAtlasScene.Find(OwnSceneName);"),
				"the atlas lookup must use the resolved scene name, not a fresh one per read");
			LogAssert.IsTrue(source.Contains("if (ownScene.handle == 0)"),
				"the scene must be resolved only when it has not been yet");
			LogAssert.IsTrue(source.Contains("if (!Application.isPlaying)\n\t\t\t\t{\n\t\t\t\t\treturn gameObject.scene;"),
				"outside play mode the scene must be read live, as before");
		}
	}
}
