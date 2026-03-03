using System;
using System.Collections.Generic;
using UnityEngine;

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
		/// Speed at which camera distance changes (zoom).
		/// </summary>
		public float DistanceMovementSpeed = 5f;
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
		private bool _distanceIsObstructed;
		/// <summary>
		/// The current camera distance.
		/// </summary>
		private float _currentDistance;
		/// <summary>
		/// The current target vertical angle.
		/// </summary>
		private float _targetVerticalAngle;
		/// <summary>
		/// Number of current obstructions detected.
		/// </summary>
		private int _obstructionCount;
		/// <summary>
		/// Array of detected obstructions from sphere cast.
		/// </summary>
		private RaycastHit[] _obstructions = new RaycastHit[MaxObstructions];
		/// <summary>
		/// The current position the camera is following.
		/// </summary>
		private Vector3 _currentFollowPosition;

		/// <summary>
		/// Maximum number of obstructions to check for.
		/// </summary>
		private const int MaxObstructions = 32;

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

			_currentDistance = DefaultDistance;
			TargetDistance = _currentDistance;

			_targetVerticalAngle = 0f;

			PlanarDirection = Vector3.forward;
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
				_currentFollowPosition = FollowTransform.position;
			}
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
			_targetVerticalAngle -= rotationInput.y * RotationSpeed;
			_targetVerticalAngle = Mathf.Clamp(_targetVerticalAngle, MinVerticalAngle, MaxVerticalAngle);
			Quaternion verticalRotation = Quaternion.Euler(_targetVerticalAngle, 0, 0);

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
			if (_distanceIsObstructed && Mathf.Abs(zoomInput) > 0f)
			{
				TargetDistance = _currentDistance;
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
			_currentFollowPosition = Vector3.Lerp(_currentFollowPosition, FollowTransform.position, 1f - Mathf.Exp(-FollowingSharpness * deltaTime));
		}

		/// <summary>
		/// Handles camera obstructions by sphere casting and adjusting distance.
		/// </summary>
		/// <param name="deltaTime">Frame time.</param>
		private void HandleObstructions(float deltaTime)
		{
			RaycastHit closestHit = new RaycastHit();
			closestHit.distance = Mathf.Infinity;

			Vector3 playerToCameraDir = transform.position - _currentFollowPosition;
			playerToCameraDir.Normalize();

			_obstructionCount = Physics.SphereCastNonAlloc(_currentFollowPosition, ObstructionCheckRadius, playerToCameraDir, _obstructions, TargetDistance, ObstructionLayers, QueryTriggerInteraction.Ignore);

			// Check for the closest obstruction
			for (int i = 0; i < _obstructionCount; i++)
			{
				if (!IsColliderIgnored(_obstructions[i].collider) && _obstructions[i].distance < closestHit.distance && _obstructions[i].distance > 0)
				{
					closestHit = _obstructions[i];
				}
			}

			// Handle obstruction results
			if (closestHit.distance < Mathf.Infinity)
			{
				_distanceIsObstructed = true;
				_currentDistance = Mathf.Lerp(_currentDistance, closestHit.distance, 1 - Mathf.Exp(-ObstructionSharpness * deltaTime));
			}
			else
			{
				_distanceIsObstructed = false;
				_currentDistance = Mathf.Lerp(_currentDistance, TargetDistance, 1 - Mathf.Exp(-DistanceMovementSharpness * deltaTime));
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
		/// Calculates the final camera position, including framing adjustments.
		/// </summary>
		/// <param name="targetRotation">Target camera rotation.</param>
		/// <returns>Final camera position vector.</returns>
		private Vector3 CalculateTargetPosition(Quaternion targetRotation)
		{
			Vector3 targetPosition = _currentFollowPosition - (targetRotation * Vector3.forward * _currentDistance);
			if (TargetDistance > 0.0f)
			{
				targetPosition += Transform.right * FollowPointFraming.x;
				targetPosition += Transform.up * FollowPointFraming.y;
			}
			return targetPosition;
		}
	}
}