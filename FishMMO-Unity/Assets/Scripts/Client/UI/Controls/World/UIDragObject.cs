using UnityEngine;
using UnityEngine.UI;

namespace FishMMO.Client
{
	public class UIDragObject : UIControl
	{
		public const long NULL_REFERENCE_ID = -1;

		public Image Icon;
		public long ReferenceID = NULL_REFERENCE_ID;
		public ReferenceButtonType Type = ReferenceButtonType.None;

		public LayerMask LayerMask;
		public float DropDistance = 5.0f;

		public override void OnStarting()
		{
		}

		public override void OnDestroying()
		{
		}

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

				// clear the hotkey if we are clicking anywhere that isn't the UI
				// also we can handle dropping items to the ground here if we want
				if (Input.GetMouseButtonDown(0) && !UIManager.ControlHasFocus())
				{
					// we can drop items on the ground from inventory
					if (Type == ReferenceButtonType.Inventory)
					{
						Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
						RaycastHit hit;
						if (Physics.Raycast(ray, out hit, DropDistance, LayerMask))
						{
							//Drop item at position of hit
							Debug.Log("Dropping item at pos[" + hit.point + "]");
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

		public void SetReference(Sprite icon, long referenceID, ReferenceButtonType type)
		{
			Icon.sprite = icon;
			ReferenceID = referenceID;
			Type = type;

			// set position immediately so we don't have any position glitches before Update is triggered
			Vector3 offset = new Vector3(Icon.sprite.bounds.size.x * 0.5f + 1.0f, Icon.sprite.bounds.size.y * -0.5f - 1.0f, 0.0f);
			transform.position = Input.mousePosition + offset;

			Show();
		}

		public void Clear()
		{
			Hide();

			Icon.sprite = null;
			ReferenceID = NULL_REFERENCE_ID;
			Type = ReferenceButtonType.None;
			//transform.position = new Vector3(-9999.0f, -9999.0f, 0.0f); // do we need to do this?
		}
	}
}