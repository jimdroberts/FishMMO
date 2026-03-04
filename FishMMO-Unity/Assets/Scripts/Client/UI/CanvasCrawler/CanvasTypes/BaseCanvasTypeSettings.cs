using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>
	/// Abstract base class for canvas type settings handlers.
	/// Each subclass applies themed settings from a <see cref="UITheme"/> to a specific UI component type.
	/// </summary>
	public abstract class BaseCanvasTypeSettings
	{
		/// <summary>
		/// Applies pre-parsed theme settings to the given UI component.
		/// </summary>
		/// <param name="component">The UI component to apply settings to.</param>
		/// <param name="theme">The pre-parsed UI theme.</param>
		public abstract void ApplySettings(Component component, UITheme theme);
	}
}