using UnityEngine;

namespace FishMMO.Client
{
	public sealed class Billboard : MonoBehaviour
	{
#if !UNITY_SERVER
		/// <summary>
		/// Reference to the camera used for billboarding. The object's rotation matches this camera.
		/// </summary>
		private Camera Camera;

		/// <summary>
		/// If true, only the Y-axis (vertical) rotation is matched, creating a horizontal billboard effect.
		/// </summary>
		public bool PivotYAxis = false;

		/// <summary>
		/// Cached transform of the camera. Used for efficient access to camera orientation.
		/// </summary>
		public Transform Transform { get; private set; }

		/// <summary>
		/// Called when the script instance is being loaded. Sets the camera reference.
		/// </summary>
		void Awake()
		{
			SetCamera(Camera.main);
		}

		/// <summary>
		/// Called after all Update functions have been called. Updates the object's rotation to match the camera.
		/// </summary>
		void LateUpdate()
		{
			if (Camera != null)
			{
				// Make the object share the same rotation as the camera
				transform.rotation = Camera.transform.rotation;
				if (PivotYAxis)
				{
					// Only match the Y-axis rotation for horizontal billboarding
					transform.rotation = Quaternion.Euler(0.0f, transform.rotation.eulerAngles.y, 0.0f);
				}
			}
			else
			{
				// Try to get the new main camera if the reference is lost
				Camera = Camera.main;
			}
		}

		/// <summary>
		/// Sets the camera to use for billboarding and caches its transform.
		/// </summary>
		/// <param name="target">Camera to use for billboarding.</param>
		public void SetCamera(Camera target)
		{
			Camera = target;
			Transform = Camera == null ? null : Camera.transform;
		}
#endif
	}
}