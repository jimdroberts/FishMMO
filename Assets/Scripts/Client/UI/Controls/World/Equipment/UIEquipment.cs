using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Client
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

			if (buttons == null || buttons.Count != character.EquipmentController.items.Count)
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

			Dictionary<string, CharacterAttribute> attributes = character.AttributeController.Attributes;
			if (attributeLabels == null || attributeLabels.Count != attributes.Count)
			{
				if (attributeLabels != null)
				{
					foreach (TMP_Text obj in attributeLabels.Values)
					{
						Destroy(obj.gameObject);
					}
					attributeLabels.Clear();
				}

				foreach (CharacterAttribute attribute in attributes.Values)
				{
					attribute.OnAttributeUpdated -= OnAttributeUpdated; // just incase..
					TMP_Text label = Instantiate(attributePrefab, content);
					label.text = attribute.ToString();
					attributeLabels.Add(attribute.Template.Name, label);
					attribute.OnAttributeUpdated += OnAttributeUpdated;
				}
			}
		}

		public void OnEquipmentSlotUpdated(Item item, int equipmentSlot)
		{
			if (buttons == null || equipmentSlot < 0 || equipmentSlot > buttons.Count)
			{
				return;
			}
			UIEquipmentButton button = buttons[equipmentSlot];
			button.referenceID = ((int)equipmentSlot).ToString();
			// just for safety
			button.hotkeyType = HotkeyType.Equipment;
			// update icon data -- should probably do this in UIEquipment
			if (button.icon != null) button.icon.texture = item != null ? item.Template.Icon : null;
			// equipmentSlots[i].cooldownText = character.CooldownController.IsOnCooldown();
			if (button.amountText != null) button.amountText.text = item != null ? item.stackSize.ToString() : null;
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