using System.Collections.Generic;
using TMPro;
using UnityEngine;
using FishMMO.Shared;

namespace FishMMO.Client
{
	public class UIEquipment : UICharacterControl
	{
		private Camera equipmentViewCamera;

		public RectTransform content;
		public TMP_Text UILabel;
		public UIAttribute AttributeLabelPrefab;

		public List<UIEquipmentButton> buttons = new List<UIEquipmentButton>();

		public List<TMP_Text> attributeCategoryLabels = new List<TMP_Text>();
		public Dictionary<long, UIAttribute> attributeLabels = new Dictionary<long, UIAttribute>();

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
			equipmentViewCamera = null;
			DestroyAttributes();
		}

		public override void ToggleVisibility()
		{
			base.ToggleVisibility();

			if (equipmentViewCamera != null)
			{
				equipmentViewCamera.gameObject.SetActive(Visible);
			}
		}

		public override void Show()
		{
			base.Show();

			if (equipmentViewCamera != null)
			{
				equipmentViewCamera.gameObject.SetActive(true);
			}
		}

		public override void Hide()
		{
			base.Hide();

			if (equipmentViewCamera != null)
			{
				equipmentViewCamera.gameObject.SetActive(false);
			}
		}

		private void DestroyAttributes()
		{
			if (attributeLabels != null)
			{
				foreach (UIAttribute obj in attributeLabels.Values)
				{
					Destroy(obj.gameObject);
				}
				attributeLabels.Clear();
			}
			if (attributeCategoryLabels != null)
			{
				foreach (TMP_Text text in attributeCategoryLabels)
				{
					Destroy(text.gameObject);
				}
				attributeCategoryLabels.Clear();
			}
		}

		public override void OnPreSetCharacter()
		{
			if (Character != null &&
				Character.TryGet(out IEquipmentController equipmentController))
			{
				equipmentController.OnSlotUpdated -= OnEquipmentSlotUpdated;
			}
		}

		public override void OnPostSetCharacter()
		{
			base.OnPostSetCharacter();
			DestroyAttributes();

			if (buttons != null &&
				Character != null &&
				Character.TryGet(out IEquipmentController equipmentController))
			{
				equipmentController.OnSlotUpdated -= OnEquipmentSlotUpdated;
				foreach (UIEquipmentButton button in buttons)
				{
					button.Character = Character;
					if (Character != null)
					{
						SetButtonSlot(equipmentController, button);
					}
				}
				equipmentController.OnSlotUpdated += OnEquipmentSlotUpdated;
			}

			if (Character != null &&
				Character.TryGet(out ICharacterAttributeController attributeController))
			{
				List<CharacterAttribute> resourceAttributes = new List<CharacterAttribute>();
				List<CharacterAttribute> damageAttributes = new List<CharacterAttribute>();
				List<CharacterAttribute> resistanceAttributes = new List<CharacterAttribute>();
				List<CharacterAttribute> coreAttributes = new List<CharacterAttribute>();

				foreach (CharacterResourceAttribute resourceAttribute in attributeController.ResourceAttributes.Values)
				{
					resourceAttributes.Add(resourceAttribute);
				}

				foreach (CharacterAttribute attribute in attributeController.Attributes.Values)
				{
					if (attribute.Template.Name.Contains("Regeneration"))
					{
						resourceAttributes.Add(attribute);
					}
					else if (attribute.Template is DamageAttributeTemplate)
					{
						damageAttributes.Add(attribute);
					}
					else if (attribute.Template is ResistanceAttributeTemplate)
					{
						resistanceAttributes.Add(attribute);
					}
					else
					{
						coreAttributes.Add(attribute);
					}
				}

				TMP_Text label = Instantiate(UILabel, content);
				label.text = "Resource";
				label.fontSize = 16.0f;
				label.alignment = TextAlignmentOptions.Center;
				attributeCategoryLabels.Add(label);

				foreach (CharacterAttribute core in resourceAttributes)
				{
					AddCharacterAttributeLabel(core);
				}

				label = Instantiate(UILabel, content);
				label.text = "Damage";
				label.fontSize = 16.0f;
				label.alignment = TextAlignmentOptions.Center;
				attributeCategoryLabels.Add(label);

				foreach (CharacterAttribute damage in damageAttributes)
				{
					AddCharacterAttributeLabel(damage);
				}

				label = Instantiate(UILabel, content);
				label.text = "Resistance";
				label.fontSize = 16.0f;
				label.alignment = TextAlignmentOptions.Center;
				attributeCategoryLabels.Add(label);

				foreach (CharacterAttribute resistance in resistanceAttributes)
				{
					AddCharacterAttributeLabel(resistance);
				}

				label = Instantiate(UILabel, content);
				label.text = "Core";
				label.fontSize = 16.0f;
				label.alignment = TextAlignmentOptions.Center;
				attributeCategoryLabels.Add(label);

				foreach (CharacterAttribute core in coreAttributes)
				{
					AddCharacterAttributeLabel(core);
				}

				resourceAttributes.Clear();
				damageAttributes.Clear();
				resistanceAttributes.Clear();
				coreAttributes.Clear();
			}
		}

		private void AddCharacterAttributeLabel(CharacterAttribute attribute)
		{
			attribute.OnAttributeUpdated -= OnAttributeUpdated; // just in case..
			UIAttribute label = Instantiate(AttributeLabelPrefab, content);
			label.Name.text = attribute.Template.Name;
			if (attribute.Template.IsResourceAttribute)
			{
				CharacterResourceAttribute resource = attribute as CharacterResourceAttribute;
				if (resource != null)
				{
					label.Value.text = Mathf.RoundToInt(resource.CurrentValue) + " / " + resource.FinalValue;
				}
			}
			else
			{
				label.Value.text = attribute.FinalValue.ToString();
				if (attribute.Template.IsPercentage)
				{
					label.Value.text += "%";
				}
			}
			attributeLabels.Add(attribute.Template.ID, label);
			attribute.OnAttributeUpdated += OnAttributeUpdated;
		}

		private void SetButtonSlot(IEquipmentController container, UIEquipmentButton button)
		{
			if (container == null || button == null)
			{
				return;
			}

			if (container.TryGetItem((int)button.ItemSlotType, out Item item))
			{
				// update our button display
				if (button.Icon != null)
				{
					button.Icon.sprite = item.Template.Icon;
				}
				//inventorySlots[i].cooldownText = character.CooldownController.IsOnCooldown();
				if (button.AmountText != null)
				{
					button.AmountText.text = item.IsStackable ? item.Stackable.Amount.ToString() : "";
				}
			}
			else
			{
				// clear the slot
				button.Clear();
			}
		}

		public void OnEquipmentSlotUpdated(IItemContainer container, Item item, int equipmentSlot)
		{
			if (container == null || buttons == null)
			{
				return;
			}

			if (!container.IsSlotEmpty(equipmentSlot))
			{
				// update our button display
				UIEquipmentButton button = buttons[equipmentSlot];
				if (button.Icon != null)
				{
					button.Icon.sprite = item.Template.Icon;
				}
				//inventorySlots[i].cooldownText = character.CooldownController.IsOnCooldown();
				if (button.AmountText != null)
				{
					button.AmountText.text = item.IsStackable ? item.Stackable.Amount.ToString() : "";
				}
			}
			else
			{
				// clear the slot
				buttons[equipmentSlot].Clear();
			}
		}

		public void OnAttributeUpdated(CharacterAttribute attribute)
		{
			if (attributeLabels.TryGetValue(attribute.Template.ID, out UIAttribute label))
			{
				label.Name.text = attribute.Template.Name;
				if (attribute.Template.IsResourceAttribute)
				{
					CharacterResourceAttribute resource = attribute as CharacterResourceAttribute;
					if (resource != null)
					{
						label.Value.text = Mathf.RoundToInt(resource.CurrentValue) + " / " + resource.FinalValue;
					}
				}
				else
				{
					label.Value.text = attribute.FinalValue.ToString();
					if (attribute.Template.IsPercentage)
					{
						label.Value.text += "%";
					}
				}
			}
		}

		public void SetEquipmentViewCamera(Camera camera)
		{
			if (camera == null)
			{
				equipmentViewCamera = null;
				return;
			}
			equipmentViewCamera = camera;
		}
	}
}