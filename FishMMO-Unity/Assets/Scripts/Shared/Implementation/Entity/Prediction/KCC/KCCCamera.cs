using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishMMO.Shared
{
	/// <summary>
	/// Third-person camera controller for character following, orbiting, zoom, and obstruction handling.
	/// Supports smooth movement, rotation, and framing adjustments.
	/// </summary>
	public class KCCCamera : MonoBehaviour
	{
		[Header("Framing")]
		/// <summary>
		/// Offset for camera framing relative to the follow point.
		/// </summary>
		public Vector2 FollowPointFraming = new Vector2(0f, 0f);
		/// <summary>
		/// Sharpness for following movement (higher = snappier).
		/// </summary>
		public float FollowingSharpness = 10000f;

		[Header("Distance")]
		/// <summary>
		/// Default camera distance from the target.
		/// </summary>
		public float DefaultDistance = 6f;
		/// <summary>
		/// Minimum allowed camera distance.
		/// </summary>
		public float MinDistance = 0f;
		/// <summary>
		/// Maximum allowed camera distance.
		/// </summary>
		public float MaxDistance = 10f;
		/// <summary>
		/// Distance the camera moves per notch of scroll wheel.
		/// </summary>
		/// <remarks>
		/// This is a distance per notch, not a rate: the scroll delta arrives normalised to -1..1
		/// (the Input System's default <c>ScrollDeltaBehavior</c>), so one notch moves the camera
		/// by exactly this much before smoothing.
		///
		/// Half a unit, down from five. Over the usual 0-10 range five gave two notches from fully
		/// in to fully out, so the wheel behaved as a toggle rather than a zoom and no intermediate
		/// framing was reachable. The value only reads as a "speed" if the delta is a rate; it is
		/// not, which is what made 5 look reasonable.
		/// </remarks>
		public float DistanceMovementSpeed = 0.5f;
		/// <summary>
		/// Sharpness for distance movement smoothing.
		/// </summary>
		public float DistanceMovementSharpness = 10f;

		[Header("Rotation")]
		/// <summary>
		/// Invert horizontal rotation input.
		/// </summary>
		public bool InvertX = false;
		/// <summary>
		/// Invert vertical rotation input.
		/// </summary>
		public bool InvertY = false;
		/// <summary>
		/// Default vertical angle for camera.
		/// </summary>
		[Range(-89f, 89f)]
		public float DefaultVerticalAngle = 20f;
		/// <summary>
		/// Minimum vertical angle for camera.
		/// </summary>
		[Range(-89f, 89f)]
		public float MinVerticalAngle = -89f;
		/// <summary>
		/// Maximum vertical angle for camera.
		/// </summary>
		[Range(-89f, 89f)]
		public float MaxVerticalAngle = 89f;
		/// <summary>
		/// Speed of camera rotation.
		/// </summary>
		public float RotationSpeed = 1f;
		/// <summary>
		/// Sharpness for rotation smoothing.
		/// </summary>
		public float RotationSharpness = 10000f;
		/// <summary>
		/// If true, camera rotates with physics mover.
		/// </summary>
		public bool RotateWithPhysicsMover = false;

		[Header("Obstruction")]
		/// <summary>
		/// Radius for obstruction sphere cast.
		/// </summary>
		public float ObstructionCheckRadius = 0.2f;
		/// <summary>
		/// Layers to check for camera obstructions.
		/// </summary>
		public LayerMask ObstructionLayers = -1;
		/// <summary>
		/// Sharpness for obstruction smoothing.
		/// </summary>
		public float ObstructionSharpness = 10000f;
		/// <summary>
		/// Colliders to ignore when checking for obstructions.
		/// </summary>
		public List<Collider> IgnoredColliders = new List<Collider>();

		/// <summary>
		/// The camera's transform.
		/// </summary>
		public Transform Transform { get; private set; }
		/// <summary>
		/// The transform the camera follows/orbits around.
		/// </summary>
		public Transform FollowTransform { get; private set; }

		/// <summary>
		/// The current planar (horizontal) direction of the camera.
		/// </summary>
		public Vector3 PlanarDirection { get; set; }
		/// <summary>
		/// The target camera distance (zoom).
		/// </summary>
		public float TargetDistance { get; set; }

		/// <summary>
		/// True if camera distance is currently obstructed.
		/// </summary>
		private bool distanceIsObstructed;
		/// <summary>
		/// The current camera distance.
		/// </summary>
		private float currentDistance;
		/// <summary>
		/// The current target vertical angle.
		/// </summary>
		private float targetVerticalAngle;
		/// <summary>
		/// Buffer the obstruction sphere cast fills. Grown through
		/// <see cref="TargetOrdering.TryGrowQueryBuffer{T}"/> rather than fixed.
		/// </summary>
		private RaycastHit[] obstructions = new RaycastHit[TargetOrdering.QueryBufferSize(0)];
		/// <summary>
		/// The current position the camera is following.
		/// </summary>
		private Vector3 currentFollowPosition;

		/// <summary>
		/// The position authored in the scene, captured in <see cref="Awake"/> before anything can
		/// have moved the camera. What <see cref="ReleaseFollowTarget"/> puts back.
		/// </summary>
		private Vector3 restPosition;
		/// <summary>
		/// The rotation authored in the scene. See <see cref="restPosition"/>.
		/// </summary>
		private Quaternion restRotation;

		/// <summary>
		/// Clamps default distance and vertical angle values when edited in inspector.
		/// </summary>
		void OnValidate()
		{
			DefaultDistance = Mathf.Clamp(DefaultDistance, MinDistance, MaxDistance);
			DefaultVerticalAngle = Mathf.Clamp(DefaultVerticalAngle, MinVerticalAngle, MaxVerticalAngle);
		}

		/// <summary>
		/// Initializes camera transform, distance, angle, and direction.
		/// </summary>
		void Awake()
		{
			Transform = this.transform;

			/* Captured here rather than by whoever later wants the camera back where it started.
			 * This is the last moment before anything in the game can have moved it, and the pose
			 * belongs to the object that has to restore it. */
			restPosition = Transform.position;
			restRotation = Transform.rotation;

			ResetMotionState();
		}

		/// <summary>
		/// Returns every field that decides where the camera sits and which way it looks to the
		/// values it holds before a character claims it.
		/// </summary>
		private void ResetMotionState()
		{
			currentDistance = DefaultDistance;
			TargetDistance = currentDistance;

			targetVerticalAngle = 0f;

			PlanarDirection = Vector3.forward;

			distanceIsObstructed = false;
			currentFollowPosition = Vector3.zero;
		}

		/// <summary>
		/// Sets the transform that the camera will orbit/follow.
		/// </summary>
		/// <param name="t">The target transform to follow.</param>
		public void SetFollowTransform(Transform t)
		{
			FollowTransform = t;
			if (FollowTransform != null)
			{
				PlanarDirection = FollowTransform.forward;
				currentFollowPosition = FollowTransform.position;
			}
		}

		/// <summary>
		/// Detaches the camera from the character it was following and puts it back where the scene
		/// authored it.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Called when the world is left. Writing the transform from outside does not accomplish
		/// this: every <see cref="UpdateWithInput"/> re-derives both the position and the rotation
		/// from this component's own state, so a pose written from a teardown path is undone by the
		/// next LateUpdate and the camera ends up framed from wherever the player logged out.
		/// Clearing <see cref="FollowTransform"/> is what makes the restore stick — every update
		/// returns early from then on.
		/// </para>
		/// <para>
		/// Not a one-way door. <see cref="SetFollowTransform"/> is how the next character adopts the
		/// camera, and it re-seeds the direction from that character, so the state reset here is not
		/// something a later session inherits.
		/// </para>
		/// </remarks>
		public void ReleaseFollowTarget()
		{
			FollowTransform = null;

			/* Cleared rather than kept: these were the departed character's own colliders, and the
			 * next character contributes its own on bind. Holding them would keep a list of
			 * destroyed objects alive for the remainder of the session. */
			IgnoredColliders.Clear();

			ResetMotionState();

			Transform.SetPositionAndRotation(restPosition, restRotation);
		}

		/// <summary>
		/// Updates camera position and rotation based on input and deltaTime.
		/// Handles rotation, zoom, following, obstruction, and framing.
		/// </summary>
		/// <param name="deltaTime">Frame time.</param>
		/// <param name="zoomInput">Zoom input value.</param>
		/// <param name="rotationInput">Rotation input vector.</param>
		public void UpdateWithInput(float deltaTime, float zoomInput, Vector3 rotationInput)
		{
			if (!FollowTransform)
			{
				return;
			}

			// Invert rotation input if necessary
			rotationInput = InvertRotationInput(rotationInput);

			// Process horizontal (planar) rotation
			Quaternion targetRotation = ProcessRotation(rotationInput, deltaTime);

			// Apply the rotation to the transform
			Transform.rotation = targetRotation;

			// Process zoom input
			HandleZoom(zoomInput, deltaTime);

			// Handle the smooth follow position
			UpdateFollowPosition(deltaTime);

			// Handle obstructions
			HandleObstructions(deltaTime);

			// Calculate the final camera position, including framing adjustments
			Vector3 targetPosition = CalculateTargetPosition(targetRotation);
			Transform.position = targetPosition;
		}

		/// <summary>
		/// Inverts the rotation input vector based on user preferences.
		/// </summary>
		/// <param name="input">Input rotation vector.</param>
		/// <returns>Inverted rotation vector.</returns>
		private Vector3 InvertRotationInput(Vector3 input)
		{
			if (InvertX) input.x *= -1f;
			if (InvertY) input.y *= -1f;
			return input;
		}

		/// <summary>
		/// Handles camera rotation based on user input (horizontal and vertical axes).
		/// </summary>
		/// <param name="rotationInput">Input rotation vector.</param>
		/// <param name="deltaTime">Frame time.</param>
		/// <returns>Target camera rotation quaternion.</returns>
		private Quaternion ProcessRotation(Vector3 rotationInput, float deltaTime)
		{
			// Planar rotation (horizontal axis)
			Quaternion planarRotation = Quaternion.Euler(FollowTransform.up * (rotationInput.x * RotationSpeed));
			PlanarDirection = planarRotation * PlanarDirection;
			PlanarDirection = Vector3.Cross(FollowTransform.up, Vector3.Cross(PlanarDirection, FollowTransform.up));
			planarRotation = Quaternion.LookRotation(PlanarDirection, FollowTransform.up);

			// Vertical rotation (clamped)
			targetVerticalAngle -= rotationInput.y * RotationSpeed;
			targetVerticalAngle = Mathf.Clamp(targetVerticalAngle, MinVerticalAngle, MaxVerticalAngle);
			Quaternion verticalRotation = Quaternion.Euler(targetVerticalAngle, 0, 0);

			// Combine planar and vertical rotations
			return Quaternion.Slerp(Transform.rotation, planarRotation * verticalRotation, 1f - Mathf.Exp(-RotationSharpness * deltaTime));
		}

		/// <summary>
		/// Handles zoom input, adjusting camera distance.
		/// </summary>
		/// <param name="zoomInput">Zoom input value.</param>
		/// <param name="deltaTime">Frame time.</param>
		private void HandleZoom(float zoomInput, float deltaTime)
		{
			if (distanceIsObstructed && Mathf.Abs(zoomInput) > 0f)
			{
				TargetDistance = currentDistance;
			}

			TargetDistance += zoomInput * DistanceMovementSpeed;
			TargetDistance = Mathf.Clamp(TargetDistance, MinDistance, MaxDistance);
		}

		/// <summary>
		/// Smoothly updates the follow position for the camera.
		/// </summary>
		/// <param name="deltaTime">Frame time.</param>
		private void UpdateFollowPosition(float deltaTime)
		{
			currentFollowPosition = Vector3.Lerp(currentFollowPosition, FollowTransform.position, 1f - Mathf.Exp(-FollowingSharpness * deltaTime));
		}

		/// <summary>
		/// Handles camera obstructions by sphere casting and adjusting distance.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>The cast runs in the physics scene the character lives in, not the default one.</b>
		/// World scenes are loaded with <c>LocalPhysicsMode.Physics3D</c> — the scene server asks for
		/// it on the connection load and FishNet hands the client the same options — so the terrain,
		/// the walls and every other collider of the world sit in a private
		/// <see cref="PhysicsScene"/>. The static <c>Physics.*</c> queries only ever see the default
		/// scene, so the sphere cast this used to run matched nothing there however close the
		/// geometry was: the camera kept its full distance and travelled straight through the world,
		/// which is what "the camera clips through the terrain and walls" was. Resolving the scene
		/// from <see cref="FollowTransform"/> is what points the cast at the colliders the player is
		/// actually standing among.
		/// </para>
		/// <para>
		/// <b>The direction is this frame's rotation, not the camera's own position.</b> Deriving it
		/// from where the camera currently sits lags a frame behind the rotation the player just
		/// gave — and on the first update after a character claims the camera it is not a camera
		/// direction at all, it is the pose the scene authored. <see cref="UpdateWithInput"/> has
		/// already applied the rotation by the time this runs, so the camera is about to be placed
		/// backwards along <c>Transform.forward</c> and that is the ray to test.
		/// </para>
		/// </remarks>
		/// <param name="deltaTime">Frame time.</param>
		private void HandleObstructions(float deltaTime)
		{
			RaycastHit closestHit = new RaycastHit();
			closestHit.distance = Mathf.Infinity;

			Vector3 playerToCameraDir = -Transform.forward;

			PhysicsScene physicsScene = ResolvePhysicsScene();

			/* A non-allocating query that comes back full has already discarded results in broadphase
			 * order, and the one it discarded may be the closest — which is the only entry this method
			 * reads. Grow until it stops coming back full. */
			int obstructionCount;
			while (true)
			{
				obstructionCount = physicsScene.SphereCast(currentFollowPosition, ObstructionCheckRadius, playerToCameraDir, obstructions, TargetDistance, ObstructionLayers, QueryTriggerInteraction.Ignore);
				if (!TargetOrdering.TryGrowQueryBuffer(ref obstructions, obstructionCount))
				{
					break;
				}
			}

			// Check for the closest obstruction
			for (int i = 0; i < obstructionCount; i++)
			{
				if (!IsColliderIgnored(obstructions[i].collider) && obstructions[i].distance < closestHit.distance && obstructions[i].distance > 0)
				{
					closestHit = obstructions[i];
				}
			}

			// Handle obstruction results
			if (closestHit.distance < Mathf.Infinity)
			{
				distanceIsObstructed = true;
				currentDistance = Mathf.Lerp(currentDistance, closestHit.distance, 1 - Mathf.Exp(-ObstructionSharpness * deltaTime));
			}
			else
			{
				distanceIsObstructed = false;
				currentDistance = Mathf.Lerp(currentDistance, TargetDistance, 1 - Mathf.Exp(-DistanceMovementSharpness * deltaTime));
			}
		}

		/// <summary>
		/// Checks if the given collider should be ignored for obstruction checks.
		/// </summary>
		/// <param name="collider">Collider to check.</param>
		/// <returns>True if ignored, false otherwise.</returns>
		private bool IsColliderIgnored(Collider collider)
		{
			return IgnoredColliders.Contains(collider);
		}

		/// <summary>
		/// The physics scene the obstruction cast has to run in: the one the character being followed
		/// lives in.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The scene is taken from <see cref="FollowTransform"/> rather than from this component's own
		/// <c>gameObject</c>. The camera is persistent — it is authored in ClientPreboot and outlives
		/// every world — while the character is spawned into a world scene, and when those are two
		/// different physics scenes the camera's own answers with the default one. The default scene
		/// holds none of the world's colliders, so a cast made there matches nothing at all.
		/// </para>
		/// <para>
		/// The <c>IsValid</c> check is not decoration: <c>Scene.GetPhysicsScene</c> throws on an
		/// invalid scene instead of answering, and a follow target that is mid-destruction has one.
		/// Everything else — including a scene that owns no physics of its own, which answers with the
		/// default scene — resolves without it.
		/// </para>
		/// </remarks>
		/// <returns>The physics scene to query for obstructions.</returns>
		private PhysicsScene ResolvePhysicsScene()
		{
			if (FollowTransform != null)
			{
				Scene scene = FollowTransform.gameObject.scene;
				if (scene.IsValid())
				{
					return scene.GetPhysicsScene();
				}
			}

			return Physics.defaultPhysicsScene;
		}

		/// <summary>
		/// Calculates the final camera position, including framing adjustments.
		/// </summary>
		/// <param name="targetRotation">Target camera rotation.</param>
		/// <returns>Final camera position vector.</returns>
		private Vector3 CalculateTargetPosition(Quaternion targetRotation)
		{
			Vector3 targetPosition = currentFollowPosition - (targetRotation * Vector3.forward * currentDistance);
			if (TargetDistance > 0.0f)
			{
				targetPosition += Transform.right * FollowPointFraming.x;
				targetPosition += Transform.up * FollowPointFraming.y;
			}
			return targetPosition;
		}
	}
}