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

	}
}
