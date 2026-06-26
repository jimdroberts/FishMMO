using TMPro;
using UnityEngine;
using UnityEngine.UI;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Client
{
	/// <summary>
	/// Abstract class representing a UI resource bar for a character, such as health or stamina.
	/// The slider smoothly interpolates toward the target value to avoid jitter from prediction corrections.
	/// </summary>
	public abstract class UIResourceBar : UICharacterControl
	{
		/// <summary>
		/// The slider UI element representing the resource value visually.
		/// </summary>
		public Slider slider;
		/// <summary>
		/// The text UI element displaying the current and maximum resource value.
		/// </summary>
		public TMP_Text resourceValue;

		/// <summary>
		/// The attribute template ID used to identify which resource this bar represents (e.g., health, stamina).
		/// </summary>
		[TemplateReference(typeof(CharacterAttributeTemplate))]
		public int TemplateID;

		/// <summary>
		/// How fast the slider interpolates toward the target value (units per second).
		/// Higher values produce snappier bars; lower values produce smoother motion.
		/// </summary>
		[Tooltip("Interpolation speed for the slider (units per second).")]
		public float SmoothSpeed = 8.0f;

		/// <summary>
		/// When the difference between the displayed and target value exceeds this threshold,
		/// the slider snaps instantly instead of interpolating (e.g., death or resurrection).
		/// </summary>
		[Tooltip("If the change exceeds this fraction (0-1), the slider snaps instantly.")]
		public float SnapThreshold = 0.5f;

		private float targetValue;
		private bool initialized;

		/// <summary>
		/// Called before the character is set. Unsubscribes from attribute update events and updates the bar.
		/// </summary>
		public override void OnPreSetCharacter()
		{
			if (Character != null &&
				Character.TryGet(out ICharacterAttributeController attributeController) &&
				attributeController.TryGetResourceAttribute(TemplateID, out CharacterResourceAttribute attribute))
			{
				attribute.OnAttributeUpdated -= CharacterAttribute_OnAttributeUpdated;

				CharacterAttribute_OnAttributeUpdated(attribute);
			}
		}

		/// <summary>
		/// Called after the character is set. Subscribes to attribute update events and updates the bar.
		/// </summary>
		public override void OnPostSetCharacter()
		{
			base.OnPostSetCharacter();

			if (Character != null &&
				Character.TryGet(out ICharacterAttributeController attributeController) &&
				attributeController.TryGetResourceAttribute(TemplateID, out CharacterResourceAttribute attribute))
			{
				attribute.OnAttributeUpdated += CharacterAttribute_OnAttributeUpdated;

				// Snap to the initial value immediately.
				initialized = false;
				CharacterAttribute_OnAttributeUpdated(attribute);
			}
		}

		/// <summary>
		/// Called when the resource attribute is updated. Sets the target value for the slider
		/// and immediately updates the text label with the authoritative value.
		/// </summary>
		/// <param name="attribute">The updated character attribute.</param>
		public void CharacterAttribute_OnAttributeUpdated(CharacterAttribute attribute)
		{
			if (Character != null &&
				Character.TryGet(out ICharacterAttributeController attributeController) &&
				attributeController.TryGetResourceAttribute(TemplateID, out CharacterResourceAttribute resource))
			{
				targetValue = resource.FinalValueAsFloat > 0.0f
					? resource.CurrentValue / resource.FinalValueAsFloat
					: 0.0f;

				// Text always shows the actual value instantly.
				if (resourceValue != null)
				{
					resourceValue.text = Mathf.RoundToInt(resource.CurrentValue) + "/" + resource.FinalValue;
				}

				// On first update or large changes, snap the slider immediately.
				if (!initialized || (slider != null && Mathf.Abs(slider.value - targetValue) >= SnapThreshold))
				{
					if (slider != null)
					{
						slider.value = targetValue;
					}
					initialized = true;
				}
			}
		}

		/// <summary>
		/// Smoothly interpolates the slider value toward the target value each frame for a fluid resource bar animation.
		/// </summary>
		private void Update()
		{
			if (slider == null || !initialized)
			{
				return;
			}

			if (Mathf.Approximately(slider.value, targetValue))
			{
				return;
			}

			slider.value = Mathf.MoveTowards(slider.value, targetValue, SmoothSpeed * Time.deltaTime);
		}
	}
}