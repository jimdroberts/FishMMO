using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Rotates the GameObject to face the main camera (nameplates, world labels).
	/// Lives in <c>FishMMO.Shared</c> so dedicated server builds resolve the prefab
	/// script reference (no missing-script holes). On <c>UNITY_SERVER</c> the
	/// component is a no-op — servers have no camera / billboard rendering.
	/// </summary>
	public sealed class Billboard : MonoBehaviour
	{
		/// <summary>
		/// If true, only the Y-axis (vertical) rotation is matched, creating a horizontal billboard effect.
		/// </summary>
		public bool PivotYAxis = false;

#if !UNITY_SERVER
		/// <summary>
		/// Reference to the camera used for billboarding. The object's rotation matches this camera.
		/// </summary>
		private Camera targetCamera;

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
			if (this.targetCamera != null)
			{
				// Make the object share the same rotation as the camera
				transform.rotation = this.targetCamera.transform.rotation;
				if (PivotYAxis)
				{
					// Only match the Y-axis rotation for horizontal billboarding
					transform.rotation = Quaternion.Euler(0.0f, transform.rotation.eulerAngles.y, 0.0f);
				}
			}
			else
			{
				// Try to get the new main camera if the reference is lost
				this.targetCamera = Camera.main;
			}
		}

		/// <summary>
		/// Sets the camera to use for billboarding and caches its transform.
		/// </summary>
		/// <param name="target">Camera to use for billboarding.</param>
		public void SetCamera(Camera target)
		{
			this.targetCamera = target;
			Transform = this.targetCamera == null ? null : this.targetCamera.transform;
		}
#else
		void Awake()
		{
			// Keep component present for prefab stability; no camera work on dedicated server.
			enabled = false;
		}
#endif
	}
}
