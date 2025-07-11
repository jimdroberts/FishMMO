using UnityEngine;
using UnityEngine.Splines;
using System;
using System.Collections;
using FishMMO.Shared;
using FishMMO.Logging;

namespace FishMMO.Client
{
	public class CinematicCamera : MonoBehaviour
	{
		public SplineContainer SplineContainer;
		[Tooltip("Speed at which to move along the spline in units per second.")]
		public float SpeedInUnitsPerSecond = 25.0f;
		[Tooltip("Point on the spline where rotation smoothing will stop being applied. (0-1)")]
		public float RotationSmoothStart = 0.9f;
		[Tooltip("The strength of the smoothing applied to the rotation.")]
		public float RotationSmoothStrength = 0.75f;

		[Tooltip("An optional target to look at. The camera will rotate towards this target.")]
		public Transform LookAtTarget;

		private Transform cameraTransform;
		private float t = 0.0f;
		private float splineLength = 0.0f;

		/// <summary>
		/// Called when the camera starts moving
		/// </summary>
		public IEnumerator MoveToNextWaypoint(Action onComplete, bool allowInputSkip = false)
		{
			// Ensure that there's a valid camera and spline container
			if (Camera.main == null || SplineContainer == null || SplineContainer.Splines.Count == 0)
			{
				Log.Error("CinematicCamera", "Camera or SplineContainer is missing.");
				yield break;
			}

			cameraTransform = Camera.main.transform;
			Spline spline = SplineContainer.Splines[0];
			splineLength = spline.GetLength();

			// Loop through the spline movement from t = 0 to t = 1 (start to finish)
			while (t < 1f)
			{
				// Handle input skip
				if (allowInputSkip && Input.anyKeyDown)
				{
					t = 1f;  // Skip to the end of the spline
				}

				// Update t based on speed and distance
				t = Mathf.Clamp01(t + (SpeedInUnitsPerSecond * Time.deltaTime) / splineLength);

				// Get the target position from the spline
				Vector3 targetPosition = spline.EvaluatePosition(t);

				// Smoothly interpolate the camera's position using a slightly more gradual approach
				cameraTransform.position = Vector3.Lerp(cameraTransform.position, targetPosition, Mathf.Pow(t, 0.5f)); // More gradual interpolation

				// If there's a LookAtTarget, make sure the camera faces it instantly
				if (LookAtTarget != null)
				{
					Vector3 targetLookDirection = LookAtTarget.position - cameraTransform.position;
					cameraTransform.rotation = Quaternion.LookRotation(targetLookDirection);
				}
				else
				{
					// Apply rotation smoothing only if LookAtTarget is not set
					float rotationSmoothFactor = 1f;

					// Apply rotation smoothing
					if (t < RotationSmoothStart)
					{
						// Use smooth easing to gradually accelerate and decelerate the rotation
						rotationSmoothFactor = Mathf.Pow(t / RotationSmoothStrength, 3.0f); // Exponential smoothing
					}
					else
					{
						// Use linear interpolation for the final phase
						rotationSmoothFactor = Mathf.Lerp(1f, t, 0.5f); // Smooth transition to the final target rotation
					}

					// Calculate the look direction to the next knot
					float nextT = Mathf.Min(t + 0.1f, 1f); // Get a slightly ahead position for smoother rotation
					Vector3 nextKnotPosition = spline.EvaluatePosition(nextT);
					Vector3 lookDirection = nextKnotPosition - cameraTransform.position;

					if (lookDirection.sqrMagnitude > Mathf.Epsilon)
					{
						Quaternion targetRotation = Quaternion.LookRotation(lookDirection);
						cameraTransform.rotation = Quaternion.Slerp(cameraTransform.rotation, targetRotation, rotationSmoothFactor);
					}
					else
					{
						// When reaching the last knot, smoothly rotate to the final tangent
						Vector3 finalTangent = spline.EvaluateTangent(1f);
						if (finalTangent.sqrMagnitude > Mathf.Epsilon)
						{
							Quaternion finalRotation = Quaternion.LookRotation(finalTangent);
							cameraTransform.rotation = Quaternion.Slerp(cameraTransform.rotation, finalRotation, rotationSmoothFactor);
						}
					}
				}

				yield return null;  // Wait until the next frame
			}

			// Call the onComplete callback after finishing the movement
			onComplete?.Invoke();
			Reset();
		}

		/// <summary>
		/// Reset the time to 0
		/// </summary>
		public void Reset()
		{
			t = 0f;
		}
	}
}