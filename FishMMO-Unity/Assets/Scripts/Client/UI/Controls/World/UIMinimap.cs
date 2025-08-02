using UnityEngine;
using FishMMO.Shared;
using FishMMO.Logging;

namespace FishMMO.Client
{
	/// <summary>
	/// The UIMinimap class handles the minimap UI element, including its camera and rendering settings.
	/// </summary>
	public class UIMinimap : UICharacterControl
	{
		/// <summary>
		/// The camera used to render the minimap view.
		/// </summary>
		public Camera MinimapCamera;

		/// <summary>
		/// Stores the original global fog state before minimap rendering.
		/// </summary>
		private bool originalFogState;

		/// <summary>
		/// Called when the minimap UI is starting. Initializes camera settings for minimap rendering.
		/// </summary>
		public override void OnStarting()
		{
			if (MinimapCamera == null)
			{
				Log.Warning("UIMinimap", "MinimapCamera is not assigned to UIMinimap. Minimap will not function correctly.");
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

		/// <summary>
		/// Called after the character is set. Updates minimap camera position to follow the character.
		/// </summary>
		public override void OnPostSetCharacter()
		{
			base.OnPostSetCharacter();
#if !UNITY_SERVER
			if (Character == null || Character.MeshRoot == null)
			{
				Log.Warning("UIMinimap", "Character or Character.MeshRoot is null on OnPostSetCharacter.");
				return;
			}

			// Position the minimap camera above the character
			Vector3 newPosition = Character.MeshRoot.position;
			newPosition.y = 1000.0f;

			MinimapCamera.transform.position = newPosition;
#endif
		}

		/// <summary>
		/// Called before the character is unset. Can be used for cleanup if needed.
		/// </summary>
		public override void OnPreUnsetCharacter()
		{
			base.OnPreUnsetCharacter();
		}

		/// <summary>
		/// Updates the minimap camera position every frame to follow the character.
		/// </summary>
		void LateUpdate()
		{
#if !UNITY_SERVER
			if (Character == null || Character.MeshRoot == null || MinimapCamera == null)
			{
				return;
			}

			// Position the minimap camera above the character
			Vector3 newPosition = Character.MeshRoot.position;
			newPosition.y = 1000.0f;

			MinimapCamera.transform.position = newPosition;
#endif
		}

		/// <summary>
		/// Called before the minimap camera starts rendering. Disables fog for the minimap render pass.
		/// </summary>
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

		/// <summary>
		/// Called after the minimap camera has finished rendering. Restores fog to its original state.
		/// </summary>
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