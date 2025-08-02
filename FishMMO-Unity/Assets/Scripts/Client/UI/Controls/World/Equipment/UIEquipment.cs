using System.Collections.Generic;
using TMPro;
using UnityEngine;
using FishMMO.Shared;

namespace FishMMO.Client
{
	/// <summary>
	/// UI control for managing and displaying player equipment and attributes.
	/// </summary>
	public class UIEquipment : UICharacterControl
	{
		/// <summary>
		/// Camera used for equipment view (e.g., 3D preview).
		/// </summary>
		private Camera equipmentViewCamera;

		/// <summary>
		/// The parent RectTransform for equipment UI elements.
		/// </summary>
		public RectTransform content;

		/// <summary>
		/// The label prefab for attribute categories and attributes.
		/// </summary>
		public TMP_Text UILabel;

		/// <summary>
		/// Prefab used to instantiate attribute labels.
		/// </summary>
		public UIAttribute AttributeLabelPrefab;

		/// <summary>
		/// List of equipment slot buttons.
		/// </summary>
		public List<UIEquipmentButton> buttons = new List<UIEquipmentButton>();

		/// <summary>
		/// List of category labels for attributes (e.g., Resource, Damage).
		/// </summary>
		public List<TMP_Text> attributeCategoryLabels = new List<TMP_Text>();

		/// <summary>
		/// Dictionary of attribute labels by attribute template ID.
		/// </summary>
		public Dictionary<long, UIAttribute> attributeLabels = new Dictionary<long, UIAttribute>();

		/// <summary>
		/// Called when the UI is starting. Initializes equipment buttons and their slot references.
		/// </summary>
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
					int itemSlot = (int)button.ItemSlotType;
					button.ReferenceID = itemSlot;

					// Ensure the buttons list is large enough for the slot index
					while (buttons.Count <= button.ReferenceID)
					{
						buttons.Add(null);
					}

					buttons[itemSlot] = button;
				}
			}
		}

		/// <summary>
		/// Called when the UI is being destroyed. Cleans up camera and attribute labels.
		/// </summary>
		public override void OnDestroying()
		{
			equipmentViewCamera = null;
			DestroyAttributes();
		}

		/// <summary>
		/// Toggles the visibility of the equipment UI and associated camera.
		/// </summary>
		public override void ToggleVisibility()
		{
			base.ToggleVisibility();

			if (equipmentViewCamera != null)
			{
				equipmentViewCamera.gameObject.SetActive(Visible);
			}
		}

		/// <summary>
		/// Shows the equipment UI and associated camera.
		/// </summary>
		public override void Show()
		{
			base.Show();

			if (equipmentViewCamera != null)
			{
				equipmentViewCamera.gameObject.SetActive(true);
			}
		}

		/// <summary>
		/// Hides the equipment UI and associated camera.
		/// </summary>
		public override void Hide()
		{
			base.Hide();

			if (equipmentViewCamera != null)
			{
				equipmentViewCamera.gameObject.SetActive(false);
			}
		}

		/// <summary>
		/// Destroys all attribute labels and category labels in the UI.
		/// </summary>
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

		/// <summary>
		/// Called before setting the character reference. Unsubscribes from equipment slot updates.
		/// </summary>
		public override void OnPreSetCharacter()
		{
			if (Character != null &&
				Character.TryGet(out IEquipmentController equipmentController))
			{
				equipmentController.OnSlotUpdated -= OnEquipmentSlotUpdated;
			}
		}

		/// <summary>
		/// Called after setting the character reference. Initializes equipment buttons and attribute labels.
		/// </summary>
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
				// Categorize attributes for display
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

				// Add category labels and attribute labels for each category
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

				label.gameObject.SetActive(true);

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

		/// <summary>
		/// Adds a UI label for a character attribute and subscribes to updates.
		/// </summary>
		/// <param name="attribute">The character attribute to display.</param>
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
			label.gameObject.SetActive(true);
			attributeLabels.Add(attribute.Template.ID, label);
			attribute.OnAttributeUpdated += OnAttributeUpdated;
		}

		/// <summary>
		/// Sets the equipment button slot display based on the item in the container.
		/// </summary>
		/// <param name="container">The equipment controller.</param>
		/// <param name="button">The equipment button to update.</param>
		private void SetButtonSlot(IEquipmentController container, UIEquipmentButton button)
		{
			if (container == null || button == null)
			{
				return;
			}

			if (container.TryGetItem((int)button.ItemSlotType, out Item item))
			{
				// Update button display with item icon and amount
				if (button.Icon != null)
				{
					button.Icon.sprite = item.Template.Icon;
				}
				if (button.AmountText != null)
				{
					button.AmountText.text = item.IsStackable ? item.Stackable.Amount.ToString() : "";
				}
			}
			else
			{
				// Clear the slot if no item
				button.Clear();
			}
		}

		/// <summary>
		/// Callback for when an equipment slot is updated. Refreshes the corresponding button display.
		/// </summary>
		/// <param name="container">The item container holding the equipment.</param>
		/// <param name="item">The item in the updated slot.</param>
		/// <param name="equipmentSlot">The index of the updated equipment slot.</param>
		public void OnEquipmentSlotUpdated(IItemContainer container, Item item, int equipmentSlot)
		{
			if (container == null || buttons == null)
			{
				return;
			}

			if (!container.IsSlotEmpty(equipmentSlot))
			{
				// Update button display with item icon and amount
				UIEquipmentButton button = buttons[equipmentSlot];
				if (button.Icon != null)
				{
					button.Icon.sprite = item.Template.Icon;
				}
				if (button.AmountText != null)
				{
					button.AmountText.text = item.IsStackable ? item.Stackable.Amount.ToString() : "";
				}
			}
			else
			{
				// Clear the slot if no item
				buttons[equipmentSlot].Clear();
			}
		}

		/// <summary>
		/// Callback for when an attribute is updated. Refreshes the corresponding attribute label.
		/// </summary>
		/// <param name="attribute">The updated character attribute.</param>
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

		/// <summary>
		/// Sets the camera used for equipment view (e.g., 3D preview).
		/// </summary>
		/// <param name="camera">The camera to set.</param>
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