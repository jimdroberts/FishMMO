using UnityEngine;
using UnityEngine.UI;

namespace FishMMO.Client
{
	/// <summary>
	/// Applies consistent spacing and padding to <see cref="GridLayoutGroup"/> components.
	/// Values are read from the <see cref="UITheme"/> which caches them from configuration.
	/// </summary>
	public sealed class GridLayoutGroupCanvasTypeSettings : BaseCanvasTypeSettings
	{
		/// <summary>
		/// Applies grid spacing and padding settings to the GridLayoutGroup.
		/// </summary>
		/// <param name="component">The UI component (must be a <see cref="GridLayoutGroup"/>).</param>
		/// <param name="theme">The pre-parsed UI theme containing grid layout values.</param>
		public override void ApplySettings(Component component, UITheme theme)
		{
			if (component is not GridLayoutGroup grid) return;

			grid.spacing = theme.GridSpacing;
			grid.padding.left = theme.GridPadding.left;
			grid.padding.right = theme.GridPadding.right;
			grid.padding.top = theme.GridPadding.top;
			grid.padding.bottom = theme.GridPadding.bottom;
		}
	}
}