using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FishMMO.Client
{
	public class UIDragObject : UIControl
	{
		public RawImage icon;
		public string referenceID = "";
		public HotkeyType hotkeyType = HotkeyType.None;

		public LayerMask layerMask;
		public float dropDistance = 5.0f;

		public override void OnStarting()
		{
		}

		public override void OnDestroying()
		{
		}

		void Update()
		{
			if (visible)
			{
				if (icon == null || icon.texture == null || string.IsNullOrWhiteSpace(referenceID))
				{
					Clear();
					return;
				}

				// clear the hotkey if we are clicking anywhere that isn't the UI
				// also we can handle dropping items to the ground here if we want
				if (Input.GetMouseButtonDown(0) && !EventSystem.current.IsPointerOverGameObject())
				{
					// we can drop items on the ground from inventory
					if (hotkeyType == HotkeyType.Inventory)
					{
						Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
						RaycastHit hit;
						if (Physics.Raycast(ray, out hit, dropDistance, layerMask))
						{
							//Drop item at position of hit
							Debug.Log("Dropping item to ground at pos[" + hit.point + "]");
						}
					}
					Clear();
					return;
				}

				// UIDragObject always follows the mouse cursor
				Vector3 offset = new Vector3(icon.texture.width * 0.5f + 1.0f, icon.texture.height * -0.5f - 1.0f, 0.0f);
				transform.position = Input.mousePosition + offset;
			}
		}

		public void SetReference(Texture icon, string referenceID, HotkeyType hotkeyType)
		{
			this.icon.texture = icon;
			this.referenceID = referenceID;
			this.hotkeyType = hotkeyType;

			// set position immediately so we don't have any position glitches before Update is triggered
			Vector3 offset = new Vector3(this.icon.texture.width * 0.5f + 1.0f, this.icon.texture.height * -0.5f - 1.0f, 0.0f);
			transform.position = Input.mousePosition + offset;

			visible = true;
		}

		public void Clear()
		{
			visible = false;

			icon.texture = null;
			referenceID = "";
			hotkeyType = HotkeyType.None;
			//transform.position = new Vector3(-9999.0f, -9999.0f, 0.0f); // do we need to do this?
		}
	}
}