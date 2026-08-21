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
		/// Clears the character-derived state when the character goes away.
		/// </summary>
		/// <remarks>
		/// The camera is parked rather than left where it was. <c>LateUpdate</c> stops following
		/// the instant <c>Character</c> is null, so without this the minimap kept rendering — and
		/// displaying — the patch of world the *previous* character was standing in, which on a
		/// character switch is another character's location shown under the new one's name. Turning
		/// the camera off also stops it rendering into its RenderTexture while there is nothing to
		/// follow.
		/// </remarks>
		public override void OnPostUnsetCharacter()
		{
			base.OnPostUnsetCharacter();

			if (MinimapCamera != null)
			{
				MinimapCamera.enabled = false;
			}
		}

		/// <summary>
		/// Updates the minimap camera position every frame to follow the character.
		/// </summary>
		/// <remarks>
		/// <para>The camera is also enabled and disabled from here, and that is the point of the
		/// method rather than an aside. A <c>Camera</c> renders its target every frame for as long
		/// as it is enabled, entirely independently of whether anything is looking at the
		/// RenderTexture — so a minimap the player had closed still cost a full extra scene render
		/// per frame, for the whole session. Gating on <see cref="UITKControl.Visible"/> makes a
		/// closed minimap cost what a closed panel should cost: nothing but this comparison.</para>
		/// <para><c>LateUpdate</c> rather than <c>Update</c> so the camera follows the position the
		/// character actually ended the frame at, after movement and any camera rig have run.
		/// Following in <c>Update</c> lags by a frame and shows as jitter on the map.</para>
		/// </remarks>
		void LateUpdate()
		{
			if (MinimapCamera == null)
			{
				return;
			}

			if (!Visible || Character == null || Character.MeshRoot == null)
			{
				if (MinimapCamera.enabled)
				{
					MinimapCamera.enabled = false;
				}
				return;
			}

			if (!MinimapCamera.enabled)
			{
				MinimapCamera.enabled = true;
			}

			// Position the minimap camera above the character
			Vector3 newPosition = Character.MeshRoot.position;
			newPosition.y = 1000.0f;

			MinimapCamera.transform.position = newPosition;
		}

		/* OnPreRender/OnPostRender used to live here, saving and restoring RenderSettings.fog
		 * around the minimap pass. They never ran, and could not have:
		 *
		 *   - Unity delivers both messages only to components on the GameObject that carries the
		 *     Camera doing the rendering. This component lives on the UI panel; MinimapCamera is
		 *     an inspector reference to a camera somewhere else entirely.
		 *   - They are built-in-render-pipeline callbacks, and are not invoked at all under SRP.
		 *
		 * So the fog state they claimed to manage was never touched, and the minimap has always
		 * rendered with whatever the scene's fog happened to be — which is the behaviour players
		 * have. Removed rather than repaired: doing this properly means a component on the camera
		 * itself (or an SRP begin/end-camera-render hook), and mutating the GLOBAL RenderSettings
		 * mid-frame to do it is a scene-wide side effect for one panel's benefit. If fog on the
		 * minimap is ever actually a problem, the fix is the camera's own layer/volume setup, not
		 * a global toggle. */
	}
}
