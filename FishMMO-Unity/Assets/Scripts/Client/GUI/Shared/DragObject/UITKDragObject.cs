using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using FishMMO.Logging;

namespace FishMMO.Client
{
	/// <summary>
	/// UI Toolkit drag object. Displays a dragged icon that follows the cursor and handles
	/// drop logic for items. Mirrors the legacy UGUI <c>UIDragObject</c> API.
	/// </summary>
	public class UITKDragObject : UITKControl
	{
		/// <summary>
		/// Constant representing a null reference ID for drag objects.
		/// </summary>
		public const long NULL_REFERENCE_ID = -1;

		/// <summary>
		/// Name of the drag icon element.
		/// </summary>
		private const string DRAG_ICON_NAME = "drag-icon";

		/// <summary>
		/// The reference ID associated with the dragged object.
		/// </summary>
		public long ReferenceID = NULL_REFERENCE_ID;

		/// <summary>
		/// The type of reference button (e.g., inventory, skill, etc.).
		/// </summary>
		public ReferenceButtonType Type = ReferenceButtonType.None;

		/// <summary>
		/// Layer mask used for raycasting when dropping items.
		/// </summary>
		public LayerMask LayerMask;

		/// <summary>
		/// Maximum distance for drop raycast.
		/// </summary>
		public float DropDistance = 5.0f;

		/// <summary>
		/// The visual element used as the drag icon.
		/// </summary>
		private VisualElement dragIcon;
		/// <summary>
		/// The sprite displayed while dragging.
		/// </summary>
		private Sprite iconSprite;

		/// <summary>
		/// The sprite currently displayed by the drag object, or null when inactive.
		/// </summary>
		public Sprite IconSprite => iconSprite;

		/// <summary>
		/// Resolves cached elements and prepares the drag icon for absolute positioning.
		/// </summary>
		public override void OnStarting()
		{
			if (Root == null)
			{
				return;
			}

			dragIcon = Root.Q<VisualElement>(DRAG_ICON_NAME);
			Root.pickingMode = PickingMode.Ignore;
			if (dragIcon != null)
			{
				dragIcon.pickingMode = PickingMode.Ignore;
				dragIcon.style.position = Position.Absolute;
			}
		}

		/// <summary>
		/// Per-frame update for the drag object. Handles drag visuals and drop logic.
		/// </summary>
		private void Update()
		{
			if (!Visible)
			{
				return;
			}

			if (dragIcon == null || iconSprite == null || ReferenceID == NULL_REFERENCE_ID)
			{
				Clear();
				return;
			}

			Mouse mouse = Mouse.current;
			Vector2 mousePosition = mouse != null ? mouse.position.ReadValue() : Vector2.zero;

			// Clear the drag if clicking anywhere that isn't the UI.
			// Also handles dropping items to the ground from inventory.
			if (mouse != null && mouse.leftButton.wasPressedThisFrame && !UIManager.ControlHasFocus())
			{
				if (Type == ReferenceButtonType.Inventory && Camera.main != null)
				{
					Ray ray = Camera.main.ScreenPointToRay(mousePosition);
					if (Physics.Raycast(ray, out RaycastHit hit, DropDistance, LayerMask))
					{
						Log.Debug("UITKDragObject", "Dropping item at pos[" + hit.point + "]");
					}
				}
				Clear();
				return;
			}

			UpdatePosition(mousePosition);
		}

		/// <summary>
		/// Sets the reference data for the drag object and positions it at the mouse cursor.
		/// </summary>
		/// <param name="icon">Sprite to display while dragging.</param>
		/// <param name="referenceID">Reference ID for the dragged object.</param>
		/// <param name="type">Type of reference button.</param>
		public void SetReference(Sprite icon, long referenceID, ReferenceButtonType type)
		{
			iconSprite = icon;
			ReferenceID = referenceID;
			Type = type;

			if (dragIcon != null)
			{
				dragIcon.style.backgroundImage = icon != null ? new StyleBackground(icon) : new StyleBackground();
			}

			Mouse mouse = Mouse.current;
			Vector2 mousePosition = mouse != null ? mouse.position.ReadValue() : Vector2.zero;
			UpdatePosition(mousePosition);

			Show();
		}

		/// <summary>
		/// Clears the drag object state, hides it, and resets reference data.
		/// </summary>
		public void Clear()
		{
			Hide();

			iconSprite = null;
			ReferenceID = NULL_REFERENCE_ID;
			Type = ReferenceButtonType.None;

			if (dragIcon != null)
			{
				dragIcon.style.backgroundImage = new StyleBackground();
			}
		}

		/// <summary>
		/// Positions the drag icon at the given screen position, converted to panel space.
		/// </summary>
		/// <param name="screenPosition">Screen-space cursor position.</param>
		private void UpdatePosition(Vector2 screenPosition)
		{
			if (dragIcon == null || Root == null || Root.panel == null)
			{
				return;
			}

			Vector2 panelPosition = RuntimePanelUtils.ScreenToPanel(Root.panel, screenPosition);
			dragIcon.style.left = panelPosition.x;
			dragIcon.style.top = panelPosition.y;
		}
	}
}
