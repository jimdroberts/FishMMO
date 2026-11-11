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
		public void ApplyLookSensitivity_HoldsTheValueAndPutsTheCameraIntoDegrees()
		{
			ClientCameraSettings.ApplyLookSensitivity(1.5f);

			LogAssert.AreEqual(1.5f, ClientCameraSettings.LookSensitivity,
				"the chosen sensitivity is what the look input reads every frame");
			LogAssert.AreEqual(1f, cameraObject.GetComponent<KCCCamera>().RotationSpeed,
				"the look input arrives in degrees, so the camera must not scale it again");
		}

		[Test]
		public void AMouseCount_TurnsTheCommonScale_AtTheReferenceView()
		{
			/* 0.022 degrees per count at sensitivity 1, the common scale for mouse look, at a 90-degree 4:3
			 * view (73.74 degrees vertical), so a sensitivity a player already uses carries over. */
			ClientCameraSettings.ApplyLookSensitivity(2.0f);

			Assert.That(ClientCameraSettings.MouseDegreesPerCount(73.7398f), Is.EqualTo(0.044f).Within(1e-5f),
				"two at the reference view is 0.044 degrees a count");
		}

		[Test]
		public void TheTurn_FollowsTheFieldOfView_SoThePictureMovesTheSame()
		{
			// tan(fov/2) against the reference's: a narrower view turns more slowly, by its focal length.
			ClientCameraSettings.ApplyLookSensitivity(1.0f);

			float at60 = ClientCameraSettings.MouseDegreesPerCount(60f);
			Assert.That(at60, Is.EqualTo(0.022f * Mathf.Tan(30f * Mathf.Deg2Rad) / Mathf.Tan(0.5f * 73.7398f * Mathf.Deg2Rad)).Within(1e-6f),
				"at the world camera's 60 degrees the turn is scaled by tan 30 / tan 36.87");
			LogAssert.IsTrue(ClientCameraSettings.MouseDegreesPerCount(30f) < at60,
				"zooming in turns more slowly");
			LogAssert.AreEqual(1f, ClientCameraSettings.FieldOfViewScale(0f),
				"no usable view angle leaves the turn unscaled rather than at zero");
		}

		[Test]
		public void ApplyLookSensitivity_ClampsAValueTooLargeToRecoverFrom()
		{
			/* The value scales raw mouse movement and lives in a text file the player can edit. A
			 * large one makes the view unusable at exactly the moment they would need to aim at the
			 * menu to put it back. */
			ClientCameraSettings.ApplyLookSensitivity(500.0f);

			LogAssert.AreEqual(ClientCameraSettings.MaximumLookSensitivity, ClientCameraSettings.LookSensitivity,
				"an out-of-range sensitivity must be clamped, not honoured");
		}

		[Test]
		public void ApplyLookSensitivity_RefusesAZeroThatWouldFreezeTheView()
		{
			ClientCameraSettings.ApplyLookSensitivity(0.0f);

			LogAssert.IsTrue(ClientCameraSettings.LookSensitivity > 0.0f,
				"zero is a camera that cannot be moved at all");
		}

		[Test]
		public void ApplyLookSensitivity_FallsBackToTheDefaultOnANonNumber()
		{
			ClientCameraSettings.ApplyLookSensitivity(float.NaN);

			LogAssert.AreEqual(ClientCameraSettings.DefaultLookSensitivity, ClientCameraSettings.LookSensitivity,
				"a corrupt stored value must land on the default, not propagate as NaN");
		}

		[Test]
		public void ApplyLookSensitivity_ReportsWhetherACameraReceivedIt()
		{
			/* What the Debug line in ApplySaved reports. The no-camera path is the normal one at boot, so
			 * it cannot be an error, which is exactly why it needs to be visible instead. */
			LogAssert.IsTrue(ClientCameraSettings.ApplyLookSensitivity(0.4f),
				"a camera is present, so it is set to take degrees");

			UnityEngine.Object.DestroyImmediate(cameraObject);
			cameraObject = null;

			LogAssert.IsFalse(ClientCameraSettings.ApplyLookSensitivity(0.4f),
				"with no camera there is nothing to set, and the caller must be able to tell");
		}

		[Test]
		public void TheLookInput_IsConvertedToDegreesEveryFrame()
		{
			/* Pinned in source: the conversion needs the live field of view, so it runs where the camera is
			 * fed rather than once in the settings, and the stick is a rate scaled by frame time. */
			string source = ReadSource("Assets/Scripts/Client/Input/PlayerInputController.cs");
			StringAssert.Contains("Character.KCCPlayer.UpdateCamera(-mouseScrollInput, LookDegrees());", source);
			StringAssert.Contains("lookInput * ClientCameraSettings.MouseDegreesPerCount(fieldOfView)", source);
			StringAssert.Contains("ClientCameraSettings.StickDegreesPerSecondAt(fieldOfView) * Time.unscaledDeltaTime", source);
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
