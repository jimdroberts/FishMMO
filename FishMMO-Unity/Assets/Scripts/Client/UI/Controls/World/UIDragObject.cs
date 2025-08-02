using UnityEngine;
using UnityEngine.UI;
using FishMMO.Logging;

namespace FishMMO.Client
{
	/// <summary>
	/// UIDragObject handles the dragging of UI elements, providing visual feedback and
	/// handling drop logic for items, skills, etc.
	/// </summary>
	public class UIDragObject : UIControl
	{
		/// <summary>
		/// Constant representing a null reference ID for drag objects.
		/// </summary>
		public const long NULL_REFERENCE_ID = -1;

		/// <summary>
		/// The icon image displayed while dragging.
		/// </summary>
		public Image Icon;
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
		/// Per-frame update for drag object. Handles drag visuals and drop logic.
		/// </summary>
		void Update()
		{
			if (Visible)
			{
				if (Icon == null ||
					Icon.sprite == null ||
					ReferenceID == NULL_REFERENCE_ID)
				{
					Clear();
					return;
				}

				// Clear the hotkey if clicking anywhere that isn't the UI
				// Also handles dropping items to the ground from inventory
				if (Input.GetMouseButtonDown(0) && !UIManager.ControlHasFocus())
				{
					// Only drop items if dragging from inventory
					if (Type == ReferenceButtonType.Inventory)
					{
						Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
						RaycastHit hit;
						if (Physics.Raycast(ray, out hit, DropDistance, LayerMask))
						{
							// Drop item at position of hit
							Log.Debug("UIDragObject", "Dropping item at pos[" + hit.point + "]");
						}
					}
					Clear();
					return;
				}

				// UIDragObject always follows the mouse cursor
				Vector3 offset = new Vector3(Icon.sprite.bounds.size.x * 0.5f + 1.0f, Icon.sprite.bounds.size.y * -0.5f - 1.0f, 0.0f);
				transform.position = Input.mousePosition + offset;
			}
		}

		/// <summary>
		/// Sets the reference data for the drag object and positions it at the mouse cursor.
		/// </summary>
		/// <param name="icon">Sprite to display while dragging.</param>
		/// <param name="referenceID">Reference ID for the dragged object.</param>
		/// <param name="type">Type of reference button.</param>
		public void SetReference(Sprite icon, long referenceID, ReferenceButtonType type)
		{
			Icon.sprite = icon;
			ReferenceID = referenceID;
			Type = type;

			// Set position immediately to avoid glitches before Update is triggered
			Vector3 offset = Vector3.zero;
			if (Icon != null && Icon.sprite != null)
			{
				offset = new Vector3(Icon.sprite.bounds.size.x * 0.5f + 1.0f, Icon.sprite.bounds.size.y * -0.5f - 1.0f, 0.0f);
			}
			transform.position = Input.mousePosition + offset;

			Show();
		}

		/// <summary>
		/// Clears the drag object state, hides it, and resets reference data.
		/// </summary>
		public void Clear()
		{
			Hide();

			Icon.sprite = null;
			ReferenceID = NULL_REFERENCE_ID;
			Type = ReferenceButtonType.None;
			// Optionally move off-screen if needed
			//transform.position = new Vector3(-9999.0f, -9999.0f, 0.0f); // do we need to do this?
		}
	}
}