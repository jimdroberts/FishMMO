using UnityEngine;
using UnityEngine.UI;

namespace FishMMO.Client
{
	/// <summary>
	/// Applies consistent scroll behavior settings and scrollbar colors to <see cref="ScrollRect"/> components.
	/// Configures movement type, sensitivity, inertia, and applies theme colors to associated scrollbar handles.
	/// </summary>
	public sealed class ScrollRectCanvasTypeSettings : BaseCanvasTypeSettings
	{
		/// <summary>
		/// Applies scroll behavior settings and scrollbar theming to the ScrollRect.
		/// </summary>
		/// <param name="component">The UI component (must be a <see cref="ScrollRect"/>).</param>
		/// <param name="theme">The pre-parsed UI theme containing scroll and color values.</param>
		public override void ApplySettings(Component component, UITheme theme)
		{
			if (component is not ScrollRect scrollRect) return;

			scrollRect.scrollSensitivity = theme.ScrollSensitivity;
			scrollRect.movementType = (ScrollRect.MovementType)theme.ScrollMovementType;
			scrollRect.elasticity = theme.ScrollElasticity;
			scrollRect.inertia = theme.ScrollInertia;
			scrollRect.decelerationRate = theme.ScrollDecelerationRate;
		}
	}
}