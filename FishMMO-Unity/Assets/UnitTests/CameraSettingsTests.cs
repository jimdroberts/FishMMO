using System;
using System.Collections.Generic;
using System.IO;
using FishMMO.Client;
using FishMMO.Shared;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Proofs for the two camera preferences the player controls: how fast the view turns, and how
	/// far one notch of scroll wheel moves it.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Both had the same shape of defect, which is why they are tested together. A value that is
	/// correct in isolation is worthless if the thing that ships never receives it: the sensitivity
	/// was stored and displayed correctly while the camera ran at its authored speed, and the zoom
	/// step was a sensible-looking number that produced two usable positions.
	/// </para>
	/// <para>
	/// So these tests deliberately check the authored scene and the wiring, not just the constants.
	/// A test that only asserted on <see cref="ClientCameraSettings"/> would have passed throughout
	/// the entire period the feature was broken.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class CameraSettingsTests
	{
		private const string PrebootScene = "Assets/Scenes/Client/ClientPreboot.unity";

		/// <summary>
		/// Fewest scroll notches that count as a zoom rather than a toggle.
		/// </summary>
		/// <remarks>
		/// Ten is a floor, not a target. The point is that intermediate framings are reachable at
		/// all: with the previous step of five over a range of ten there were two, so the wheel
		/// chose between "fully in" and "fully out" and nothing else.
		/// </remarks>
		private const int MinimumUsefulNotches = 10;

		private GameObject cameraObject;
		private readonly List<GameObject> suppressed = new List<GameObject>();

		[SetUp]
		public void SetUp()
		{
			/* ClientCameraSettings resolves its target through Camera.main, so any other camera
			 * already tagged MainCamera would win and every assertion below would be made against a
			 * camera nothing wrote to. Suppressing them is not tidiness: the first run of this
			 * fixture failed exactly that way, and two of its tests PASSED while doing so, because
			 * the value they expected happened to equal the camera's authored RotationSpeed of 1. */
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

			cameraObject = new GameObject("CameraSettingsTestCamera");
			cameraObject.tag = "MainCamera";
			cameraObject.AddComponent<Camera>();
			cameraObject.AddComponent<KCCCamera>();

			// The premise every sensitivity test rests on, checked rather than assumed.
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

		// --- Sensitivity reaches the camera ----------------------------------------------------

		[Test]
		public void ApplyLookSensitivity_WritesTheValueOntoTheCamera()
		{
			ClientCameraSettings.ApplyLookSensitivity(0.3f);

			LogAssert.AreEqual(0.3f, cameraObject.GetComponent<KCCCamera>().RotationSpeed,
				"the chosen sensitivity must reach the camera that actually turns the view");
		}

		[Test]
		public void ApplyLookSensitivity_ClampsAValueTooLargeToRecoverFrom()
		{
			/* The value multiplies raw mouse delta and lives in a text file the player can edit. A
			 * large one makes the view unusable at exactly the moment they would need to aim at the
			 * menu to put it back. */
			ClientCameraSettings.ApplyLookSensitivity(500.0f);

			LogAssert.AreEqual(ClientCameraSettings.MaximumLookSensitivity,
				cameraObject.GetComponent<KCCCamera>().RotationSpeed,
				"an out-of-range sensitivity must be clamped, not honoured");
		}

		[Test]
		public void ApplyLookSensitivity_RefusesAZeroThatWouldFreezeTheView()
		{
			ClientCameraSettings.ApplyLookSensitivity(0.0f);

			LogAssert.IsTrue(cameraObject.GetComponent<KCCCamera>().RotationSpeed > 0.0f,
				"zero is a camera that cannot be moved at all");
		}

		[Test]
		public void ApplyLookSensitivity_FallsBackToTheDefaultOnANonNumber()
		{
			ClientCameraSettings.ApplyLookSensitivity(float.NaN);

			LogAssert.AreEqual(ClientCameraSettings.DefaultLookSensitivity,
				cameraObject.GetComponent<KCCCamera>().RotationSpeed,
				"a corrupt stored value must land on the default, not propagate as NaN");
		}

		[Test]
		public void TheSavedSensitivity_IsAppliedWhenTheLocalCharacterInitializes()
		{
			/* Pinned in source. This is the wiring the feature was missing, and it is not reachable
			 * from a unit test: PlayerInputController.Initialize needs a networked player character.
			 *
			 * Boot applies settings before a world camera exists, so that apply does nothing. Until
			 * this second one existed, the camera kept its authored RotationSpeed for the whole
			 * session -- the saved value was stored, and shown correctly by the options panel, but
			 * did not take effect until the player moved the slider, once per launch. Every other
			 * test in this fixture passed throughout. */
			string source = ReadSource("Assets/Scripts/Client/Input/PlayerInputController.cs");

			int initialize = source.IndexOf("public void Initialize(", StringComparison.Ordinal);
			LogAssert.IsTrue(initialize >= 0, "PlayerInputController.Initialize must exist");

			int apply = source.IndexOf("ClientCameraSettings.ApplySaved()", initialize,
				StringComparison.Ordinal);

			LogAssert.IsTrue(apply >= 0,
				"the saved camera settings must be applied once the local character exists");
		}

		// --- Zoom reaches intermediate distances ------------------------------------------------

		[Test]
		public void TheZoomStepDefault_ReachesIntermediateDistances()
		{
			KCCCamera camera = cameraObject.GetComponent<KCCCamera>();

			LogAssert.IsTrue(Notches(camera) >= MinimumUsefulNotches,
				$"a wheel with {Notches(camera)} positions between fully in and fully out is a " +
				"toggle, not a zoom");
		}

		[Test]
		public void TheAuthoredCamera_ReachesIntermediateDistances()
		{
			/* The one that matters. The serialised scene value is what ships, and it overrides the
			 * field initialiser entirely -- changing the default in code moves nothing that already
			 * exists, so a test against the constant alone would report a fix that no player got. */
			Scene scene = EditorSceneManager.OpenScene(PrebootScene, OpenSceneMode.Additive);
			try
			{
				KCCCamera authored = null;
				foreach (GameObject root in scene.GetRootGameObjects())
				{
					authored = root.GetComponentInChildren<KCCCamera>(includeInactive: true);
					if (authored != null)
					{
						break;
					}
				}

				LogAssert.IsNotNull(authored, $"{PrebootScene} must carry the player's camera");
				LogAssert.IsTrue(Notches(authored) >= MinimumUsefulNotches,
					$"the authored camera offers {Notches(authored)} scroll positions");
			}
			finally
			{
				EditorSceneManager.CloseScene(scene, removeScene: true);
			}
		}

		/// <summary>
		/// Scroll notches between fully zoomed in and fully zoomed out.
		/// </summary>
		/// <remarks>
		/// The scroll delta arrives normalised to -1..1 (the Input System's default
		/// <c>ScrollDeltaBehavior</c>), so one notch moves the camera by exactly
		/// <c>DistanceMovementSpeed</c> and the count is a plain division.
		/// </remarks>
		private static int Notches(KCCCamera camera)
		{
			if (camera.DistanceMovementSpeed <= 0.0f)
			{
				return 0;
			}

			return Mathf.FloorToInt(
				(camera.MaxDistance - camera.MinDistance) / camera.DistanceMovementSpeed);
		}

		/// <summary>Reads a project source file, so wiring that lives in code can be pinned.</summary>
		private static string ReadSource(string relativePath)
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), relativePath);
			LogAssert.IsTrue(File.Exists(path), $"{relativePath} not found at {path}.");
			return File.ReadAllText(path);
		}
	}
}
