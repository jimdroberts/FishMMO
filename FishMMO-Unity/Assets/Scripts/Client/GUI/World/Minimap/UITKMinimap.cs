using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Shared;
using FishMMO.Logging;

namespace FishMMO.Client
{
	/// <summary>
	/// UI Toolkit minimap control. Drives an overhead orthographic camera that renders the
	/// Minimap layer (plus any additional layers) into a RenderTexture displayed in the panel.
	/// Attach <see cref="MinimapIcon"/> to any object that should appear on the minimap.
	/// </summary>
	public class UITKMinimap : UITKCharacterControl
	{
		/// <summary>Draw order tier for this panel. See <see cref="UITKPanelLayer"/>.</summary>
		protected override UITKPanelLayer Layer => UITKPanelLayer.Hud;

		/// <summary>Name of the minimap view element in the UXML.</summary>
		private const string MINIMAP_VIEW_NAME = "minimap-view";

		/// <summary>
		/// The camera used to render the minimap view.
		/// </summary>
		public Camera MinimapCamera;

		/// <summary>
		/// Additional layers the minimap camera should render besides the Minimap layer.
		/// Assign Ground, Water, or other environment layers here in the Inspector.
		/// </summary>
		[Tooltip("Additional layers the minimap camera should render besides the Minimap layer (e.g. Ground, Water).")]
		public LayerMask AdditionalLayers;

		/// <summary>
		/// Stores the original global fog state before minimap rendering.
		/// </summary>
		private bool originalFogState;

		/// <summary>
		/// The element that displays the minimap camera's RenderTexture.
		/// </summary>
		private VisualElement minimapView;

		/// <summary>
		/// Configures the minimap camera and binds the camera's RenderTexture to the view element.
		/// </summary>
		public override void OnStarting()
		{
			if (Root != null)
			{
				minimapView = Root.Q<VisualElement>(MINIMAP_VIEW_NAME);
			}

			if (MinimapCamera == null)
			{
				Log.Warning("UITKMinimap", "MinimapCamera is not assigned to UITKMinimap. Minimap will not function correctly.");
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

			// Configure culling mask: always include the Minimap layer, plus any additional layers
			int minimapLayer = LayerMask.GetMask(MinimapIcon.MINIMAP_LAYER);
			MinimapCamera.cullingMask = minimapLayer | AdditionalLayers.value;

			// Display the camera's render texture in the view element, if assigned.
			if (minimapView != null && MinimapCamera.targetTexture != null)
			{
				minimapView.style.backgroundImage = Background.FromRenderTexture(MinimapCamera.targetTexture);
			}
		}

		/// <summary>
		/// Positions the minimap camera above the character once it is set.
		/// </summary>
		public override void OnPostSetCharacter()
		{
			base.OnPostSetCharacter();
			/* MinimapCamera is checked here for the same reason OnStarting and the Update
			 * paths check it: it is an inspector reference that is legitimately null when the
			 * panel is placed without a minimap camera. Omitting it from this guard threw a
			 * NullReferenceException out of SetCharacter, which runs during world entry — so
			 * an unassigned minimap camera aborted the whole entry sequence rather than just
			 * disabling the minimap. */
			if (Character == null || Character.MeshRoot == null || MinimapCamera == null)
			{
				Log.Warning("UITKMinimap", $"Skipping camera placement: Character null={Character == null}, MeshRoot null={Character?.MeshRoot == null}, MinimapCamera null={MinimapCamera == null}.");
				return;
			}

			// Position the minimap camera above the character
			Vector3 newPosition = Character.MeshRoot.position;
			newPosition.y = 1000.0f;

			MinimapCamera.transform.position = newPosition;
		}

		/// <summary>
		/// Updates the minimap camera position every frame to follow the character.
		/// </summary>
		void LateUpdate()
		{
			if (Character == null || Character.MeshRoot == null || MinimapCamera == null)
			{
				return;
			}

			// Position the minimap camera above the character
			Vector3 newPosition = Character.MeshRoot.position;
			newPosition.y = 1000.0f;

			MinimapCamera.transform.position = newPosition;
		}

		/// <summary>
		/// Disables fog for the minimap render pass.
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
		/// Restores fog after the minimap render pass.
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
