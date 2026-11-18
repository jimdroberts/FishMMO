using System.Collections.Generic;
using System.IO;
using System.Reflection;
using FishMMO.Client;
using FishMMO.Shared;
using NUnit.Framework;
using UnityEngine;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Proofs for the camera view options (Options → Gameplay → CAMERA and OVER SHOULDER, 2026-11-15): that each value
	/// is clamped, saved, read back and put on the camera, and that the over-the-shoulder view no longer moves where a
	/// projectile starts.
	/// </summary>
	/// <remarks>
	/// Runs against a scratch <see cref="Configuration"/> swapped in as the global store, so nothing here reads or writes
	/// the developer's own Configuration.cfg, and against a camera of its own tagged MainCamera (others are suppressed:
	/// see CameraSettingsTests.SetUp for why that matters).
	/// </remarks>
	[TestFixture]
	public class CameraViewSettingsTests
	{
		private Configuration previous;
		private GameObject cameraObject;
		private GameObject follow;
		private readonly List<GameObject> suppressed = new List<GameObject>();

		[SetUp]
		public void SetUp()
		{
			previous = Configuration.GlobalSettings;
			Configuration.SetGlobalSettings(new Configuration(
				Path.Combine(Path.GetTempPath(), "FishMMO-CameraViewSettingsTests")));

			foreach (Camera existing in Resources.FindObjectsOfTypeAll<Camera>())
			{
				if (existing.gameObject.scene.IsValid() && existing.gameObject.activeInHierarchy && existing.CompareTag("MainCamera"))
				{
					existing.gameObject.SetActive(false);
					suppressed.Add(existing.gameObject);
				}
			}

			cameraObject = new GameObject("CameraViewSettingsTestCamera");
			cameraObject.tag = "MainCamera";
			cameraObject.AddComponent<Camera>();
			cameraObject.AddComponent<KCCCamera>();
			Assume.That(Camera.main, Is.EqualTo(cameraObject.GetComponent<Camera>()));

			ClientCameraSettings.LoadSavedView();
		}

		[TearDown]
		public void TearDown()
		{
			if (cameraObject != null)
			{
				Object.DestroyImmediate(cameraObject);
			}
			if (follow != null)
			{
				Object.DestroyImmediate(follow);
			}
			foreach (GameObject restored in suppressed)
			{
				if (restored != null)
				{
					restored.SetActive(true);
				}
			}
			suppressed.Clear();

			// Put the statics back to the defaults from an empty store, then the real store back.
			Configuration.SetGlobalSettings(new Configuration(
				Path.Combine(Path.GetTempPath(), "FishMMO-CameraViewSettingsTests-Reset")));
			ClientCameraSettings.LoadSavedView();
			FieldInfo field = typeof(Configuration).GetField("globalSettings", BindingFlags.NonPublic | BindingFlags.Static);
			if (field != null)
			{
				field.SetValue(null, previous);
			}
			else if (previous != null)
			{
				Configuration.SetGlobalSettings(previous);
			}
		}

		private KCCCamera Camera_ => cameraObject.GetComponent<KCCCamera>();

		// --- Values reach the camera --------------------------------------------------------------

		[Test]
		public void EveryViewSetting_ReachesTheCamera()
		{
			ClientCameraSettings.SetFieldOfView(75f);
			ClientCameraSettings.SetMaxZoom(18f);
			ClientCameraSettings.SetZoomStep(1f);
			ClientCameraSettings.SetInvertX(true);
			ClientCameraSettings.SetInvertY(true);
			ClientCameraSettings.SetShoulderLeft(true);
			ClientCameraSettings.SetShoulderOffset(0.7f);
			ClientCameraSettings.SetShoulderHeight(0.4f);
			ClientCameraSettings.SetShoulderMaxZoom(7f);

			KCCCamera camera = Camera_;
			LogAssert.AreEqual(75f, cameraObject.GetComponent<Camera>().fieldOfView, "field of view");
			LogAssert.AreEqual(18f, camera.MaxDistance, "third-person zoom limit");
			LogAssert.AreEqual(1f, camera.DistanceMovementSpeed, "zoom step");
			LogAssert.IsTrue(camera.InvertX, "invert X");
			LogAssert.IsTrue(camera.InvertY, "invert Y");
			LogAssert.IsTrue(camera.ShoulderLeft, "left shoulder");
			LogAssert.AreEqual(new Vector2(0.7f, 0.4f), camera.ShoulderOffset, "shoulder offset and height");
			LogAssert.AreEqual(7f, camera.ShoulderMaxDistance, "shoulder zoom limit");
		}

		[Test]
		public void SavedValues_ReadBackTheSame()
		{
			ClientCameraSettings.SetFieldOfView(80f);
			ClientCameraSettings.SetMaxZoom(15f);
			ClientCameraSettings.SetShoulderLeft(true);
			ClientCameraSettings.SetShoulderHeight(-0.1f);

			ClientCameraSettings.LoadSavedView();

			LogAssert.AreEqual(80f, ClientCameraSettings.FieldOfView, "field of view");
			LogAssert.AreEqual(15f, ClientCameraSettings.MaxZoom, "zoom limit");
			LogAssert.IsTrue(ClientCameraSettings.ShoulderLeft, "shoulder side");
			Assert.That(ClientCameraSettings.ShoulderHeight, Is.EqualTo(-0.1f).Within(1e-6f), "shoulder height");
		}

		[Test]
		public void TheZoomLimit_ReachesDoubleTheAuthoredOne()
		{
			// "Max Zoom distance should be configurable up to double what it is now". The authored
			// camera stops at 10 m (ClientPreboot) and over the shoulder at 4 m.
			LogAssert.AreEqual(2f * ClientCameraSettings.DefaultMaxZoom, ClientCameraSettings.MaximumMaxZoom,
				"third person reaches double the authored limit");
			LogAssert.AreEqual(2f * ClientCameraSettings.DefaultShoulderMaxZoom, ClientCameraSettings.MaximumShoulderMaxZoom,
				"over the shoulder reaches double its authored limit");

			ClientCameraSettings.SetMaxZoom(1000f);
			LogAssert.AreEqual(ClientCameraSettings.MaximumMaxZoom, Camera_.MaxDistance, "clamped, not honoured");
		}

		[Test]
		public void NonNumbers_LandOnTheDefaults()
		{
			ClientCameraSettings.SetFieldOfView(float.NaN);
			ClientCameraSettings.SetMaxZoom(float.PositiveInfinity);

			LogAssert.AreEqual(ClientCameraSettings.DefaultFieldOfView, ClientCameraSettings.FieldOfView, "field of view");
			LogAssert.AreEqual(ClientCameraSettings.DefaultMaxZoom, ClientCameraSettings.MaxZoom, "zoom limit");
		}

		[Test]
		public void TheDefaults_AreTheAuthoredCamera()
		{
			// A player who never opens the panel keeps the camera they had.
			KCCCamera authored = new GameObject().AddComponent<KCCCamera>();
			try
			{
				LogAssert.AreEqual(authored.MaxDistance, ClientCameraSettings.DefaultMaxZoom, "zoom limit");
				LogAssert.AreEqual(authored.DistanceMovementSpeed, ClientCameraSettings.DefaultZoomStep, "zoom step");
				LogAssert.AreEqual(authored.ShoulderOffset, new Vector2(ClientCameraSettings.DefaultShoulderOffset, ClientCameraSettings.DefaultShoulderHeight), "shoulder");
				LogAssert.AreEqual(authored.ShoulderMaxDistance, ClientCameraSettings.DefaultShoulderMaxZoom, "shoulder zoom limit");
			}
			finally
			{
				Object.DestroyImmediate(authored.gameObject);
			}
		}

		[Test]
		public void SwapShoulder_FlipsSavesAndTellsThePanel()
		{
			int raised = 0;
			System.Action<bool> count = _ => raised++;
			ClientCameraSettings.OnShoulderChanged += count;
			try
			{
				ClientCameraSettings.SwapShoulder();
				LogAssert.IsTrue(ClientCameraSettings.ShoulderLeft, "first swap goes left");
				LogAssert.IsTrue(Camera_.ShoulderLeft, "and reaches the camera");
				ClientCameraSettings.LoadSavedView();
				LogAssert.IsTrue(ClientCameraSettings.ShoulderLeft, "and is saved");
				ClientCameraSettings.SwapShoulder();
				LogAssert.IsFalse(ClientCameraSettings.ShoulderLeft, "second swap comes back");
				LogAssert.AreEqual(2, raised, "the panel hears both");
			}
			finally
			{
				ClientCameraSettings.OnShoulderChanged -= count;
			}
		}

		// --- The shoulder moves the view, not the shot ---------------------------------------------

		[Test]
		public void TheShoulderDisplacement_IsTheShoulderOnEitherSide_AndZeroInThirdPerson()
		{
			KCCCamera camera = FollowedCamera();
			camera.SetStyle(KCCCameraStyle.OverShoulder);
			Settle(camera);

			Vector3 right = camera.transform.right;
			AssertNear(right * 0.55f + Vector3.up * 0.2f, camera.ShoulderDisplacement, "right shoulder");

			camera.ShoulderLeft = true;
			Settle(camera);
			AssertNear(-right * 0.55f + Vector3.up * 0.2f, camera.ShoulderDisplacement, "left shoulder");

			camera.SetStyle(KCCCameraStyle.ThirdPerson);
			Settle(camera);
			AssertNear(Vector3.zero, camera.ShoulderDisplacement, "no shoulder in third person");
		}

		[Test]
		public void OverTheShoulder_TheLaunchLineStartsAtTheCharacter_AsInThirdPerson()
		{
			/* The camera's ray runs through the shoulder pivot; the
			 * owner takes the shoulder's displacement back out before stating the line (KCCPlayer.ResolveOwnerAim). */
			Vector3 eye = new Vector3(10f, 2f, 10f);
			Vector3 forward = Quaternion.Euler(12f, 30f, 0f) * Vector3.forward;
			AimConvergence.AimFrame(forward, out Vector3 right, out _);
			Vector3 displacement = right * 0.55f + Vector3.up * 0.2f;
			Vector3 cameraPosition = eye + displacement - forward * 2.5f;

			Vector3 launchPoint = cameraPosition + forward * AimConvergence.RayStart(cameraPosition, forward, eye);
			AimConvergence.AimLaunch raw = AimConvergence.EncodeLaunch(eye, launchPoint, forward, 20f);
			AimConvergence.AimLaunch corrected = AimConvergence.EncodeLaunch(eye, launchPoint - displacement, forward, 20f);

			LogAssert.IsTrue(raw.Right > 40, $"the camera's own line starts at the shoulder ({raw.Right} cm right): the defect");
			LogAssert.IsTrue(Mathf.Abs(corrected.Right) <= 1 && Mathf.Abs(corrected.Up) <= 1,
				$"with the shoulder taken out the line starts at the eye ({corrected.Right}, {corrected.Up} cm)");

			AimConvergence.LaunchLine(eye, forward, corrected, out Vector3 origin, out Vector3 direction);
			AssertNear(eye, origin, "the projectile starts at the character");
			Assert.That(Vector3.Angle(direction, forward), Is.LessThan(0.5f), "and flies to the crosshair's point");
		}

		[Test]
		public void TheOwnerAim_TakesTheShoulderOutOfTheLaunchLine()
		{
			string source = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(),
				"Assets/FishMMO/Shared/Implementation/Entity/Prediction/KCC/KCCPlayer.cs"));
			int subtract = source.IndexOf("launchPoint -= CharacterCamera.ShoulderDisplacement;", System.StringComparison.Ordinal);
			int encode = source.IndexOf("launch = AimConvergence.EncodeLaunch(eye, launchPoint", System.StringComparison.Ordinal);
			LogAssert.IsTrue(subtract >= 0, "ResolveOwnerAim must take the shoulder out of the launch line");
			LogAssert.IsTrue(encode > subtract, "before the line is encoded");
		}

		/// <summary>A camera following a point far from anything the test scene might hold.</summary>
		private KCCCamera FollowedCamera()
		{
			follow = new GameObject("CameraViewSettingsTestFollow");
			follow.transform.position = new Vector3(0f, 5000f, 0f);
			KCCCamera camera = Camera_;
			// Unity does not raise Awake for a component added in edit mode, and Awake is what binds its transform.
			typeof(KCCCamera).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(camera, null);
			camera.SetFollowTransform(follow.transform);
			return camera;
		}

		/// <summary>Runs enough long updates for every eased value to arrive.</summary>
		private static void Settle(KCCCamera camera)
		{
			for (int i = 0; i < 10; i++)
			{
				camera.UpdateWithInput(1f, 0f, Vector3.zero);
			}
		}

		private static void AssertNear(Vector3 expected, Vector3 actual, string message)
		{
			LogAssert.IsTrue((expected - actual).magnitude < 1e-3f, $"{message}: expected {expected}, got {actual}");
		}
	}
}
