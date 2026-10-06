using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Client
{
	/// <summary>
	/// UI Toolkit stamina bar. Inherits resource bar logic from <see cref="UITKResourceBar"/>, and carries the breath
	/// meter: a thin bar along its top edge, shown while the character holds its breath under water and until the
	/// breath is full again.
	/// </summary>
	/// <remarks>
	/// On the stamina bar rather than a panel of its own because each HUD panel is its own scene document: the meter
	/// rides this one, drags with it, and needs nothing added to a scene. It reads
	/// <see cref="CharacterAttributeController.BreathSeconds"/>, which is predicted, so it runs smoothly.
	/// </remarks>
	public class UITKStaminaBar : UITKResourceBar
	{
		/// <inheritdoc />
		protected override string FillModifierClass => "fish-bar__fill--stam";

		/// <inheritdoc/>
		protected override string RootModifierClass => "res-bar--stam";

		/// <inheritdoc/>
		protected override int RowSlot => 1;

		private VisualElement breathBar;
		private VisualElement breathFill;
		private bool breathShown;
		private float breathShownFraction = -1f;

		/// <inheritdoc />
		public override void OnStarting()
		{
			base.OnStarting();
			VisualElement barRoot = Root?.Q("bar-root");
			if (barRoot == null || breathBar != null)
			{
				return;
			}
			breathBar = new VisualElement { name = "breath-bar", pickingMode = PickingMode.Ignore };
			breathBar.AddToClassList("fish-bar");
			breathBar.AddToClassList("res-breath");
			breathFill = new VisualElement { name = "breath-fill", pickingMode = PickingMode.Ignore };
			breathFill.AddToClassList("fish-bar__fill");
			breathFill.AddToClassList("res-bar__fill");
			breathFill.AddToClassList("fish-bar__fill--xp");
			breathBar.Add(breathFill);
			barRoot.Add(breathBar);
			breathBar.style.visibility = Visibility.Hidden;
			breathShown = false;
		}

		/// <inheritdoc />
		protected override void OnTick()
		{
			base.OnTick();
			if (breathBar == null)
			{
				return;
			}
			float fraction = 1f;
			bool holding = false;
			if (Character != null &&
				Character.TryGet(out ICharacterAttributeController attributes) &&
				attributes is CharacterAttributeController controller &&
				controller.MaxBreathSeconds > 0f)
			{
				fraction = Mathf.Clamp01(controller.BreathSeconds / controller.MaxBreathSeconds);
				holding = controller.IsHoldingBreath;
			}
			bool show = holding || fraction < 0.999f;
			if (show != breathShown)
			{
				breathShown = show;
				// Never an explicit Visible on a child: visibility is inherited, and this panel hides by its root's.
				breathBar.style.visibility = show ? new StyleEnum<Visibility>(StyleKeyword.Null) : Visibility.Hidden;
			}
			if (show && Mathf.Abs(fraction - breathShownFraction) > 0.001f)
			{
				breathShownFraction = fraction;
				breathFill.style.width = Length.Percent(fraction * 100f);
			}
		}
	}
}
