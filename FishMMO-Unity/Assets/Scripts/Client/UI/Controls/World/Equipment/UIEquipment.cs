using System.Collections.Generic;
using TMPro;
using UnityEngine;
using FishMMO.Shared;

namespace FishMMO.Client
{
	public class UIEquipment : UIControl
	{
		public RectTransform content;
		public TMP_Text attributePrefab;

		public List<UIEquipmentButton> buttons = new List<UIEquipmentButton>();

		public Dictionary<string, TMP_Text> attributeLabels = new Dictionary<string, TMP_Text>();

		public override void OnStarting()
		{
		}

		public override void OnDestroying()
		{
		}

		void Update()
		{
			Character character = Character.localCharacter;
			if (character == null) return;

			if (buttons == null || buttons.Count != character.EquipmentController.Items.Count)
			{
				character.EquipmentController.OnSlotUpdated -= OnEquipmentSlotUpdated;
				UIEquipmentButton[] equipmentButtons = gameObject.GetComponentsInChildren<UIEquipmentButton>();
				if (equipmentButtons != null)
				{
					buttons = new List<UIEquipmentButton>();
					for (int i = 0; i < equipmentButtons.Length; ++i)
					{
						buttons.Add(equipmentButtons[i]);
					}
				}
				character.EquipmentController.OnSlotUpdated += OnEquipmentSlotUpdated;
			}

			if (attributeLabels == null || attributeLabels.Count != character.AttributeController.Attributes.Count)
			{
				if (attributeLabels != null)
				{
					foreach (TMP_Text obj in attributeLabels.Values)
					{
						Destroy(obj.gameObject);
					}
					attributeLabels.Clear();
				}

				foreach (CharacterAttribute attribute in character.AttributeController.Attributes.Values)
				{
					attribute.OnAttributeUpdated -= OnAttributeUpdated; // just incase..
					TMP_Text label = Instantiate(attributePrefab, content);
					label.text = attribute.ToString();
					attributeLabels.Add(attribute.Template.Name, label);
					attribute.OnAttributeUpdated += OnAttributeUpdated;
				}
			}
		}

		public void OnEquipmentSlotUpdated(ItemContainer container, Item item, int equipmentSlot)
		{
			if (buttons == null)
			{
				return;
			}

			if (container.IsValidItem(equipmentSlot))
			{
				// update our button display
				UIEquipmentButton button = buttons[equipmentSlot];
				button.ReferenceID = equipmentSlot;
				button.HotkeyType = HotkeyType.Equipment;
				if (button.Icon != null) button.Icon.texture = item.Template.Icon;
				//inventorySlots[i].cooldownText = character.CooldownController.IsOnCooldown();
				if (button.AmountText != null) button.AmountText.text = item.IsStackable ? item.Stackable.Amount.ToString() : "";
			}
			else
			{
				// clear the slot
				buttons[equipmentSlot].Clear();
			}
		}

		public void OnAttributeUpdated(CharacterAttribute attribute)
		{
			if (attributeLabels.TryGetValue(attribute.Template.Name, out TMP_Text label))
			{
				label.text = attribute.ToString();
			}
		}
	}
}