using FishMMO.Shared;
using FishMMO.Shared.Core;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FishMMO.Client
{
	/// <summary>
	/// Displays another player character's name, equipment, and attributes by reading
	/// their in-memory data directly. All character data is already synchronised to
	/// observers via WritePayload/ReadPayload, so no server round-trip is required.
	/// </summary>
	public class UIInspect : UIControl
	{
		/// <summary>
		/// Label displaying the inspected character's name.
		/// </summary>
		[Tooltip("Label displaying the inspected character name.")]
		public TMP_Text CharacterNameLabel;

		/// <summary>
		/// Parent transform for dynamically created equipment slot entries.
		/// </summary>
		[Tooltip("Parent transform for equipment slot entries.")]
		public RectTransform SlotParent;

		/// <summary>
		/// Prefab instantiated for each equipment slot. Must contain an Image component for the icon
		/// and optionally a TMP_Text component for the item name.
		/// </summary>
		[Tooltip("Prefab for each equipment slot entry. Must have an Image component.")]
		public GameObject SlotPrefab;

		/// <summary>
		/// Pool of previously instantiated slot GameObjects for reuse.
		/// </summary>
		private readonly List<GameObject> slotPool = new List<GameObject>();

		/// <summary>
		/// Populates the inspect panel with the target player's data and shows it.
		/// Reads character name, equipment items, and icons directly from the in-memory character.
		/// </summary>
		/// <param name="target">The player character to inspect.</param>
		public void Inspect(IPlayerCharacter target)
		{
			if (target == null || target.Transform == null)
			{
				return;
			}

			ClearSlots();

			if (CharacterNameLabel != null)
			{
				CharacterNameLabel.text = target.CharacterName;
			}

			if (target.TryGet(out IEquipmentController equipmentController))
			{
				for (int i = 0; i < equipmentController.Items.Count; ++i)
				{
					Item item = equipmentController.Items[i];
					if (item == null || item.Template == null)
					{
						continue;
					}

					GameObject slotObject = GetPooledSlot();
					slotObject.SetActive(true);

					Image iconImage = slotObject.GetComponentInChildren<Image>();
					if (iconImage != null)
					{
						iconImage.sprite = item.Template.Icon;
						iconImage.enabled = item.Template.Icon != null;
					}

					TMP_Text slotLabel = slotObject.GetComponentInChildren<TMP_Text>();
					if (slotLabel != null)
					{
						slotLabel.text = item.Template.Name;
					}
				}
			}

			Show();
		}

		/// <summary>
		/// Deactivates all pooled slot GameObjects.
		/// </summary>
		private void ClearSlots()
		{
			for (int i = 0; i < slotPool.Count; ++i)
			{
				slotPool[i].SetActive(false);
			}
		}

		/// <summary>
		/// Returns an inactive pooled slot or instantiates a new one if none are available.
		/// </summary>
		/// <returns>A slot GameObject ready for use.</returns>
		private GameObject GetPooledSlot()
		{
			for (int i = 0; i < slotPool.Count; ++i)
			{
				if (!slotPool[i].activeInHierarchy)
				{
					return slotPool[i];
				}
			}

			GameObject newSlot = Instantiate(SlotPrefab, SlotParent);
			slotPool.Add(newSlot);
			return newSlot;
		}
	}
}
