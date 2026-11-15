using System;
using System.Collections.Generic;
using KinematicCharacterController;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishMMO.Shared
{
	/// <summary>
	/// Where the player's camera sits relative to their character.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Stored as a player setting (ClientCameraSettings), so the values are ours and never reordered: a saved file keeps
	/// its meaning across releases. The order the Options dropdown shows them in is separate.
	/// </para>
	/// <para>
	/// The character faces the camera's heading in every style (OrientationMethod.TowardsCamera); the styles differ only
	/// in where the view is taken from. Where the shots go is the same rule in all three: from the character's eye to the
	/// point under the crosshair (<see cref="AimConvergence"/>), which is what lets the view sit off the character's
	/// line at all.
	/// </para>
	/// </remarks>
	public enum KCCCameraStyle : byte
	{
		/// <summary>Centred behind the character, orbiting and zooming freely. The original camera.</summary>
		ThirdPerson = 0,

		/// <summary>Close behind and to the right of the character, looking past their shoulder.</summary>
		OverShoulder = 1,

		/// <summary>At the character's eyes.</summary>
		FirstPerson = 2,
	}

	/// <summary>
	/// The player's camera: follows, orbits, zooms and keeps out of walls, in one of three styles
	/// (<see cref="KCCCameraStyle"/>): third person, over the shoulder, or first person.
	/// </summary>
	/// <remarks>
	/// The camera decides only what the player sees. Where shots go is worked out from it each tick in
	/// KCCPlayer.PopulateInput (<see cref="AimConvergence"/>), which is what keeps them on the crosshair when the view is
	/// off the character's line.
	/// </remarks>
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

		[Header("Over Shoulder")]
		/// <summary>
		/// Where the view pivots in <see cref="KCCCameraStyle.OverShoulder"/>, from the follow point: metres to the
		/// camera's right (x) and up (y).
		/// </summary>
		/// <remarks>
		/// The pivot is moved out sideways before the camera is pulled back from it, and that sideways move is itself
		/// cast against the world (<see cref="ResolvePivot"/>): a shoulder against a wall brings the view in towards the
		/// character instead of putting it through the wall.
		/// </remarks>
		public Vector2 ShoulderOffset = new Vector2(0.55f, 0.2f);
		/// <summary>Distance behind the shoulder when the style is first taken.</summary>
		public float ShoulderDefaultDistance = 2.5f;
		/// <summary>Closest the wheel brings the view to the shoulder.</summary>
		public float ShoulderMinDistance = 1.25f;
		/// <summary>Farthest the wheel takes the view from the shoulder.</summary>
		public float ShoulderMaxDistance = 4f;
		/// <summary>
		/// True to look past the LEFT shoulder: <see cref="ShoulderOffset"/>'s sideways part is mirrored. Changing it
		/// slides the view across rather than jumping (<see cref="ShoulderSwapSharpness"/>).
		/// </summary>
		public bool ShoulderLeft = false;
		/// <summary>How quickly the view slides to the other shoulder (higher = snappier).</summary>
		public float ShoulderSwapSharpness = 12f;

		/// <summary>
		/// The farthest the shoulder pivot may sit from the follow point, metres, whatever the player configures.
		/// </summary>
		/// <remarks>
		/// A view bound, not an aim one: the shot no longer depends on where the shoulder is
		/// (<see cref="ShoulderDisplacement"/>). Kept so a hand-edited settings file cannot put the view a body's width
		/// away from the character, where the follow point's own obstruction cast would no longer protect it.
		/// </remarks>
		public const float MaxShoulderReach = 1f;

		/// <summary>The sideways sign the view is easing toward: +1 right shoulder, -1 left.</summary>
		private float shoulderSide = 1f;

		[Header("First Person")]
		/// <summary>
		/// Eye height above the character's feet when no motor has been given (<see cref="EyeMotor"/>). With one, the
		/// eye is the one shots are aimed from (<see cref="CharacterAimOrigin"/>), so the view and the aim share it.
		/// </summary>
		public float FallbackEyeHeight = 1.6f;

		/// <summary>
		/// The style in effect. Set through <see cref="SetStyle"/>, which keeps each style's own zoom.
		/// </summary>
		public KCCCameraStyle Style { get; private set; } = KCCCameraStyle.ThirdPerson;

		/// <summary>
		/// The motor of the character being followed, for its eye height in first person. Optional: without it the
		/// eye is <see cref="FallbackEyeHeight"/> above the feet.
		/// </summary>
		public KinematicCharacterMotor EyeMotor { get; set; }

		/// <summary>The third-person zoom, kept while another style is in use so returning to it restores the framing.</summary>
		private float thirdPersonDistance = -1f;
		/// <summary>The over-the-shoulder zoom, kept the same way.</summary>
		private float shoulderDistance = -1f;

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
		/// How far the view's pivot was moved off the follow point by the shoulder this update: zero in third and first
		/// person.
		/// </summary>
		/// <remarks>
		/// The crosshair's ray passes through the pivot, so over the shoulder it runs this far beside the character. The
		/// owner's aim takes it back out of the launch line (KCCPlayer.ResolveOwnerAim), so a projectile leaves the
		/// character exactly as it does in third person, and only the view sits off to the side.
		/// </remarks>
		public Vector3 ShoulderDisplacement { get; private set; }

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
			/* The style survives (it is the player's setting, applied once at boot), but the zoom each style
			 * remembered belonged to the session that just ended. */
			thirdPersonDistance = -1f;
			shoulderDistance = -1f;
			currentDistance = StyleDefaultDistance(Style);
			TargetDistance = currentDistance;

			targetVerticalAngle = 0f;

			PlanarDirection = Vector3.forward;

			distanceIsObstructed = false;
			currentFollowPosition = Vector3.zero;
			shoulderSide = ShoulderLeft ? -1f : 1f;
			ShoulderDisplacement = Vector3.zero;
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
			EyeMotor = null;

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

			PhysicsScene physicsScene = ResolvePhysicsScene();

			// The view slides to the other shoulder rather than jumping a body's width.
			float side = ShoulderLeft ? -1f : 1f;
			shoulderSide = Style == KCCCameraStyle.OverShoulder
				? Mathf.Lerp(shoulderSide, side, 1f - Mathf.Exp(-ShoulderSwapSharpness * deltaTime))
				: side;

			// Where the view turns about this frame: the follow point, the shoulder, or the eye.
			Vector3 pivot = ResolvePivot(physicsScene);
			ShoulderDisplacement = Style == KCCCameraStyle.OverShoulder ? pivot - currentFollowPosition : Vector3.zero;

			if (Style == KCCCameraStyle.FirstPerson)
			{
				/* At the eye there is no boom to obstruct. The distance is dropped at once rather than eased, so the view
				 * never passes through the character's own head on the way in; leaving first person eases back out from
				 * zero as any zoom does. */
				currentDistance = 0f;
				distanceIsObstructed = false;
				Transform.position = pivot;
				return;
			}

			// Handle obstructions
			HandleObstructions(physicsScene, pivot, deltaTime);

			// Calculate the final camera position, including framing adjustments
			Vector3 targetPosition = CalculateTargetPosition(pivot, targetRotation);
			Transform.position = targetPosition;
		}

		/// <summary>
		/// Changes where the view is taken from, keeping the zoom each style was left at.
		/// </summary>
		/// <remarks>
		/// Safe before a character has claimed the camera: the style is held and used from the first update. The view's
		/// heading and pitch are untouched, so switching never turns the player.
		/// </remarks>
		/// <param name="style">The style to use. An undefined value is taken as <see cref="KCCCameraStyle.ThirdPerson"/>.</param>
		public void SetStyle(KCCCameraStyle style)
		{
			if (!Enum.IsDefined(typeof(KCCCameraStyle), style))
			{
				style = KCCCameraStyle.ThirdPerson;
			}
			if (style == Style)
			{
				return;
			}

			switch (Style)
			{
				case KCCCameraStyle.ThirdPerson:
					thirdPersonDistance = TargetDistance;
					break;
				case KCCCameraStyle.OverShoulder:
					shoulderDistance = TargetDistance;
					break;
			}

			Style = style;

			float remembered = style == KCCCameraStyle.ThirdPerson ? thirdPersonDistance
				: style == KCCCameraStyle.OverShoulder ? shoulderDistance
				: 0f;
			TargetDistance = Mathf.Clamp(remembered >= 0f ? remembered : StyleDefaultDistance(style), ActiveMinDistance, ActiveMaxDistance);
		}

		/// <summary>The distance a style starts at.</summary>
		private float StyleDefaultDistance(KCCCameraStyle style)
		{
			switch (style)
			{
				case KCCCameraStyle.OverShoulder:
					return ShoulderDefaultDistance;
				case KCCCameraStyle.FirstPerson:
					return 0f;
				default:
					return DefaultDistance;
			}
		}

		/// <summary>The closest the wheel may bring the view, in the current style.</summary>
		public float ActiveMinDistance => Style == KCCCameraStyle.OverShoulder ? ShoulderMinDistance
			: Style == KCCCameraStyle.FirstPerson ? 0f
			: MinDistance;

		/// <summary>The farthest the wheel may take the view, in the current style.</summary>
		public float ActiveMaxDistance => Style == KCCCameraStyle.OverShoulder ? Mathf.Max(ShoulderMinDistance, ShoulderMaxDistance)
			: Style == KCCCameraStyle.FirstPerson ? 0f
			: MaxDistance;

		/// <summary>
		/// The point the view turns about this frame, and is pulled back from.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>Third person:</b> the follow point.
		/// </para>
		/// <para>
		/// <b>Over the shoulder:</b> the follow point moved by <see cref="ShoulderOffset"/>, to the view's right and up
		/// the character. The move is cast first: pulling the camera back from a shoulder that is already inside a wall
		/// would start the boom's own cast inside it, and a cast that starts inside a collider does not report it.
		/// </para>
		/// <para>
		/// <b>First person:</b> the eye, measured up from the character's feet as drawn (the follow point's parent, the
		/// smoothed mesh root) by the same height shots are aimed from, so the view moves as smoothly as the body and
		/// sits where the aim begins.
		/// </para>
		/// </remarks>
		private Vector3 ResolvePivot(PhysicsScene physicsScene)
		{
			switch (Style)
			{
				case KCCCameraStyle.FirstPerson:
					{
						Transform feet = FollowTransform.parent;
						float eyeHeight = EyeMotor != null ? CharacterAimOrigin.EyeHeight(EyeMotor) : FallbackEyeHeight;
						return feet != null
							? feet.position + FollowTransform.up * eyeHeight
							: currentFollowPosition;
					}
				case KCCCameraStyle.OverShoulder:
					{
						Vector3 offset = Vector3.ClampMagnitude(
							Transform.right * (ShoulderOffset.x * shoulderSide) + FollowTransform.up * ShoulderOffset.y,
							MaxShoulderReach);
						float length = offset.magnitude;
						if (length < 1e-4f)
						{
							return currentFollowPosition;
						}
						Vector3 direction = offset / length;
						float clear = NearestObstruction(physicsScene, currentFollowPosition, direction, length);
						return currentFollowPosition + direction * Mathf.Min(length, clear);
					}
				default:
					return currentFollowPosition;
			}
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
			TargetDistance = Mathf.Clamp(TargetDistance, ActiveMinDistance, ActiveMaxDistance);
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
		/// <param name="physicsScene">The physics scene the character lives in (<see cref="ResolvePhysicsScene"/>).</param>
		/// <param name="pivot">Where the boom starts: the follow point, or the shoulder (<see cref="ResolvePivot"/>).</param>
		/// <param name="deltaTime">Frame time.</param>
		private void HandleObstructions(PhysicsScene physicsScene, Vector3 pivot, float deltaTime)
		{
			float closest = NearestObstruction(physicsScene, pivot, -Transform.forward, TargetDistance);

			// Handle obstruction results
			if (closest < Mathf.Infinity)
			{
				distanceIsObstructed = true;
				currentDistance = Mathf.Lerp(currentDistance, closest, 1 - Mathf.Exp(-ObstructionSharpness * deltaTime));
			}
			else
			{
				distanceIsObstructed = false;
				currentDistance = Mathf.Lerp(currentDistance, TargetDistance, 1 - Mathf.Exp(-DistanceMovementSharpness * deltaTime));
			}
		}

		/// <summary>
		/// How far a sphere of <see cref="ObstructionCheckRadius"/> travels from <paramref name="origin"/> before it
		/// touches something that is not the character's own; infinity when nothing is in reach.
		/// </summary>
		private float NearestObstruction(PhysicsScene physicsScene, Vector3 origin, Vector3 direction, float maxDistance)
		{
			if (maxDistance <= 0f)
			{
				return Mathf.Infinity;
			}

			/* A non-allocating query that comes back full has already discarded results in broadphase
			 * order, and the one it discarded may be the closest — which is the only entry this method
			 * reads. Grow until it stops coming back full. */
			int obstructionCount;
			while (true)
			{
				obstructionCount = physicsScene.SphereCast(origin, ObstructionCheckRadius, direction, obstructions, maxDistance, ObstructionLayers, QueryTriggerInteraction.Ignore);
				if (!TargetOrdering.TryGrowQueryBuffer(ref obstructions, obstructionCount))
				{
					break;
				}
			}

			float closest = Mathf.Infinity;
			for (int i = 0; i < obstructionCount; i++)
			{
				if (!IsColliderIgnored(obstructions[i].collider) && obstructions[i].distance < closest && obstructions[i].distance > 0)
				{
					closest = obstructions[i].distance;
				}
			}
			return closest;
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
		/// <remarks>
		/// <see cref="FollowPointFraming"/> is third person's own: over the shoulder the offset is
		/// <see cref="ShoulderOffset"/>, applied to the pivot, where it is cast against the world.
		/// </remarks>
		/// <param name="pivot">The point the boom starts from (<see cref="ResolvePivot"/>).</param>
		/// <param name="targetRotation">Target camera rotation.</param>
		/// <returns>Final camera position vector.</returns>
		private Vector3 CalculateTargetPosition(Vector3 pivot, Quaternion targetRotation)
		{
			Vector3 targetPosition = pivot - (targetRotation * Vector3.forward * currentDistance);
			if (Style == KCCCameraStyle.ThirdPerson && TargetDistance > 0.0f)
			{
				targetPosition += Transform.right * FollowPointFraming.x;
				targetPosition += Transform.up * FollowPointFraming.y;
			}
			return targetPosition;
		}
	}
}