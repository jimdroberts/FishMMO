using UnityEngine;

namespace FishMMO.Client
{
	public class UIMinimap : UICharacterControl
	{
		public Camera MinimapCamera;

		private bool originalFogState;

		public override void OnStarting()
		{
			if (MinimapCamera == null)
			{
				Debug.LogWarning("MinimapCamera is not assigned to UIMinimap. Minimap will not function correctly.");
				return;
			}

			// Set the minimap camera's rotation to look straight down
			MinimapCamera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

			// Ensure the camera is orthographic for a map-like view
			MinimapCamera.orthographic = true;
			MinimapCamera.orthographicSize = 25f;

			// Set clear flags to solid color to prevent skybox or previous frames from showing
			MinimapCamera.clearFlags = CameraClearFlags.SolidColor;
			MinimapCamera.backgroundColor = Color.black;
		}

		public override void OnPostSetCharacter()
		{
			base.OnPostSetCharacter();
#if !UNITY_SERVER
			if (Character == null || Character.MeshRoot == null)
			{
				Debug.LogWarning("Character or Character.MeshRoot is null on OnPostSetCharacter.");
				return;
			}

			Vector3 newPosition = Character.MeshRoot.position;
			newPosition.y = 1000.0f;

			MinimapCamera.transform.position = newPosition;
#endif
		}

		public override void OnPreUnsetCharacter()
		{
			base.OnPreUnsetCharacter();
		}

		void LateUpdate()
		{
#if !UNITY_SERVER
			if (Character == null || Character.MeshRoot == null || MinimapCamera == null)
			{
				return;
			}

			Vector3 newPosition = Character.MeshRoot.position;
			newPosition.y = 1000.0f;

			MinimapCamera.transform.position = newPosition;
#endif
		}

		// Called before the camera starts rendering the scene
		void OnPreRender()
		{
			if (MinimapCamera == null || !MinimapCamera.enabled)
			{
				return;
			}

			// Store the current global fog state
			originalFogState = RenderSettings.fog;
			// Disable fog for this camera's render pass
			RenderSettings.fog = false;
		}

		// Called after the camera has finished rendering the scene
		void OnPostRender()
		{
			if (MinimapCamera == null || !MinimapCamera.enabled)
			{
				return;
			}
			
			// Revert fog to its original state after this camera has finished rendering
			RenderSettings.fog = originalFogState;
		}
	}
}