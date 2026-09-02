using System;
using System.Collections.Generic;
using System.IO;
using FishMMO.Client;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Proofs for the antialiasing option: that the chosen mode reaches the camera, and that the
	/// stored value keeps its meaning.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The camera ships with antialiasing disabled while post-processing is enabled, which is what
	/// made character silhouettes visibly stair-stepped. Turning it on is the easy half; the half
	/// worth testing is that the setting survives being stored and read back, and that it is
	/// applied at a moment when a camera actually exists.
	/// </para>
	/// <para>
	/// That second point is not hypothetical for this class. The look sensitivity it sits beside
	/// shipped applying only at boot, before any world camera exists, so the saved value never
	/// reached the camera at all. Antialiasing goes through the same apply, so the same test is
	/// owed.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class AntialiasingSettingTests
	{
		private GameObject cameraObject;
		private readonly List<GameObject> suppressed = new List<GameObject>();

		[SetUp]
		public void SetUp()
		{
			/* Any other camera tagged MainCamera would win Camera.main and every assertion here
			 * would be made against a camera nothing wrote to. */
			foreach (Camera existing in Resources.FindObjectsOfTypeAll<Camera>())
			{
				if (existing.gameObject.scene.IsValid() &&
					existing.gameObject.activeInHierarchy &&
					existing.CompareTag("MainCamera"))
				{
					existing.gameObject.SetActive(false);
					suppressed.Add(existing.gameObject);
				}
			}

			cameraObject = new GameObject("AntialiasingTestCamera");
			cameraObject.tag = "MainCamera";
			cameraObject.AddComponent<Camera>();
			cameraObject.AddComponent<UniversalAdditionalCameraData>();

			Assume.That(Camera.main, Is.EqualTo(cameraObject.GetComponent<Camera>()),
				"the fixture's camera must be the one ClientCameraSettings resolves");
		}

		[TearDown]
		public void TearDown()
		{
			if (cameraObject != null)
			{
				UnityEngine.Object.DestroyImmediate(cameraObject);
			}

			foreach (GameObject restored in suppressed)
			{
				if (restored != null)
				{
					restored.SetActive(true);
				}
			}
			suppressed.Clear();
		}

		private AntialiasingMode Applied() =>
			cameraObject.GetComponent<UniversalAdditionalCameraData>().antialiasing;

		[Test]
		public void EachOption_ReachesTheCameraAsItsOwnMode()
		{
			/* Table-driven because the mapping is the whole feature: a switch that returned the
			 * same mode for two options would still pass a test that only checked one of them. */
			var expected = new Dictionary<ClientCameraSettings.AntialiasingOption, AntialiasingMode>
			{
				{ ClientCameraSettings.AntialiasingOption.Off, AntialiasingMode.None },
				{ ClientCameraSettings.AntialiasingOption.Fast, AntialiasingMode.FastApproximateAntialiasing },
				{ ClientCameraSettings.AntialiasingOption.Balanced, AntialiasingMode.SubpixelMorphologicalAntiAliasing },
				{ ClientCameraSettings.AntialiasingOption.Temporal, AntialiasingMode.TemporalAntiAliasing },
			};

			foreach (var pair in expected)
			{
				ClientCameraSettings.ApplyAntialiasing(pair.Key);

				LogAssert.AreEqual(pair.Value, Applied(),
					$"{pair.Key} must select {pair.Value}");
			}
		}

		[Test]
		public void EveryOption_MapsToADistinctMode()
		{
			/* Guards the mapping as a whole rather than entry by entry. Two options that resolve to
			 * the same mode would leave the player a choice that does nothing. */
			var seen = new HashSet<AntialiasingMode>();

			foreach (ClientCameraSettings.AntialiasingOption option in
				Enum.GetValues(typeof(ClientCameraSettings.AntialiasingOption)))
			{
				ClientCameraSettings.ApplyAntialiasing(option);

				LogAssert.IsTrue(seen.Add(Applied()),
					$"{option} selects a mode another option already selects");
			}
		}

		[Test]
		public void ApplyAntialiasing_ReportsWhetherACameraReceivedIt()
		{
			LogAssert.IsTrue(
				ClientCameraSettings.ApplyAntialiasing(ClientCameraSettings.AntialiasingOption.Balanced),
				"a camera is present, so the mode lands");

			UnityEngine.Object.DestroyImmediate(cameraObject);
			cameraObject = null;

			LogAssert.IsFalse(
				ClientCameraSettings.ApplyAntialiasing(ClientCameraSettings.AntialiasingOption.Balanced),
				"with no camera the caller must be able to tell the mode went nowhere");
		}

		[Test]
		public void TheDefault_IsNotOff()
		{
			/* The point of the change. The camera already had antialiasing available and switched
			 * off; a default that leaves it off would ship the same jagged edges behind a new
			 * control that looks like it should have fixed them. */
			LogAssert.IsTrue(
				ClientCameraSettings.DefaultAntialiasing != ClientCameraSettings.AntialiasingOption.Off,
				"the default must actually smooth edges");
		}

		[Test]
		public void TheStoredOrdinals_AreFixed()
		{
			/* The saved file holds these numbers. Reordering the enum silently changes what an
			 * existing player's setting means -- they would find their choice had become a
			 * different one, with nothing to explain it. */
			LogAssert.AreEqual(0, (int)ClientCameraSettings.AntialiasingOption.Off);
			LogAssert.AreEqual(1, (int)ClientCameraSettings.AntialiasingOption.Fast);
			LogAssert.AreEqual(2, (int)ClientCameraSettings.AntialiasingOption.Balanced);
			LogAssert.AreEqual(3, (int)ClientCameraSettings.AntialiasingOption.Temporal);
		}

		[Test]
		public void TheDropdownLabels_CoverEveryOptionInOrder()
		{
			/* The dropdown stores the selected INDEX, so its labels and the enum have to stay the
			 * same length and the same order. A label list that drifted would quietly map a
			 * player's click onto a different mode. */
			string source = File.ReadAllText(Path.Combine(
				Directory.GetCurrentDirectory(),
				"Assets/Scripts/Client/GUI/World/Options/UITKOptions.cs"));

			int labels = source.IndexOf("AntialiasingLabels =", StringComparison.Ordinal);
			LogAssert.IsTrue(labels >= 0, "the dropdown must still declare its labels");
			int end = source.IndexOf("};", labels, StringComparison.Ordinal);
			LogAssert.IsTrue(end > labels, "the label list must be terminated");

			string block = source.Substring(labels, end - labels);

			int optionCount = Enum.GetValues(typeof(ClientCameraSettings.AntialiasingOption)).Length;
			int labelCount = block.Split('"').Length / 2;

			LogAssert.AreEqual(optionCount, labelCount,
				"there must be exactly one dropdown label per option");
		}

		[Test]
		public void EachLabel_NamesItsTechnique()
		{
			/* The acronym is what a player can act on outside the game -- comparing notes, searching
			 * for what it looks like, reading a recommendation. The plain word is what they can act
			 * on inside it. Dropping either half is a real loss, and dropping the acronym is the
			 * easy one to do while "tidying up" the labels. */
			string source = File.ReadAllText(Path.Combine(
				Directory.GetCurrentDirectory(),
				"Assets/Scripts/Client/GUI/World/Options/UITKOptions.cs"));

			int labels = source.IndexOf("AntialiasingLabels =", StringComparison.Ordinal);
			LogAssert.IsTrue(labels >= 0, "the dropdown must still declare its labels");

			int end = source.IndexOf("};", labels, StringComparison.Ordinal);
			string block = source.Substring(labels, end - labels);

			foreach (string technique in new[] { "FXAA", "SMAA", "TAA" })
			{
				LogAssert.IsTrue(block.Contains(technique),
					$"the labels must name {technique}, not only describe it");
			}
		}

		[Test]
		public void TheSavedAntialiasing_IsAppliedWhenTheLocalCharacterInitializes()
		{
			/* Pinned in source, for the same reason the sensitivity apply is: this is the wiring
			 * whose absence made the sensitivity setting silently do nothing for a release. The
			 * boot-time apply runs before a world camera exists, so it cannot be the only one. */
			string source = File.ReadAllText(Path.Combine(
				Directory.GetCurrentDirectory(),
				"Assets/Scripts/Client/Settings/ClientCameraSettings.cs"));

			int applySaved = source.IndexOf("public static void ApplySaved()", StringComparison.Ordinal);
			LogAssert.IsTrue(applySaved >= 0, "ApplySaved must still exist");

			/* Bounded to ApplySaved's own body, ending at the next method. Searching forward without
			 * a bound finds the DECLARATION of ApplySavedAntialiasing further down the file, and
			 * passes whether or not ApplySaved ever calls it -- which is exactly what it did, until
			 * a control run caught it. */
			int end = source.IndexOf("public static bool ApplyLookSensitivity", applySaved, StringComparison.Ordinal);
			LogAssert.IsTrue(end > applySaved, "the end of ApplySaved must be locatable");

			string body = source.Substring(applySaved, end - applySaved);

			LogAssert.IsTrue(body.Contains("ApplySavedAntialiasing()"),
				"ApplySaved must apply antialiasing too, or it only reaches the camera at boot");
		}
	}
}
