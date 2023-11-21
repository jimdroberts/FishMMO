using System.Collections.Generic;
using TMPro;
using UnityEngine;
using FishMMO.Shared;

namespace FishMMO.Client
{
	public class UIEquipment : UICharacterControl
	{
		public RectTransform content;
		public TMP_Text attributePrefab;

		public List<UIEquipmentButton> buttons = new List<UIEquipmentButton>();

		public Dictionary<string, TMP_Text> attributeLabels = new Dictionary<string, TMP_Text>();

		public override void OnStarting()
		{
			UIEquipmentButton[] equipmentButtons = gameObject.GetComponentsInChildren<UIEquipmentButton>();
			if (equipmentButtons != null)
			{
				buttons = new List<UIEquipmentButton>();
				for (int i = 0; i < equipmentButtons.Length; ++i)
				{
					UIEquipmentButton button = equipmentButtons[i];
					button.Type = ReferenceButtonType.Equipment;
					button.ReferenceID = (int)button.ItemSlotType;
					buttons.Add(button);
				}
			}
		}

		public override void OnDestroying()
		{
			DestroyAttributes();
		}

		private void DestroyAttributes()
		{
			if (attributeLabels != null)
			{
				foreach (TMP_Text obj in attributeLabels.Values)
				{
					Destroy(obj.gameObject);
				}
				attributeLabels.Clear();
			}
		}

		public override void OnPreSetCharacter()
		{
			if (Character != null)
			{
				if (Character.AttributeController != null)
				{
					foreach (CharacterAttribute attribute in Character.AttributeController.Attributes.Values)
					{
						attribute.OnAttributeUpdated -= OnAttributeUpdated;
					}
				}
				if (Character.EquipmentController != null)
				{
					Character.EquipmentController.OnSlotUpdated -= OnEquipmentSlotUpdated;
				}
			}
		}

		public override void SetCharacter(Character character)
		{
			base.SetCharacter(character);
			DestroyAttributes();

			if (buttons != null &&
				Character != null &&
				Character.EquipmentController != null)
			{
				Character.EquipmentController.OnSlotUpdated -= OnEquipmentSlotUpdated;
				foreach (UIEquipmentButton button in buttons)
				{
					button.Character = Character;
					if (Character != null)
					{
						SetButtonSlot(Character.EquipmentController, button);
					}
				}
				Character.EquipmentController.OnSlotUpdated += OnEquipmentSlotUpdated;
			}

			if (Character != null &&
				Character.AttributeController != null)
			{
				foreach (CharacterAttribute attribute in Character.AttributeController.Attributes.Values)
				{
					attribute.OnAttributeUpdated -= OnAttributeUpdated; // just incase..
					TMP_Text label = Instantiate(attributePrefab, content);
					label.text = attribute.ToString();
					attributeLabels.Add(attribute.Template.Name, label);
					attribute.OnAttributeUpdated += OnAttributeUpdated;
				}
			}
		}

		private void SetButtonSlot(ItemContainer container, UIEquipmentButton button)
		{
			if (container == null || button == null)
			{
				return;
			}

			if (container.TryGetItem((int)button.ItemSlotType, out Item item))
			{
				// update our button display
				if (button.Icon != null) button.Icon.texture = item.Template.Icon;
				//inventorySlots[i].cooldownText = character.CooldownController.IsOnCooldown();
				if (button.AmountText != null) button.AmountText.text = item.IsStackable ? item.Stackable.Amount.ToString() : "";
			}
			else
			{
				// clear the slot
				button.Clear();
			}
		}

		public void OnEquipmentSlotUpdated(ItemContainer container, Item item, int equipmentSlot)
		{
			if (container == null || buttons == null)
			{
				return;
			}

			if (!container.IsSlotEmpty(equipmentSlot))
			{
				// update our button display
				UIEquipmentButton button = buttons[equipmentSlot];
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