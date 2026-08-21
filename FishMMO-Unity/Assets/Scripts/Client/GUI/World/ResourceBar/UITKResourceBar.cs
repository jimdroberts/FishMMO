using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Client
{
	/// <summary>
	/// Abstract UI Toolkit resource bar (health/mana/stamina) bound to a character attribute.
	/// The value is rendered as a fill VisualElement whose width is a percentage of the bar, and
	/// smoothly interpolates toward the target value to avoid jitter from prediction corrections.
	/// </summary>
	public abstract class UITKResourceBar : UITKCharacterControl
	{
		/// <summary>Draw order tier for this panel. See <see cref="UITKPanelLayer"/>.</summary>
		protected override UITKPanelLayer Layer => UITKPanelLayer.Hud;

		/// <summary>Name of the fill element inside the resource-bar UXML.</summary>
		private const string FILL_NAME = "bar-fill";

		/// <summary>Name of the value label inside the resource-bar UXML.</summary>
		private const string LABEL_NAME = "bar-label";

		/// <summary>Name of the bar's own root element inside the resource-bar UXML.</summary>
		private const string BAR_ROOT_NAME = "bar-root";

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

		/// <summary>
		/// USS modifier class applied to the bar root to place it on screen
		/// (e.g. "res-bar--hp"). Supplied by the concrete subclass.
		/// </summary>
		/// <remarks>
		/// Each bar owns a separate <see cref="UIDocument"/>, so the three of them cannot be
		/// laid out relative to one another by a shared container — every panel root fills the
		/// screen independently. Without a per-bar anchor they all resolve to the same strip
		/// and draw on top of each other, leaving only whichever panel sorts last visible.
		/// </remarks>
		protected abstract string RootModifierClass { get; }

		/// <summary>Cached reference to the bar fill element.</summary>
		private VisualElement fill;
		/// <summary>Cached reference to the bar value label.</summary>
		private Label label;
		/// <summary>The target fraction (0-1) the fill should animate toward.</summary>
		private float targetValue;
		/// <summary>The currently displayed fraction (0-1) used for smooth interpolation.</summary>
		private float displayedValue;
		/// <summary>True once the bar has been initialised with its first value.</summary>
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

			// Applied to the bar root itself rather than the panel root, which belongs to the
			// UIDocument and is shared with nothing.
			VisualElement barRoot = root.Q(BAR_ROOT_NAME);
			if (barRoot != null && !string.IsNullOrEmpty(RootModifierClass))
			{
				barRoot.AddToClassList(RootModifierClass);
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

		/// <summary>
		/// Smoothly interpolates the fill width toward the target value each frame.
		/// </summary>
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
			// Null until OnStarting has run, which does not happen until this panel's visual
			// tree exists — and an attribute update can arrive before that. The value is not
			// lost: UITKCharacterControl re-applies the character once the tree is available.
			if (fill == null)
			{
				return;
			}
			fill.style.width = Length.Percent(Mathf.Clamp01(displayedValue) * 100.0f);
		}
	}
}
