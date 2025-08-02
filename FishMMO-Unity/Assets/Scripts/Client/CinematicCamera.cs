using UnityEngine;
using UnityEngine.Splines;
using System;
using System.Collections;
using FishMMO.Logging;

namespace FishMMO.Client
{
	/// <summary>
	/// CinematicCamera moves the camera along a spline path for cinematic effects.
	/// </summary>
	public class CinematicCamera : MonoBehaviour
	{
		/// <summary>
		/// Container holding the spline path for the cinematic camera movement.
		/// </summary>
		public SplineContainer SplineContainer;
		/// <summary>
		/// Speed at which the camera moves along the spline, in units per second.
		/// </summary>
		[Tooltip("Speed at which to move along the spline in units per second.")]
		public float SpeedInUnitsPerSecond = 25.0f;
		/// <summary>
		/// Normalized point (0-1) on the spline where rotation smoothing stops being applied.
		/// </summary>
		[Tooltip("Point on the spline where rotation smoothing will stop being applied. (0-1)")]
		public float RotationSmoothStart = 0.9f;
		/// <summary>
		/// Strength of the smoothing applied to the camera's rotation.
		/// </summary>
		[Tooltip("The strength of the smoothing applied to the rotation.")]
		public float RotationSmoothStrength = 0.75f;

		/// <summary>
		/// Optional target for the camera to look at. If set, camera rotates towards this target.
		/// </summary>
		[Tooltip("An optional target to look at. The camera will rotate towards this target.")]
		public Transform LookAtTarget;

		/// <summary>
		/// Cached reference to the camera's transform.
		/// </summary>
		private Transform cameraTransform;
		/// <summary>
		/// Normalized progress along the spline (0=start, 1=end).
		/// </summary>
		private float t = 0.0f;
		/// <summary>
		/// Total length of the spline path.
		/// </summary>
		private float splineLength = 0.0f;

		/// <summary>
		/// Coroutine to move the camera along the spline to the next waypoint.
		/// </summary>
		/// <param name="onComplete">Callback invoked when movement is finished.</param>
		/// <param name="allowInputSkip">If true, allows user input to skip to the end.</param>
		/// <returns>Coroutine enumerator.</returns>
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
				// Handle input skip: if allowed and any key is pressed, jump to end
				if (allowInputSkip && Input.anyKeyDown)
				{
					t = 1f;  // Skip to the end of the spline
				}

				// Update t based on speed and distance
				t = Mathf.Clamp01(t + (SpeedInUnitsPerSecond * Time.deltaTime) / splineLength);

				// Get the target position from the spline
				Vector3 targetPosition = spline.EvaluatePosition(t);

				// Smoothly interpolate the camera's position using a gradual approach
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
		/// Resets the camera's progress along the spline to the start.
		/// </summary>
		public void Reset()
		{
			t = 0f;
		}
	}
}