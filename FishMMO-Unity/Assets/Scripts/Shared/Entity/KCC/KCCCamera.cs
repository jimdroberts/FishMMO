using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	public class KCCCamera : MonoBehaviour
	{
		[Header("Framing")]
		public Vector2 FollowPointFraming = new Vector2(0f, 0f);
		public float FollowingSharpness = 10000f;

		[Header("Distance")]
		public float DefaultDistance = 6f;
		public float MinDistance = 0f;
		public float MaxDistance = 10f;
		public float DistanceMovementSpeed = 5f;
		public float DistanceMovementSharpness = 10f;

		[Header("Rotation")]
		public bool InvertX = false;
		public bool InvertY = false;
		[Range(-89f, 89f)]
		public float DefaultVerticalAngle = 20f;
		[Range(-89f, 89f)]
		public float MinVerticalAngle = -89f;
		[Range(-89f, 89f)]
		public float MaxVerticalAngle = 89f;
		public float RotationSpeed = 1f;
		public float RotationSharpness = 10000f;
		public bool RotateWithPhysicsMover = false;

		[Header("Obstruction")]
		public float ObstructionCheckRadius = 0.2f;
		public LayerMask ObstructionLayers = -1;
		public float ObstructionSharpness = 10000f;
		public List<Collider> IgnoredColliders = new List<Collider>();

		public Transform Transform { get; private set; }
		public Transform FollowTransform { get; private set; }

		public Vector3 PlanarDirection { get; set; }
		public float TargetDistance { get; set; }

		private bool _distanceIsObstructed;
		private float _currentDistance;
		private float _targetVerticalAngle;
		private int _obstructionCount;
		private RaycastHit[] _obstructions = new RaycastHit[MaxObstructions];
		private Vector3 _currentFollowPosition;

		private const int MaxObstructions = 32;

		void OnValidate()
		{
			DefaultDistance = Mathf.Clamp(DefaultDistance, MinDistance, MaxDistance);
			DefaultVerticalAngle = Mathf.Clamp(DefaultVerticalAngle, MinVerticalAngle, MaxVerticalAngle);
		}

		void Awake()
		{
			Transform = this.transform;

			_currentDistance = DefaultDistance;
			TargetDistance = _currentDistance;

			_targetVerticalAngle = 0f;

			PlanarDirection = Vector3.forward;
		}

		// Set the transform that the camera will orbit around
		public void SetFollowTransform(Transform t)
		{
			FollowTransform = t;
			if (FollowTransform != null)
			{
				PlanarDirection = FollowTransform.forward;
				_currentFollowPosition = FollowTransform.position;
			}
		}

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

		// Inverts the rotation input based on user preferences
		private Vector3 InvertRotationInput(Vector3 input)
		{
			if (InvertX) input.x *= -1f;
			if (InvertY) input.y *= -1f;
			return input;
		}

		// Handles rotation based on user input (horizontal and vertical)
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

		// Handles zoom input (camera distance)
		private void HandleZoom(float zoomInput, float deltaTime)
		{
			if (_distanceIsObstructed && Mathf.Abs(zoomInput) > 0f)
			{
				TargetDistance = _currentDistance;
			}

			TargetDistance += zoomInput * DistanceMovementSpeed;
			TargetDistance = Mathf.Clamp(TargetDistance, MinDistance, MaxDistance);
		}

		// Smoothly updates the follow position
		private void UpdateFollowPosition(float deltaTime)
		{
			_currentFollowPosition = Vector3.Lerp(_currentFollowPosition, FollowTransform.position, 1f - Mathf.Exp(-FollowingSharpness * deltaTime));
		}

		// Handles potential obstructions that may block the camera view
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

		// Check if the collider is ignored
		private bool IsColliderIgnored(Collider collider)
		{
			return IgnoredColliders.Contains(collider);
		}

		// Calculates the final camera position with framing adjustments
		private Vector3 CalculateTargetPosition(Quaternion targetRotation)
		{
			Vector3 targetPosition = _currentFollowPosition - (targetRotation * Vector3.forward * _currentDistance);
			targetPosition += Transform.right * FollowPointFraming.x;
			targetPosition += Transform.up * FollowPointFraming.y;
			return targetPosition;
		}
	}
}