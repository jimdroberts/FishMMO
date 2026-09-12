using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using FishMMO.Shared;
using NUnit.Framework;
using UnityEngine;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Proofs that the camera goes back to where the login screen expects it when the player quits
	/// to login.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The reported symptom was the camera keeping its world position and rotation after "Quit To
	/// Login". The cause was not a missing restore — there was one, writing the transform back to a
	/// pose captured at boot — but that the restore was inert. <see cref="KCCCamera"/> re-derives
	/// both the position and the rotation from its own state on every update, so a pose written
	/// from outside survives exactly until the next LateUpdate, and once the local character is
	/// destroyed nothing writes again and the camera is frozen at the world pose for good.
	/// </para>
	/// <para>
	/// So the test that matters is not "does a restore exist" but "does the camera stay restored
	/// when the character it left behind is still being driven one more frame". That is the first
	/// case below, and it is the one that fails against the old behaviour.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class CameraRestTests
	{
		/// <summary>Frame time handed to the camera. Large enough that its smoothing has landed.</summary>
		private const float DeltaTime = 0.05f;

		/// <summary>Pose the fixture's camera is authored at, i.e. what the scene would contribute.</summary>
		private static readonly Vector3 AuthoredPosition = new Vector3(3f, 12f, -7f);
		private static readonly Quaternion AuthoredRotation = Quaternion.Euler(15f, 210f, 0f);

		/// <summary>Where the "character" stands while it owns the camera.</summary>
		private static readonly Vector3 WorldPosition = new Vector3(120f, 7f, -80f);

		/// <summary>Where the next character stands, after the camera has been released.</summary>
		private static readonly Vector3 NextWorldPosition = new Vector3(-40f, 2f, 15f);

		private GameObject cameraObject;
		private GameObject followPoint;
		private readonly List<GameObject> suppressed = new List<GameObject>();

		[SetUp]
		public void SetUp()
		{
			/* The camera under test is resolved through Camera.main, so any other camera already
			 * tagged MainCamera would win and every assertion below would be made against an object
			 * nothing wrote to. */
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

			cameraObject = new GameObject("CameraRestTestCamera");
			cameraObject.tag = "MainCamera";

			/* Positioned before the component exists. The camera captures its rest pose in Awake, so
			 * this is the authored pose as far as the component is concerned — which is exactly the
			 * relationship the real camera in ClientPreboot has with the scene that authors it. */
			cameraObject.transform.SetPositionAndRotation(AuthoredPosition, AuthoredRotation);
			cameraObject.AddComponent<Camera>();

			KCCCamera camera = cameraObject.AddComponent<KCCCamera>();
			Awaken(camera);

			/* Obstruction test disabled. The test scene is full of things a sphere cast could hit,
			 * and the pulled-in distance would make every "the camera moved" assertion depend on
			 * whatever else happens to be lying around. */
			camera.ObstructionLayers = 0;

			Assume.That(Camera.main, Is.EqualTo(cameraObject.GetComponent<Camera>()),
				"the fixture's camera must be the one the game resolves");
		}

		[TearDown]
		public void TearDown()
		{
			if (cameraObject != null)
			{
				UnityEngine.Object.DestroyImmediate(cameraObject);
			}
			if (followPoint != null)
			{
				UnityEngine.Object.DestroyImmediate(followPoint);
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

		// --- The defect ------------------------------------------------------------------------

		[Test]
		public void LeavingTheWorld_LeavesTheCameraAtRest_EvenWhileTheOldCharacterIsDrivenOneMoreFrame()
		{
			/* The exact shape of the bug. Quitting to login tears the world down asynchronously, so
			 * on the frame the button is pressed the character is still alive and the player input
			 * controller still issues its camera update after the teardown has run. Any restore that
			 * only writes the transform is overwritten right here. */
			KCCCamera camera = cameraObject.GetComponent<KCCCamera>();

			BindAndDriveTo(camera, WorldPosition);

			LogAssert.IsTrue(Vector3.Distance(camera.transform.position, AuthoredPosition) > 1f,
				"the fixture must drive the camera away from its rest pose first, or the rest of this test proves nothing");

			camera.ReleaseFollowTarget();

			// The update the input controller would still issue on that frame.
			camera.UpdateWithInput(DeltaTime, 0f, Vector3.zero);

			LogAssert.IsTrue(
				Vector3.Distance(camera.transform.position, AuthoredPosition) < 0.0001f,
				"the camera must not be pulled back to the character it just left behind");
			LogAssert.IsTrue(
				Quaternion.Angle(camera.transform.rotation, AuthoredRotation) < 0.01f,
				"the camera must not be turned back towards the character it just left behind");
		}

		[Test]
		public void LeavingTheWorld_PutsTheCameraBackAtItsAuthoredPose()
		{
			KCCCamera camera = cameraObject.GetComponent<KCCCamera>();
			BindAndDriveTo(camera, WorldPosition);

			camera.ReleaseFollowTarget();

			LogAssert.IsTrue(
				Vector3.Distance(camera.transform.position, AuthoredPosition) < 0.0001f,
				"the camera must return to the pose its scene authored, not to the world origin and " +
				"not to wherever the player logged out");
			LogAssert.IsTrue(
				Quaternion.Angle(camera.transform.rotation, AuthoredRotation) < 0.01f,
				"the camera's rotation must be restored with its position");
		}

		[Test]
		public void ReleasingTheCamera_DoesNotPreventTheNextCharacterFromTakingIt()
		{
			/* The release is called from a teardown path that also runs on a reconnect, so it must
			 * leave the camera usable rather than latched off. */
			KCCCamera camera = cameraObject.GetComponent<KCCCamera>();
			BindAndDriveTo(camera, WorldPosition);

			camera.ReleaseFollowTarget();
			BindAndDriveTo(camera, NextWorldPosition);

			LogAssert.IsTrue(
				Vector3.Distance(camera.transform.position, AuthoredPosition) > 1f,
				"a character entering the world after a release must be able to adopt the camera again");
			LogAssert.IsTrue(
				Vector3.Distance(camera.transform.position, NextWorldPosition) < 20f,
				"and the camera must frame that character, not the one before it");
		}

		// --- The wiring ------------------------------------------------------------------------

		[Test]
		public void QuitToLogin_ReleasesTheCamera()
		{
			/* Pinned in source because the path is not reachable from a unit test: Client.QuitToLogin
			 * needs a live client, a connection and a loaded world scene. Without the call, the
			 * component above is correct and unused — which is precisely how the previous restore
			 * shipped broken. */
			string source = ReadSource("Assets/Scripts/Client/Client.cs");

			int quitToLogin = source.IndexOf("public void QuitToLogin(", StringComparison.Ordinal);
			LogAssert.IsTrue(quitToLogin >= 0, "Client.QuitToLogin must exist");

			int invoke = source.IndexOf("OnQuitToLogin?.Invoke()", quitToLogin, StringComparison.Ordinal);
			LogAssert.IsTrue(invoke >= 0, "Client.QuitToLogin must still raise OnQuitToLogin");

			int release = source.IndexOf("ReleaseFollowTarget()", quitToLogin, StringComparison.Ordinal);
			LogAssert.IsTrue(release >= 0,
				"leaving the world must hand the camera back before the login screens come up");
			LogAssert.IsTrue(release < invoke,
				"the camera must be released before OnQuitToLogin, so login scene work of its own " +
				"cannot run against a camera still parked in the world");
		}

		[Test]
		public void TheCameraPose_IsRestoredInExactlyOnePlace()
		{
			/* The old restore lived in ClientPostbootSystem and wrote Camera.main.transform
			 * directly. It was inert, and leaving it in place next to the new one would be two
			 * owners of the same pose — including one that still does nothing. */
			string source = ReadSource("Assets/Scripts/Client/ClientPostbootSystem.cs");

			LogAssert.IsFalse(
				source.Contains("Camera.main.transform.position ="),
				"the camera pose is KCCCamera's to restore; writing its transform from the " +
				"postboot system does not survive the next LateUpdate");
		}

		// --- Helpers ---------------------------------------------------------------------------

		/// <summary>
		/// Runs the component's <c>Awake</c>, which Unity does not call for a component added from
		/// a test.
		/// </summary>
		/// <remarks>
		/// In edit mode Unity raises <c>Awake</c>/<c>OnEnable</c> only for components that were
		/// already in a loaded scene; one added with <c>AddComponent</c> stays uninitialised until
		/// play mode starts. <see cref="KCCCamera"/> both resolves its transform and captures its
		/// rest pose there, so without this the component under test would hold a null transform and
		/// every update would throw — the fixture would be measuring nothing. Calling the real method
		/// keeps the test on the production path rather than beside it, and the capture then happens
		/// at the same point in the sequence it does in the game: before anything has moved the
		/// camera.
		/// </remarks>
		private static void Awaken(KCCCamera camera)
		{
			MethodInfo awake = typeof(KCCCamera).GetMethod("Awake",
				BindingFlags.Instance | BindingFlags.NonPublic);
			LogAssert.IsNotNull(awake,
				"KCCCamera must still initialize in Awake, or the camera it hands out is inert");
			awake.Invoke(camera, null);
		}

		/// <summary>
		/// Points the camera at a character standing at <paramref name="position"/> and runs the
		/// updates that would settle it there.
		/// </summary>
		/// <remarks>
		/// Two updates, not one, because the follow position is smoothed towards the target. The
		/// sharpness is high enough that it effectively lands on the first, and the second keeps
		/// that from being an assumption the fixture depends on.
		/// </remarks>
		private void BindAndDriveTo(KCCCamera camera, Vector3 position)
		{
			if (followPoint == null)
			{
				followPoint = new GameObject("CameraRestTestFollowPoint");
			}
			followPoint.transform.position = position;

			camera.SetFollowTransform(followPoint.transform);
			camera.UpdateWithInput(DeltaTime, 0f, Vector3.zero);
			camera.UpdateWithInput(DeltaTime, 0f, Vector3.zero);
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
