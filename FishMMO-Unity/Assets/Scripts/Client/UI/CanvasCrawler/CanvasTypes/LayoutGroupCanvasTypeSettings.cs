using UnityEngine;
using UnityEngine.UI;

namespace FishMMO.Client
{
	/// <summary>
	/// Applies consistent spacing and padding to any <see cref="HorizontalOrVerticalLayoutGroup"/>
	/// (VerticalLayoutGroup, HorizontalLayoutGroup).
	/// Values are read from the <see cref="UITheme"/> which caches them from configuration.
	/// </summary>
	public sealed class LayoutGroupCanvasTypeSettings : BaseCanvasTypeSettings
	{
		/// <summary>
		/// Applies spacing and padding settings to the layout group.
		/// </summary>
		/// <param name="component">The UI component (must be a <see cref="HorizontalOrVerticalLayoutGroup"/>).</param>
		/// <param name="theme">The pre-parsed UI theme containing layout values.</param>
		public override void ApplySettings(Component component, UITheme theme)
		{
			if (component is not HorizontalOrVerticalLayoutGroup layoutGroup) return;

			layoutGroup.spacing = theme.LayoutSpacing;
			layoutGroup.padding.left = theme.PaddingLeft;
			layoutGroup.padding.right = theme.PaddingRight;
			layoutGroup.padding.top = theme.PaddingTop;
			layoutGroup.padding.bottom = theme.PaddingBottom;
		}
	}
}