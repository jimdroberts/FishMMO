using System.Collections.Generic;
using System.Reflection;
using FishMMO.Shared;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Proofs that the camera is kept out of the world it is framing — that the obstruction cast runs
	/// against the colliders the character is standing among, along the direction the camera is about
	/// to take.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The reported symptom was the camera passing straight through terrain and walls, so the player
	/// could see through them. The cause was not the shape of the cast — that was already a sphere —
	/// but the physics world it ran in. World scenes are loaded with
	/// <c>LocalPhysicsMode.Physics3D</c>, which puts every collider of the world in a private
	/// <see cref="PhysicsScene"/>, while the camera is a persistent object sitting in ClientPreboot.
	/// The static <c>Physics.*</c> queries only reach the default scene, so the cast matched nothing
	/// there and the camera kept its full distance however close the geometry was.
	/// </para>
	/// <para>
	/// <see cref="TheCast_SeesAWorldScene_NotTheSceneTheCameraSitsIn"/> is the case that fails against
	/// that behaviour, and the arrangement it builds is the game's: the camera and the character
	/// deliberately live in different scenes. An <see cref="EditorSceneManager"/> preview scene has a
	/// physics scene of its own, which is what makes that reachable from an edit-mode test —
	/// <c>SceneManager.CreateScene</c>, the runtime call, is rejected outside play mode.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class CameraObstructionTests
	{
		/// <summary>Frame time handed to the camera. Large enough that its smoothing has landed.</summary>
		private const float DeltaTime = 0.05f;

		/// <summary>How far the camera wants to sit behind the character when nothing is in the way.</summary>
		private const float DefaultDistance = 6f;

		/// <summary>
		/// Layer the fixture's blockers are put on. The project names nothing above layer 8, and the
		/// camera under test is given a mask for this one layer alone, so no collider the test scene
		/// happens to be carrying can be mistaken for a blocker.
		/// </summary>
		private const int BlockerLayer = 31;

		private static readonly LayerMask BlockerMask = 1 << BlockerLayer;

		/// <summary>
		/// Where the fixture works: far from the origin, so that even a mask slip could not reach
		/// anything the test scene left lying around.
		/// </summary>
		private static readonly Vector3 Origin = new Vector3(9000f, 500f, 9000f);

		/// <summary>Where a blocker sits to be between the character and the camera's default side.</summary>
		private static readonly Vector3 BehindTheCharacter = Origin + new Vector3(0f, 0f, -3f);

		/// <summary>The sphere the camera casts, matching <c>ObstructionCheckRadius</c>.</summary>
		private const float CastRadius = 0.2f;

		private GameObject cameraObject;
		private GameObject followPoint;
		private readonly List<GameObject> blockers = new List<GameObject>();
		private readonly List<Scene> previewScenes = new List<Scene>();

		[SetUp]
		public void SetUp()
		{
			followPoint = new GameObject("CameraObstructionTestFollowPoint");
			followPoint.transform.position = Origin;
		}

		[TearDown]
		public void TearDown()
		{
			/* The preview scenes go first: their contents are theirs, and closing the scene is what
			 * disposes of them. Destroying an object a closed scene already took with it is a no-op
			 * through Unity's null check, so the order is safe either way. */
			foreach (Scene scene in previewScenes)
			{
				if (scene.IsValid())
				{
					EditorSceneManager.ClosePreviewScene(scene);
				}
			}
			previewScenes.Clear();

			foreach (GameObject blocker in blockers)
			{
				if (blocker != null)
				{
					Object.DestroyImmediate(blocker);
				}
			}
			blockers.Clear();

			if (followPoint != null)
			{
				Object.DestroyImmediate(followPoint);
			}
			if (cameraObject != null)
			{
				Object.DestroyImmediate(cameraObject);
			}

			followPoint = null;
			cameraObject = null;
		}

		// --- The defect ------------------------------------------------------------------------

		[Test]
		public void TheCast_SeesAWorldScene_NotTheSceneTheCameraSitsIn()
		{
			/* The shape of the game. The camera is authored in ClientPreboot and outlives every world;
			 * the character is spawned into a world scene loaded with its own physics. */
			Scene world = EditorSceneManager.NewPreviewScene();
			previewScenes.Add(world);

			GameObject character = new GameObject("CameraObstructionTestCharacter");
			character.transform.position = Origin;
			SceneManager.MoveGameObjectToScene(character, world);

			CreateBlocker(BehindTheCharacter, world);

			KCCCamera camera = CreateCamera(character.transform);

			/* Control. Without it the rest of the test would pass just as well against a wall that was
			 * never out of reach, and would go on passing if preview scenes ever started sharing the
			 * default physics scene. */
			LogAssert.IsFalse(
				Physics.SphereCast(Origin, CastRadius, Vector3.back, out _, DefaultDistance, BlockerMask, QueryTriggerInteraction.Ignore),
				"control: the wall must be out of reach of the default physics scene, or this test proves nothing");

			Drive(camera);

			AssertPulledIn(camera, "a wall the character shares a physics scene with");
		}

		[Test]
		public void AWallBetweenTheCharacterAndTheCamera_PullsTheCameraIn()
		{
			KCCCamera camera = CreateCamera(BindFollowPoint().transform);

			CreateBlocker(BehindTheCharacter, default);

			Drive(camera);

			AssertPulledIn(camera, "a wall between the character and the camera");
		}

		// --- The rest of the cast ---------------------------------------------------------------

		[Test]
		public void TheCameraIsTested_AlongTheDirectionItIsAboutToTake()
		{
			KCCCamera camera = CreateCamera(BindFollowPoint().transform);

			CreateBlocker(BehindTheCharacter, default);

			Drive(camera);

			float behind = Vector3.Distance(camera.transform.position, Origin);
			LogAssert.IsTrue(behind < 3f,
				$"the fixture must have the camera pulled in first, or the turn below proves nothing (got {behind})");

			/* Half a turn in one update. The camera is about to be placed on the other side of the
			 * character, where nothing is in the way, so the distance has to start opening back up on
			 * this same update. Testing along the camera's own position instead re-reports the wall it
			 * was just behind — a frame stale at best, and on the turn that matters it pins the camera
			 * against a wall the player has already swung away from. */
			camera.UpdateWithInput(DeltaTime, 0f, new Vector3(180f, 0f, 0f));

			float after = Vector3.Distance(camera.transform.position, Origin);
			LogAssert.IsTrue(after > behind + 1f,
				$"the camera must be tested along the direction it is about to take, not the one it came from (got {after}, " +
				$"against {behind} before the turn)");
		}

		[Test]
		public void AColliderTheCharacterCarries_DoesNotPullTheCameraIn()
		{
			KCCCamera camera = CreateCamera(BindFollowPoint().transform);

			/* The character's own capsule now shares a physics scene with the walls, which is what
			 * makes the ignore list the thing standing between the camera and a character it could
			 * never back away from. In the cast's path rather than at its origin, so the cast would
			 * find it if it were not named here. */
			GameObject body = new GameObject("CameraObstructionTestBody");
			body.layer = BlockerLayer;
			body.transform.position = Origin + new Vector3(0f, 0f, -1f);
			BoxCollider capsule = body.AddComponent<BoxCollider>();
			capsule.size = new Vector3(6f, 6f, 0.6f);
			blockers.Add(body);

			/* Control: the collider is somewhere the cast reaches. */
			LogAssert.IsTrue(
				Physics.SphereCast(Origin, CastRadius, Vector3.back, out _, DefaultDistance, BlockerMask, QueryTriggerInteraction.Ignore),
				"control: the character's collider must be in the cast's path, or this test proves nothing");

			camera.IgnoredColliders.Add(capsule);

			Drive(camera);

			float distance = Vector3.Distance(camera.transform.position, Origin);
			LogAssert.IsTrue(distance > DefaultDistance - 0.01f,
				$"a collider the character owns must not be treated as world geometry (got {distance})");
		}

		[Test]
		public void AColliderOnAnUnwatchedLayer_DoesNotPullTheCameraIn()
		{
			KCCCamera camera = CreateCamera(BindFollowPoint().transform);

			CreateBlocker(BehindTheCharacter, default, layer: 0);

			/* Control: the wall is real and in reach, and only the mask is keeping the camera out. */
			LogAssert.IsTrue(
				Physics.SphereCast(Origin, CastRadius, Vector3.back, out _, DefaultDistance, ~0, QueryTriggerInteraction.Ignore),
				"control: the wall must be in reach of a cast that ignores no layers, or this test proves nothing");

			Drive(camera);

			float distance = Vector3.Distance(camera.transform.position, Origin);
			LogAssert.IsTrue(distance > DefaultDistance - 0.01f,
				$"only ObstructionLayers may obstruct the camera (got {distance})");
		}

		// --- Helpers ---------------------------------------------------------------------------

		/// <summary>
		/// Builds the camera under test and hands it the transform it should frame.
		/// </summary>
		/// <remarks>
		/// No <see cref="Camera"/> component: <see cref="KCCCamera"/> reads none of its settings, and
		/// adding one would leave a second live camera in the test scene.
		/// </remarks>
		private KCCCamera CreateCamera(Transform follow)
		{
			cameraObject = new GameObject("CameraObstructionTestCamera");
			KCCCamera camera = cameraObject.AddComponent<KCCCamera>();
			Awaken(camera);

			/* The authored reference camera's numbers, so the assertions below are about the same
			 * distances the game uses. */
			camera.ObstructionCheckRadius = CastRadius;
			camera.ObstructionLayers = BlockerMask;
			camera.DefaultDistance = DefaultDistance;

			camera.SetFollowTransform(follow);
			return camera;
		}

		/// <summary>The fixture's follow point, as the transform the camera frames.</summary>
		private GameObject BindFollowPoint()
		{
			return followPoint;
		}

		/// <summary>
		/// Puts an opaque wall across the cast at <paramref name="position"/>, optionally inside the
		/// world scene it should belong to.
		/// </summary>
		/// <param name="position">Where the wall's centre goes.</param>
		/// <param name="scene">The scene to move it into, or <c>default</c> to leave it where it is.</param>
		/// <param name="layer">Layer to put the wall on. Defaults to the fixture's blocker layer.</param>
		private void CreateBlocker(Vector3 position, Scene scene, int layer = BlockerLayer)
		{
			GameObject blocker = new GameObject("CameraObstructionTestBlocker");
			blocker.layer = layer;
			blocker.transform.position = position;

			BoxCollider box = blocker.AddComponent<BoxCollider>();
			box.size = new Vector3(6f, 6f, 1f);

			if (scene.IsValid())
			{
				SceneManager.MoveGameObjectToScene(blocker, scene);
			}

			blockers.Add(blocker);
		}

		/// <summary>
		/// Runs the updates that would settle the camera at its follow target.
		/// </summary>
		/// <remarks>
		/// Two updates, not one, because the follow position is smoothed towards the target. The
		/// sharpness is high enough that it effectively lands on the first, and the second keeps that
		/// from being an assumption the fixture depends on.
		/// </remarks>
		private static void Drive(KCCCamera camera)
		{
			camera.UpdateWithInput(DeltaTime, 0f, Vector3.zero);
			camera.UpdateWithInput(DeltaTime, 0f, Vector3.zero);
		}

		/// <summary>
		/// Asserts the camera has been shortened onto a blocker three units out, which a sphere of
		/// <see cref="CastRadius"/> puts at 2.3.
		/// </summary>
		/// <param name="camera">The camera under test.</param>
		/// <param name="subject">What the blocker is, for the failure message.</param>
		private static void AssertPulledIn(KCCCamera camera, string subject)
		{
			float distance = Vector3.Distance(camera.transform.position, Origin);
			LogAssert.IsTrue(distance > 2f && distance < 2.6f,
				$"{subject} must shorten the camera to the wall's surface (got {distance}, against a desired {DefaultDistance})");
		}

		/// <summary>
		/// Runs the component's <c>Awake</c>, which Unity does not call for a component added from a
		/// test.
		/// </summary>
		/// <remarks>
		/// In edit mode Unity raises <c>Awake</c>/<c>OnEnable</c> only for components that were
		/// already in a loaded scene; one added with <c>AddComponent</c> stays uninitialised until
		/// play mode starts. <see cref="KCCCamera"/> resolves its own transform there, and every
		/// update below reads it, so without this the component under test would throw on the first
		/// call and the fixture would be measuring nothing.
		/// </remarks>
		private static void Awaken(KCCCamera camera)
		{
			MethodInfo awake = typeof(KCCCamera).GetMethod("Awake",
				BindingFlags.Instance | BindingFlags.NonPublic);
			LogAssert.IsNotNull(awake,
				"KCCCamera must still initialize in Awake, or the camera it hands out is inert");
			awake.Invoke(camera, null);
		}
	}
}
