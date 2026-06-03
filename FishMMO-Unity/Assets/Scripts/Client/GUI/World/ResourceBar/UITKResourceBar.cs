using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Client
{
	/// <summary>
	/// Abstract UI Toolkit resource bar (health/mana/stamina) bound to a character attribute.
	/// Replicates the behaviour of the legacy UGUI <see cref="UIResourceBar"/> using a fill
	/// VisualElement (percent width) in place of a UGUI Slider. The fill smoothly interpolates
	/// toward the target value to avoid jitter from prediction corrections.
	/// </summary>
	public abstract class UITKResourceBar : UITKCharacterControl
	{
		/// <summary>Name of the fill element inside the resource-bar UXML.</summary>
		private const string FILL_NAME = "bar-fill";

		/// <summary>Name of the value label inside the resource-bar UXML.</summary>
		private const string LABEL_NAME = "bar-label";

		/// <summary>
		/// The attribute template ID used to identify which resource this bar represents.
		/// </summary>
		[TemplateReference(typeof(CharacterAttributeTemplate))]
		public int TemplateID;

		/// <summary>
		/// How fast the fill interpolates toward the target value (fraction per second).
		/// </summary>
		[Tooltip("Interpolation speed for the bar fill (fraction per second).")]
		public float SmoothSpeed = 8.0f;

		/// <summary>
		/// When the difference between the displayed and target fraction exceeds this threshold,
		/// the fill snaps instantly instead of interpolating (e.g., death or resurrection).
		/// </summary>
		[Tooltip("If the change exceeds this fraction (0-1), the fill snaps instantly.")]
		public float SnapThreshold = 0.5f;

		/// <summary>
		/// USS modifier class applied to the fill element to colour this bar
		/// (e.g. "fish-bar__fill--hp"). Supplied by the concrete subclass.
		/// </summary>
		protected abstract string FillModifierClass { get; }

		private VisualElement fill;
		private Label label;
		private float targetValue;
		private float displayedValue;
		private bool initialized;

		/// <summary>
		/// Queries the fill and label elements and applies the colour modifier class.
		/// </summary>
		public override void OnStarting()
		{
			VisualElement root = Root;
			if (root == null)
			{
				return;
			}

			fill = root.Q(FILL_NAME);
			label = root.Q<Label>(LABEL_NAME);

			if (fill != null && !string.IsNullOrEmpty(FillModifierClass))
			{
				fill.AddToClassList(FillModifierClass);
			}
		}

		/// <summary>
		/// Unsubscribes from the resource attribute and refreshes once before the character changes.
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
		/// Subscribes to the resource attribute and snaps the bar to the initial value.
		/// </summary>
		public override void OnPostSetCharacter()
		{
			base.OnPostSetCharacter();

			if (Character != null &&
				Character.TryGet(out ICharacterAttributeController attributeController) &&
				attributeController.TryGetResourceAttribute(TemplateID, out CharacterResourceAttribute attribute))
			{
				attribute.OnAttributeUpdated += CharacterAttribute_OnAttributeUpdated;

				initialized = false;
				CharacterAttribute_OnAttributeUpdated(attribute);
			}
		}

		/// <summary>
		/// Updates the target fraction and the value label when the resource attribute changes.
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

				if (label != null)
				{
					label.text = Mathf.RoundToInt(resource.CurrentValue) + "/" + resource.FinalValue;
				}

				if (!initialized || Mathf.Abs(displayedValue - targetValue) >= SnapThreshold)
				{
					displayedValue = targetValue;
					ApplyFill();
					initialized = true;
				}
			}
		}

		private void Update()
		{
			if (fill == null || !initialized)
			{
				return;
			}

			if (Mathf.Approximately(displayedValue, targetValue))
			{
				return;
			}

			displayedValue = Mathf.MoveTowards(displayedValue, targetValue, SmoothSpeed * Time.deltaTime);
			ApplyFill();
		}

		/// <summary>
		/// Writes the current displayed fraction to the fill element's width.
		/// </summary>
		private void ApplyFill()
		{
			fill.style.width = Length.Percent(Mathf.Clamp01(displayedValue) * 100.0f);
		}
	}
}
